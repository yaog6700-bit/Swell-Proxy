using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnywhereWinUI.Services
{
    public sealed class CoreManager
    {
        private static readonly Lazy<CoreManager> _instance = new(() => new CoreManager());
        public static CoreManager Instance => _instance.Value;

        public string ExePath => Path.Combine(AppContext.BaseDirectory, "Assets", "sing-box.exe");
        public string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "AnywhereProxy", 
            "singbox_config.json"
        );

        private const int LogBufferMax = 500;
        private readonly string[] _logBuffer = new string[LogBufferMax];
        private int _logHead;
        private int _logCount;
        private readonly object _bufferLock = new();

        private Process? _process;
        private readonly StringBuilder _startupLog = new();
        private readonly object _startupLogLock = new();
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        public bool IsRunning => _process is { HasExited: false };
        public string LastError { get; private set; } = string.Empty;

        public event EventHandler<string>? LogReceived;
        public event EventHandler<bool>? RunningChanged;
        public event EventHandler<(long Down, long Up, long DownSpeed, long UpSpeed)>? TrafficUpdated;

        private CancellationTokenSource? _statsCts;
        private long _lastDown;
        private long _lastUp;
        private DateTime _lastStatsTime;

        private CoreManager()
        {
        }

        public IReadOnlyList<string> GetLogBuffer()
        {
            lock (_bufferLock)
            {
                if (_logCount == 0) return Array.Empty<string>();

                var snapshot = new string[_logCount];
                if (_logCount < LogBufferMax)
                {
                    Array.Copy(_logBuffer, 0, snapshot, 0, _logCount);
                }
                else
                {
                    int tailCount = LogBufferMax - _logHead;
                    Array.Copy(_logBuffer, _logHead, snapshot, 0, tailCount);
                    Array.Copy(_logBuffer, 0, snapshot, tailCount, _logHead);
                }
                return snapshot;
            }
        }

        public void ClearLogBuffer()
        {
            lock (_bufferLock)
            {
                Array.Clear(_logBuffer, 0, _logBuffer.Length);
                _logHead = 0;
                _logCount = 0;
            }
        }

        public void AppendLog(string line)
        {
            lock (_bufferLock)
            {
                _logBuffer[_logHead] = line;
                _logHead = (_logHead + 1) % LogBufferMax;
                if (_logCount < LogBufferMax) _logCount++;
            }
            LogReceived?.Invoke(this, line);
        }

        public async Task<bool> StartAsync(string configJson)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (IsRunning) await StopInternalAsync();

                // Self-healing: Kill any orphaned sing-box processes from previous crashes
                try
                {
                    foreach (var p in Process.GetProcessesByName("sing-box"))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }

                LastError = string.Empty;
                lock (_startupLogLock) { _startupLog.Clear(); }

            // Ensure directory exists
            var localDir = Path.GetDirectoryName(ConfigPath);
            if (localDir != null)
            {
                Directory.CreateDirectory(localDir);
                
                // Self-healing: Copy local binary rule-sets (*.srs) from Assets/rules to LocalAppData/AnywhereProxy
                var sourceRulesDir = Path.Combine(AppContext.BaseDirectory, "Assets", "rules");
                if (Directory.Exists(sourceRulesDir))
                {
                    foreach (var file in Directory.GetFiles(sourceRulesDir, "*.srs"))
                    {
                        var destFile = Path.Combine(localDir, Path.GetFileName(file));
                        try
                        {
                            File.Copy(file, destFile, overwrite: true);
                        }
                        catch { }
                    }
                }
            }

            // Write configuration
            await File.WriteAllTextAsync(ConfigPath, configJson);

            // In a real scenario, make sure ExePath exists.
            // For now, we allow simulation if it is missing, or we can handle it gracefully.
            if (!File.Exists(ExePath))
            {
                // Standalone Simulation fallback for visual UI demo when sing-box is not copied yet
                AppendLog("[系统] 未在 Assets 文件夹下检测到 sing-box.exe。启用独立 UI 仿真模式。");
                AppendLog("[启动] 仿真引擎启动成功...");
                RunningChanged?.Invoke(this, true);
                return true;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ExePath,
                    Arguments = $"run -c \"{ConfigPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(ExePath)!,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                _process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    lock (_startupLogLock) { _startupLog.AppendLine(e.Data); }
                    AppendLog("[sing-box] " + e.Data);
                };

                _process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is null) return;
                    lock (_startupLogLock) { _startupLog.AppendLine(e.Data); }
                    AppendLog("[sing-box] " + e.Data);
                };

                _process.Exited += (_, _) =>
                {
                    AppendLog("[sing-box 进程已退出]");
                    if (AppSession.Instance.EnableTunMode)
                    {
                        var tunService = new TunService();
                        tunService.CleanupTunRoutes(AppSession.Instance.LastTunServerHost);
                    }
                    else if (AppSession.Instance.EnableSystemProxy)
                    {
                        SystemProxyManager.DisableProxy();
                    }
                    RunningChanged?.Invoke(this, false);
                };

                _process.Start();
                ChildProcessTracker.AddProcess(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                AppendLog($"[启动] {ExePath}");
                AppendLog($"[配置] {ConfigPath}");

                await Task.Delay(800);

                if (_process.HasExited)
                {
                    lock (_startupLogLock)
                    {
                        LastError = _startupLog.Length > 0
                            ? _startupLog.ToString().Trim()
                            : $"sing-box 立即退出（退出码 {_process.ExitCode}）";
                    }
                    AppendLog("[错误] 启动失败：" + LastError);
                    return false;
                }

                RunningChanged?.Invoke(this, true);
                StartStatsPolling();
                if (AppSession.Instance.EnableSystemProxy)
                {
                    SystemProxyManager.EnableProxy("127.0.0.1", 2080);
                    AppendLog("[SystemProxy] 系统代理已开启");
                }
                else if (AppSession.Instance.EnableTunMode)
                {
                    SystemProxyManager.DisableProxy();
                    AppendLog("[TUN] TUN 模式已激活");
                }
                else
                {
                    SystemProxyManager.DisableProxy();
                    AppendLog("[Local] 仅本地代理运行");
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AppendLog("[异常] " + ex.Message);
                return false;
            }
            } // Close the outer try block
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _stateLock.WaitAsync();
            try
            {
                await StopInternalAsync();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private Task StopInternalAsync()
        {
            StopStatsPolling();
            if (AppSession.Instance.EnableTunMode)
            {
                var tunService = new TunService();
                tunService.CleanupTunRoutes(AppSession.Instance.LastTunServerHost);
            }
            else
            {
                SystemProxyManager.DisableProxy();
            }

            if (_process is null)
            {
                // Fallback simulation state
                AppendLog("[系统] 仿真引擎已停止...");
                RunningChanged?.Invoke(this, false);
                return Task.CompletedTask;
            }

            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);

                // Wait up to 3 s; if the process still hasn't exited, abandon it and move on
                // so we never block the caller indefinitely.
                if (!_process.WaitForExit(3000))
                {
                    AppendLog("[警告] sing-box 进程未在 3 秒内退出，强制放弃等待。");
                }
            }
            catch { }
            finally
            {
                _process.Dispose();
                _process = null;
            }

            AppendLog("[已停止]");
            RunningChanged?.Invoke(this, false);
            return Task.CompletedTask;
        }

        private void StartStatsPolling()
        {
            _statsCts?.Cancel();
            _statsCts = new CancellationTokenSource();
            _lastDown = 0;
            _lastUp = 0;
            _lastStatsTime = DateTime.Now;

            // Seed initial baseline to avoid sudden traffic spikes
            var (initDown, initUp) = GetCurrentNetworkBytes();
            _lastDown = initDown;
            _lastUp = initUp;

            _ = Task.Run(async () =>
            {
                var token = _statsCts.Token;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, token);
                        
                        var (down, up) = GetCurrentNetworkBytes();
                        var now = DateTime.Now;
                        var elapsed = (now - _lastStatsTime).TotalSeconds;
                        if (elapsed <= 0) elapsed = 1;

                        long downSpeed = _lastDown > 0 ? (long)Math.Max(0, (down - _lastDown) / elapsed) : 0;
                        long upSpeed = _lastUp > 0 ? (long)Math.Max(0, (up - _lastUp) / elapsed) : 0;

                        _lastDown = down;
                        _lastUp = up;
                        _lastStatsTime = now;

                        TrafficUpdated?.Invoke(this, (down, up, downSpeed, upSpeed));
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch { }
                }
            });
        }

        private void StopStatsPolling()
        {
            _statsCts?.Cancel();
            _statsCts = null;
        }

        /// <summary>
        /// Returns the current cumulative received/sent byte counts across all active non-loopback NICs.
        /// Made public so TrafficViewModel can snapshot a baseline when a proxy session starts.
        /// </summary>
        public static (long received, long sent) GetCurrentNetworkBytes()
        {
            long received = 0, sent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    var stats = nic.GetIPStatistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch { }
            }
            return (received, sent);
        }
    }
}
