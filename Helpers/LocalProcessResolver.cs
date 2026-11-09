using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Lightweight Windows socket-to-process resolver inspired by bowel's internal/procinfo.
    /// Maps local TCP/UDP source ports to owning process executable names (e.g., "chrome.exe")
    /// using cached iphlpapi tables and reusable native buffers to avoid LOH/GC churn.
    /// </summary>
    public static class LocalProcessResolver
    {
        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;
        private const int UDP_TABLE_OWNER_PID = 1;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const long SnapshotTtlMs = 450;
        private const int MaxPidCacheEntries = 512;

        private static readonly object _sync = new();
        private static readonly Dictionary<ushort, uint> _tcpPortToPid = new(256);
        private static readonly Dictionary<ushort, uint> _udpPortToPid = new(128);
        private static readonly Dictionary<uint, string> _pidToProcessName = new(256);

        private static byte[] _nativeBuffer = new byte[16 * 1024];
        private static long _lastTcpRefreshTick;
        private static long _lastUdpRefreshTick;
        private static readonly uint _currentPid = (uint)Environment.ProcessId;

        public static string ResolveProcessName(string? coreProcess, string? coreProcessPath, string? network, string? sourcePortStr)
        {
            if (!string.IsNullOrWhiteSpace(coreProcess))
            {
                return ExtractShortProcessName(coreProcess);
            }

            if (!string.IsNullOrWhiteSpace(coreProcessPath))
            {
                return ExtractShortProcessName(coreProcessPath);
            }

            if (string.IsNullOrWhiteSpace(sourcePortStr) || !ushort.TryParse(sourcePortStr, out ushort localPort) || localPort == 0)
            {
                return string.Empty;
            }

            bool isUdp = string.Equals(network, "udp", StringComparison.OrdinalIgnoreCase);

            lock (_sync)
            {
                long now = Environment.TickCount64;
                if (isUdp)
                {
                    if (now - _lastUdpRefreshTick > SnapshotTtlMs)
                    {
                        RefreshUdpTableLocked();
                        _lastUdpRefreshTick = now;
                    }

                    if (_udpPortToPid.TryGetValue(localPort, out uint pid) && pid > 4)
                    {
                        return GetProcessNameByPidLocked(pid);
                    }
                }
                else
                {
                    if (now - _lastTcpRefreshTick > SnapshotTtlMs)
                    {
                        RefreshTcpTableLocked();
                        _lastTcpRefreshTick = now;
                    }

                    if (_tcpPortToPid.TryGetValue(localPort, out uint pid) && pid > 4)
                    {
                        return GetProcessNameByPidLocked(pid);
                    }
                }
            }

            return string.Empty;
        }

        public static void ClearCache()
        {
            lock (_sync)
            {
                _tcpPortToPid.Clear();
                _udpPortToPid.Clear();
                _pidToProcessName.Clear();
                _lastTcpRefreshTick = 0;
                _lastUdpRefreshTick = 0;
            }
        }

        private static string ExtractShortProcessName(string raw)
        {
            try
            {
                string trimmed = raw.Trim();
                int lastSlash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
                return lastSlash >= 0 && lastSlash + 1 < trimmed.Length
                    ? trimmed.Substring(lastSlash + 1)
                    : trimmed;
            }
            catch
            {
                return raw;
            }
        }

        private static void RefreshTcpTableLocked()
        {
            _tcpPortToPid.Clear();
            ReadTcpv4TableLocked();
            ReadTcpv6TableLocked();
        }

        private static void RefreshUdpTableLocked()
        {
            _udpPortToPid.Clear();
            ReadUdpv4TableLocked();
            ReadUdpv6TableLocked();
        }

        private static void ReadTcpv4TableLocked()
        {
            uint size = (uint)_nativeBuffer.Length;
            uint res = GetExtendedTcpTable(_nativeBuffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                EnsureBufferCapacityLocked((int)size);
                size = (uint)_nativeBuffer.Length;
                res = GetExtendedTcpTable(_nativeBuffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            }
            if (res != 0 || size < 4) return;

            int count = BitConverter.ToInt32(_nativeBuffer, 0);
            int offset = 4;
            const int rowSize = 24; // MIB_TCPROW_OWNER_PID: state(4), localAddr(4), localPort(4), remoteAddr(4), remotePort(4), owningPid(4)

            for (int i = 0; i < count && offset + rowSize <= size; i++, offset += rowSize)
            {
                uint rawLocalPort = BitConverter.ToUInt32(_nativeBuffer, offset + 8);
                ushort localPort = NetworkOrderToPort(rawLocalPort);
                uint pid = BitConverter.ToUInt32(_nativeBuffer, offset + 20);

                if (localPort > 0 && pid > 4)
                {
                    if (!_tcpPortToPid.TryGetValue(localPort, out uint existingPid) || existingPid == _currentPid)
                    {
                        _tcpPortToPid[localPort] = pid;
                    }
                }
            }
        }

        private static void ReadTcpv6TableLocked()
        {
            uint size = (uint)_nativeBuffer.Length;
            uint res = GetExtendedTcpTable(_nativeBuffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                EnsureBufferCapacityLocked((int)size);
                size = (uint)_nativeBuffer.Length;
                res = GetExtendedTcpTable(_nativeBuffer, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            }
            if (res != 0 || size < 4) return;

            int count = BitConverter.ToInt32(_nativeBuffer, 0);
            int offset = 4;
            const int rowSize = 56; // MIB_TCP6ROW_OWNER_PID: localAddr(16), localScope(4), localPort(4), remoteAddr(16), remoteScope(4), remotePort(4), state(4), owningPid(4)

            for (int i = 0; i < count && offset + rowSize <= size; i++, offset += rowSize)
            {
                uint rawLocalPort = BitConverter.ToUInt32(_nativeBuffer, offset + 20);
                ushort localPort = NetworkOrderToPort(rawLocalPort);
                uint pid = BitConverter.ToUInt32(_nativeBuffer, offset + 52);

                if (localPort > 0 && pid > 4)
                {
                    if (!_tcpPortToPid.TryGetValue(localPort, out uint existingPid) || existingPid == _currentPid)
                    {
                        _tcpPortToPid[localPort] = pid;
                    }
                }
            }
        }

        private static void ReadUdpv4TableLocked()
        {
            uint size = (uint)_nativeBuffer.Length;
            uint res = GetExtendedUdpTable(_nativeBuffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                EnsureBufferCapacityLocked((int)size);
                size = (uint)_nativeBuffer.Length;
                res = GetExtendedUdpTable(_nativeBuffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            }
            if (res != 0 || size < 4) return;

            int count = BitConverter.ToInt32(_nativeBuffer, 0);
            int offset = 4;
            const int rowSize = 12; // MIB_UDPROW_OWNER_PID: localAddr(4), localPort(4), owningPid(4)

            for (int i = 0; i < count && offset + rowSize <= size; i++, offset += rowSize)
            {
                uint rawLocalPort = BitConverter.ToUInt32(_nativeBuffer, offset + 4);
                ushort localPort = NetworkOrderToPort(rawLocalPort);
                uint pid = BitConverter.ToUInt32(_nativeBuffer, offset + 8);

                if (localPort > 0 && pid > 4)
                {
                    if (!_udpPortToPid.TryGetValue(localPort, out uint existingPid) || existingPid == _currentPid)
                    {
                        _udpPortToPid[localPort] = pid;
                    }
                }
            }
        }

        private static void ReadUdpv6TableLocked()
        {
            uint size = (uint)_nativeBuffer.Length;
            uint res = GetExtendedUdpTable(_nativeBuffer, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
            if (res == ERROR_INSUFFICIENT_BUFFER)
            {
                EnsureBufferCapacityLocked((int)size);
                size = (uint)_nativeBuffer.Length;
                res = GetExtendedUdpTable(_nativeBuffer, ref size, false, AF_INET6, UDP_TABLE_OWNER_PID, 0);
            }
            if (res != 0 || size < 4) return;

            int count = BitConverter.ToInt32(_nativeBuffer, 0);
            int offset = 4;
            const int rowSize = 28; // MIB_UDP6ROW_OWNER_PID: localAddr(16), localScope(4), localPort(4), owningPid(4)

            for (int i = 0; i < count && offset + rowSize <= size; i++, offset += rowSize)
            {
                uint rawLocalPort = BitConverter.ToUInt32(_nativeBuffer, offset + 20);
                ushort localPort = NetworkOrderToPort(rawLocalPort);
                uint pid = BitConverter.ToUInt32(_nativeBuffer, offset + 24);

                if (localPort > 0 && pid > 4)
                {
                    if (!_udpPortToPid.TryGetValue(localPort, out uint existingPid) || existingPid == _currentPid)
                    {
                        _udpPortToPid[localPort] = pid;
                    }
                }
            }
        }

        private static void EnsureBufferCapacityLocked(int requiredBytes)
        {
            int target = Math.Min(Math.Max(requiredBytes + 4096, _nativeBuffer.Length * 2), 80 * 1024); // Stay below 85KB LOH threshold when possible
            if (target < requiredBytes)
                target = requiredBytes;
            _nativeBuffer = new byte[target];
        }

        private static ushort NetworkOrderToPort(uint dwPort)
        {
            return (ushort)(((dwPort & 0xFF) << 8) | ((dwPort >> 8) & 0xFF));
        }

        private static string GetProcessNameByPidLocked(uint pid)
        {
            if (_pidToProcessName.TryGetValue(pid, out var cached))
            {
                return cached;
            }

            if (_pidToProcessName.Count >= MaxPidCacheEntries)
            {
                _pidToProcessName.Clear();
            }

            string resolved = QueryProcessFileName(pid);
            if (!string.IsNullOrEmpty(resolved))
            {
                _pidToProcessName[pid] = resolved;
            }
            return resolved;
        }

        private static unsafe string QueryProcessFileName(uint pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero)
                return string.Empty;

            try
            {
                char* buffer = stackalloc char[260];
                uint capacity = 260;
                if (QueryFullProcessImageNameW(hProcess, 0, buffer, ref capacity) && capacity > 0)
                {
                    ReadOnlySpan<char> fullPath = new ReadOnlySpan<char>(buffer, (int)capacity);
                    int slash = fullPath.LastIndexOfAny('\\', '/');
                    var fileName = slash >= 0 ? fullPath.Slice(slash + 1) : fullPath;
                    return fileName.ToString();
                }
            }
            catch
            {
                // Ignore protected system processes
            }
            finally
            {
                CloseHandle(hProcess);
            }

            return string.Empty;
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetExtendedTcpTable(
            [Out] byte[] pTcpTable,
            ref uint pdwSize,
            [MarshalAs(UnmanagedType.Bool)] bool bOrder,
            int ulAf,
            int TableClass,
            uint Reserved);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetExtendedUdpTable(
            [Out] byte[] pUdpTable,
            ref uint pdwSize,
            [MarshalAs(UnmanagedType.Bool)] bool bOrder,
            int ulAf,
            int TableClass,
            uint Reserved);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool QueryFullProcessImageNameW(
            IntPtr hProcess,
            uint dwFlags,
            char* lpExeName,
            ref uint lpdwSize);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}

