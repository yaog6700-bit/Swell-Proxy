using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Resolves as soon as sing-box reports readiness via its stdout/stderr log stream,
    /// replacing the old fixed Task.Delay(800) startup wait.
    ///
    /// sing-box prints "sing-box started" (with elapsed time, e.g. "INFO[0000] sing-box started (0.33s)")
    /// once every inbound listener has successfully bound. This is the authoritative signal
    /// that the proxy is ready to accept connections.
    ///
    /// Degradation: if a future sing-box version rewords the line, WaitAsync returns TimedOut
    /// after the cap elapses — the caller treats that identically to the old fixed-delay behavior,
    /// so there is no regression even if the signal is never fired.
    /// </summary>
    internal sealed class CoreReadySignal
    {
        public enum Outcome { Ready, Exited, TimedOut }

        private readonly TaskCompletionSource<Outcome> _outcome =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private CoreReadySignal() { }

        /// <summary>
        /// Wires this signal onto <paramref name="process"/> by subscribing its own
        /// OutputDataReceived / ErrorDataReceived / Exited handlers. These are multicast
        /// delegates, so any existing handlers the caller already added are unaffected.
        /// Call after creating the process object but BEFORE calling Start().
        /// </summary>
        public static CoreReadySignal Attach(Process process)
        {
            var signal = new CoreReadySignal();
            // EnableRaisingEvents is already set by CoreManager; no need to set again here.
            process.OutputDataReceived += (_, e) => signal.OnLine(e.Data);
            process.ErrorDataReceived  += (_, e) => signal.OnLine(e.Data);
            process.Exited             += (_, _) => signal._outcome.TrySetResult(Outcome.Exited);
            return signal;
        }

        private void OnLine(string? line)
        {
            // Matches sing-box's startup confirmation line, e.g.:
            //   INFO[0000] sing-box started (0.33s)
            // The substring "sing-box started" is stable across known versions.
            if (line is not null &&
                line.Contains("sing-box started", StringComparison.OrdinalIgnoreCase))
            {
                _outcome.TrySetResult(Outcome.Ready);
            }
        }

        /// <summary>
        /// Awaits the first of: readiness line → Ready, process exit → Exited,
        /// or <paramref name="cap"/> elapsing → TimedOut.
        /// Cancellation propagates as <see cref="OperationCanceledException"/>.
        /// </summary>
        public async Task<Outcome> WaitAsync(TimeSpan cap, CancellationToken ct = default)
        {
            try
            {
                return await _outcome.Task.WaitAsync(cap, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return Outcome.TimedOut;
            }
        }
    }
}
