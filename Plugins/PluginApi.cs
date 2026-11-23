using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AnywhereWinUI.Plugins
{
    /// <summary>
    /// The C# API surface exposed to every plugin's JavaScript sandbox.
    /// Marked with DynamicallyAccessedMembers so ILLink trimming keeps all JS-invoked methods/properties.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    public class PluginApi
    {
        private const long MaxTextResponseBytes = 8 * 1024 * 1024;       // 8 MB max text body
        private const long MaxDownloadFileBytes = 200L * 1024 * 1024;    // 200 MB max download file

        private static readonly HttpClient _http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = MaxTextResponseBytes
        };

        private static readonly HashSet<string> _blockedWriteExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".wsf", ".msi", ".scr", ".com", ".js"
        };

        private readonly PluginManifest _manifest;
        private static readonly string _baseDir = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy"));
        private static readonly string _pluginsJailDir = Path.GetFullPath(Path.Combine(_baseDir, "plugins"));
        private static readonly string _pluginsJailPrefix = _pluginsJailDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        internal PluginApi(PluginManifest manifest)
        {
            _manifest = manifest;
        }

        // ── Identity ────────────────────────────────────────────────

        public string Id => _manifest.Id;
        public string Name => _manifest.Name;
        public string Version => _manifest.Version;

        // ── Logging ─────────────────────────────────────────────────

        public void Log(string message) =>
            Services.CoreManager.Instance.AppendLog($"[Plugin:{_manifest.Name}] {message}");

        public void LogError(string message) =>
            Services.CoreManager.Instance.AppendLog($"[Plugin:{_manifest.Name}] ❌ {message}");

        // ── Configuration ───────────────────────────────────────────

        public string GetConfig(string key)
        {
            foreach (var item in _manifest.Configuration)
            {
                if (item.Key == key)
                    return item.Value;
            }
            return string.Empty;
        }

        public void SetConfig(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var existing = _manifest.Configuration.Find(c => string.Equals(c.Key, key, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Value = value ?? string.Empty;
            }
            else
            {
                _manifest.Configuration.Add(new PluginConfigItem
                {
                    Key = key.Trim(),
                    Label = key.Trim(),
                    Value = value ?? string.Empty
                });
            }
            _ = PluginManager.Instance.SaveAsync();
        }

        // ── HTTP ────────────────────────────────────────────────────

        public string HttpGet(string url)
        {
            try
            {
                ValidateOutboundUrl(url);
                using var response = _http.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"HttpGet failed ({url}): {ex.Message}");
                throw new Jint.Runtime.JavaScriptException(ex.Message);
            }
        }

        public string HttpPost(string url, string body, string contentType = "application/json")
        {
            try
            {
                ValidateOutboundUrl(url);
                using var content = new StringContent(body ?? string.Empty, System.Text.Encoding.UTF8, contentType);
                using var response = _http.PostAsync(url, content).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"HttpPost failed ({url}): {ex.Message}");
                throw new Jint.Runtime.JavaScriptException(ex.Message);
            }
        }

        public void DownloadFile(string url, string destPath)
        {
            try
            {
                ValidateOutboundUrl(url);
                var fullPath = ResolveJailedPath(destPath, isWriteOrDelete: true);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var response = _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is long contentLen && contentLen > MaxDownloadFileBytes)
                {
                    throw new InvalidOperationException($"Download exceeds {MaxDownloadFileBytes / (1024 * 1024)} MB limit.");
                }

                using var stream = response.Content.ReadAsStream(cts.Token);
                using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[65536];
                long totalBytes = 0;
                int read;
                while ((read = stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).GetAwaiter().GetResult()) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > MaxDownloadFileBytes)
                    {
                        throw new InvalidOperationException($"Download exceeded {MaxDownloadFileBytes / (1024 * 1024)} MB limit.");
                    }
                    fileStream.Write(buffer, 0, read);
                }
            }
            catch (Exception ex)
            {
                LogError($"DownloadFile failed ({url}): {ex.Message}");
                throw;
            }
        }

        // ── File I/O (Jailed strictly to %LocalAppData%\SwellProxy\plugins\) ──

        public string ReadFile(string path)
        {
            var full = ResolveJailedPath(path, isWriteOrDelete: false);
            var fi = new FileInfo(full);
            if (fi.Exists && fi.Length > MaxTextResponseBytes)
                throw new InvalidOperationException("File exceeds 8 MB plugin read limit.");
            return File.ReadAllText(full);
        }

        public void WriteFile(string path, string content)
        {
            if (content != null && content.Length > MaxTextResponseBytes)
                throw new InvalidOperationException("Content exceeds 8 MB plugin write limit.");

            var full = ResolveJailedPath(path, isWriteOrDelete: true);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            Helpers.LocalSettingsHelper.WriteAllTextAtomic(full, content ?? string.Empty);
        }

        public bool FileExists(string path)
        {
            try
            {
                return File.Exists(ResolveJailedPath(path, isWriteOrDelete: false));
            }
            catch
            {
                return false;
            }
        }

        public void DeleteFile(string path)
        {
            var full = ResolveJailedPath(path, isWriteOrDelete: true);
            if (File.Exists(full)) File.Delete(full);
        }

        // ── Notifications ────────────────────────────────────────────

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
                Log($"Notification: {title} — {message}");
            }
        }

        // ── Sandbox Helpers ──────────────────────────────────────────

        private string ResolveJailedPath(string path, bool isWriteOrDelete)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("File path cannot be empty.");

            if (Path.IsPathRooted(path))
                throw new UnauthorizedAccessException("Plugins are not allowed to access absolute filesystem paths.");

            string normalizedRel = path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string candidate;
            if (normalizedRel.StartsWith("plugins" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                candidate = Path.GetFullPath(Path.Combine(_baseDir, normalizedRel));
            }
            else
            {
                string safeId = SanitizeFolderToken(_manifest.Id);
                candidate = Path.GetFullPath(Path.Combine(_pluginsJailDir, "data", safeId, normalizedRel));
            }

            if (!candidate.StartsWith(_pluginsJailPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Plugin file path must stay inside the plugins directory.");
            }

            if (isWriteOrDelete)
            {
                string ext = Path.GetExtension(candidate);
                if (_blockedWriteExtensions.Contains(ext))
                {
                    throw new UnauthorizedAccessException($"Plugins are not permitted to write or delete '{ext}' files.");
                }
            }

            return candidate;
        }

        private static string SanitizeFolderToken(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "default";
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static void ValidateOutboundUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid URL.");

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Unsupported URI scheme: {uri.Scheme}");
            }

            string host = uri.IdnHost.Trim('[', ']');
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Plugins are not allowed to access localhost control endpoints.");
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip) ||
                    ip.Equals(IPAddress.Any) ||
                    ip.Equals(IPAddress.IPv6Any) ||
                    IsLinkLocal(ip))
                {
                    throw new UnauthorizedAccessException("Plugins are not allowed to access loopback or link-local addresses.");
                }
            }
        }

        private static bool IsLinkLocal(IPAddress ip)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                return ip.IsIPv6LinkLocal;

            var b = ip.GetAddressBytes();
            return b.Length == 4 && b[0] == 169 && b[1] == 254;
        }
    }
}
