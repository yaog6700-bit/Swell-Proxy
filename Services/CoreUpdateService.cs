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
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Services
{
    public sealed class CoreUpdateService
    {
        private const string SingboxApiUrl = "https://api.github.com/repos/SagerNet/sing-box/releases/latest";
        private const string ShoesApiLatestUrl = "https://api.github.com/repos/yaog6700-bit/shoes/releases/latest";
        private const string ShoesApiListUrl = "https://api.github.com/repos/yaog6700-bit/shoes/releases?per_page=15";

        private static readonly string EngineDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        public static readonly string BundledBackupExePath = Path.Combine(EngineDir, "sing-box.bundled.bak");

        // Core binaries updated or manually replaced by the user live under %LocalAppData%\SwellProxy\bin
        // so they survive .NET single-file %TEMP% re-extraction across reboots and client self-updates,
        // and work without elevation even if the app is placed in a read-only folder.
        public static readonly string ShoesBinDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "bin");
        public static readonly string SingboxUserExePath = Path.Combine(ShoesBinDir, "sing-box.exe");
        public static readonly string SingboxBundledExePath = Path.Combine(EngineDir, "sing-box.exe");
        public static string SingboxExePath => ResolveSingboxExePath();
        public static bool HasBundledBackup =>
            (IsNonEmptyFile(SingboxUserExePath) && IsNonEmptyFile(SingboxBundledExePath)) ||
            File.Exists(BundledBackupExePath);

        public static readonly string ShoesUserExePath = Path.Combine(ShoesBinDir, "shoes.exe");
        public static readonly string ShoesBundledExePath = Path.Combine(EngineDir, "shoes.exe");

        public static string ResolveSingboxExePath()
        {
            if (IsNonEmptyFile(SingboxUserExePath)) return SingboxUserExePath;
            if (IsNonEmptyFile(SingboxBundledExePath)) return SingboxBundledExePath;
            return SingboxUserExePath;
        }

        public static bool IsShoesInstalled =>
            IsNonEmptyFile(ShoesUserExePath) || IsNonEmptyFile(ShoesBundledExePath);

        public static string ResolveShoesExePath()
        {
            if (IsNonEmptyFile(ShoesUserExePath)) return ShoesUserExePath;
            if (IsNonEmptyFile(ShoesBundledExePath)) return ShoesBundledExePath;
            return ShoesUserExePath;
        }

        private static bool IsNonEmptyFile(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CoreUpdateInfo?> CheckSingboxAsync(string? proxyUrl, CancellationToken ct)
        {
            var release = await FetchReleaseAsync(SingboxApiUrl, proxyUrl, ct);
            if (release is null) return null;

            var tag = (release.TagName ?? string.Empty).TrimStart('v');
            var dashIdx = tag.IndexOf('-');
            var cleanTag = dashIdx > 0 ? tag.Substring(0, dashIdx) : tag;
            if (!Version.TryParse(cleanTag, out var remote)) return null;

            var local = GetLocalSingboxNumericVersion();
            if (local != null && remote <= local)
            {
                return null;
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

        public async Task<CoreUpdateInfo?> CheckShoesAsync(
            string? proxyUrl,
            bool forceInstallIfMissing,
            CancellationToken ct)
        {
            string archMatch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64-pc-windows",
                Architecture.Arm64 => "aarch64-pc-windows",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(archMatch)) return null;

            GhRelease? release = null;
            try
            {
                release = await FetchReleaseAsync(ShoesApiLatestUrl, proxyUrl, ct);
            }
            catch { }

            if (release == null || PickShoesZipAsset(release, archMatch).ZipUrl == null)
            {
                var list = await FetchReleaseListAsync(ShoesApiListUrl, proxyUrl, ct);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (item == null || item.Draft) continue;
                        if (PickShoesZipAsset(item, archMatch).ZipUrl != null)
                        {
                            release = item;
                            break;
                        }
                    }
                }
            }

            if (release is null || string.IsNullOrWhiteSpace(release.TagName)) return null;

            var (zipName, zipUrl, shaUrl) = PickShoesZipAsset(release, archMatch);
            if (zipName is null || zipUrl is null) return null;

            var tag = release.TagName.TrimStart('v', 'V');
            var dashIdx = tag.IndexOf('-');
            var cleanTag = dashIdx > 0 ? tag[..dashIdx] : tag;
            if (!Version.TryParse(cleanTag, out var remote))
            {
                remote = new Version(0, 3, 5);
            }

            if (!forceInstallIfMissing && IsShoesInstalled)
            {
                var local = GetLocalShoesNumericVersion();
                if (local != null && remote <= local)
                {
                    return null;
                }
            }

            return new CoreUpdateInfo(
                remote,
                release.TagName,
                zipUrl,
                shaUrl,
                shaUrl is not null ? ChecksumFormat.Sha256 : null,
                "shoes.exe",
                zipName);
        }

        private static (string? ZipName, string? ZipUrl, string? ShaUrl) PickShoesZipAsset(GhRelease release, string archMatch)
        {
            if (release.Assets == null) return (null, null, null);

            string? zipName = null, zipUrl = null;
            foreach (var a in release.Assets)
            {
                if (string.IsNullOrEmpty(a?.Name) || string.IsNullOrEmpty(a.Url)) continue;
                var lower = a.Name.ToLowerInvariant();
                if (lower.Contains(archMatch) && lower.EndsWith(".zip", StringComparison.Ordinal))
                {
                    zipName = a.Name;
                    zipUrl = a.Url;
                    break;
                }
            }

            if (zipName == null) return (null, null, null);

            string? shaUrl = null;
            string expectedShaName = zipName + ".sha256";
            foreach (var a in release.Assets)
            {
                if (a?.Name != null && a.Url != null &&
                    string.Equals(a.Name, expectedShaName, StringComparison.OrdinalIgnoreCase))
                {
                    shaUrl = a.Url;
                    break;
                }
            }

            return (zipName, zipUrl, shaUrl);
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
                progress.Report(L.Update_FetchingChecksum);
                try
                {
                    var checksumText = await client.GetStringAsync(info.ChecksumUrl, ct);
                    expectedHash = ParsePureSha256(checksumText) ?? throw new InvalidDataException(L.Update_InvalidChecksumFmt);
                }
                catch (Exception ex) { throw new InvalidDataException(L.Update_DownloadChecksumErr + ex.Message); }
            }
            else
            {
                progress.Report(L.Core_NoChecksumSkip);
            }

            var actualHash = await DownloadWithMirrorFallbackAsync(
                client, info.ZipUrl, zipPath, info.ZipAssetName, proxyUrl, progress, ct);

            if (expectedHash is not null)
            {
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L.Update_HashMismatch);
            }

            progress.Report(L.Update_Extracting);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            }
            catch (Exception ex) { throw new InvalidDataException(L.Update_ExtractErr + ex.Message); }

            var extractedExe = FindExeInDirectory(extractDir, info.TargetExeName);
            if (extractedExe is null)
                throw new InvalidDataException(string.Format(L.Core_MissingTargetFmt, info.TargetExeName));

            if (onBeforeInstall != null)
            {
                progress.Report(L.Core_PreparingEnv);
                await onBeforeInstall();
            }

            progress.Report(string.Format(L.Core_InstallingFmt, info.TargetExeName));
            bool isShoes = string.Equals(info.TargetExeName, "shoes.exe", StringComparison.OrdinalIgnoreCase);
            var targetPath = Path.Combine(ShoesBinDir, info.TargetExeName);
            Directory.CreateDirectory(ShoesBinDir);

            var bakPath = targetPath + ".bak";
            try { File.Delete(bakPath); } catch { }
            if (File.Exists(targetPath))
                try { File.Move(targetPath, bakPath); } catch { }

            File.Copy(extractedExe, targetPath, overwrite: true);

            if (isShoes)
            {
                CopySidecarDlls(Path.GetDirectoryName(extractedExe), ShoesBinDir);
            }

            try { Directory.Delete(stageDir, recursive: true); } catch { }

            progress.Report(string.Format(L.Core_UpdatedStatusFmt, info.TagName));
        }

        public static void InstallLocalShoesExecutable(string sourceExePath)
        {
            Directory.CreateDirectory(ShoesBinDir);
            var targetPath = ShoesUserExePath;
            if (!string.Equals(Path.GetFullPath(sourceExePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceExePath, targetPath, overwrite: true);
            }
            CopySidecarDlls(Path.GetDirectoryName(sourceExePath), ShoesBinDir);
        }

        private static void CopySidecarDlls(string? srcDir, string dstDir)
        {
            if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir)) return;
            try
            {
                foreach (var dll in Directory.GetFiles(srcDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var dstDll = Path.Combine(dstDir, Path.GetFileName(dll));
                    if (!string.Equals(Path.GetFullPath(dll), Path.GetFullPath(dstDll), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Copy(dll, dstDll, overwrite: true); } catch { }
                    }
                }
            }
            catch { }
        }

        private static async Task<GhRelease?> FetchReleaseAsync(string apiUrl, string? proxyUrl, CancellationToken ct)
        {
            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromSeconds(20));
            return await client.GetFromJsonAsync(apiUrl, UpdateJsonSerializerContext.Default.GhRelease, ct);
        }

        private static async Task<System.Collections.Generic.List<GhRelease>?> FetchReleaseListAsync(
            string apiUrl, string? proxyUrl, CancellationToken ct)
        {
            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromSeconds(20));
            return await client.GetFromJsonAsync(apiUrl, UpdateJsonSerializerContext.Default.ListGhRelease, ct);
        }

        private static async Task<string> DownloadWithMirrorFallbackAsync(
            HttpClient client,
            string url,
            string destPath,
            string displayName,
            string? proxyUrl,
            IProgress<string> progress,
            CancellationToken ct)
        {
            try
            {
                return await DownloadAndHashAsync(client, url, destPath, displayName, progress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception firstEx) when (
                string.IsNullOrEmpty(proxyUrl) &&
                (url.Contains("github.com/", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("githubusercontent.com/", StringComparison.OrdinalIgnoreCase)))
            {
                string[] mirrors =
                {
                    "https://ghfast.top/",
                    "https://mirror.ghproxy.com/",
                    "https://ghproxy.net/"
                };

                foreach (var mirror in mirrors)
                {
                    try
                    {
                        try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                        return await DownloadAndHashAsync(client, mirror + url, destPath, displayName, progress, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Try next mirror
                    }
                }

                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstEx).Throw();
                throw;
            }
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
                ? string.Format(L.Update_DownloadingWithTotalFmt, name, mb, total.Value / 1024.0 / 1024.0)
                : string.Format(L.Update_DownloadingNoTotalFmt, name, mb);
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

        public static (bool IsValid, string VersionDisplay, Version? NumericVersion, string RawOutput) ProbeSingboxExecutable(string exePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return (false, L.Core_NotInstalled, null, string.Empty);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    var err = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(3000);
                    var combined = string.IsNullOrWhiteSpace(output) ? err : output;

                    // Capture full semver including pre-release tag (e.g. 1.15.0-alpha.13 or 1.14.0)
                    var fullMatch = System.Text.RegularExpressions.Regex.Match(
                        combined,
                        @"sing-box\s+version\s+v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z\.\-]+)?)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (fullMatch.Success)
                    {
                        var fullVerStr = fullMatch.Groups[1].Value;
                        var dashIdx = fullVerStr.IndexOf('-');
                        var numericPart = dashIdx > 0 ? fullVerStr.Substring(0, dashIdx) : fullVerStr;
                        Version.TryParse(numericPart, out var numericVer);
                        return (true, $"v{fullVerStr}", numericVer, combined);
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, L.Core_CannotExec, null, ex.Message);
            }

            return (false, L.Core_InvalidBinary, null, string.Empty);
        }

        public static Version? GetLocalSingboxNumericVersion()
        {
            if (!File.Exists(SingboxExePath)) return null;
            var probe = ProbeSingboxExecutable(SingboxExePath);
            return probe.NumericVersion;
        }

        public static string GetLocalSingboxVersionText()
        {
            if (!File.Exists(SingboxExePath)) return L.Core_NotInstalled;
            var probe = ProbeSingboxExecutable(SingboxExePath);
            return probe.IsValid ? probe.VersionDisplay : L.Core_InstalledUnknownVer;
        }

        public static (bool IsValid, string VersionDisplay, Version? NumericVersion, string RawOutput) ProbeShoesExecutable(string exePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return (false, L.Core_NotInstalled, null, string.Empty);

                string combined = RunVersionProbeOnce(exePath, "version");
                if (string.IsNullOrWhiteSpace(combined) ||
                    combined.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("unexpected argument", StringComparison.OrdinalIgnoreCase))
                {
                    combined = RunVersionProbeOnce(exePath, "--version");
                }

                if (combined.Contains("sing-box", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, L.Core_InvalidBinary, null, combined);
                }

                var verMatch = System.Text.RegularExpressions.Regex.Match(
                    combined,
                    @"v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z\.\-]+)?)");

                if (verMatch.Success)
                {
                    // Confirm this is actually a shoes binary (either output says "shoes" or --help mentions --no-reload/shoes)
                    bool looksLikeShoes = combined.Contains("shoes", StringComparison.OrdinalIgnoreCase);
                    if (!looksLikeShoes)
                    {
                        string helpOut = RunVersionProbeOnce(exePath, "--help");
                        looksLikeShoes = helpOut.Contains("shoes", StringComparison.OrdinalIgnoreCase) ||
                                         helpOut.Contains("no-reload", StringComparison.OrdinalIgnoreCase);
                    }

                    if (looksLikeShoes)
                    {
                        var fullVerStr = verMatch.Groups[1].Value;
                        var dashIdx = fullVerStr.IndexOf('-');
                        var numericPart = dashIdx > 0 ? fullVerStr[..dashIdx] : fullVerStr;
                        Version.TryParse(numericPart, out var numericVer);
                        return (true, $"v{fullVerStr}", numericVer, combined);
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, L.Core_CannotExec, null, ex.Message);
            }

            return (false, L.Core_InvalidBinary, null, string.Empty);
        }

        private static string RunVersionProbeOnce(string exePath, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return string.Empty;
            var output = proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(output) ? err : output;
        }

        public static Version? GetLocalShoesNumericVersion()
        {
            if (!IsShoesInstalled) return null;
            var probe = ProbeShoesExecutable(ResolveShoesExePath());
            return probe.NumericVersion;
        }

        public static string GetLocalShoesVersionText()
        {
            if (!IsShoesInstalled) return L.Core_NotInstalled;
            var probe = ProbeShoesExecutable(ResolveShoesExePath());
            return probe.IsValid ? probe.VersionDisplay : L.Core_InstalledUnknownVer;
        }
    }
}
