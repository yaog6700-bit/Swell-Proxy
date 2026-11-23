using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    internal sealed class CoreReadySignal : IDisposable
    {
        public enum Outcome { Ready, Exited, TimedOut }

        private readonly TaskCompletionSource<Outcome> _outcome =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        private CoreReadySignal() { }

        public static CoreReadySignal Attach(Process process, int mixedPort)
        {
            var signal = new CoreReadySignal();
            bool portAlreadyOccupied = mixedPort is > 0 and <= 65535 && IsLocalPortListening(mixedPort);

            DataReceivedEventHandler onOut = (_, e) => signal.OnLine(e.Data);
            EventHandler onExit = (_, _) =>
            {
                try { signal._cts.Cancel(); } catch { }
                signal._outcome.TrySetResult(Outcome.Exited);
            };

            process.OutputDataReceived += onOut;
            process.ErrorDataReceived  += onOut;
            process.Exited             += onExit;

            _ = Task.Run(async () =>
            {
                try { await signal.PollPortAsync(mixedPort, portAlreadyOccupied).ConfigureAwait(false); }
                catch { /* intentionally swallowed */ }
            });

            return signal;
        }

        private static bool IsLocalPortListening(int port)
        {
            try
            {
                foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                {
                    if (ep.Port == port) return true;
                }
            }
            catch { }
            return false;
        }

        private void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;

            if (line.Contains("sing-box started", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("shoes", StringComparison.OrdinalIgnoreCase) && line.Contains("listening", StringComparison.OrdinalIgnoreCase))
            {
                _outcome.TrySetResult(Outcome.Ready);
            }
        }

        private async Task PollPortAsync(int port, bool portAlreadyOccupied)
        {
            var ct = _cts.Token;
            if (port <= 0 || port > 65535 || portAlreadyOccupied)
            {
                // Either no TCP listen port (e.g. pure TUN custom config) OR the port is already occupied
                // by a foreign app (connecting to it would falsely report Ready before bind failure exits!).
                try
                {
                    await Task.Delay(750, ct).ConfigureAwait(false);
                    _outcome.TrySetResult(Outcome.Ready);
                }
                catch (OperationCanceledException) { }
                return;
            }

            await Task.Delay(30, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync("127.0.0.1", port, ct).ConfigureAwait(false);

                    _outcome.TrySetResult(Outcome.Ready);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    try { await Task.Delay(75, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        public async Task<Outcome> WaitAsync(TimeSpan cap, CancellationToken ct = default)
        {
            try
            {
                var result = await _outcome.Task.WaitAsync(cap, ct).ConfigureAwait(false);
                try { _cts.Cancel(); } catch { }
                return result;
            }
            catch (TimeoutException)
            {
                try { _cts.Cancel(); } catch { }
                return Outcome.TimedOut;
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts.Dispose(); } catch { }
        }
    }
}
