using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnywhereWinUI.Helpers
{
    public static class LocalSettingsHelper
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "local_settings.json"
        );

        private static readonly object _sync = new();
        private static readonly Dictionary<string, object> _settings = new();
        private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

        static LocalSettingsHelper()
        {
            Load();
        }

        private static void Load()
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                        return;

                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json))
                        return;

                    var node = JsonNode.Parse(json) as JsonObject;
                    if (node != null)
                    {
                        _settings.Clear();
                        foreach (var kvp in node)
                        {
                            if (kvp.Value is JsonValue jv)
                            {
                                if (jv.TryGetValue<bool>(out var b)) _settings[kvp.Key] = b;
                                else if (jv.TryGetValue<long>(out var l)) _settings[kvp.Key] = l;
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
                catch
                {
                    BackupCorruptFileBestEffort(SettingsPath);
                }
            }
        }

        private static void SaveLocked()
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
                        node[kvp.Key] = s;
                    }
                    else node[kvp.Key] = kvp.Value?.ToString();
                }

                string json = node.ToJsonString(_writeOptions);
                WriteAllTextAtomic(SettingsPath, json);
            }
            catch { }
        }

        public static void SetValue(string key, object value)
        {
            lock (_sync)
            {
                _settings[key] = value;
                SaveLocked();
            }
        }

        public static void SetValues(IEnumerable<KeyValuePair<string, object>> values)
        {
            lock (_sync)
            {
                foreach (var kvp in values)
                {
                    _settings[kvp.Key] = kvp.Value;
                }
                SaveLocked();
            }
        }

        public static bool TryGetValue<T>(string key, out T? value)
        {
            object? rawVal;
            lock (_sync)
            {
                if (!_settings.TryGetValue(key, out rawVal))
                {
                    value = default;
                    return false;
                }
            }

            try
            {
                if (rawVal is T typedVal)
                {
                    value = typedVal;
                    return true;
                }

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
                if (typeof(T) == typeof(int))
                {
                    if (rawVal is long l && l is >= int.MinValue and <= int.MaxValue)
                    {
                        value = (T)(object)(int)l;
                        return true;
                    }
                    if (rawVal is string si && int.TryParse(si, out var ires))
                    {
                        value = (T)(object)ires;
                        return true;
                    }
                }
                if (typeof(T) == typeof(long))
                {
                    if (rawVal is int i)
                    {
                        value = (T)(object)(long)i;
                        return true;
                    }
                    if (rawVal is string sl && long.TryParse(sl, out var lres))
                    {
                        value = (T)(object)lres;
                        return true;
                    }
                }
                if (typeof(T) == typeof(double))
                {
                    if (rawVal is long dl)
                    {
                        value = (T)(object)(double)dl;
                        return true;
                    }
                    if (rawVal is int di)
                    {
                        value = (T)(object)(double)di;
                        return true;
                    }
                    if (rawVal is string sd && double.TryParse(sd, out var dres))
                    {
                        value = (T)(object)dres;
                        return true;
                    }
                }
            }
            catch { }

            value = default;
            return false;
        }

        internal static void WriteAllTextAtomic(string targetPath, string content)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    sw.Write(content);
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        internal static void BackupCorruptFileBestEffort(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length == 0) return;
                string bak = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
                File.Copy(path, bak, overwrite: true);
            }
            catch { }
        }
    }
}
