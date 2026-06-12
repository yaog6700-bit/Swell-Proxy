using System;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public enum LatencyProbeStatus
    {
        Success,
        Timeout,
        Failed
    }

    public sealed class LatencyProbeResult
    {
        public LatencyProbeStatus Status { get; init; }
        public int? Milliseconds { get; init; }
    }

    public sealed class LatencyProbeService
    {
        private readonly TcpConnectProbeService _tcpConnectProbe;
        private readonly PingProbeService _pingProbe;

        public LatencyProbeService(
            TcpConnectProbeService tcpConnectProbe,
            PingProbeService pingProbe)
        {
            _tcpConnectProbe = tcpConnectProbe;
            _pingProbe = pingProbe;
        }

        public LatencyProbeService()
        {
            _tcpConnectProbe = new TcpConnectProbeService();
            _pingProbe = new PingProbeService();
        }

        public async Task<LatencyProbeResult> ProbeAsync(
            string protocol,
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return new LatencyProbeResult { Status = LatencyProbeStatus.Failed };
            }

            // UDP/QUIC 协议（TUIC、Hysteria2 等）无法直接用 UDP 探测延迟。
            // 策略：优先尝试 TCP connect（大多数服务器同端口也响应 TCP）；
            //       若 TCP 失败/超时则 fallback 到 ICMP Ping 兜底。
            var isUdpBased = string.Equals(protocol, "hysteria2", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(protocol, "tuic",      StringComparison.OrdinalIgnoreCase)
                          || string.Equals(protocol, "hysteria 2", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(protocol, "hysteria",  StringComparison.OrdinalIgnoreCase)
                          || string.Equals(protocol, "nowhere",   StringComparison.OrdinalIgnoreCase);

            if (!isUdpBased)
            {
                // TCP 协议直接 TCP connect
                return await _tcpConnectProbe.ProbeAsync(host, port, timeout, cancellationToken);
            }

            // UDP 类协议：先用 TCP connect，失败后 fallback 到 ICMP Ping
            var tcpResult = await _tcpConnectProbe.ProbeAsync(host, port, timeout, cancellationToken);
            if (tcpResult.Status == LatencyProbeStatus.Success)
            {
                return tcpResult;
            }

            // TCP 探测失败，尝试 ICMP Ping
            var pingResult = await _pingProbe.ProbeAsync(host, timeout, cancellationToken);
            return pingResult;
        }
    }
}
