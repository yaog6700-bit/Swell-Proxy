using System;
using System.Threading.Tasks;
using Jint;
using Jint.Native;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// Wraps a single Jint engine instance for one plugin.
    /// Each plugin runs in its own isolated JS sandbox.
    /// </summary>
    public sealed class PluginRuntime : IDisposable
    {
        private readonly Engine _engine;
        private bool _disposed;
        private bool _initialized;

        public PluginManifest Manifest { get; }
        public bool IsEnabled => !Manifest.Disabled && !_disposed;

        internal PluginRuntime(PluginManifest manifest)
        {
            Manifest = manifest;

            _engine = new Engine(cfg =>
            {
                // Hard limits to prevent runaway plugins
                cfg.LimitMemory(64 * 1024 * 1024);              // 64 MB per plugin
                cfg.TimeoutInterval(TimeSpan.FromSeconds(30));   // 30s timeout per call
                cfg.Strict();                                    // ES strict mode
            });

            // Inject the Plugin API object as a global variable
            var api = new PluginApi(manifest);
            _engine.SetValue("Plugin", api);

            // Provide a console shim so plugins can use console.log()
            _engine.SetValue("console", new JsConsoleShim(api));
        }

        /// <summary>
        /// Execute the plugin's source code to register its functions.
        /// Must be called before any trigger can fire.
        /// </summary>
        public async Task InitAsync(string code)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(() => _engine.Execute(code));
            _initialized = true;
        }

        /// <summary>
        /// Call a named JS function in the plugin.
        /// Returns the raw Jint value, or null if the function is not defined.
        /// </summary>
        public async Task<JsValue?> CallAsync(string functionName, params object[] args)
        {
            if (!_initialized || _disposed || Manifest.Disabled)
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
                    throw; // Re-throw JS errors so the caller can log them
                }
                catch (Exception)
                {
                    // fn was not actually callable (e.g. a plain object named OnFoo)
                    return null;
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.Dispose();
        }
    }

    /// <summary>Provides console.log / console.error for plugin developers.</summary>
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
