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
        /// Cleanly disposes tray/mutex and exits once UAC elevation succeeds.
        /// Returns false if the user cancelled the UAC prompt.
        /// </summary>
        public static bool RestartAsAdmin(string extraArgs = "")
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            var currentPid = Environment.ProcessId;
            var arguments = string.IsNullOrWhiteSpace(extraArgs)
                ? $"--parent-pid={currentPid}"
                : $"{extraArgs} --parent-pid={currentPid}";

            try
            {
                var started = Process.Start(new ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = arguments,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb            = "runas"
                });
                started?.Dispose();

                // Only after UAC succeeds: clean up tray, proxy, and single-instance mutex before exiting
                try { Services.SystemProxyManager.DisableProxyIfOwned(); } catch { }
                try { MainWindow.Instance?.DisposeTrayForRestart(); } catch { }
                try { App.ReleaseSingleInstanceMutex(); } catch { }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(120);
                    Environment.Exit(0);
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
