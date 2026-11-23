using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Manages startup registration with seamless UAC-free TUN support:
    /// - Standard mode: HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    /// - Elevated / TUN mode: Windows Scheduled Task ("SwellProxy_Autostart", ONLOGON + HIGHEST)
    ///   so TUN mode starts silently on boot without triggering a UAC prompt or getting blocked.
    /// </summary>
    public static class AutostartManager
    {
        private const string RegistryRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryKeyName = "SwellProxy";
        private const string ScheduledTaskName = "SwellProxy_Autostart";
        private const string AutostartLaunchArgs = "--autostart --auto-start-proxy";

        public static bool IsAutostartEnabled()
        {
            return IsRegistryAutostartPresent() || IsScheduledTaskPresent();
        }

        public static void EnableAutostart()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                bool wantsElevatedTask = AdminHelper.IsAdministrator() || AppSession.Instance.EnableTunMode;
                if (wantsElevatedTask && TryRegisterScheduledTask(exePath, allowUacPrompt: !AdminHelper.IsAdministrator()))
                {
                    DeleteRegistryAutostart();
                    return;
                }

                // Standard non-elevated Run key fallback
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunSubKey, writable: true);
                key?.SetValue(RegistryKeyName, $"\"{exePath}\" {AutostartLaunchArgs}");
            }
            catch { }
        }

        /// <summary>
        /// Automatically upgrades an existing standard HKCU Run autostart entry to a UAC-free
        /// HIGHEST Scheduled Task whenever the app is running with Administrator privileges.
        /// </summary>
        public static void UpgradeToElevatedTaskIfNeeded()
        {
            try
            {
                if (!AdminHelper.IsAdministrator()) return;
                if (!IsRegistryAutostartPresent()) return;

                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                if (TryRegisterScheduledTask(exePath, allowUacPrompt: false))
                {
                    DeleteRegistryAutostart();
                }
            }
            catch { }
        }

        public static void DisableAutostart()
        {
            DeleteRegistryAutostart();
            DeleteScheduledTask();
        }

        private static bool IsRegistryAutostartPresent()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunSubKey, writable: false);
                return key?.GetValue(RegistryKeyName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteRegistryAutostart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunSubKey, writable: true);
                key?.DeleteValue(RegistryKeyName, throwOnMissingValue: false);
            }
            catch { }
        }

        private static bool IsScheduledTaskPresent()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("/Query");
                psi.ArgumentList.Add("/TN");
                psi.ArgumentList.Add(ScheduledTaskName);

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(2500);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRegisterScheduledTask(string exePath, bool allowUacPrompt)
        {
            try
            {
                string trCommand = $"\"{exePath}\" {AutostartLaunchArgs}";

                if (AdminHelper.IsAdministrator())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    psi.ArgumentList.Add("/Create");
                    psi.ArgumentList.Add("/F");
                    psi.ArgumentList.Add("/TN");
                    psi.ArgumentList.Add(ScheduledTaskName);
                    psi.ArgumentList.Add("/TR");
                    psi.ArgumentList.Add(trCommand);
                    psi.ArgumentList.Add("/SC");
                    psi.ArgumentList.Add("ONLOGON");
                    psi.ArgumentList.Add("/RL");
                    psi.ArgumentList.Add("HIGHEST");
                    psi.ArgumentList.Add("/IT");

                    using var proc = Process.Start(psi);
                    if (proc == null) return false;
                    proc.WaitForExit(4000);
                    return proc.ExitCode == 0;
                }

                if (!allowUacPrompt) return false;

                string escapedTr = trCommand.Replace("\"", "\\\"");
                var elevatedPsi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /F /TN \"{ScheduledTaskName}\" /TR \"{escapedTr}\" /SC ONLOGON /RL HIGHEST /IT",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var elevatedProc = Process.Start(elevatedPsi);
                if (elevatedProc == null) return false;
                elevatedProc.WaitForExit(5000);
                return elevatedProc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteScheduledTask()
        {
            try
            {
                if (!IsScheduledTaskPresent()) return;

                if (AdminHelper.IsAdministrator())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add("/Delete");
                    psi.ArgumentList.Add("/F");
                    psi.ArgumentList.Add("/TN");
                    psi.ArgumentList.Add(ScheduledTaskName);
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                else
                {
                    var elevatedPsi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Delete /F /TN \"{ScheduledTaskName}\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var proc = Process.Start(elevatedPsi);
                    proc?.WaitForExit(5000);
                }
            }
            catch { }
        }
    }
}
