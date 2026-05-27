using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Utility for administrator privilege detection and UAC elevation restart.
    /// </summary>
    public static class AdminHelper
    {
        /// <summary>Returns true if the current process is running with Administrator privileges.</summary>
        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts the application as Administrator with an optional extra argument string.
        /// The current (non-elevated) process is killed ~800 ms after the new process starts,
        /// giving the elevated process enough time to acquire the single-instance mutex.
        /// Returns false if the user cancelled the UAC prompt.
        /// </summary>
        public static bool RestartAsAdmin(string extraArgs = "")
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var currentPid = Environment.ProcessId;
            // Pass parent PID so the elevated process can wait for us to exit if needed
            var arguments = string.IsNullOrWhiteSpace(extraArgs)
                ? $"--parent-pid={currentPid}"
                : $"{extraArgs} --parent-pid={currentPid}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = arguments,
                    UseShellExecute = true,
                    Verb            = "runas"
                });

                // Kill self after a short delay so the new elevated process can start cleanly
                _ = Task.Run(async () =>
                {
                    await Task.Delay(900);
                    try { Process.GetCurrentProcess().Kill(); }
                    catch { /* ignore */ }
                });

                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User clicked "No" on UAC dialog
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdminHelper] RestartAsAdmin failed: {ex.Message}");
                return false;
            }
        }
    }
}
