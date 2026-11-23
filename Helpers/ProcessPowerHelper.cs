using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Manages Windows EcoQoS (Efficiency Mode / Power Throttling), process priority,
    /// and working-set trimming so the WinUI GUI host consumes near-zero CPU/RAM in tray
    /// while ensuring the proxy core (sing-box / shoes) always runs completely unthrottled.
    /// </summary>
    internal static class ProcessPowerHelper
    {
        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int ProcessInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
            int ProcessInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
        private static extern bool K32EmptyWorkingSet(IntPtr hProcess);

        public static void SetGuiHibernationMode(bool hibernate)
        {
            try
            {
                IntPtr hProc = GetCurrentProcess();

                using (var self = Process.GetCurrentProcess())
                {
                    self.PriorityClass = hibernate
                        ? ProcessPriorityClass.BelowNormal
                        : ProcessPriorityClass.Normal;
                }

                uint fullMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = fullMask,
                    StateMask = hibernate ? fullMask : 0u
                };

                if (!SetProcessInformation(hProc, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                {
                    // Older Windows 10 builds only support EXECUTION_SPEED (0x1)
                    state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    state.StateMask = hibernate ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u;
                    SetProcessInformation(hProc, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                }
            }
            catch
            {
                // Best effort on older OS versions
            }
        }

        /// <summary>
        /// Ensures child proxy core processes (sing-box / shoes) never inherit GUI EcoQoS
        /// or BelowNormal priority when launched or restarted while the GUI is hidden in tray.
        /// </summary>
        public static void EnsureCoreUnthrottled(Process? coreProcess)
        {
            if (coreProcess == null) return;
            try
            {
                if (coreProcess.HasExited) return;
                coreProcess.PriorityClass = ProcessPriorityClass.Normal;

                uint fullMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = fullMask,
                    StateMask = 0u
                };

                if (!SetProcessInformation(coreProcess.Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                {
                    state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    state.StateMask = 0u;
                    SetProcessInformation(coreProcess.Handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                }
            }
            catch { }
        }

        public static void TrimWorkingSet()
        {
            try
            {
                K32EmptyWorkingSet(GetCurrentProcess());
            }
            catch { }
        }
    }
}
