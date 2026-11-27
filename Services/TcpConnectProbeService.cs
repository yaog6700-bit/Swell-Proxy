using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public sealed class TcpConnectProbeService
    {
        public async Task<LatencyProbeResult> ProbeAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                return new LatencyProbeResult
                {
                    Status = LatencyProbeStatus.Failed
                };
            }

            host = host.Trim();
            if (host.Length > 2 && host[0] == '[' && host[^1] == ']')
                host = host.Substring(1, host.Length - 2);
            if (string.IsNullOrWhiteSpace(host))
            {
                return new LatencyProbeResult { Status = LatencyProbeStatus.Failed };
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            try
            {
                // Pre-resolve domain BEFORE starting the stopwatch so DNS lookup latency (especially on
                // TTL=1s / multi-CNAME airport domains) is never conflated with actual TCP RTT!
                System.Net.IPAddress? targetIp = null;
                if (System.Net.IPAddress.TryParse(host, out var parsedIp))
                {
                    targetIp = parsedIp;
                }
                else
                {
                    var addrs = await System.Net.Dns.GetHostAddressesAsync(host, linkedCts.Token).ConfigureAwait(false);
                    foreach (var a in addrs)
                    {
                        if (a.AddressFamily == AddressFamily.InterNetwork)
                        {
                            targetIp = a;
                            break;
                        }
                        targetIp ??= a;
                    }
                    if (targetIp == null)
                    {
                        return new LatencyProbeResult { Status = LatencyProbeStatus.Failed };
                    }
                }

                using var client = new TcpClient(targetIp.AddressFamily);
                var stopwatch = Stopwatch.StartNew();
                await client.ConnectAsync(targetIp, port, linkedCts.Token).ConfigureAwait(false);
                stopwatch.Stop();
                try { client.LingerState = new LingerOption(true, 0); } catch { }

                return new LatencyProbeResult
                {
                    Status = LatencyProbeStatus.Success,
                    Milliseconds = Math.Max(1, (int)Math.Round(stopwatch.Elapsed.TotalMilliseconds))
                };
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new LatencyProbeResult
                {
                    Status = LatencyProbeStatus.Timeout
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException)
            {
                return new LatencyProbeResult
                {
                    Status = LatencyProbeStatus.Failed
                };
            }
            catch (Exception)
            {
                return new LatencyProbeResult
                {
                    Status = LatencyProbeStatus.Failed
                };
            }
        }
    }
}
