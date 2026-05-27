using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
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
                string json = JsonSerializer.Serialize(_settings);
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
                    if (rawVal is JsonElement je)
                    {
                        if (typeof(T) == typeof(bool)) value = (T)(object)je.GetBoolean();
                        else if (typeof(T) == typeof(string)) value = (T)(object)(je.GetString() ?? string.Empty);
                        else value = JsonSerializer.Deserialize<T>(je.GetRawText());
                        return true;
                    }
                    else if (rawVal is T typedVal)
                    {
                        value = typedVal;
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
