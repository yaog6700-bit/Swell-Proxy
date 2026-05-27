using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AnywhereWinUI.Services
{
    public static class SystemProxyManager
    {
        private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        /// <summary>
        /// Enable Windows System Proxy pointing to the specified mixed inbound host and port.
        /// </summary>
        public static bool EnableProxy(string host, int port)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true)!)
                {
                    if (key == null) return false;

                    key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", $"{host}:{port}", RegistryValueKind.String);
                    // Standard bypass rules for local subnetworks
                    key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>", RegistryValueKind.String);
                }

                NotifySystemOfProxyChange();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disable Windows System Proxy cleanly.
        /// </summary>
        public static bool DisableProxy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true)!)
                {
                    if (key == null) return false;

                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }

                NotifySystemOfProxyChange();
                return true;
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
