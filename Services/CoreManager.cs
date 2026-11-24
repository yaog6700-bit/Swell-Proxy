using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Services
{
    public sealed class CoreManager
    {
        private static readonly Lazy<CoreManager> _instance = new(() => new CoreManager());
        public static CoreManager Instance => _instance.Value;

        public string ExePath => CoreUpdateService.ResolveSingboxExePath();
        public string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "singbox_config.json"
        );
        public string ShoesConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwellProxy",
            "runtime_shoes.yaml"
        );

        private const int LogBufferMax = 500;
        private readonly string[] _logBuffer = new string[LogBufferMax];
        private int _logHead;
        private int _logCount;
        private readonly object _bufferLock = new();

        private Process? _process;
        private bool _isSimulating;
        private readonly StringBuilder _startupLog = new();
        private readonly object _startupLogLock = new();
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        public bool IsRunning => _isSimulating || _process is { HasExited: false };
        public string LastError { get; private set; } = string.Empty;
        public SystemProxyTamperResult LastProxyTamperState { get; private set; } = SystemProxyTamperResult.Ok();

        public event EventHandler<string>? LogReceived;
        public event EventHandler<SystemProxyTamperResult>? SystemProxyTampered;

        public void RetakeSystemProxy()
        {
            if (SystemProxyManager.RetakeProxy())
            {
                LastProxyTamperState = SystemProxyTamperResult.Ok(SystemProxyManager.ExpectedServer);
                AppendLog($"[SystemProxy] 已重新接管系统代理 ({SystemProxyManager.ExpectedServer})");
                SystemProxyTampered?.Invoke(this, LastProxyTamperState);
            }
        }
        public event EventHandler<bool>? RunningChanged;
        public event EventHandler<(long Down, long Up, long DownSpeed, long UpSpeed)>? TrafficUpdated;
        public event EventHandler? ActiveNodeChanged;
        public event EventHandler? ModeStateChanged;

        private volatile bool _isTrayPanelOpen;
        private volatile bool _desiredRunning;
        private volatile string _activeRunningOutboundTag = string.Empty;
        private int _isHandlingResume;

        /// <summary>
        /// Automatically heals proxy state when Windows resumes from sleep / hibernation (WM_POWERBROADCAST):
        /// - Restarts core if it exited during suspend
        /// - Closes half-open pre-sleep TCP/UDP connections so apps immediately establish fresh sockets
        /// - Verifies and re-applies Windows System Proxy if cleared during network re-init
        /// </summary>
        public async Task HandleSystemResumeAsync()
        {
            if (Interlocked.CompareExchange(ref _isHandlingResume, 1, 0) != 0)
                return;

            try
            {
                // Allow Wi-Fi / Ethernet adapter link & DHCP routes to settle after wake
                await Task.Delay(1800).ConfigureAwait(false);

                if (AdminHelper.IsAdministrator())
                {
                    AutostartManager.UpgradeToElevatedTaskIfNeeded();
                }

                if (!_desiredRunning)
                    return;

                if (!_isSimulating && (_process == null || _process.HasExited))
                {
                    AppendLog("[电源恢复] 检测到系统睡眠唤醒后内核已退出，正在自动重新连接...");
                    var node = NodesManager.Instance.FindNode(NodesManager.Instance.SelectedNodeId);
                    string cfg = await ConfigBuilder.BuildAsync(node).ConfigureAwait(false);
                    await StartAsync(cfg).ConfigureAwait(false);
                    return;
                }

                await SingboxApiClient.Instance.CloseAllConnectionsAsync().ConfigureAwait(false);

                if (SystemProxyManager.IsExpectedActive)
                {
                    var tamper = SystemProxyManager.CheckSystemProxyIntegrity();
                    if (tamper.IsTampered)
                    {
                        RetakeSystemProxy();
                    }
                }

                if (_pollWakeSignal.CurrentCount == 0)
                {
                    try { _pollWakeSignal.Release(); } catch { }
                }

                AppendLog("[电源恢复] 系统从睡眠唤醒，已清理失效旧连接并同步网络状态");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PowerResume] {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isHandlingResume, 0);
            }
        }

        /// <summary>
        /// Called by TrayPanelControl when the left-click quick panel opens or closes.
        /// Wakes stats polling to 1s cadence ONLY while the panel is visibly open on screen,
        /// and drops straight back to 5s zero-allocation hibernation the instant it closes.
        /// </summary>
        public void SetTrayPanelOpen(bool isOpen)
        {
            _isTrayPanelOpen = isOpen;
            if (isOpen && _pollWakeSignal.CurrentCount == 0)
            {
                try { _pollWakeSignal.Release(); } catch { }
            }
        }

        private long _latestDown;
        private long _latestUp;
        private long _latestDownSpeed;
        private long _latestUpSpeed;

        public (long Down, long Up, long DownSpeed, long UpSpeed) LatestTrafficStats =>
            (Interlocked.Read(ref _latestDown),
             Interlocked.Read(ref _latestUp),
             Interlocked.Read(ref _latestDownSpeed),
             Interlocked.Read(ref _latestUpSpeed));

        private CancellationTokenSource? _statsCts;
        private readonly SemaphoreSlim _pollWakeSignal = new(0, 1);
        private DateTime _lastStatsTime;
        private readonly ProxyTrafficTracker _proxyTraffic = new();

        private CoreManager()
        {
            MainWindow.VisibilityToUserChanged += (_, isVisible) =>
            {
                if (isVisible && _pollWakeSignal.CurrentCount == 0)
                {
                    try { _pollWakeSignal.Release(); } catch { }
                }
            };
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
                return await StartLockedAsync(configJson, allowSingleNodeSelfHeal: true);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task<bool> StartLockedAsync(string configJson, bool allowSingleNodeSelfHeal)
        {
            {
                if (IsRunning) await StopInternalAsync();

                // Self-healing: only kill orphaned sing-box / shoes processes belonging to Swell Proxy's own directories,
                // and always dispose Process handles!
                KillOwnedOrphanedCoreProcesses();

                LastError = string.Empty;
                lock (_startupLogLock) { _startupLog.Clear(); }

                // Fail fast with an explicit explanation if the user selected an Xray-exclusive node (e.g. xhttp / VLESS-PQ)
                // instead of starting a broken TCP/H2 fallback that connects locally with 0 internet!
                var selectedBuildNode = AppSession.Instance.LastBuildNode;
                if (!AppSession.Instance.IsActiveNodeCustom && !AppSession.Instance.IsAutoNodeSelect)
                {
                    string? xrayOnlyReason = ConfigBuilder.GetXrayExclusiveIncompatibilityReason(selectedBuildNode);
                    if (xrayOnlyReason != null)
                    {
                        _desiredRunning = false;
                        _activeRunningOutboundTag = string.Empty;
                        LastError = $"当前节点使用了 {xrayOnlyReason}，官方 sing-box 与 Shoes 内核暂不支持该特性，请切换其他传输协议节点（如 Reality TCP / TLS / WS / gRPC / Hysteria 2）。";
                        AppendLog("[错误] " + LastError);
                        return false;
                    }
                }

                // Ensure directory & shared *.srs rule-set files exist
                var localDir = ShoesConfigBuilder.EnsureLocalRuleSetDirectory();

                bool useShoes = string.Equals(
                    AppSession.Instance.ActiveCoreEngine,
                    ShoesConfigBuilder.EngineShoes,
                    StringComparison.OrdinalIgnoreCase);

                string activeExePath = ExePath;
                string activeConfigPath = ConfigPath;

                if (useShoes)
                {
                    var shoesExe = CoreUpdateService.ResolveShoesExePath();
                    if (!File.Exists(shoesExe))
                    {
                        AppSession.Instance.ActiveCoreEngine = ShoesConfigBuilder.EngineSingBox;
                        AppSession.Instance.ActiveEngineFallbackReason = L.Shoes_ReasonNotInstalled;
                        configJson = await ConfigBuilder.BuildAsync(AppSession.Instance.LastBuildNode, forceSingbox: true);
                        useShoes = false;
                    }
                    else
                    {
                        await File.WriteAllTextAsync(ShoesConfigPath, configJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        var (checkOk, checkErr) = await ShoesConfigBuilder.ValidateAndAutoHealAsync(shoesExe, ShoesConfigPath);
                        if (!checkOk)
                        {
                            AppendLog($"[Shoes] 配置预检未通过 ({checkErr})");
                            AppSession.Instance.ActiveCoreEngine = ShoesConfigBuilder.EngineSingBox;
                            AppSession.Instance.ActiveEngineFallbackReason = L.Shoes_ReasonPrecheckFallback;
                            configJson = await ConfigBuilder.BuildAsync(AppSession.Instance.LastBuildNode, forceSingbox: true);
                            useShoes = false;
                        }
                        else
                        {
                            activeExePath = shoesExe;
                            activeConfigPath = ShoesConfigPath;
                            AppSession.Instance.ClashApiSecret = ShoesConfigBuilder.TryExtractClashSecret(configJson) ?? string.Empty;
                            AppSession.Instance.ClearHotSwitchTags();
                        }
                    }
                }

                if (!useShoes)
                {
                    if (!string.IsNullOrEmpty(AppSession.Instance.ActiveEngineFallbackReason))
                    {
                        AppendLog(string.Format(L.Shoes_FallbackLogFmt, AppSession.Instance.ActiveEngineFallbackReason));
                    }

                    // Sync ClashApiSecret & HotSwitchTags from configJson after StopInternalAsync() cleared them
                    SyncClashApiSecretFromConfig(configJson);

                    // Write configuration
                    await File.WriteAllTextAsync(ConfigPath, configJson);

                    // In a real scenario, make sure ExePath exists.
                    // For now, we allow simulation if it is missing, or we can handle it gracefully.
                    if (!File.Exists(ExePath))
                    {
                        // Standalone Simulation fallback for visual UI demo when sing-box is not copied yet
                        _isSimulating = true;
                        _desiredRunning = true;
                        _activeRunningOutboundTag = AppSession.Instance.IsAutoNodeSelect
                            ? "urltest"
                            : NodesManager.Instance.SelectedNodeId;
                        AppendLog("[系统] 未在 Assets 文件夹下检测到 sing-box.exe。启用独立 UI 仿真模式。");
                        AppendLog("[启动] 仿真引擎启动成功...");
                        RunningChanged?.Invoke(this, true);
                        return true;
                    }
                }

                try
                {
                    bool isCustom = !useShoes && AppSession.Instance.IsActiveNodeCustom;
                    string coreTag = useShoes ? "shoes" : "sing-box";
                    string workingDir = useShoes
                        ? (Path.GetDirectoryName(activeExePath) ?? localDir)
                        : (isCustom && !string.IsNullOrEmpty(localDir)
                            ? localDir
                            : Path.GetDirectoryName(ExePath)!);

                    var psi = new ProcessStartInfo
                    {
                        FileName = activeExePath,
                        Arguments = useShoes
                            ? $"--no-reload \"{activeConfigPath}\""
                            : $"run -c \"{activeConfigPath}\"",
                        WorkingDirectory = workingDir,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    _process = proc;
                    bool captureStartupLog = true;

                    proc.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data is null) return;
                        if (captureStartupLog)
                        {
                            lock (_startupLogLock)
                            {
                                if (_startupLog.Length < 16384)
                                    _startupLog.AppendLine(e.Data);
                            }
                        }
                        AppendLog($"[{coreTag}] " + e.Data);
                    };

                    proc.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data is null) return;
                        if (captureStartupLog)
                        {
                            lock (_startupLogLock)
                            {
                                if (_startupLog.Length < 16384)
                                    _startupLog.AppendLine(e.Data);
                            }
                        }
                        AppendLog($"[{coreTag}] " + e.Data);
                    };

                    proc.Exited += (_, _) =>
                    {
                        // Ignore stale exit callbacks from a previous process replaced during restart
                        if (!ReferenceEquals(_process, proc)) return;
                        _process = null;
                        _activeRunningOutboundTag = string.Empty;
                        try { proc.Dispose(); } catch { }

                        AppendLog($"[{coreTag} 进程已退出]");
                        StopStatsPolling();
                        AppSession.Instance.ClashApiSecret = string.Empty;
                        AppSession.Instance.ClearHotSwitchTags();
                        _ = Plugins.PluginManager.Instance.FireAsync(Plugins.PluginTrigger.OnCoreStopped);
                        if (AppSession.Instance.EnableTunMode)
                        {
                            var tunService = new TunService();
                            tunService.CleanupTunRoutes(AppSession.Instance.LastTunServerHost);
                        }
                        if (AppSession.Instance.EnableSystemProxy)
                        {
                            SystemProxyManager.DisableProxyIfOwned();
                            LastProxyTamperState = SystemProxyTamperResult.Ok();
                            SystemProxyTampered?.Invoke(this, LastProxyTamperState);
                        }
                        RunningChanged?.Invoke(this, false);
                    };

                    // Attach the ready signal BEFORE Start() so no output line is missed.
                    int readyPort = isCustom
                        ? (AppSession.Instance.ActiveCustomReadyPort ?? 0)
                        : AppSession.Instance.MixedPort;
                    var readySignal = CoreReadySignal.Attach(_process, readyPort);

                    _process.Start();
                    ChildProcessTracker.AddProcess(_process);
                    ProcessPowerHelper.EnsureCoreUnthrottled(_process);
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();

                    AppendLog($"[启动] {activeExePath}");
                    AppendLog($"[配置] {activeConfigPath}");

                    // Wait for core to confirm readiness via TCP inbound port / ready line.
                    var signalOutcome = await readySignal.WaitAsync(TimeSpan.FromSeconds(3));
                    captureStartupLog = false;

                    if (signalOutcome == CoreReadySignal.Outcome.Exited || _process.HasExited)
                    {
                        _desiredRunning = false;
                        _activeRunningOutboundTag = string.Empty;
                        int exitCode = 0;
                        try { exitCode = proc.ExitCode; } catch { }
                        if (ReferenceEquals(_process, proc)) _process = null;
                        try { proc.Dispose(); } catch { }

                        if (allowSingleNodeSelfHeal &&
                            !useShoes &&
                            !isCustom &&
                            AppSession.Instance.LastBuildNode != null)
                        {
                            string firstErr;
                            lock (_startupLogLock)
                            {
                                firstErr = _startupLog.ToString().Trim();
                                _startupLog.Clear();
                            }
                            AppendLog($"[自愈] 检测到多节点预加载配置异常 ({firstErr})，正在以单节点兼容模式自动重试...");
                            string singleNodeCfg = await ConfigBuilder.BuildAsync(
                                AppSession.Instance.LastBuildNode,
                                forceSingbox: true,
                                singleNodeOnly: true);
                            return await StartLockedAsync(singleNodeCfg, allowSingleNodeSelfHeal: false);
                        }

                        lock (_startupLogLock)
                        {
                            var rawErr = _startupLog.Length > 0
                                ? _startupLog.ToString().Trim()
                                : $"{coreTag} 立即退出（退出码 {exitCode}）";
                            _startupLog.Clear();
                            _startupLog.Capacity = 0;

                            if (!useShoes &&
                                rawErr.Contains("nowhere", StringComparison.OrdinalIgnoreCase) &&
                                rawErr.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                            {
                                rawErr += "\n\n提示：官方原版 sing-box 内核不包含私有 nowhere 协议，请切换其他协议节点或在设置中恢复定制核心。";
                            }

                            LastError = rawErr;
                        }
                        AppendLog("[错误] 启动失败：" + LastError);
                        return false;
                    }

                    // Release startup error buffer immediately once core is up
                    lock (_startupLogLock)
                    {
                        _startupLog.Clear();
                        _startupLog.Capacity = 0;
                    }

                    AppendLog(signalOutcome == CoreReadySignal.Outcome.Ready
                        ? $"[启动] {coreTag} 就绪信号已收到"
                        : $"[启动] 等待超时，假设 {coreTag} 已就绪");

                    SyncClashApiEndpointFromConfig(configJson, useShoes);
                    IpInfoService.ResetPooledConnections();
                    AiUnlockCheckService.ResetPooledConnections();

                    // When FakeDNS cache_file is enabled, sing-box's cache_file restores the previous session's
                    // selector choice via LoadSelected("proxy"); force-sync it to the user's current node!
                    if (!useShoes && !isCustom && AppSession.Instance.EnableFakeDns)
                    {
                        string expectedTag = AppSession.Instance.IsAutoNodeSelect
                            ? "urltest"
                            : NodesManager.Instance.SelectedNodeId;
                        if (!string.IsNullOrEmpty(expectedTag) && AppSession.Instance.CanHotSwitchTo(expectedTag))
                        {
                            _ = await SingboxApiClient.Instance.SelectOutboundAsync("proxy", expectedTag).ConfigureAwait(false);
                        }
                    }

                    // Configure Windows System Proxy (and DNS cache flush) BEFORE firing RunningChanged,
                    // so OS proxy/DNS state is settled before UI triggers AI unlock & exit IP checks.
                    if (isCustom && AppSession.Instance.ActiveCustomSkipSystemProxy)
                    {
                        SystemProxyManager.DisableProxyIfOwned();
                        LastProxyTamperState = SystemProxyTamperResult.Ok();
                        SystemProxyTampered?.Invoke(this, LastProxyTamperState);
                        AppendLog("[CustomConfig] 原生配置直连或未声明本地 HTTP/Mixed 入站，已跳过系统代理修改");
                    }
                    else if (AppSession.Instance.EnableSystemProxy)
                    {
                        int sysProxyPort = isCustom
                            ? (AppSession.Instance.ActiveCustomHttpProxyPort ?? AppSession.Instance.MixedPort)
                            : AppSession.Instance.MixedPort;
                        SystemProxyManager.EnableProxy("127.0.0.1", sysProxyPort);
                        AppendLog($"[SystemProxy] 系统代理已开启 (127.0.0.1:{sysProxyPort})");
                    }
                    else
                    {
                        SystemProxyManager.DisableProxyIfOwned();
                        LastProxyTamperState = SystemProxyTamperResult.Ok();
                        SystemProxyTampered?.Invoke(this, LastProxyTamperState);
                    }

                    if (AppSession.Instance.EnableTunMode || (isCustom && AppSession.Instance.ActiveCustomHasTun))
                    {
                        AppendLog("[TUN] TUN 模式已激活");
                    }
                    else if (!AppSession.Instance.EnableSystemProxy || (isCustom && AppSession.Instance.ActiveCustomSkipSystemProxy))
                    {
                        AppendLog("[Local] 仅本地代理运行");
                    }

                    _desiredRunning = true;
                    _activeRunningOutboundTag = (!useShoes && !isCustom)
                        ? (AppSession.Instance.IsAutoNodeSelect
                            ? "urltest"
                            : (AppSession.Instance.LastBuildNode?.Id ?? NodesManager.Instance.SelectedNodeId))
                        : (AppSession.Instance.LastBuildNode?.Id ?? NodesManager.Instance.SelectedNodeId);

                    StartStatsPolling();
                    if (AdminHelper.IsAdministrator())
                    {
                        AutostartManager.UpgradeToElevatedTaskIfNeeded();
                    }
                    RunningChanged?.Invoke(this, true);
                    _ = Plugins.PluginManager.Instance.FireAsync(Plugins.PluginTrigger.OnCoreStarted);
                    return true;
                }
                catch (Exception ex)
                {
                    _desiredRunning = false;
                    _activeRunningOutboundTag = string.Empty;
                    var failedProc = _process;
                    _process = null;
                    try { failedProc?.Dispose(); } catch { }
                    LastError = ex.Message;
                    AppendLog("[异常] " + ex.Message);
                    return false;
                }
            }
        }

        public async Task StopAsync()
        {
            await _stateLock.WaitAsync();
            try
            {
                _desiredRunning = false;
                await StopInternalAsync();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// Switches to <paramref name="node"/> with &lt;5ms zero-drop Clash API selector hot-switch whenever
        /// sing-box is running and has <paramref name="node"/> loaded in its "proxy" selector;
        /// seamlessly falls back to rebuilding &amp; restarting the core otherwise (e.g. Shoes, Custom JSON, or newly added/edited node).
        /// </summary>
        public async Task<bool> SwitchToNodeAsync(PersistedNode node, bool forceReload = false)
        {
            if (node == null) return false;

            var session = AppSession.Instance;
            string currentRunningTag = _activeRunningOutboundTag;

            session.IsAutoNodeSelect = false;
            LocalSettingsHelper.SetValue("isAutoNodeSelect", false);

            NodesManager.Instance.SelectedNodeId = node.Id;
            NodesManager.Instance.Save();

            session.SelectedNodeName = node.Name;
            session.SelectedNodeProtocol = node.Protocol;
            session.SelectedNodeHost = node.Host;

            if (!IsRunning)
            {
                ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (!forceReload &&
                !string.IsNullOrEmpty(currentRunningTag) &&
                string.Equals(currentRunningTag, node.Id, StringComparison.Ordinal))
            {
                ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            bool canHotSwitch =
                !forceReload &&
                string.Equals(session.ActiveCoreEngine, ShoesConfigBuilder.EngineSingBox, StringComparison.OrdinalIgnoreCase) &&
                !session.IsActiveNodeCustom &&
                !CustomConfigService.IsCustomNode(node) &&
                !string.Equals(session.CoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase) &&
                session.CanHotSwitchTo(node.Id);

            if (canHotSwitch)
            {
                bool switched = await SingboxApiClient.Instance.SelectOutboundAsync("proxy", node.Id).ConfigureAwait(false);
                if (switched)
                {
                    var verifiedNow = await SingboxApiClient.Instance.GetGroupSelectedAsync("proxy").ConfigureAwait(false);
                    if (verifiedNow == null || string.Equals(verifiedNow, node.Id, StringComparison.Ordinal))
                    {
                        _activeRunningOutboundTag = node.Id;

                        // Immediately close existing HTTP/2, QUIC, DoH & pooled keep-alive tunnels from the old node
                        // so both browsers and in-app Exit IP / AI checks route through the new node immediately!
                        await SingboxApiClient.Instance.CloseAllConnectionsAsync().ConfigureAwait(false);
                        IpInfoService.ResetPooledConnections();
                        AiUnlockCheckService.ResetPooledConnections();

                        session.LastBuildNode = node;
                        AppendLog($"[热切换] 已切换至节点：{node.Name}");
                        ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                        return true;
                    }
                }
            }

            string configJson = await ConfigBuilder.BuildAsync(node).ConfigureAwait(false);
            bool started = await StartAsync(configJson).ConfigureAwait(false);
            ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
            return started;
        }

        /// <summary>
        /// Switches the active outbound to "urltest" (Auto: Fastest + Failover), using &lt;5ms zero-drop
        /// hot-switch when sing-box is already running with "urltest" available.
        /// </summary>
        public async Task<bool> EnableAutoNodeSelectAsync()
        {
            var session = AppSession.Instance;
            string currentRunningTag = _activeRunningOutboundTag;

            session.IsAutoNodeSelect = true;
            LocalSettingsHelper.SetValue("isAutoNodeSelect", true);

            if (!IsRunning)
            {
                ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (string.Equals(currentRunningTag, "urltest", StringComparison.Ordinal) &&
                string.Equals(session.ActiveCoreEngine, ShoesConfigBuilder.EngineSingBox, StringComparison.OrdinalIgnoreCase) &&
                !session.IsActiveNodeCustom &&
                session.CanHotSwitchTo("urltest"))
            {
                ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            bool canHotSwitch =
                string.Equals(session.ActiveCoreEngine, ShoesConfigBuilder.EngineSingBox, StringComparison.OrdinalIgnoreCase) &&
                !session.IsActiveNodeCustom &&
                session.CanHotSwitchTo("urltest");

            if (canHotSwitch)
            {
                bool switched = await SingboxApiClient.Instance.SelectOutboundAsync("proxy", "urltest").ConfigureAwait(false);
                if (switched)
                {
                    _activeRunningOutboundTag = "urltest";
                    await SingboxApiClient.Instance.CloseAllConnectionsAsync().ConfigureAwait(false);
                    IpInfoService.ResetPooledConnections();
                    AiUnlockCheckService.ResetPooledConnections();

                    AppendLog("[热切换] 已切换至自动选点（最快 + 故障转移）");
                    ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }

            var fallbackNode = NodesManager.Instance.FindNode(NodesManager.Instance.SelectedNodeId);
            string configJson = await ConfigBuilder.BuildAsync(fallbackNode).ConfigureAwait(false);
            bool started = await StartAsync(configJson).ConfigureAwait(false);
            ActiveNodeChanged?.Invoke(this, EventArgs.Empty);
            return started;
        }

        public async Task SetRoutingModeAsync(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return;
            var session = AppSession.Instance;
            if (string.Equals(session.RoutingMode, mode, StringComparison.OrdinalIgnoreCase))
                return;

            session.RoutingMode = mode;
            LocalSettingsHelper.SetValue("routingMode", mode);
            ModeStateChanged?.Invoke(this, EventArgs.Empty);

            if (IsRunning)
            {
                AppendLog($"[系统] 路由模式切换为 {mode}，正在重新加载配置...");
                var node = NodesManager.Instance.GetNodesSnapshot().Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                string realConfig = await ConfigBuilder.BuildAsync(node).ConfigureAwait(false);
                await StartAsync(realConfig).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Toggles Windows System Proxy without restarting sing-box (mixed-in is always listening on MixedPort!).
        /// </summary>
        public void SetSystemProxyEnabled(bool enable)
        {
            var session = AppSession.Instance;
            bool hasTun = session.EnableTunMode;
            int newModeIndex = enable
                ? (hasTun ? 3 : 0)
                : (hasTun ? 1 : 2);

            if (session.ProxyModeIndex == newModeIndex)
                return;

            session.ProxyModeIndex = newModeIndex;
            LocalSettingsHelper.SetValue("proxyModeIndex", newModeIndex);

            if (IsRunning && !(session.IsActiveNodeCustom && session.ActiveCustomSkipSystemProxy))
            {
                if (enable)
                {
                    int sysProxyPort = session.IsActiveNodeCustom
                        ? (session.ActiveCustomHttpProxyPort ?? session.MixedPort)
                        : session.MixedPort;
                    SystemProxyManager.EnableProxy("127.0.0.1", sysProxyPort);
                    LastProxyTamperState = SystemProxyTamperResult.Ok(SystemProxyManager.ExpectedServer);
                    SystemProxyTampered?.Invoke(this, LastProxyTamperState);
                    AppendLog($"[SystemProxy] 系统代理已开启 (127.0.0.1:{sysProxyPort})");
                }
                else
                {
                    SystemProxyManager.DisableProxyIfOwned();
                    LastProxyTamperState = SystemProxyTamperResult.Ok();
                    SystemProxyTampered?.Invoke(this, LastProxyTamperState);
                    AppendLog("[SystemProxy] 系统代理已关闭");
                }
            }

            ModeStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyModeStateChanged()
        {
            ModeStateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal static void KillOwnedOrphanedCoreProcesses()
        {
            string appDir = Path.GetFullPath(AppContext.BaseDirectory);
            string dataDir = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy"));

            foreach (var coreName in new[] { "sing-box", "shoes" })
            {
                Process[]? procs = null;
                try
                {
                    procs = Process.GetProcessesByName(coreName);
                    foreach (var p in procs)
                    {
                        try
                        {
                            string? exeFile = null;
                            try { exeFile = p.MainModule?.FileName; } catch { }
                            bool isOwned = string.IsNullOrEmpty(exeFile) ||
                                           exeFile.StartsWith(appDir, StringComparison.OrdinalIgnoreCase) ||
                                           exeFile.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase);
                            if (isOwned)
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static void SyncClashApiSecretFromConfig(string configJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(configJson, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                var root = doc.RootElement;
                if (root.TryGetProperty("experimental", out var exp) &&
                    exp.TryGetProperty("clash_api", out var clashApi))
                {
                    if (clashApi.TryGetProperty("secret", out var secretProp) &&
                        secretProp.ValueKind == JsonValueKind.String)
                    {
                        AppSession.Instance.ClashApiSecret = secretProp.GetString() ?? string.Empty;
                    }
                    if (clashApi.TryGetProperty("external_controller", out var ctrlProp) &&
                        ctrlProp.ValueKind == JsonValueKind.String)
                    {
                        SingboxApiClient.Instance.UpdateControllerEndpoint(ctrlProp.GetString());
                    }
                    else
                    {
                        SingboxApiClient.Instance.UpdateControllerEndpoint("127.0.0.1:9090");
                    }
                }

                // Re-populate HotSwitchTags directly from the actual "proxy" selector outbound in configJson,
                // because StopInternalAsync() clears HotSwitchTags when restarting/reloading sing-box!
                if (!AppSession.Instance.IsActiveNodeCustom &&
                    root.TryGetProperty("outbounds", out var outboundsEl) &&
                    outboundsEl.ValueKind == JsonValueKind.Array)
                {
                    var extractedTags = new List<string>();
                    foreach (var ob in outboundsEl.EnumerateArray())
                    {
                        if (ob.ValueKind != JsonValueKind.Object) continue;
                        string? tag = ob.TryGetProperty("tag", out var tProp) ? tProp.GetString() : null;
                        string? type = ob.TryGetProperty("type", out var tyProp) ? tyProp.GetString() : null;
                        if (string.Equals(tag, "proxy", StringComparison.Ordinal) &&
                            string.Equals(type, "selector", StringComparison.OrdinalIgnoreCase) &&
                            ob.TryGetProperty("outbounds", out var members) &&
                            members.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in members.EnumerateArray())
                            {
                                if (m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } memberTag)
                                {
                                    extractedTags.Add(memberTag);
                                }
                            }
                            break;
                        }
                    }

                    if (extractedTags.Count > 0)
                    {
                        AppSession.Instance.SetHotSwitchTags(extractedTags);
                    }
                    else
                    {
                        AppSession.Instance.ClearHotSwitchTags();
                    }
                }
                else
                {
                    AppSession.Instance.ClearHotSwitchTags();
                }
            }
            catch { }
        }

        private static void SyncClashApiEndpointFromConfig(string configJson, bool useShoes)
        {
            if (useShoes)
            {
                SingboxApiClient.Instance.UpdateControllerEndpoint("127.0.0.1:9090");
            }
            else
            {
                SyncClashApiSecretFromConfig(configJson);
            }
        }

        private async Task StopInternalAsync()
        {
            var proc = _process;
            _process = null;
            _activeRunningOutboundTag = string.Empty;
            bool wasSimulating = _isSimulating;
            _isSimulating = false;

            StopStatsPolling();
            Interlocked.Exchange(ref _latestDown, 0);
            Interlocked.Exchange(ref _latestUp, 0);
            Interlocked.Exchange(ref _latestDownSpeed, 0);
            Interlocked.Exchange(ref _latestUpSpeed, 0);
            AppSession.Instance.ClashApiSecret = string.Empty;
            AppSession.Instance.ClearHotSwitchTags();
            SystemProxyManager.DisableProxyIfOwned();
            LastProxyTamperState = SystemProxyTamperResult.Ok();
            SystemProxyTampered?.Invoke(this, LastProxyTamperState);

            if (proc is null)
            {
                if (wasSimulating)
                {
                    AppendLog("[系统] 仿真引擎已停止...");
                }
                RunningChanged?.Invoke(this, false);
                return;
            }

            try
            {
                if (!proc.HasExited)
                {
                    // Allow OnBeforeCoreStop plugins up to 800ms before killing the core process
                    try
                    {
                        await Plugins.PluginManager.Instance
                            .FireAsync(Plugins.PluginTrigger.OnBeforeCoreStop)
                            .WaitAsync(TimeSpan.FromMilliseconds(800));
                    }
                    catch { }

                    proc.Kill(entireProcessTree: true);
                }

                if (!proc.WaitForExit(3000))
                {
                    AppendLog("[警告] sing-box 进程未在 3 秒内退出，强制放弃等待。");
                }
            }
            catch { }
            finally
            {
                proc.Dispose();
            }

            if (AppSession.Instance.EnableTunMode)
            {
                var tunService = new TunService();
                tunService.CleanupTunRoutes(AppSession.Instance.LastTunServerHost);
            }

            AppendLog("[已停止]");
            RunningChanged?.Invoke(this, false);
        }

        private void StartStatsPolling()
        {
            StopStatsPolling();
            var cts = new CancellationTokenSource();
            _statsCts = cts;
            _proxyTraffic.Reset();
            _lastStatsTime = DateTime.Now;
            bool seeded = false;

            // Drain any stale wake signal before starting
            while (_pollWakeSignal.CurrentCount > 0)
            {
                try { _pollWakeSignal.Wait(0); } catch { break; }
            }

            _ = Task.Run(async () =>
            {
                var token = cts.Token;
                int proxyCheckElapsedMs = 0;
                LastProxyTamperState = SystemProxyTamperResult.Ok();
                // Brief wait so clash_api is listening after ready signal.
                try { await Task.Delay(400, token); } catch (TaskCanceledException) { return; }

                while (!token.IsCancellationRequested)
                {
                    bool isVisible = MainWindow.IsVisibleToUser || _isTrayPanelOpen;
                    int delayMs = isVisible ? 1000 : 5000;

                    try
                    {
                        var now = DateTime.Now;
                        double elapsed = (now - _lastStatsTime).TotalSeconds;
                        if (elapsed <= 0) elapsed = 1;

                        if (SingboxApiClient.Instance.HasActiveVisualConsumers)
                        {
                            // Full object snapshot ONLY when window is visible and ConnectionsPage / TopologyGraph is open
                            var message = await SingboxApiClient.Instance.FetchConnectionsAsync(token).ConfigureAwait(false);
                            if (message != null)
                            {
                                _lastStatsTime = now;
                                (long down, long up, long downSpeed, long upSpeed) stats;
                                if (!seeded)
                                {
                                    _proxyTraffic.Seed(message);
                                    seeded = true;
                                    stats = (0, 0, 0, 0);
                                }
                                else
                                {
                                    stats = _proxyTraffic.Apply(message, elapsed);
                                }

                                RecordAndPublishStats(stats);
                                SingboxApiClient.Instance.PublishConnectionsSnapshot(message);
                            }
                        }
                        else
                        {
                            // Zero-allocation Utf8JsonReader path (used in tray hibernation AND on non-connection pages)
                            var statsOpt = await SingboxApiClient.Instance.FetchAndApplyTrafficZeroAllocAsync(
                                _proxyTraffic,
                                seedOnly: !seeded,
                                elapsedSeconds: elapsed,
                                token: token).ConfigureAwait(false);

                            if (statsOpt.HasValue)
                            {
                                _lastStatsTime = now;
                                seeded = true;
                                RecordAndPublishStats(statsOpt.Value);
                            }
                        }

                        // Check Windows System Proxy integrity every ~3s when visible, or every ~15s when hibernating in tray
                        proxyCheckElapsedMs += delayMs;
                        int targetProxyCheckMs = isVisible ? 3000 : 15000;
                        if (proxyCheckElapsedMs >= targetProxyCheckMs)
                        {
                            proxyCheckElapsedMs = 0;
                            if (SystemProxyManager.IsExpectedActive)
                            {
                                CheckAndNotifySystemProxyTamper();
                            }
                        }

                        await _pollWakeSignal.WaitAsync(delayMs, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        proxyCheckElapsedMs += delayMs;
                        int targetProxyCheckMs = isVisible ? 3000 : 15000;
                        if (proxyCheckElapsedMs >= targetProxyCheckMs)
                        {
                            proxyCheckElapsedMs = 0;
                            if (SystemProxyManager.IsExpectedActive)
                            {
                                CheckAndNotifySystemProxyTamper();
                            }
                        }
                        try { await _pollWakeSignal.WaitAsync(delayMs, token).ConfigureAwait(false); } catch { break; }
                    }
                }
            });
        }

        private void RecordAndPublishStats((long Down, long Up, long DownSpeed, long UpSpeed) stats)
        {
            Interlocked.Exchange(ref _latestDown, stats.Down);
            Interlocked.Exchange(ref _latestUp, stats.Up);
            Interlocked.Exchange(ref _latestDownSpeed, stats.DownSpeed);
            Interlocked.Exchange(ref _latestUpSpeed, stats.UpSpeed);
            TrafficUpdated?.Invoke(this, stats);
        }

        private void CheckAndNotifySystemProxyTamper()
        {
            var tamper = SystemProxyManager.CheckSystemProxyIntegrity();
            if (tamper != LastProxyTamperState)
            {
                LastProxyTamperState = tamper;
                if (tamper.IsTampered)
                {
                    string detail = tamper.Kind == SystemProxyTamperKind.DisabledByExternal
                        ? "已被外部程序关闭"
                        : $"已被外部程序修改为 {tamper.ActualServer}";
                    AppendLog($"[SystemProxy] 警告：系统代理{detail}");
                }
                SystemProxyTampered?.Invoke(this, tamper);
            }
        }

        private void StopStatsPolling()
        {
            var old = Interlocked.Exchange(ref _statsCts, null);
            if (old == null) return;
            try { old.Cancel(); } catch { }
            try { old.Dispose(); } catch { }
        }

        /// <summary>
        /// Legacy NIC-wide counters. Prefer Clash API totals via TrafficUpdated.
        /// Kept for any external callers that still snapshot baselines.
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
