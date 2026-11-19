using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
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

        private sealed class ProxySnapshot
        {
            public int ProxyEnable { get; set; }
            public string? ProxyServer { get; set; }
            public string? ProxyOverride { get; set; }
        }

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        /// <summary>
        /// Enable Windows System Proxy pointing to the specified mixed inbound host and port.
        /// Captures a pre-takeover snapshot of the user's original proxy settings (if not already captured)
        /// so DisableProxy can cleanly restore them without touching AutoConfigURL (PAC).
        /// </summary>
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
                        // Standard bypass rules for local subnetworks; intentionally leaves AutoConfigURL untouched
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

        /// <summary>
        /// Re-applies the expected system proxy without overwriting the original pre-takeover backup snapshot.
        /// </summary>
        public static bool RetakeProxy()
        {
            if (!_expectedActive || string.IsNullOrWhiteSpace(_expectedHost) || _expectedPort <= 0)
                return false;

            return EnableProxy(_expectedHost, _expectedPort);
        }

        /// <summary>
        /// Disable Windows System Proxy cleanly, restoring the user's original pre-takeover proxy snapshot
        /// unless that original proxy points to a dead local port.
        /// </summary>
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
                        }
                        else
                        {
                            key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                            if (snapshot?.ProxyOverride != null)
                            {
                                key.SetValue("ProxyOverride", snapshot.ProxyOverride, RegistryValueKind.String);
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

        /// <summary>
        /// Inspects live Windows Internet Settings to detect whether an external program
        /// (e.g., game booster, VPN, or another proxy) disabled or overwrote our system proxy.
        /// </summary>
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
                string? currentServer = key.GetValue("ProxyServer") as string;
                string? currentOverride = key.GetValue("ProxyOverride") as string;

                // Don't snapshot our own proxy server as the "original" setting.
                if (currentEnable != 0 &&
                    string.Equals(currentServer?.Trim(), targetServer, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var snapshot = new ProxySnapshot
                {
                    ProxyEnable = currentEnable,
                    ProxyServer = currentServer,
                    ProxyOverride = currentOverride
                };

                string? dir = Path.GetDirectoryName(BackupFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(snapshot));
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
                return JsonSerializer.Deserialize<ProxySnapshot>(json);
            }
            catch
            {
                try { File.Delete(BackupFilePath); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Returns true if proxyServer points to a loopback address (127.0.0.1 / localhost)
        /// whose TCP port has no active listener (e.g., leftover from a crashed proxy).
        /// </summary>
        private static bool IsDeadLocalProxy(string? proxyServer)
        {
            if (string.IsNullOrWhiteSpace(proxyServer))
                return true;

            string trimmed = proxyServer.Trim();
            // Handle simple "host:port" form
            int colon = trimmed.LastIndexOf(':');
            if (colon <= 0 || colon >= trimmed.Length - 1)
                return false;

            string host = trimmed[..colon].Trim();
            if (!string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(trimmed[(colon + 1)..], out int port) || port <= 0 || port > 65535)
                return false;

            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                foreach (var ep in listeners)
                {
                    if (ep.Port == port)
                        return false; // Alive
                }
                return true; // Dead local port
            }
            catch
            {
                return false;
            }
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        private static extern void DnsFlushResolverCache();

        /// <summary>
        /// Notify active applications and browsers that the internet configuration has been updated.
        /// </summary>
        private static void NotifySystemOfProxyChange()
        {
            // Send settings changed option
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            // Refresh internet options cache
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

            // Flush DNS Cache natively (if enabled in settings) to prevent DNS leak / stale entries
            if (AppSession.Instance.FlushDNS)
            {
                try
                {
                    DnsFlushResolverCache();
                }
                catch
                {
                    // Ignore P/Invoke exceptions on unsupported platforms
                }
            }
        }
    }
}
