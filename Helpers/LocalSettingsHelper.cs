using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnywhereWinUI.Helpers
{
    public static class LocalSettingsHelper
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnywhereProxy",
            "local_settings.json"
        );

        private static Dictionary<string, object> _settings = new();

        static LocalSettingsHelper()
        {
            Load();
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var node = JsonNode.Parse(json) as JsonObject;
                    if (node != null)
                    {
                        _settings.Clear();
                        foreach (var kvp in node)
                        {
                            if (kvp.Value is JsonValue jv)
                            {
                                if (jv.TryGetValue<bool>(out var b)) _settings[kvp.Key] = b;
                                else if (jv.TryGetValue<long>(out var l)) _settings[kvp.Key] = (int)l; // Use long parsing to capture integers safely
                                else if (jv.TryGetValue<double>(out var d)) _settings[kvp.Key] = d;
                                else if (jv.TryGetValue<string>(out var s)) _settings[kvp.Key] = s;
                                else _settings[kvp.Key] = kvp.Value.ToJsonString();
                            }
                            else if (kvp.Value != null)
                            {
                                _settings[kvp.Key] = kvp.Value.ToJsonString();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                var node = new JsonObject();
                foreach (var kvp in _settings)
                {
                    if (kvp.Value is bool b) node[kvp.Key] = b;
                    else if (kvp.Value is int i) node[kvp.Key] = i;
                    else if (kvp.Value is long l) node[kvp.Key] = l;
                    else if (kvp.Value is double d) node[kvp.Key] = d;
                    else if (kvp.Value is string s)
                    {
                        // Some strings might be raw JSON arrays/objects from other serializers. Store them as string.
                        node[kvp.Key] = s;
                    }
                    else node[kvp.Key] = kvp.Value?.ToString();
                }
                
                string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static void SetValue(string key, object value)
        {
            _settings[key] = value;
            Save();
        }

        public static bool TryGetValue<T>(string key, out T? value)
        {
            if (_settings.TryGetValue(key, out var rawVal))
            {
                try
                {
                    if (rawVal is T typedVal)
                    {
                        value = typedVal;
                        return true;
                    }
                    
                    // Attempt fallback conversions for types stored differently (e.g. string to bool, int to long)
                    if (typeof(T) == typeof(string))
                    {
                        value = (T)(object)(rawVal?.ToString() ?? string.Empty);
                        return true;
                    }
                    if (typeof(T) == typeof(bool) && rawVal is string sb && bool.TryParse(sb, out var bres))
                    {
                        value = (T)(object)bres;
                        return true;
                    }
                    if (typeof(T) == typeof(int) && rawVal is long l)
                    {
                        value = (T)(object)(int)l;
                        return true;
                    }
                }
                catch { }
            }
            value = default;
            return false;
        }
    }
}
