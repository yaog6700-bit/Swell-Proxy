using System;
using System.IO;
using Microsoft.Win32;

namespace AnywhereWinUI.Services
{
    public static class AutostartManager
    {
        private const string RegistryKeyName = "SwellProxy";

        public static bool IsAutostartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key == null) return false;
                    var value = key.GetValue(RegistryKeyName);
                    return value != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void EnableAutostart()
        {
            try
            {
                string exePath = Path.Combine(AppContext.BaseDirectory, "AnywhereWinUI.exe");
                if (!File.Exists(exePath)) return;

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    // Run with -silent or no arguments, window minimizes to tray by default or checks args
                    key.SetValue(RegistryKeyName, $"\"{exePath}\"");
                }
            }
            catch { }
        }

        public static void DisableAutostart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.DeleteValue(RegistryKeyName, false);
                }
            }
            catch { }
        }
    }
}
