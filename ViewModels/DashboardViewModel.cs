using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AnywhereWinUI.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isCoreRunning;

        [ObservableProperty]
        private string _coreStatusText = "未连接";

        [ObservableProperty]
        private string _selectedMode = "规则 (Rule)";

        [ObservableProperty]
        private int _selectedModeIndex = GetInitialRoutingModeIndex();

        private static int GetInitialRoutingModeIndex()
        {
            return AppSession.Instance.RoutingMode switch
            {
                "global" => 1,
                "direct" => 2,
                _ => 0
            };
        }

        partial void OnSelectedModeIndexChanged(int value)
        {
            string newMode = value switch
            {
                1 => "global",
                2 => "direct",
                _ => "smart"
            };

            if (AppSession.Instance.RoutingMode == newMode) return;

            AppSession.Instance.RoutingMode = newMode;
            LocalSettingsHelper.SetValue("routingMode", newMode);
            
            // Notify ServersPage if it is listening
            if (CoreManager.Instance.IsRunning)
            {
                CoreManager.Instance.AppendLog($"[系统] 路由模式切换为 {newMode}，正在重新加载配置...");
                _ = TriggerCoreRestartIfNeeded();
            }
        }

        [ObservableProperty]
        private string _currentNodeName = "未选择节点";

        [ObservableProperty]
        private string _currentLatency = "- ms";

        [ObservableProperty]
        private string _currentIp = "-";

        [ObservableProperty]
        private string _uploadSpeed = "0.0 KB/s";

        [ObservableProperty]
        private string _downloadSpeed = "0.0 KB/s";

        [ObservableProperty]
        private string _subUsed = "0G";

        [ObservableProperty]
        private string _subTotal = "0G";

        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        public ObservableCollection<double> UploadTraffic { get; } = new();
        public ObservableCollection<double> DownloadTraffic { get; } = new();

        public DashboardViewModel()
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            for (int i = 0; i < 60; i++)
            {
                UploadTraffic.Add(0);
                DownloadTraffic.Add(0);
            }

            _isCoreRunning = CoreManager.Instance.IsRunning;
            _coreStatusText = _isCoreRunning ? "已连接" : "未连接";

            CoreManager.Instance.RunningChanged += CoreManager_RunningChanged;
            CoreManager.Instance.TrafficUpdated += CoreManager_TrafficUpdated;

            UpdateNodeInfo();
        }

        private void CoreManager_RunningChanged(object? sender, bool isRunning)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsCoreRunning = isRunning;
                CoreStatusText = isRunning ? "已连接" : "未连接";
                if (isRunning)
                {
                    UpdateNodeInfo();
                }
                else
                {
                    UploadSpeed = "0.0 KB/s";
                    DownloadSpeed = "0.0 KB/s";
                }
            });
        }

        private void CoreManager_TrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                DownloadSpeed = FormatBytes(e.DownSpeed) + "/s";
                UploadSpeed = FormatBytes(e.UpSpeed) + "/s";

                UploadTraffic.Add(e.UpSpeed);
                DownloadTraffic.Add(e.DownSpeed);

                if (UploadTraffic.Count > 60) UploadTraffic.RemoveAt(0);
                if (DownloadTraffic.Count > 60) DownloadTraffic.RemoveAt(0);
            });
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
                if (counter >= suffixes.Length - 1) break;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private void UpdateNodeInfo()
        {
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
            if (node != null)
            {
                CurrentNodeName = node.Name;
                CurrentLatency = "- ms"; // Requires active testing
                CurrentIp = "-"; // Requires geoip lookup
            }
            else
            {
                CurrentNodeName = "未选择节点";
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SubUsedPercent))]
        [NotifyPropertyChangedFor(nameof(SubProgressBrush))]
        private double _subProgress = 10.0;

        /// <summary>Formatted percentage string for the subscription usage bar label.</summary>
        public string SubUsedPercent => $"{SubProgress:F0}%";

        /// <summary>
        /// Color-coded brush for the ProgressBar:
        ///   green  (< 60%)  — plenty of quota left
        ///   amber  (60-80%) — getting low
        ///   red    (≥ 80%)  — nearly exhausted
        /// </summary>
        public SolidColorBrush SubProgressBrush => SubProgress switch
        {
            < 60 => new SolidColorBrush(Color.FromArgb(255, 16,  185, 129)), // #10B981 emerald
            < 80 => new SolidColorBrush(Color.FromArgb(255, 245, 158,  11)), // #F59E0B amber
            _    => new SolidColorBrush(Color.FromArgb(255, 239,  68,  68))  // #EF4444 red
        };

        [ObservableProperty]
        private string _subRemainingDays = "15 天";

        [ObservableProperty]
        private int _proxyModeIndex = AppSession.Instance.ProxyModeIndex;

        partial void OnProxyModeIndexChanged(int value)
        {
            if (AppSession.Instance.ProxyModeIndex == value) return;

            if (value == 1)
            {
                _ = HandleTunToggleAsync();
            }
            else
            {
                AppSession.Instance.ProxyModeIndex = value;
                LocalSettingsHelper.SetValue("proxyModeIndex", value);
                LocalSettingsHelper.SetValue("enableTunMode", false);
                _ = TriggerCoreRestartIfNeeded();
            }
        }

        private async Task HandleTunToggleAsync()
        {
            bool isAdmin = AdminHelper.IsAdministrator();

            if (!isAdmin)
            {
                // Ensure we run on UI thread with a short delay to let any UI transitions finish
                var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                var dispatcher = MainWindow.Instance?.DispatcherQueue;
                
                if (dispatcher == null)
                {
                    ProxyModeIndex = AppSession.Instance.ProxyModeIndex;
                    return;
                }

                dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                {
                    try
                    {
                        // Short delay to let the Segmented control animation settle
                        await Task.Delay(150);

                        var xamlRoot = MainWindow.Instance?.Content?.XamlRoot;
                        if (xamlRoot == null)
                        {
                            ProxyModeIndex = AppSession.Instance.ProxyModeIndex;
                            tcs.SetResult(false);
                            return;
                        }

                        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                        {
                            Title = "需要管理员权限",
                            Content = "开启 TUN 模式需要管理员权限以创建虚拟网卡和接管系统路由。\n即将请求 UAC 提权并以管理员身份重启客户端。",
                            PrimaryButtonText = "确认提权",
                            CloseButtonText = "取消",
                            XamlRoot = xamlRoot
                        };

                        var result = await dialog.ShowAsync();
                        if (result != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                        {
                            ProxyModeIndex = AppSession.Instance.ProxyModeIndex;
                            tcs.SetResult(false);
                            return;
                        }

                        bool wasRunning = CoreManager.Instance.IsRunning;
                        if (wasRunning)
                        {
                            await CoreManager.Instance.StopAsync();
                        }

                        string arg = wasRunning ? "--tun-start" : "--tun";
                        bool restarted = AdminHelper.RestartAsAdmin(arg);
                        if (!restarted)
                        {
                            ProxyModeIndex = AppSession.Instance.ProxyModeIndex;
                        }
                        tcs.SetResult(restarted);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TUN] Failed to show admin dialog: {ex.Message}");
                        ProxyModeIndex = AppSession.Instance.ProxyModeIndex;
                        tcs.SetResult(false);
                    }
                });

                await tcs.Task;
            }
            else
            {
                AppSession.Instance.ProxyModeIndex = 1;
                LocalSettingsHelper.SetValue("proxyModeIndex", 1);
                LocalSettingsHelper.SetValue("enableTunMode", true);
                _ = TriggerCoreRestartIfNeeded();
            }
        }

        private async Task TriggerCoreRestartIfNeeded()
        {
            if (CoreManager.Instance.IsRunning)
            {
                CoreManager.Instance.AppendLog("[系统] 检测到代理模式更新，正在自动重启代理引擎...");
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                string realConfig = node != null ? ConfigBuilder.Build(node) : ConfigBuilder.Build();
                await CoreManager.Instance.StopAsync();
                await CoreManager.Instance.StartAsync(realConfig);
            }
        }

        [RelayCommand]
        private async Task ToggleCoreAsync()
        {
            if (CoreManager.Instance.IsRunning)
            {
                await CoreManager.Instance.StopAsync();
            }
            else
            {
                // Select default node if none selected
                if (string.IsNullOrEmpty(NodesManager.Instance.SelectedNodeId) && NodesManager.Instance.Nodes.Count > 0)
                {
                    NodesManager.Instance.SelectedNodeId = NodesManager.Instance.Nodes[0].Id;
                    NodesManager.Instance.Save();
                }

                if (AppSession.Instance.ProxyModeIndex == 1 && !AdminHelper.IsAdministrator())
                {
                    _ = HandleTunToggleAsync();
                    return;
                }

                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                string config = node != null ? ConfigBuilder.Build(node) : ConfigBuilder.Build();
                
                bool success = await CoreManager.Instance.StartAsync(config);
                if (!success)
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "启动失败",
                        Content = "代理引擎启动失败，请检查配置。\n详细信息请查看日志页面。",
                        CloseButtonText = "确定",
                        XamlRoot = MainWindow.Instance?.Content.XamlRoot
                    };
                    _ = dialog.ShowAsync();
                }
            }
        }

        [RelayCommand]
        private void RefreshSub()
        {
            // Mock refresh action
        }
    }
}
