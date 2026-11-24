using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// Wraps a single Jint engine instance for one plugin.
    /// Each plugin runs in its own isolated JS sandbox with strict serialized execution.
    /// </summary>
    public sealed class PluginRuntime : IDisposable
    {
        private readonly Engine _engine;
        private readonly SemaphoreSlim _engineLock = new(1, 1);
        private int _disposedInt;
        private bool _initialized;

        private bool IsDisposed => Volatile.Read(ref _disposedInt) != 0;
        public PluginManifest Manifest { get; }
        public bool IsEnabled => !Manifest.Disabled && !IsDisposed;

        internal PluginRuntime(PluginManifest manifest)
        {
            Manifest = manifest;

            _engine = new Engine(cfg =>
            {
                // Hard limits to prevent runaway plugins and stack overflows
                cfg.LimitMemory(64 * 1024 * 1024);              // 64 MB per plugin
                cfg.TimeoutInterval(TimeSpan.FromSeconds(30));   // 30s timeout per call
                cfg.LimitRecursion(128);                         // Prevent CLR StackOverflowException
                cfg.MaxStatements(500_000);                      // Prevent runaway tight loops
                cfg.Strict();                                    // ES strict mode
            });

            var api = new PluginApi(manifest);
            _engine.SetValue("Plugin", api);
            _engine.SetValue("console", new JsConsoleShim(api));
        }

        public async Task InitAsync(string code)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            await _engineLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsDisposed) return;
                await Task.Run(() => _engine.Execute(code)).ConfigureAwait(false);
                _initialized = true;
            }
            finally
            {
                _engineLock.Release();
            }
        }

        public async Task<JsValue?> CallAsync(string functionName, params object[] args)
        {
            if (!_initialized || IsDisposed || Manifest.Disabled)
                return null;

            await _engineLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_initialized || IsDisposed || Manifest.Disabled)
                    return null;

                return await Task.Run(() =>
                {
                    var fn = _engine.GetValue(functionName);
                    if (fn.IsUndefined() || fn.IsNull() || fn.Type != Jint.Runtime.Types.Object)
                        return null;

                    var jsArgs = new JsValue[args.Length];
                    for (int i = 0; i < args.Length; i++)
                        jsArgs[i] = JsValue.FromObject(_engine, args[i]);

                    try
                    {
                        return (JsValue?)_engine.Invoke(fn, jsArgs);
                    }
                    catch (Jint.Runtime.JavaScriptException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _engineLock.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedInt, 1) != 0)
                return;

            // Never block the UI thread synchronously waiting for an active JS call, and do not dispose
            // _engineLock while queued async waiters may still be resuming!
            _ = DisposeEngineAsync();
        }

        private async Task DisposeEngineAsync()
        {
            await _engineLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _engine.Dispose();
            }
            catch { }
            finally
            {
                _engineLock.Release();
            }
        }
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    internal sealed class JsConsoleShim(PluginApi api)
    {
        public void Log(params object[] args) =>
            api.Log(string.Join(" ", args));

        public void Error(params object[] args) =>
            api.LogError(string.Join(" ", args));

        public void Warn(params object[] args) =>
            api.Log("[WARN] " + string.Join(" ", args));

        public void Info(params object[] args) =>
            api.Log(string.Join(" ", args));
    }
}
