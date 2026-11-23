using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnywhereWinUI.Helpers;
using Microsoft.Win32;

namespace AnywhereWinUI.Services
{
    public enum SystemProxyTamperKind
    {
        Ok,
        DisabledByExternal,
        ChangedByExternal
    }

    public readonly record struct SystemProxyTamperResult(
        SystemProxyTamperKind Kind,
        string ExpectedServer,
        string ActualServer)
    {
        public bool IsTampered => Kind != SystemProxyTamperKind.Ok;
        public static SystemProxyTamperResult Ok(string expected = "") => new(SystemProxyTamperKind.Ok, expected, expected);
    }

    public static class SystemProxyManager
    {
        private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string DefaultProxyOverride = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

        private static readonly string BackupFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "sysproxy_backup.json");

        private static readonly object _sync = new();
        private static bool _expectedActive;
        private static string _expectedHost = "127.0.0.1";
        private static int _expectedPort = 2080;
        private static string _expectedServer = "127.0.0.1:2080";

        public static bool IsExpectedActive => _expectedActive;
        public static string ExpectedServer => _expectedServer;

        public sealed class ProxySnapshot
        {
            public int ProxyEnable { get; set; }
            public string? ProxyServer { get; set; }
            public string? ProxyOverride { get; set; }
            public bool HadProxyServer { get; set; }
            public bool HadProxyOverride { get; set; }
        }

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static bool EnableProxy(string host, int port)
        {
            lock (_sync)
            {
                try
                {
                    string targetServer = $"{host}:{port}";
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true))
                    {
                        if (key == null) return false;

                        CaptureOriginalSnapshotIfNeeded(key, targetServer);

                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                        key.SetValue("ProxyServer", targetServer, RegistryValueKind.String);
                        key.SetValue("ProxyOverride", DefaultProxyOverride, RegistryValueKind.String);
                    }

                    _expectedHost = host;
                    _expectedPort = port;
                    _expectedServer = targetServer;
                    _expectedActive = true;

                    NotifySystemOfProxyChange();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool RetakeProxy()
        {
            if (!_expectedActive || string.IsNullOrWhiteSpace(_expectedHost) || _expectedPort <= 0)
                return false;

            return EnableProxy(_expectedHost, _expectedPort);
        }

        /// <summary>
        /// Only disables/restores the system proxy if Swell Proxy previously took ownership
        /// (preventing Local-Only or TUN-Only mode from wiping a user's external/corporate proxy).
        /// </summary>
        public static bool DisableProxyIfOwned()
        {
            lock (_sync)
            {
                if (_expectedActive || File.Exists(BackupFilePath))
                    return DisableProxy();

                try
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: false);
                    if (key != null)
                    {
                        int enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
                        string actual = (key.GetValue("ProxyServer") as string ?? string.Empty).Trim();
                        if (enabled != 0 &&
                            (string.Equals(actual, _expectedServer, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(actual, $"127.0.0.1:{AppSession.Instance.MixedPort}", StringComparison.OrdinalIgnoreCase)))
                        {
                            return DisableProxy();
                        }
                    }
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Cleans up leftover system proxy settings if a previous session crashed or was killed on shutdown.
        /// </summary>
        public static void RecoverCrashLeftoversOnStartup()
        {
            lock (_sync)
            {
                if (File.Exists(BackupFilePath))
                {
                    DisableProxy();
                    return;
                }

                try
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true);
                    if (key == null) return;

                    int enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
                    string actual = (key.GetValue("ProxyServer") as string ?? string.Empty).Trim();
                    string ownServer = $"127.0.0.1:{AppSession.Instance.MixedPort}";

                    if (enabled != 0 &&
                        string.Equals(actual, ownServer, StringComparison.OrdinalIgnoreCase) &&
                        IsDeadLocalProxy(actual))
                    {
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                        NotifySystemOfProxyChange();
                    }
                }
                catch { }
            }
        }

        public static bool DisableProxy()
        {
            lock (_sync)
            {
                _expectedActive = false;
                try
                {
                    var snapshot = ConsumeBackupSnapshot();
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true))
                    {
                        if (key == null) return false;

                        if (snapshot != null && snapshot.ProxyEnable != 0 && !IsDeadLocalProxy(snapshot.ProxyServer))
                        {
                            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                            if (snapshot.ProxyServer != null)
                                key.SetValue("ProxyServer", snapshot.ProxyServer, RegistryValueKind.String);
                            if (snapshot.ProxyOverride != null)
                                key.SetValue("ProxyOverride", snapshot.ProxyOverride, RegistryValueKind.String);
                            else if (snapshot.HadProxyOverride == false)
                                key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                        }
                        else
                        {
                            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                            if (snapshot != null)
                            {
                                if (snapshot.HadProxyServer && snapshot.ProxyServer != null && !IsDeadLocalProxy(snapshot.ProxyServer))
                                    key.SetValue("ProxyServer", snapshot.ProxyServer, RegistryValueKind.String);
                                else if (!snapshot.HadProxyServer)
                                    key.DeleteValue("ProxyServer", throwOnMissingValue: false);

                                if (snapshot.HadProxyOverride && snapshot.ProxyOverride != null)
                                    key.SetValue("ProxyOverride", snapshot.ProxyOverride, RegistryValueKind.String);
                                else if (!snapshot.HadProxyOverride)
                                    key.DeleteValue("ProxyOverride", throwOnMissingValue: false);
                            }
                        }
                    }

                    NotifySystemOfProxyChange();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static SystemProxyTamperResult CheckSystemProxyIntegrity()
        {
            if (!_expectedActive)
                return SystemProxyTamperResult.Ok();

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: false);
                if (key == null)
                    return SystemProxyTamperResult.Ok(_expectedServer);

                int enabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
                string actualServer = (key.GetValue("ProxyServer") as string ?? string.Empty).Trim();

                if (enabled == 0)
                {
                    return new SystemProxyTamperResult(
                        SystemProxyTamperKind.DisabledByExternal,
                        _expectedServer,
                        string.Empty);
                }

                if (!string.Equals(actualServer, _expectedServer, StringComparison.OrdinalIgnoreCase))
                {
                    return new SystemProxyTamperResult(
                        SystemProxyTamperKind.ChangedByExternal,
                        _expectedServer,
                        actualServer);
                }

                return SystemProxyTamperResult.Ok(_expectedServer);
            }
            catch
            {
                return SystemProxyTamperResult.Ok(_expectedServer);
            }
        }

