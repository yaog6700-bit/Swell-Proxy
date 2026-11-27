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
using AnywhereWinUI.Models;

namespace AnywhereWinUI.Services
{
    public enum AppUpdateStage
    {
        Idle,
        Available,
        Downloading,
        Installing,
        Ready,
        Failed,
        Restarting
    }

    public sealed class AppUpdateService
    {
        private const string ReleaseApiUrl = "https://api.github.com/repos/yaog6700-bit/Swell-Proxy/releases/latest";
        private const string AppExeName     = "Swell Proxy.exe";
        private const string UpdaterExeName = "Swell.Updater.exe";

        private static readonly object _stateLock = new();
        private static CancellationTokenSource? _downloadCts;

        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return ver ?? new Version(0, 0, 0, 0);
                }
                catch
                {
                    return new Version(0, 0, 0, 0);
                }
            }
        }

        public static AppUpdateStage CurrentStage { get; private set; } = AppUpdateStage.Idle;
        public static UpdateInfo? AvailableInfo { get; private set; }
        public static int DownloadPercent { get; private set; } = -1;
        public static string? LastError { get; private set; }

        public static UpdateStaging? PendingStaging { get; private set; }
        public static string? PendingVersionTag { get; private set; }
        public static bool HasPendingUpdate => PendingStaging != null;

        public static event EventHandler<string>? PendingUpdateReady;
        public static event EventHandler? StateChanged;

        public static void SetAvailableUpdate(UpdateInfo info)
        {
            if (info == null) return;
            bool changed = false;
            lock (_stateLock)
            {
                AvailableInfo = info;
                if (CurrentStage == AppUpdateStage.Idle)
                {
                    CurrentStage = AppUpdateStage.Available;
                    changed = true;
                }
            }
            if (changed)
            {
                StateChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void MarkPendingUpdateReady(string versionTag, UpdateStaging staging)
        {
            lock (_stateLock)
            {
                PendingVersionTag = versionTag;
                PendingStaging = staging;
                DownloadPercent = 100;
                LastError = null;
                CurrentStage = AppUpdateStage.Ready;
            }
            PendingUpdateReady?.Invoke(null, versionTag);
            StateChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void StartBackgroundDownload(UpdateInfo? info = null)
        {
            UpdateInfo? targetInfo;
            CancellationTokenSource cts;
            lock (_stateLock)
            {
                if (CurrentStage is AppUpdateStage.Downloading or AppUpdateStage.Installing or AppUpdateStage.Restarting or AppUpdateStage.Ready)
                    return;

                if (info != null)
                    AvailableInfo = info;

                targetInfo = AvailableInfo;
                if (targetInfo == null)
                    return;

                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
                cts = new CancellationTokenSource();
                _downloadCts = cts;

                CurrentStage = AppUpdateStage.Downloading;
                DownloadPercent = 0;
                LastError = null;
            }

            StateChanged?.Invoke(null, EventArgs.Empty);

            _ = Task.Run(async () =>
            {
                var token = cts.Token;
                var updater = new AppUpdateService();
                string? proxyUrl = CoreManager.Instance.IsRunning && AppSession.Instance.EffectiveHttpProxyPort is int p && p > 0
                    ? $"http://127.0.0.1:{p}"
                    : null;

                int lastReportedPct = 0;
                var progress = new Progress<ProgressDialogUpdate>(msg =>
                {
                    if (token.IsCancellationRequested) return;
                    bool shouldNotify = false;
                    lock (_stateLock)
                    {
                        if (CurrentStage is not (AppUpdateStage.Downloading or AppUpdateStage.Installing))
                            return;

                        if (msg.PercentComplete.HasValue)
                        {
                            int pct = Math.Clamp((int)Math.Round(msg.PercentComplete.Value), 0, 100);
                            if (pct >= 100 && CurrentStage == AppUpdateStage.Downloading)
                            {
                                DownloadPercent = 100;
                                CurrentStage = AppUpdateStage.Installing;
                                shouldNotify = true;
                            }
                            else if (pct != lastReportedPct)
                            {
                                lastReportedPct = pct;
                                DownloadPercent = pct;
                                shouldNotify = true;
                            }
                        }
                        else if (!string.IsNullOrEmpty(msg.StatusText) &&
                                 DownloadPercent >= 100 &&
                                 CurrentStage != AppUpdateStage.Installing)
                        {
                            CurrentStage = AppUpdateStage.Installing;
                            shouldNotify = true;
                        }
                    }

                    if (shouldNotify)
                    {
                        StateChanged?.Invoke(null, EventArgs.Empty);
                    }
                });

                try
                {
                    var staging = await updater.DownloadVerifyAndExtractAsync(targetInfo, proxyUrl, progress, token).ConfigureAwait(false);
                    MarkPendingUpdateReady(targetInfo.TagName, staging);
                }
                catch (OperationCanceledException)
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_downloadCts, cts))
                        {
                            CurrentStage = AvailableInfo != null ? AppUpdateStage.Available : AppUpdateStage.Idle;
                            DownloadPercent = -1;
                            LastError = null;
                        }
                    }
                    StateChanged?.Invoke(null, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_downloadCts, cts))
                        {
                            CurrentStage = AppUpdateStage.Failed;
                            DownloadPercent = -1;
                            LastError = ex.Message;
                        }
                    }
                    StateChanged?.Invoke(null, EventArgs.Empty);
                }
            });
        }

        public static void CancelBackgroundDownload()
        {
            CancellationTokenSource? ctsToCancel = null;
            lock (_stateLock)
            {
                if (CurrentStage == AppUpdateStage.Downloading)
                {
                    ctsToCancel = _downloadCts;
                    _downloadCts = null;
                    CurrentStage = AvailableInfo != null ? AppUpdateStage.Available : AppUpdateStage.Idle;
                    DownloadPercent = -1;
                    LastError = null;
                }
            }

            try { ctsToCancel?.Cancel(); } catch { }
            try { ctsToCancel?.Dispose(); } catch { }
            if (ctsToCancel != null)
            {
                StateChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static async Task ApplyPendingUpdateAndRestartAsync()
        {
            if (PendingStaging == null)
                return;

            lock (_stateLock)
            {
                CurrentStage = AppUpdateStage.Restarting;
            }
            StateChanged?.Invoke(null, EventArgs.Empty);

            var staging = PendingStaging;
            bool wasRunning = CoreManager.Instance.IsRunning;
            string launchArgs = string.Empty;
            if (wasRunning)
            {
                var activeNode = NodesManager.Instance.FindNode(NodesManager.Instance.SelectedNodeId);
                bool customNeedsAdmin = CustomConfigService.RequiresTunAdmin(activeNode) ||
                                        (CustomConfigService.IsCustomNode(activeNode) &&
                                         AdminHelper.IsAdministrator() &&
                                         AppSession.Instance.ActiveCustomHasTun);
                launchArgs = AppSession.Instance.ProxyModeIndex switch
                {
                    3 => "--tun-system-start",
                    1 => "--tun-start",
                    _ => customNeedsAdmin ? "--auto-start-proxy --require-admin" : "--auto-start-proxy"
                };
                await CoreManager.Instance.StopAsync();
            }

            try { await Plugins.PluginManager.Instance.FireAsync(Plugins.PluginTrigger.OnShutdown); } catch { }
            try { MainWindow.Instance?.DisposeTrayForRestart(); } catch { }
            try { App.ReleaseSingleInstanceMutex(); } catch { }

            new AppUpdateService().LaunchUpdater(staging, launchArgs);
            Environment.Exit(0);
        }

        private static string UpdatesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwellProxy", "Updates");

        public async Task<UpdateInfo?> CheckAsync(string? proxyUrl, CancellationToken ct)
        {
            // Note: If Version is missing from assembly or is 0.0.0.0, it might skip checking. 
            // In GitHub Actions we set -p:Version=1.0.x
            if (CurrentVersion.Major == 0 && CurrentVersion.Minor == 0) return null;

            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromSeconds(20));

            AppGhRelease? release;
            try
            {
                release = await client.GetFromJsonAsync(
                    ReleaseApiUrl, AppUpdateJsonSerializerContext.Default.AppGhRelease, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }

            if (release is null || release.Draft || release.Prerelease) return null;

            var tag = (release.TagName ?? string.Empty).TrimStart('v');
            if (!Version.TryParse(tag, out var remoteVersion)) return null;
            
            // Normalize versions for safe comparison
            var localNormalized = NormalizeForCompare(CurrentVersion);
            var remoteNormalized = NormalizeForCompare(remoteVersion);
            
            // Compare manually since Tuple<int,int,int,int> implements IComparable
            if (remoteNormalized.Item1 < localNormalized.Item1) return null;
            if (remoteNormalized.Item1 == localNormalized.Item1)
            {
                if (remoteNormalized.Item2 < localNormalized.Item2) return null;
                if (remoteNormalized.Item2 == localNormalized.Item2)
                {
                    if (remoteNormalized.Item3 < localNormalized.Item3) return null;
                    if (remoteNormalized.Item3 == localNormalized.Item3)
                    {
                        if (remoteNormalized.Item4 <= localNormalized.Item4) return null;
                    }
                }
            }

            var rid = CurrentRid();
            if (rid is null) return null;

            var zipName    = $"Swell-{rid}.zip";
            var sha256Name = $"{zipName}.sha256";

            string? zipUrl = null, shaUrl = null;
            if (release.Assets is not null)
            {
                foreach (var a in release.Assets)
                {
                    if (a?.Name is null || a.Url is null) continue;
                    if (a.Name == zipName)    zipUrl = a.Url;
                    if (a.Name == sha256Name) shaUrl = a.Url;
                }
            }

            if (zipUrl is null || shaUrl is null) return null;

            return new UpdateInfo(remoteVersion, release.TagName!, zipUrl, shaUrl, zipName);
        }

        public async Task<UpdateStaging> DownloadVerifyAndExtractAsync(
            UpdateInfo info, string? proxyUrl, IProgress<ProgressDialogUpdate> progress, CancellationToken ct)
        {
            var stageRoot   = Path.Combine(UpdatesDir, info.NewVersion.ToString());
            var downloadDir = Path.Combine(stageRoot, "download");
            var extractDir  = Path.Combine(stageRoot, "extracted");
            var runnerDir   = Path.Combine(stageRoot, "runner");

            if (Directory.Exists(stageRoot))
            {
                try { Directory.Delete(stageRoot, recursive: true); } catch { }
            }
            Directory.CreateDirectory(downloadDir);
            Directory.CreateDirectory(extractDir);
            Directory.CreateDirectory(runnerDir);

            using var client = CreateHttpClient(proxyUrl, TimeSpan.FromMinutes(10));

            // 1. Download SHA256
            progress.Report(new ProgressDialogUpdate(L.Update_FetchingChecksum));
            string expectedHash;
            try
            {
                var shaText = await client.GetStringAsync(info.Sha256Url, ct);
                expectedHash = ParseSha256SumLine(shaText)
                    ?? throw new InvalidDataException(L.Update_InvalidChecksumFmt);
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidDataException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidDataException(L.Update_DownloadChecksumErr + ex.Message);
            }

            // 2. Download ZIP and hash
            var zipPath = Path.Combine(downloadDir, info.ZipAssetName);
            var actualHash = await DownloadAndHashAsync(
                client, info.ZipUrl, zipPath, info.ZipAssetName, progress, ct);

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Update_HashMismatch);

            // 4. Extract
            progress.Report(new ProgressDialogUpdate(L.Update_Extracting));
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(L.Update_ExtractErr + ex.Message);
            }

            // 5. Sanity check
            progress.Report(new ProgressDialogUpdate(L.Update_Verifying));

            var newAppExe     = Path.Combine(extractDir, AppExeName);
            var newUpdaterExe = Path.Combine(extractDir, UpdaterExeName);

            if (!File.Exists(newAppExe) || !File.Exists(newUpdaterExe))
                throw new InvalidDataException(L.Update_MissingFiles);

            // 6. Stage current updater to runner dir
            var installDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var currentUpdater = Path.Combine(installDir, UpdaterExeName);
            if (!File.Exists(currentUpdater))
            {
                throw new FileNotFoundException(L.Update_MissingUpdater, currentUpdater);
            }

            var stagedRunner = Path.Combine(runnerDir, UpdaterExeName);
            File.Copy(currentUpdater, stagedRunner, overwrite: true);

            progress.Report(new ProgressDialogUpdate(L.Update_PreparingRestart));

            return new UpdateStaging(extractDir, stagedRunner, installDir, info.NewVersion);
        }

        public void LaunchUpdater(UpdateStaging staging, string? launchArgs = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = staging.RunnerExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add($"--parent-pid={Environment.ProcessId}");
            psi.ArgumentList.Add($"--extracted-dir={staging.ExtractedDir}");
            psi.ArgumentList.Add($"--install-dir={staging.InstallDir}");
            psi.ArgumentList.Add($"--launch-after={AppExeName}");
            if (!string.IsNullOrWhiteSpace(launchArgs))
            {
                psi.ArgumentList.Add($"--launch-args={launchArgs}");
            }

            using var started = Process.Start(psi);
        }

        public void CleanupOldStagingDirs()
        {
            try
            {
                if (!Directory.Exists(UpdatesDir)) return;
                foreach (var sub in Directory.EnumerateDirectories(UpdatesDir))
                {
                    try { Directory.Delete(sub, recursive: true); } catch { }
                }
            }
            catch { }
        }

        // --- Helpers ---

        private static string? CurrentRid() => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "win-x64",
            Architecture.X86   => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => null,
        };

        private static (int, int, int, int) NormalizeForCompare(Version v) =>
            (v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

        private static HttpClient CreateHttpClient(string? proxyUrl, TimeSpan timeout)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                handler.Proxy    = new WebProxy(proxyUrl);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Swell/{CurrentVersion}");
            return client;
        }

        private static string? ParseSha256SumLine(string content)
        {
            var line = content.Trim();
            if (line.Length == 0) return null;

            int sep = 0;
            while (sep < line.Length && !char.IsWhiteSpace(line[sep])) sep++;
            var token = line[..sep];

            if (token.Length != 64) return null;
            foreach (var c in token)
            {
                if (!char.IsAsciiHexDigit(c)) return null;
            }
            return token.ToLowerInvariant();
        }

        private static async Task<string> DownloadAndHashAsync(
            HttpClient client, string url, string destPath, string displayName,
            IProgress<ProgressDialogUpdate> progress, CancellationToken ct)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            progress.Report(new ProgressDialogUpdate(FormatProgress(displayName, 0, total), 0));

            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer      = new byte[81920];
            long received   = 0;
            long lastReport = 0;

            while (true)
            {
                var read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;

                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                hasher.AppendData(buffer, 0, read);
                received += read;

                if (received - lastReport >= 512 * 1024)
                {
                    double? percent = total.HasValue ? (double)received / total.Value * 100 : null;
                    progress.Report(new ProgressDialogUpdate(FormatProgress(displayName, received, total), percent));
                    lastReport = received;
                }
            }

            progress.Report(new ProgressDialogUpdate(FormatProgress(displayName, received, total), 100));
            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        private static string FormatProgress(string name, long received, long? total)
        {
            var mbReceived = received / 1024.0 / 1024.0;
            return total.HasValue
                ? string.Format(L.Update_DownloadingWithTotalFmt, name, mbReceived, total.Value / 1024.0 / 1024.0)
                : string.Format(L.Update_DownloadingNoTotalFmt, name, mbReceived);
        }
    }
}
