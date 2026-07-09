using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Resolves as soon as sing-box is actually accepting connections on its
    /// mixed proxy port, replacing the old fixed Task.Delay(800) startup wait.
    ///
    /// Strategy: poll the sing-box mixed inbound port (TCP connect) every 100 ms.
    /// This is reliable regardless of the configured log level — even at "warn"
    /// level sing-box emits nothing on startup, so a log-line approach would
    /// always time out. A successful TCP connect is the authoritative proof
    /// that sing-box has bound its inbounds and is ready to serve traffic.
    ///
    /// Secondary triggers (belt-and-suspenders):
    ///   - any stdout/stderr output line   → Ready
    ///   - process exits before port opens → Exited
    ///
    /// Fallback: if the port never opens within <cap> (3 s default), TimedOut
    /// is returned and the caller proceeds — identical to the old behaviour.
    /// </summary>
    internal sealed class CoreReadySignal
    {
        public enum Outcome { Ready, Exited, TimedOut }

        private readonly TaskCompletionSource<Outcome> _outcome =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationTokenSource _cts = new();

        private CoreReadySignal() { }

        /// <summary>
        /// Wires this signal onto <paramref name="process"/> and begins polling
        /// <paramref name="mixedPort"/> for TCP connectivity every 100 ms.
        /// Call BEFORE <c>process.Start()</c>.
        /// </summary>
        public static CoreReadySignal Attach(Process process, int mixedPort)
        {
            var signal = new CoreReadySignal();

            // Secondary: any stdout/stderr line = process alive = ready
            process.OutputDataReceived += (_, e) => signal.OnLine(e.Data);
            process.ErrorDataReceived  += (_, e) => signal.OnLine(e.Data);

            // Exited: process crashed before port opened
            process.Exited += (_, _) =>
            {
                signal._cts.Cancel();
                signal._outcome.TrySetResult(Outcome.Exited);
            };

            // Primary: TCP port polling on a background task.
            // Wrapped in a top-level try/catch so any unexpected exception is
            // swallowed instead of becoming an unobserved Task exception.
            _ = Task.Run(async () =>
            {
                try { await signal.PollPortAsync(mixedPort).ConfigureAwait(false); }
                catch { /* intentionally swallowed */ }
            });

            return signal;
        }

        private void OnLine(string? line)
        {
            if (!string.IsNullOrEmpty(line))
                _outcome.TrySetResult(Outcome.Ready);
        }

        private async Task PollPortAsync(int port)
        {
            var ct = _cts.Token;
            await Task.Delay(100, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var tcp = new TcpClient();
                    // Pass ct directly to ConnectAsync so cancellation throws
                    // OperationCanceledException cleanly without leaving a
                    // dangling Task that produces an unobserved SocketException.
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
                    try { await Task.Delay(100, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        /// <summary>
        /// Awaits the first of: port open → Ready, process exit → Exited,
        /// or <paramref name="cap"/> elapsing → TimedOut.
        /// </summary>
        public async Task<Outcome> WaitAsync(TimeSpan cap, CancellationToken ct = default)
        {
            try
            {
                var result = await _outcome.Task.WaitAsync(cap, ct).ConfigureAwait(false);
                _cts.Cancel(); // stop polling once resolved
                return result;
            }
            catch (TimeoutException)
            {
                _cts.Cancel();
                return Outcome.TimedOut;
            }
        }
    }
}
