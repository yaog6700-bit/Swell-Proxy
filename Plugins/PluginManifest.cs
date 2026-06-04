using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// Metadata for a single plugin, persisted to plugins.json.
    /// </summary>
    public class PluginManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "v1.0.0";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        /// <summary>"File" = local .js file; "Http" = fetched from URL on update.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "File";

        /// <summary>Relative path under LocalAppData/SwellProxy/plugins/</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>Only used when Type == "Http".</summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>List of trigger names (must match PluginTrigger enum names).</summary>
        [JsonPropertyName("triggers")]
        public List<string> Triggers { get; set; } = [];

        [JsonPropertyName("disabled")]
        public bool Disabled { get; set; }

        [JsonPropertyName("configuration")]
        public List<PluginConfigItem> Configuration { get; set; } = [];

        // ── Runtime-only fields (not persisted) ──────────────────────
        [JsonIgnore] public bool IsUpdating { get; set; }
        [JsonIgnore] public string? LastError { get; set; }
    }

    public class PluginConfigItem
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        /// <summary>Stored as string; JS plugin reads via Plugin.GetConfig(key).</summary>
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
