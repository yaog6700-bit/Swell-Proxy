using System;
using System.IO;
using System.Net.Http;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// The C# API surface exposed to every plugin's JavaScript sandbox.
    /// Plugins access this via the global <c>Plugin</c> variable injected by Jint.
    /// </summary>
    public class PluginApi
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly PluginManifest _manifest;
        private static readonly string _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy");

        internal PluginApi(PluginManifest manifest)
        {
            _manifest = manifest;
        }

        // ── Identity ────────────────────────────────────────────────

        /// <summary>Plugin's unique identifier.</summary>
        public string Id => _manifest.Id;

        /// <summary>Plugin's display name.</summary>
        public string Name => _manifest.Name;

        /// <summary>Plugin's version string.</summary>
        public string Version => _manifest.Version;

        // ── Logging ─────────────────────────────────────────────────

        /// <summary>Write an informational message to the app log.</summary>
        public void Log(string message) =>
            Services.CoreManager.Instance.AppendLog($"[Plugin:{_manifest.Name}] {message}");

        /// <summary>Write an error message to the app log.</summary>
        public void LogError(string message) =>
            Services.CoreManager.Instance.AppendLog($"[Plugin:{_manifest.Name}] ❌ {message}");

        // ── Configuration ───────────────────────────────────────────

        /// <summary>
        /// Read a plugin configuration value by key.
        /// Returns empty string if the key is not found.
        /// </summary>
        public string GetConfig(string key)
        {
            foreach (var item in _manifest.Configuration)
            {
                if (item.Key == key)
                    return item.Value;
            }
            return string.Empty;
        }

        // ── HTTP ────────────────────────────────────────────────────

        /// <summary>Perform a synchronous HTTP GET and return the response body as a string.</summary>
        public string HttpGet(string url)
        {
            try
            {
                return _http.GetStringAsync(url).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"HttpGet failed ({url}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Perform a synchronous HTTP POST.
        /// </summary>
        public string HttpPost(string url, string body, string contentType = "application/json")
        {
            try
            {
                using var content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
                var response = _http.PostAsync(url, content).GetAwaiter().GetResult();
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"HttpPost failed ({url}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Download a file directly to disk without loading it into string memory.
        /// Useful for large downloads like speedtest files or rule providers.
        /// </summary>
        public void DownloadFile(string url, string destPath)
        {
            try
            {
                var fullPath = ResolvePath(destPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var stream = _http.GetStreamAsync(url).GetAwaiter().GetResult();
                using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                LogError($"DownloadFile failed ({url}): {ex.Message}");
                throw;
            }
        }

        // ── File I/O ─────────────────────────────────────────────────

        /// <summary>
        /// Read a file. Path is relative to the SwellProxy data directory.
        /// Absolute paths are also accepted.
        /// </summary>
        public string ReadFile(string path)
        {
            var full = ResolvePath(path);
            return File.ReadAllText(full);
        }

        /// <summary>
        /// Write text to a file. Creates parent directories as needed.
        /// Path is relative to the SwellProxy data directory, or absolute.
        /// </summary>
        public void WriteFile(string path, string content)
        {
            var full = ResolvePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        /// <summary>Returns true if the specified file exists.</summary>
        public bool FileExists(string path) => File.Exists(ResolvePath(path));

        /// <summary>Delete a file if it exists.</summary>
        public void DeleteFile(string path)
        {
            var full = ResolvePath(path);
            if (File.Exists(full)) File.Delete(full);
        }

        // ── Notifications ────────────────────────────────────────────

        /// <summary>Show a Windows toast notification.</summary>
        public void Notify(string title, string message)
        {
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message);
                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch
            {
                // Notification may fail in some environments; silently ignore.
                Log($"Notification: {title} — {message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(_baseDir, path);
        }
    }
}
