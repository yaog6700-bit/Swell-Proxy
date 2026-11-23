using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace AnywhereWinUI.Updater;

internal static class Program
{
    private const string ParentPidArg    = "--parent-pid=";
    private const string ExtractedDirArg = "--extracted-dir=";
    private const string InstallDirArg   = "--install-dir=";
    private const string LaunchAfterArg  = "--launch-after=";
    private const string LaunchArgsArg   = "--launch-args=";
    private const string ElevatedFlag    = "--elevated";

    private const int CopyRetryCount      = 10;
    private const int CopyRetryDelayMs    = 250;
    private const int ParentExitTimeoutMs = 20_000;

    private static StreamWriter? _log;

    private static int Main(string[] args)
    {
        string? installDir = null;
        string? launchAfter = null;
        string? launchArgs = null;

        try
        {
            int? parentPid = null;
            string? extractedDir = null;
            var elevated = false;

            foreach (var a in args)
            {
                if (a.StartsWith(ParentPidArg, StringComparison.OrdinalIgnoreCase))
                    parentPid = int.TryParse(a[ParentPidArg.Length..], out var p) ? p : null;
                else if (a.StartsWith(ExtractedDirArg, StringComparison.OrdinalIgnoreCase))
                    extractedDir = a[ExtractedDirArg.Length..];
                else if (a.StartsWith(InstallDirArg, StringComparison.OrdinalIgnoreCase))
                    installDir = a[InstallDirArg.Length..];
                else if (a.StartsWith(LaunchAfterArg, StringComparison.OrdinalIgnoreCase))
                    launchAfter = a[LaunchAfterArg.Length..];
                else if (a.StartsWith(LaunchArgsArg, StringComparison.OrdinalIgnoreCase))
                    launchArgs = a[LaunchArgsArg.Length..];
                else if (string.Equals(a, ElevatedFlag, StringComparison.OrdinalIgnoreCase))
                    elevated = true;
            }

            if (parentPid is null || string.IsNullOrEmpty(extractedDir) ||
                string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(launchAfter))
            {
                Console.Error.WriteLine("Usage: Swell.Updater --parent-pid=N --extracted-dir=PATH --install-dir=PATH --launch-after=NAME [--launch-args=ARGS] [--elevated]");
                return 2;
            }

            OpenLog();
            Log($"Updater started. parent={parentPid}, elevated={elevated}");
            Log($"  extracted-dir = {extractedDir}");
            Log($"  install-dir   = {installDir}");
            Log($"  launch-after  = {launchAfter}");
            Log($"  launch-args   = {launchArgs}");

            if (!WaitForParentExit(parentPid.Value))
            {
                Log($"Parent {parentPid.Value} is still running after {ParentExitTimeoutMs} ms; aborting update to avoid corrupting installation.");
                return 6;
            }

            if (!TryEnsureWritable(installDir))
            {
                if (!elevated)
                {
                    Log("Install dir not writable; relaunching elevated…");
                    if (TryRelaunchElevated(args))
                    {
                        return 0;
                    }

                    // User cancelled UAC — relaunch the existing app rather than leaving nothing running!
                    Log("Elevation cancelled by user; relaunching existing app.");
                    var existingExe = Path.Combine(installDir, launchAfter);
                    if (File.Exists(existingExe))
                    {
                        try { LaunchApp(existingExe, installDir, launchArgs); } catch { }
                    }
                    return 3;
                }
                Log("Install dir still not writable after elevation. Aborting.");
                return 3;
            }

            CopyOverwriteWithRollback(extractedDir, installDir);
            Log("Copy complete.");

            var newExe = Path.Combine(installDir, launchAfter);
            if (!File.Exists(newExe))
            {
                Log($"New app exe not found after update: {newExe}. Aborting launch.");
                return 4;
            }

            CleanupLargeStagingDirs(extractedDir);

            try
            {
                LaunchApp(newExe, installDir, launchArgs);
                Log("New app launched.");
            }
            catch (Exception ex)
            {
                Log($"Failed to launch new app: {ex}");
                return 5;
            }

            return 0;
        }
        catch (Exception ex)
        {
            try { Log($"Fatal: {ex}"); } catch { }
            // Attempt best-effort relaunch of existing app if rollback preserved it
            if (!string.IsNullOrEmpty(installDir) && !string.IsNullOrEmpty(launchAfter))
            {
                var fallbackExe = Path.Combine(installDir, launchAfter);
                if (File.Exists(fallbackExe))
                {
                    try { LaunchApp(fallbackExe, installDir, launchArgs); } catch { }
                }
            }
            return 1;
        }
        finally
        {
            try { _log?.Flush(); _log?.Dispose(); } catch { }
        }
    }

    private static bool WaitForParentExit(int pid)
    {
        try
        {
            using var parent = Process.GetProcessById(pid);
            return parent.WaitForExit(ParentExitTimeoutMs);
        }
        catch (ArgumentException)
        {
            return true; // Process already gone
        }
        catch (Exception ex)
        {
            Log($"WaitForParentExit error: {ex.Message}");
            return true;
        }
    }

