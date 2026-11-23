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
        private readonly object _sync = new();
        private readonly Dictionary<string, PluginRuntime> _runtimes = [];

        public List<PluginManifest> Manifests { get; private set; } = [];

        private List<PluginRuntime> SnapshotRuntimes()
        {
            lock (_sync)
            {
                return new List<PluginRuntime>(_runtimes.Values);
            }
        }

        private static bool TryResolveSafePluginPath(string? relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                return false;

            string fullBase = Path.GetFullPath(PluginsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(DataDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = candidate;
            return true;
        }

        private PluginManager() { }

        // ── Lifecycle ────────────────────────────────────────────────

        /// <summary>
        /// Called once at app startup. Loads manifests and initialises enabled plugins.
        /// On first run (no plugins.json), seeds bundled example plugins from Assets/plugins/.
        /// </summary>
        public async Task LoadAllAsync()
        {
            Directory.CreateDirectory(PluginsDir);

            // ── First-run: seed built-in plugins from Assets/plugins/ ────
            if (!File.Exists(ManifestsPath))
            {
                await SeedBuiltInPluginsAsync();
            }

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

        /// <summary>
        /// Scans the bundled Assets/plugins/ directory for .js files,
        /// detects triggers from function definitions, copies them to the
        /// runtime plugins directory, and creates the initial plugins.json.
        /// </summary>
        private async Task SeedBuiltInPluginsAsync()
        {
            try
            {
                var assetsPluginsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "plugins");
                if (!Directory.Exists(assetsPluginsDir))
                    return;

                var jsFiles = Directory.GetFiles(assetsPluginsDir, "*.js");
                if (jsFiles.Length == 0)
                    return;

                var seeded = new List<PluginManifest>();

                foreach (var srcFile in jsFiles)
                {
                    var fileName = Path.GetFileName(srcFile);
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var destFile = Path.Combine(PluginsDir, fileName);

                    // Copy to runtime directory
                    File.Copy(srcFile, destFile, overwrite: true);

                    // Read source and detect triggers from function definitions
                    var code = await File.ReadAllTextAsync(srcFile);
                    var triggers = DetectTriggersFromCode(code);

                    var manifest = new PluginManifest
                    {
                        Id = baseName + "_" + Guid.NewGuid().ToString("N")[..6],
                        Name = baseName,
                        Type = "File",
                        Path = $"plugins/{fileName}",
                        Triggers = triggers,
                        Disabled = false
                    };

                    seeded.Add(manifest);
                    Log($"Seeded built-in plugin: {baseName} (triggers: {string.Join(", ", triggers)})");
                }

                if (seeded.Count > 0)
                {
                    Manifests = seeded;
                    await SaveAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to seed built-in plugins: {ex.Message}");
            }
        }

        /// <summary>
        /// Scans JavaScript source code for global function definitions that match
        /// known trigger names (e.g. "function OnManual()" → "OnManual").
        /// </summary>
        public static List<string> DetectTriggersFromCode(string jsCode)
        {
            var triggers = new List<string>();
            var knownTriggers = Enum.GetNames(typeof(PluginTrigger));

            // Strip /* ... */ block comments and // line comments so commented-out functions aren't matched
            string stripped = System.Text.RegularExpressions.Regex.Replace(
                jsCode ?? string.Empty,
                @"/\*[\s\S]*?\*/|//[^\r\n]*",
                string.Empty);

            foreach (var name in knownTriggers)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(
                    stripped, $@"\bfunction\s+{name}\s*\("))
                {
                    triggers.Add(name);
                }
            }

            if (triggers.Count == 0)
                triggers.Add("OnManual");

            return triggers;
        }

        /// <summary>
        /// Scans JavaScript source code for Plugin.GetConfig("key") calls so the visual settings
        /// dialog can automatically surface every script parameter even if plugins.json omitted it.
        /// </summary>
        public static List<string> DetectConfigKeysFromCode(string jsCode)
        {
            var keys = new List<string>();
            if (string.IsNullOrWhiteSpace(jsCode)) return keys;

            string stripped = System.Text.RegularExpressions.Regex.Replace(
                jsCode,
                @"/\*[\s\S]*?\*/|//[^\r\n]*",
                string.Empty);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var matches = System.Text.RegularExpressions.Regex.Matches(
                stripped,
                @"Plugin\s*\.\s*GetConfig\s*\(\s*[""']([^""']+)[""']\s*\)");

            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string key = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(key) && seen.Add(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        public async Task<List<string>> GetDetectedConfigKeysAsync(PluginManifest manifest)
        {
            if (manifest == null || !TryResolveSafePluginPath(manifest.Path, out var fullPath) || !File.Exists(fullPath))
                return [];

            try
            {
                var code = await File.ReadAllTextAsync(fullPath);
                return DetectConfigKeysFromCode(code);
            }
            catch
            {
                return [];
            }
        }

        public async Task UpdateConfigurationAsync(string id, List<PluginConfigItem> items)
        {
            PluginManifest? manifest;
            lock (_sync)
            {
                manifest = Manifests.Find(m => m.Id == id);
                if (manifest == null) return;
                manifest.Configuration = items ?? [];
            }

            if (!manifest.Disabled)
            {
                await LoadPluginAsync(manifest);
            }

            await SaveAsync();
        }

        public Task SaveAsync()
        {
            List<PluginManifest> snapshot;
            lock (_sync)
            {
                snapshot = new List<PluginManifest>(Manifests);
            }
            var ctx = new AnywhereWinUI.Models.AppJsonContext(
                new JsonSerializerOptions { WriteIndented = true });
            var json = JsonSerializer.Serialize(snapshot, ctx.ListPluginManifest);
            Helpers.LocalSettingsHelper.WriteAllTextAtomic(ManifestsPath, json);
            return Task.CompletedTask;
        }

        // ── CRUD ─────────────────────────────────────────────────────

        public async Task AddPluginAsync(PluginManifest manifest)
        {
            lock (_sync)
            {
                Manifests.Add(manifest);
            }
            if (!manifest.Disabled)
                await LoadPluginAsync(manifest);
            await SaveAsync();
        }

        public async Task SetEnabledAsync(string id, bool enabled)
        {
            PluginManifest? manifest;
            lock (_sync)
            {
                manifest = Manifests.Find(m => m.Id == id);
                if (manifest == null) return;
                manifest.Disabled = !enabled;
            }

            if (enabled)
                await LoadPluginAsync(manifest);
            else
                UnloadPlugin(id);

            await SaveAsync();
        }

        public async Task RemovePluginAsync(string id)
        {
            UnloadPlugin(id);

            PluginManifest? manifest;
            lock (_sync)
            {
                manifest = Manifests.Find(m => m.Id == id);
                Manifests.RemoveAll(m => m.Id == id);
            }

            if (manifest != null && TryResolveSafePluginPath(manifest.Path, out var fullPath))
            {
                try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { }
            }

            await SaveAsync();
        }

        public async Task UpdatePluginAsync(string id)
        {
            PluginManifest? manifest;
            lock (_sync)
            {
                manifest = Manifests.Find(m => m.Id == id);
            }
            if (manifest == null || manifest.Type != "Http" || string.IsNullOrEmpty(manifest.Url))
                return;

            if (!TryResolveSafePluginPath(manifest.Path, out var fullPath))
            {
                manifest.LastError = "Invalid plugin file path.";
                return;
            }

            manifest.IsUpdating = true;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
                var code = await http.GetStringAsync(manifest.Url);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                Helpers.LocalSettingsHelper.WriteAllTextAtomic(fullPath, code);

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

        public async Task FireAsync(PluginTrigger trigger)
        {
            if (!Services.AppSession.Instance.EnablePlugins)
                return;

            var fnName = trigger.ToString();
            foreach (var runtime in SnapshotRuntimes())
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

        public async Task<string> FireBeforeCoreStartAsync(string configJson)
        {
            if (!Services.AppSession.Instance.EnablePlugins)
                return configJson;

            const string fnName = "OnBeforeCoreStart";
            foreach (var runtime in SnapshotRuntimes())
            {
                if (!runtime.IsEnabled) continue;
                if (!runtime.Manifest.Triggers.Contains(fnName)) continue;

                try
                {
                    var result = await runtime.CallAsync(fnName, configJson);
                    if (result != null && result.Type == Jint.Runtime.Types.String)
                    {
                        var returned = result.ToString();
                        if (!string.IsNullOrWhiteSpace(returned) && IsValidJsonObject(returned))
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

        public async Task<string> FireSubscribeAsync(string nodesJson, string subscriptionName)
        {
            if (!Services.AppSession.Instance.EnablePlugins)
                return nodesJson;

            const string fnName = "OnSubscribe";
            foreach (var runtime in SnapshotRuntimes())
            {
                if (!runtime.IsEnabled) continue;
                if (!runtime.Manifest.Triggers.Contains(fnName)) continue;

                try
                {
                    var result = await runtime.CallAsync(fnName, nodesJson, subscriptionName);
                    if (result != null && result.Type == Jint.Runtime.Types.String)
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

        public async Task ManualTriggerAsync(string id)
        {
            PluginRuntime? runtime;
            lock (_sync)
            {
                _runtimes.TryGetValue(id, out runtime);
            }
            if (runtime == null)
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

        private static bool IsValidJsonObject(string candidate)
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadPluginAsync(PluginManifest manifest)
        {
            try
            {
                if (!TryResolveSafePluginPath(manifest.Path, out var fullPath) || !File.Exists(fullPath))
                {
                    Log($"Plugin file not found or invalid path: {manifest.Path}");
                    manifest.LastError = "Plugin file not found.";
                    return;
                }

                var code = await File.ReadAllTextAsync(fullPath);
                var runtime = new PluginRuntime(manifest);
                await runtime.InitAsync(code);

                PluginRuntime? previous = null;
                lock (_sync)
                {
                    _runtimes.TryGetValue(manifest.Id, out previous);
                    _runtimes[manifest.Id] = runtime;
                }
                previous?.Dispose();

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
            PluginRuntime? runtime = null;
            lock (_sync)
            {
                if (_runtimes.TryGetValue(id, out runtime))
                {
                    _runtimes.Remove(id);
                }
            }
            runtime?.Dispose();
        }

        private static void Log(string message) =>
            Services.CoreManager.Instance.AppendLog($"[PluginManager] {message}");
    }
}
