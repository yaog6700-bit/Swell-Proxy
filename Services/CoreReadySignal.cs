using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    /// <summary>
    /// Resolves as soon as sing-box produces any output on stdout/stderr,
    /// replacing the old fixed Task.Delay(800) startup wait.
    ///
    /// Strategy: trigger on the FIRST non-empty output line.
    /// sing-box outputs log/access lines almost immediately after binding its
    /// inbounds, so the first line is a reliable "process is alive" signal.
    /// If the process crashes it fires Exited instead, which the caller maps
    /// to a failure — same safety net as before, just without the blind wait.
    ///
    /// Fallback: if no output arrives within <cap> (3 s default), TimedOut is
    /// returned and the caller proceeds normally — identical to the old behaviour.
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
            // Trigger on the very first non-empty output line.
            // sing-box emits log/access lines within ~100 ms of binding inbounds,
            // regardless of the configured log level (warn/info/debug all produce output).
            // If the line happens to contain the canonical startup message we note it,
            // but we no longer rely on that specific string being present.
            if (!string.IsNullOrEmpty(line))
            {
                _outcome.TrySetResult(Outcome.Ready);
            }
        }

        /// <summary>
        /// Awaits the first of: any output line → Ready, process exit → Exited,
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