    private static bool TryEnsureWritable(string installDir)
    {
        try
        {
            var probe = Path.Combine(installDir, $".swell-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException ex) { Log($"Write probe denied: {ex.Message}"); return false; }
        catch (IOException ex)                 { Log($"Write probe IO error: {ex.Message}"); return false; }
    }

    private static bool TryRelaunchElevated(string[] originalArgs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = Environment.ProcessPath ?? "Swell.Updater.exe",
                UseShellExecute = true,
                Verb            = "runas",
            };
            foreach (var a in originalArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(ElevatedFlag);
            using var p = Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // User cancelled UAC
        }
    }

    private static void CopyOverwriteWithRollback(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        var stageRoot = Directory.GetParent(source)?.FullName ?? Path.GetTempPath();
        var rollbackDir = Path.Combine(stageRoot, "rollback_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(rollbackDir);

        var backedUpFiles = new List<(string RelativePath, bool ExistedPreviously)>();
        string? currentFile = null;

        try
        {
            foreach (var srcFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                currentFile = Path.GetRelativePath(source, srcFile);
                var dstFile = Path.Combine(dest, currentFile);
                var dstDir  = Path.GetDirectoryName(dstFile);
                if (!string.IsNullOrEmpty(dstDir))
                    Directory.CreateDirectory(dstDir);

                bool existed = File.Exists(dstFile);
                if (existed)
                {
                    var bakFile = Path.Combine(rollbackDir, currentFile);
                    var bakDir  = Path.GetDirectoryName(bakFile);
                    if (!string.IsNullOrEmpty(bakDir))
                        Directory.CreateDirectory(bakDir);
                    File.Copy(dstFile, bakFile, overwrite: true);
                }

                CopyWithRetry(srcFile, dstFile);
                backedUpFiles.Add((currentFile, existed));
            }
            Log($"Copied {backedUpFiles.Count} files into {dest}");
            DeleteDirectoryBestEffort(rollbackDir);
        }
        catch (Exception ex)
        {
            Log($"Copy aborted after {backedUpFiles.Count} files on '{currentFile}': {ex.Message}. Rolling back…");
            for (int i = backedUpFiles.Count - 1; i >= 0; i--)
            {
                var (rel, existed) = backedUpFiles[i];
                var dstFile = Path.Combine(dest, rel);
                var bakFile = Path.Combine(rollbackDir, rel);
                try
                {
                    if (existed && File.Exists(bakFile))
                        File.Copy(bakFile, dstFile, overwrite: true);
                    else if (!existed && File.Exists(dstFile))
                        File.Delete(dstFile);
                }
                catch (Exception rbEx)
                {
                    Log($"Rollback warning for '{rel}': {rbEx.Message}");
                }
            }
            DeleteDirectoryBestEffort(rollbackDir);
            throw;
        }
    }

    private static void CopyWithRetry(string src, string dst)
    {
        for (var attempt = 0; attempt < CopyRetryCount; attempt++)
        {
            try
            {
                File.Copy(src, dst, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException)
                                    && attempt < CopyRetryCount - 1)
            {
                Log($"Copy retry {attempt + 1}/{CopyRetryCount} for {dst}: {ex.Message}");
                Thread.Sleep(CopyRetryDelayMs);
            }
        }
        File.Copy(src, dst, overwrite: true);
    }

    private static void CleanupLargeStagingDirs(string extractedDir)
    {
        var stageRoot = Directory.GetParent(extractedDir)?.FullName;
        if (string.IsNullOrEmpty(stageRoot))
            return;

        DeleteDirectoryBestEffort(Path.Combine(stageRoot, "download"));
        DeleteDirectoryBestEffort(extractedDir);
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            Directory.Delete(path, recursive: true);
            Log($"Deleted staging directory: {path}");
        }
        catch (Exception ex)
        {
            Log($"Could not delete staging directory '{path}': {ex.Message}");
        }
    }

    private static void LaunchApp(string exePath, string workingDirectory, string? launchArgs)
    {
        bool needsAdminForTun = !string.IsNullOrEmpty(launchArgs) &&
                                launchArgs.Contains("--tun", StringComparison.OrdinalIgnoreCase);

        if (IsElevated() && !needsAdminForTun)
        {
            try
            {
                string explorerArgs = string.IsNullOrWhiteSpace(launchArgs)
                    ? QuoteForCommandLine(exePath)
                    : $"{QuoteForCommandLine(exePath)} {launchArgs}";

                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = explorerArgs,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                });
                return;
            }
            catch (Exception ex)
            {
                Log($"Unelevated launch via Explorer failed: {ex.Message}");
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName         = exePath,
            Arguments        = launchArgs ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute  = true,
        };
        if (needsAdminForTun && !IsElevated())
        {
            psi.Verb = "runas";
        }
        using var started = Process.Start(psi);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string QuoteForCommandLine(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void OpenLog()
    {
        try
        {
            var logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy", "Updates");
            Directory.CreateDirectory(logRoot);
            var path = Path.Combine(logRoot, "updater.log");
            _log = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
            };
        }
        catch
        {
            _log = null;
        }
    }

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
        try { _log?.WriteLine(line); } catch { }
        try { Console.WriteLine(line); } catch { }
    }
}
