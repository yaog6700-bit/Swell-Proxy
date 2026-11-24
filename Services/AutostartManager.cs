using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
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
        /// HIGHEST Scheduled Task whenever the app is running with Administrator privileges,
        /// and self-heals existing tasks if the exe path moved or lacked battery/time-limit overrides.
        /// </summary>
        public static void UpgradeToElevatedTaskIfNeeded()
        {
            try
            {
                if (!AdminHelper.IsAdministrator()) return;

                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

                if (IsRegistryAutostartPresent())
                {
                    if (TryRegisterScheduledTask(exePath, allowUacPrompt: false))
                    {
                        DeleteRegistryAutostart();
                    }
                    return;
                }

                if (IsScheduledTaskPresent() && !IsScheduledTaskUpToDate(exePath))
                {
                    TryRegisterScheduledTask(exePath, allowUacPrompt: false);
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
                if (!proc.WaitForExit(2500))
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScheduledTaskUpToDate(string exePath)
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
                psi.ArgumentList.Add("/XML");

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                string xml = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(2500))
                {
                    try { proc.Kill(); } catch { }
                    return false;
                }
                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(xml))
                    return false;

                string escapedExe = SecurityElement.Escape(exePath) ?? exePath;
                return xml.Contains(escapedExe, StringComparison.OrdinalIgnoreCase) &&
                       xml.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", StringComparison.OrdinalIgnoreCase) &&
                       xml.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", StringComparison.OrdinalIgnoreCase) &&
                       xml.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildTaskDefinitionXml(string exePath)
        {
            using var identity = WindowsIdentity.GetCurrent();
            string userId = identity.User?.Value ?? identity.Name;
            string workingDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

            string safeUser = SecurityElement.Escape(userId) ?? userId;
            string safeCmd  = SecurityElement.Escape(exePath) ?? exePath;
            string safeArgs = SecurityElement.Escape(AutostartLaunchArgs) ?? AutostartLaunchArgs;
            string safeDir  = SecurityElement.Escape(workingDir) ?? workingDir;

            return $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <Triggers>
                    <LogonTrigger>
                      <Enabled>true</Enabled>
                      <UserId>{safeUser}</UserId>
                    </LogonTrigger>
                  </Triggers>
                  <Principals>
                    <Principal id="Author">
                      <UserId>{safeUser}</UserId>
                      <LogonType>InteractiveToken</LogonType>
                      <RunLevel>HighestAvailable</RunLevel>
                    </Principal>
                  </Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>7</Priority>
                  </Settings>
                  <Actions Context="Author">
                    <Exec>
                      <Command>{safeCmd}</Command>
                      <Arguments>{safeArgs}</Arguments>
                      <WorkingDirectory>{safeDir}</WorkingDirectory>
                    </Exec>
                  </Actions>
                </Task>
                """;
        }

        private static bool TryRegisterScheduledTask(string exePath, bool allowUacPrompt)
        {
            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"swell_autostart_{Guid.NewGuid():N}.xml");
            try
            {
                string xml = BuildTaskDefinitionXml(exePath);
                File.WriteAllText(tempXmlPath, xml, Encoding.Unicode);

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
                    psi.ArgumentList.Add("/XML");
                    psi.ArgumentList.Add(tempXmlPath);

                    using var proc = Process.Start(psi);
                    if (proc == null) return false;
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(); } catch { }
                        return false;
                    }
                    return proc.ExitCode == 0;
                }

                if (!allowUacPrompt) return false;

                var elevatedPsi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /F /TN \"{ScheduledTaskName}\" /XML \"{tempXmlPath}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var elevatedProc = Process.Start(elevatedPsi);
                if (elevatedProc == null) return false;
                if (!elevatedProc.WaitForExit(8000))
                {
                    try { elevatedProc.Kill(); } catch { }
                    return false;
                }
                return elevatedProc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath); } catch { }
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