        private static void CaptureOriginalSnapshotIfNeeded(RegistryKey key, string targetServer)
        {
            if (File.Exists(BackupFilePath))
                return;

            try
            {
                int currentEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
                object? rawServer = key.GetValue("ProxyServer");
                object? rawOverride = key.GetValue("ProxyOverride");
                string? currentServer = rawServer as string;
                string? currentOverride = rawOverride as string;

                if (currentEnable != 0 &&
                    string.Equals(currentServer?.Trim(), targetServer, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var snapshot = new ProxySnapshot
                {
                    ProxyEnable = currentEnable,
                    ProxyServer = currentServer,
                    ProxyOverride = currentOverride,
                    HadProxyServer = rawServer != null,
                    HadProxyOverride = rawOverride != null
                };

                string json = JsonSerializer.Serialize(snapshot, AnywhereWinUI.Models.AppJsonContext.Default.ProxySnapshot);
                LocalSettingsHelper.WriteAllTextAtomic(BackupFilePath, json);
            }
            catch
            {
                // Non-fatal: fallback to disabling proxy on exit
            }
        }

        private static ProxySnapshot? ConsumeBackupSnapshot()
        {
            if (!File.Exists(BackupFilePath))
                return null;

            try
            {
                string json = File.ReadAllText(BackupFilePath);
                File.Delete(BackupFilePath);
                return JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.ProxySnapshot);
            }
            catch
            {
                try { File.Delete(BackupFilePath); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Returns true if proxyServer points solely to loopback port(s) (127.0.0.1 / localhost / [::1],
        /// including scheme prefixes and multi-protocol "http=...;https=..." strings) with no active TCP listener.
        /// </summary>
        private static bool IsDeadLocalProxy(string? proxyServer)
        {
            if (string.IsNullOrWhiteSpace(proxyServer))
                return true;

            var segments = proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                return true;

            System.Net.IPEndPoint[]? listeners = null;
            bool checkedAnyLoopback = false;

            foreach (var rawSeg in segments)
            {
                string seg = rawSeg;
                int eqIdx = seg.IndexOf('=');
                if (eqIdx > 0)
                    seg = seg[(eqIdx + 1)..].Trim();

                int schemeIdx = seg.IndexOf("://", StringComparison.Ordinal);
                if (schemeIdx >= 0)
                    seg = seg[(schemeIdx + 3)..].Trim();

                int slashIdx = seg.IndexOf('/');
                if (slashIdx >= 0)
                    seg = seg[..slashIdx].Trim();

                string host;
                string portPart;
                if (seg.StartsWith('['))
                {
                    int closeBracket = seg.IndexOf(']');
                    if (closeBracket <= 0 || closeBracket + 2 > seg.Length || seg[closeBracket + 1] != ':')
                        return false;
                    host = seg[1..closeBracket];
                    portPart = seg[(closeBracket + 2)..];
                }
                else
                {
                    int colon = seg.LastIndexOf(':');
                    if (colon <= 0 || colon >= seg.Length - 1)
                        return false;
                    host = seg[..colon].Trim();
                    portPart = seg[(colon + 1)..].Trim();
                }

                bool isLoopback =
                    string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

                if (!isLoopback)
                    return false;

                if (!int.TryParse(portPart, out int port) || port <= 0 || port > 65535)
                    return false;

                checkedAnyLoopback = true;
                try
                {
                    listeners ??= IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                    foreach (var ep in listeners)
                    {
                        if (ep.Port == port)
                            return false; // At least one loopback endpoint is alive
                    }
                }
                catch
                {
                    return false;
                }
            }

            return checkedAnyLoopback;
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        private static extern void DnsFlushResolverCache();

        private static void NotifySystemOfProxyChange()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

            if (AppSession.Instance.FlushDNS)
            {
                try
                {
                    DnsFlushResolverCache();
                }
                catch { }
            }
        }
    }
}
