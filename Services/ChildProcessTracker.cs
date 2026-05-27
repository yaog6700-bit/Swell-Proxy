using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Binds child processes to a Windows Job Object.
    /// If the parent process crashes or is killed, all child processes in the Job Object are killed by the OS.
    /// </summary>
    public static class ChildProcessTracker
    {
        private static readonly IntPtr _jobHandle;

        static ChildProcessTracker()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);

                var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                };

                var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = info
                };

                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                    SetInformationJobObject(_jobHandle, 9, extendedInfoPtr, (uint)length); // 9 = JobObjectExtendedLimitInformation
                }
                finally
                {
                    Marshal.FreeHGlobal(extendedInfoPtr);
                }
            }
        }

        public static void AddProcess(Process process)
        {
            if (_jobHandle != IntPtr.Zero && !process.HasExited)
            {
                try
                {
                    AssignProcessToJobObject(_jobHandle, process.Handle);
                }
                catch
                {
                    // Ignore if process has already exited
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll")]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
