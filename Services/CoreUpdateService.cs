using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public sealed class CoreUpdateService
    {
        private const string SingboxApiUrl = "https://api.github.com/repos/SagerNet/sing-box/releases/latest";
        private static readonly string EngineDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        public static readonly string SingboxExePath = Path.Combine(EngineDir, "sing-box.exe");

        public async Task<CoreUpdateInfo?> CheckSingboxAsync(string? proxyUrl, CancellationToken ct)
        {
            var release = await FetchReleaseAsync(SingboxApiUrl, proxyUrl, ct);
            if (release is null) return null;

            var tag = (release.TagName ?? string.Empty).TrimStart('v');
            if (!Version.TryParse(tag, out var remote)) return null;

            var localStr = GetLocalSingboxVersionText();
            if (localStr.StartsWith("v") && Version.TryParse(localStr.Substring(1), out var local))
            {
                if (remote <= local) return null;
            }

            if (!IsX64()) return null;
            var zipName = $"sing-box-{tag}-windows-amd64.zip";
            var shaName = zipName + ".sha256";

            string? zipUrl = null, shaUrl = null;
            foreach (var a in release.Assets ?? new System.Collections.Generic.List<GhAsset>())
            {
                if (a?.Name is null || a.Url is null) continue;
                if (a.Name == zipName) zipUrl = a.Url;
                if (a.Name == shaName) shaUrl = a.Url;
            }
            if (zipUrl is null) return null;

            return new CoreUpdateInfo(remote, release.TagName!, zipUrl, shaUrl,
                                      shaUrl is not null ? ChecksumFormat.Sha256 : null, "sing-box.exe", zipName);
        }

        public async Task UpdateAsync(
            CoreUpdateInfo info,
            string? proxyUrl,
            IProgress<string> progress,
            CancellationToken ct,
            Func<Task>? onBeforeInstall = null)
        {
            var stageDir = Path.Combine(Path.GetTempPath(), "SwellProxyUpdates", info.NewVersion.ToString());
            var zipPath = Path.Combine(stageDir, info.ZipAssetName);
            var extractDir = Path.Combine(stageDir, "extracted");

            if (Directory.Exists(stageDir))
                try { Directory.Delete(stageDir, recursive: true); } catch { }
            Directory.CreateDirectory(stageDir);
            Directory.CreateDirectory(extractDir);

            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromMinutes(10));

            string? expectedHash = null;
            if (info.ChecksumUrl is not null)
            {
                progress.Report("正在获取校验文件…");
                try
                {
                    var checksumText = await client.GetStringAsync(info.ChecksumUrl, ct);
                    expectedHash = ParsePureSha256(checksumText) ?? throw new InvalidDataException("校验文件格式异常");
                }
                catch (Exception ex) { throw new InvalidDataException("无法下载校验文件：" + ex.Message); }
            }
            else
            {
                progress.Report("无可用校验文件，跳过安全哈希验证步骤…");
            }

            var actualHash = await DownloadAndHashAsync(client, info.ZipUrl, zipPath, info.ZipAssetName, progress, ct);

            if (expectedHash is not null)
            {
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256 校验失败：下载的文件与服务器公布的哈希不一致。");
            }

            progress.Report("正在解压…");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            }
            catch (Exception ex) { throw new InvalidDataException("解压失败：" + ex.Message); }

            var extractedExe = FindExeInDirectory(extractDir, info.TargetExeName);
            if (extractedExe is null)
                throw new InvalidDataException($"更新包内未找到 {info.TargetExeName}");

            if (onBeforeInstall != null)
            {
                progress.Report("正在准备环境…");
                await onBeforeInstall();
            }

            progress.Report($"正在安装 {info.TargetExeName}…");
            var targetPath = Path.Combine(EngineDir, info.TargetExeName);
            Directory.CreateDirectory(EngineDir);

            var bakPath = targetPath + ".bak";
            try { File.Delete(bakPath); } catch { }
            if (File.Exists(targetPath))
                try { File.Move(targetPath, bakPath); } catch { }

            File.Copy(extractedExe, targetPath, overwrite: true);

            try { Directory.Delete(stageDir, recursive: true); } catch { }

            progress.Report($"更新完成！当前版本：{info.TagName}");
        }

        private static async Task<GhRelease?> FetchReleaseAsync(string apiUrl, string? proxyUrl, CancellationToken ct)
        {
            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromSeconds(20));
            return await client.GetFromJsonAsync(apiUrl, UpdateJsonSerializerContext.Default.GhRelease, ct);
        }

        private static HttpClient CreateHttpClient(string? proxyUrl, TimeSpan timeout)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                handler.Proxy = new WebProxy(proxyUrl);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SwellProxy/CoreUpdater");
            return client;
        }

        private static async Task<string> DownloadAndHashAsync(
            HttpClient client, string url, string destPath, string displayName,
            IProgress<string> progress, CancellationToken ct)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            progress.Report(FormatProgress(displayName, 0, total));

            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0, lastReport = 0;

            while (true)
            {
                var read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                hasher.AppendData(buffer, 0, read);
                received += read;
                if (received - lastReport >= 512 * 1024)
                {
                    progress.Report(FormatProgress(displayName, received, total));
                    lastReport = received;
                }
            }
            progress.Report(FormatProgress(displayName, received, total));
            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        private static string FormatProgress(string name, long received, long? total)
        {
            var mb = received / 1024.0 / 1024.0;
            return total.HasValue
                ? $"正在下载 {name} … {mb:0.0} / {total.Value / 1024.0 / 1024.0:0.0} MB"
                : $"正在下载 {name} … {mb:0.0} MB";
        }

        private static string? ParsePureSha256(string content)
        {
            var line = content.Trim();
            if (line.Length == 0) return null;
            int sep = 0;
            while (sep < line.Length && !char.IsWhiteSpace(line[sep])) sep++;
            var token = line[..sep];
            if (token.Length != 64 || !IsHex(token)) return null;
            return token.ToLowerInvariant();
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s)
                if (!char.IsAsciiHexDigit(c)) return false;
            return true;
        }

        private static string? FindExeInDirectory(string dir, string exeName)
        {
            foreach (var file in Directory.EnumerateFiles(dir, exeName, SearchOption.AllDirectories))
                return file;
            return null;
        }

        private static bool IsX64() => RuntimeInformation.ProcessArchitecture == Architecture.X64;

        public static string GetLocalSingboxVersionText()
        {
            try
            {
                if (!File.Exists(SingboxExePath)) return "未安装";

                var psi = new ProcessStartInfo
                {
                    FileName = SingboxExePath,
                    Arguments = "version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(2000);
                    var m = System.Text.RegularExpressions.Regex.Match(output, @"(\d+\.\d+\.\d+)");
                    if (m.Success) return $"v{m.Groups[1].Value}";
                }
            }
            catch { }
            return "已安装 (无法读取版本)";
        }
    }
}
