using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Jint.Native;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// Singleton that owns all plugin runtimes and dispatches lifecycle events.
    /// </summary>
    public sealed class PluginManager
    {
        // ── Singleton ────────────────────────────────────────────────
        private static readonly Lazy<PluginManager> _lazy = new(() => new PluginManager());
        public static PluginManager Instance => _lazy.Value;

        // ── Paths ────────────────────────────────────────────────────
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy");

        private static readonly string PluginsDir = Path.Combine(DataDir, "plugins");
        private static readonly string ManifestsPath = Path.Combine(DataDir, "plugins.json");

        // ── State ────────────────────────────────────────────────────
        private readonly Dictionary<string, PluginRuntime> _runtimes = [];

        public List<PluginManifest> Manifests { get; private set; } = [];

        private PluginManager() { }

        // ── Lifecycle ────────────────────────────────────────────────

        /// <summary>
        /// Called once at app startup. Loads manifests and initialises enabled plugins.
        /// </summary>
        public async Task LoadAllAsync()
        {
            Directory.CreateDirectory(PluginsDir);

            if (!File.Exists(ManifestsPath))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(ManifestsPath);
                var list = JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.ListPluginManifest);
                if (list != null) Manifests = list;
            }
            catch (Exception ex)
            {
                Log($"Failed to load plugins.json: {ex.Message}");
                return;
            }

            foreach (var manifest in Manifests)
            {
                if (!manifest.Disabled)
                    await LoadPluginAsync(manifest);
            }
        }

        /// <summary>Saves the current manifest list to disk.</summary>
        public async Task SaveAsync()
        {
            var ctx = new AnywhereWinUI.Models.AppJsonContext(
                new JsonSerializerOptions { WriteIndented = true });
            var json = JsonSerializer.Serialize(Manifests, ctx.ListPluginManifest);
            await File.WriteAllTextAsync(ManifestsPath, json);
        }

        // ── CRUD ─────────────────────────────────────────────────────

        /// <summary>Add a new plugin and load it immediately if enabled.</summary>
        public async Task AddPluginAsync(PluginManifest manifest)
        {
            Manifests.Add(manifest);
            if (!manifest.Disabled)
                await LoadPluginAsync(manifest);
            await SaveAsync();
        }

        /// <summary>Enable or disable a plugin (hot-toggle).</summary>
        public async Task SetEnabledAsync(string id, bool enabled)
        {
            var manifest = Manifests.Find(m => m.Id == id);
            if (manifest == null) return;

            manifest.Disabled = !enabled;

            if (enabled)
                await LoadPluginAsync(manifest);
            else
                UnloadPlugin(id);

            await SaveAsync();
        }

        /// <summary>Remove a plugin, unloading it first.</summary>
        public async Task RemovePluginAsync(string id)
        {
            UnloadPlugin(id);

            // Save the manifest reference BEFORE removing it from the list,
            // otherwise the Find below will always return null.
            var manifest = Manifests.Find(m => m.Id == id);
            Manifests.RemoveAll(m => m.Id == id);

            // Optionally delete the js file if it lives in our plugins dir
            if (manifest != null && manifest.Path.StartsWith("plugins/"))
            {
                var fullPath = Path.Combine(DataDir, manifest.Path.Replace('/', Path.DirectorySeparatorChar));
                try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { }
            }

            await SaveAsync();
        }

        /// <summary>
        /// Download/refresh the plugin code from its URL (Type == "Http").
        /// Then hot-reload the runtime.
        /// </summary>
        public async Task UpdatePluginAsync(string id)
        {
            var manifest = Manifests.Find(m => m.Id == id);
            if (manifest == null || manifest.Type != "Http" || string.IsNullOrEmpty(manifest.Url))
                return;

            manifest.IsUpdating = true;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var code = await http.GetStringAsync(manifest.Url);
                var fullPath = Path.Combine(DataDir, manifest.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, code);

                // Hot-reload
                UnloadPlugin(id);
                if (!manifest.Disabled)
                    await LoadPluginAsync(manifest);
            }
            finally
            {
                manifest.IsUpdating = false;
            }
        }

        // ── Trigger Dispatch ─────────────────────────────────────────

        /// <summary>
        /// Fire a simple trigger for all subscribed plugins.
        /// Any plugin errors are caught and logged, not re-thrown.
        /// </summary>
        public async Task FireAsync(PluginTrigger trigger)
        {
            var fnName = trigger.ToString();
            foreach (var (id, runtime) in _runtimes)
            {
                if (!runtime.IsEnabled) continue;
                if (!runtime.Manifest.Triggers.Contains(fnName)) continue;

                try
                {
                    await runtime.CallAsync(fnName);
                }
                catch (Exception ex)
                {
                    Log($"[{runtime.Manifest.Name}] {fnName} error: {ex.Message}");
                    runtime.Manifest.LastError = ex.Message;
                }
            }
        }

        /// <summary>
        /// OnBeforeCoreStart trigger: plugins receive the JSON config and may return
        /// a modified JSON string. If a plugin returns null/undefined, the previous
        /// config is kept.
        /// </summary>
        public async Task<string> FireBeforeCoreStartAsync(string configJson)
        {
            const string fnName = "OnBeforeCoreStart";
            foreach (var (_, runtime) in _runtimes)
            {
                if (!runtime.IsEnabled) continue;
                if (!runtime.Manifest.Triggers.Contains(fnName)) continue;

                try
                {
                    var result = await runtime.CallAsync(fnName, configJson);
                    if (result != null && result.Type != Jint.Runtime.Types.Null && result.Type != Jint.Runtime.Types.Undefined)
                    {
                        var returned = result.ToString();
                        if (!string.IsNullOrWhiteSpace(returned))
                            configJson = returned;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{runtime.Manifest.Name}] OnBeforeCoreStart error: {ex.Message}");
                    runtime.Manifest.LastError = ex.Message;
                }
            }
            return configJson;
        }

        /// <summary>
        /// OnSubscribe trigger: plugins receive serialised node array (JSON string)
        /// and may return a filtered/modified JSON string.
        /// </summary>
        public async Task<string> FireSubscribeAsync(string nodesJson, string subscriptionName)
        {
            const string fnName = "OnSubscribe";
            foreach (var (_, runtime) in _runtimes)
            {
                if (!runtime.IsEnabled) continue;
                if (!runtime.Manifest.Triggers.Contains(fnName)) continue;

                try
                {
                    var result = await runtime.CallAsync(fnName, nodesJson, subscriptionName);
                    if (result != null && result.Type != Jint.Runtime.Types.Null && result.Type != Jint.Runtime.Types.Undefined)
                    {
                        var returned = result.ToString();
                        if (!string.IsNullOrWhiteSpace(returned))
                            nodesJson = returned;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{runtime.Manifest.Name}] OnSubscribe error: {ex.Message}");
                    runtime.Manifest.LastError = ex.Message;
                }
            }
            return nodesJson;
        }

        /// <summary>
        /// Manually trigger a specific plugin's OnManual function.
        /// </summary>
        public async Task ManualTriggerAsync(string id)
        {
            if (!_runtimes.TryGetValue(id, out var runtime))
            {
                Log($"Plugin {id} is not loaded.");
                return;
            }
            try
            {
                await runtime.CallAsync(PluginTrigger.OnManual.ToString());
            }
            catch (Exception ex)
            {
                Log($"[{runtime.Manifest.Name}] OnManual error: {ex.Message}");
                runtime.Manifest.LastError = ex.Message;
            }
        }

        // ── Internal Helpers ─────────────────────────────────────────

        private async Task LoadPluginAsync(PluginManifest manifest)
        {
            try
            {
                var fullPath = Path.Combine(DataDir, manifest.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    Log($"Plugin file not found: {fullPath}");
                    manifest.LastError = "Plugin file not found.";
                    return;
                }

                var code = await File.ReadAllTextAsync(fullPath);
                var runtime = new PluginRuntime(manifest);
                await runtime.InitAsync(code);

                _runtimes[manifest.Id] = runtime;
                manifest.LastError = null;
                Log($"Loaded plugin: {manifest.Name} {manifest.Version}");
            }
            catch (Exception ex)
            {
                Log($"Failed to load plugin [{manifest.Name}]: {ex.Message}");
                manifest.LastError = ex.Message;
            }
        }

        private void UnloadPlugin(string id)
        {
            if (_runtimes.TryGetValue(id, out var runtime))
            {
                runtime.Dispose();
                _runtimes.Remove(id);
            }
        }

        private static void Log(string message) =>
            Services.CoreManager.Instance.AppendLog($"[PluginManager] {message}");
    }
}
