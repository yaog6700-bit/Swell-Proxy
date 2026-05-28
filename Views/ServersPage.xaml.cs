using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using WinUIEx;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;
using QRCoder;

namespace AnywhereWinUI.Views
{
    public sealed class ServerEntryItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public string Id { get; set; } = string.Empty;
        public string SubscriptionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 443;
        public string Network { get; set; } = string.Empty;

        public string HostPortDisplay => $"{Host}:{Port}";

        private string _pingText = "未测速";
        public string PingText
        {
            get => _pingText;
            set
            {
                if (_pingText != value)
                {
                    _pingText = value;
                    OnPropertyChanged();
                }
            }
        }

        private Brush _pingColor = new SolidColorBrush(ColorHelper.FromArgb(255, 156, 163, 175));
        public Brush PingColor
        {
            get => _pingColor;
            set
            {
                if (_pingColor != value)
                {
                    _pingColor = value;
                    OnPropertyChanged();
                }
            }
        }

        private Visibility _activeIndicatorVisibility = Visibility.Collapsed;
        public Visibility ActiveIndicatorVisibility
        {
            get => _activeIndicatorVisibility;
            set
            {
                if (_activeIndicatorVisibility != value)
                {
                    _activeIndicatorVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        private Visibility _favoriteVisibility = Visibility.Collapsed;
        public Visibility FavoriteVisibility
        {
            get => _favoriteVisibility;
            set
            {
                if (_favoriteVisibility != value)
                {
                    _favoriteVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isFavorite = false;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayProtocol => Protocol.ToUpper();

        public Brush ProtocolBrush
        {
            get
            {
                string hex = Protocol.ToUpper() switch
                {
                    "VLESS" => "#34D399",
                    "VMESS" => "#A78BFA",
                    "SHADOWSOCKS" => "#60A5FA",
                    "TROJAN" => "#F87171",
                    "HYSTERIA 2" => "#FB923C",
                    "HYSTERIA" => "#FB923C",
                    "TUIC" => "#06B6D4",
                    "NAIVEPROXY" => "#EC4899",
                    "NAIVE" => "#EC4899",
                    "SOCKS" => "#64748B",
                    "ANYTLS" => "#EAB308",
                    "SNELL" => "#C084FC",
                    _ => "#94A8A0"
                };
                return ParseColorBrush(hex);
            }
        }

        private static Brush ParseColorBrush(string hex)
        {
            try
            {
                hex = hex.Replace("#", "");
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
    }

    public sealed partial class ServersPage : Page
    {
        public ServersViewModel ViewModel { get; }

        private ObservableCollection<ServerEntryItem> AllServers => ViewModel.AllServers;
        private ObservableCollection<ServerEntryItem> FilteredServers => ViewModel.FilteredServers;
        private ObservableCollection<PersistedSubscription> Subscriptions { get; } = new();

        private string _editingNodeId = string.Empty;
        private string _lastSelectedNodeId = string.Empty;
        private string _activeIpAddress = "-";
        private bool _isIpMasked = true;

        private CancellationTokenSource? _aiCheckCts;
        private readonly LatencyProbeService _latencyProbeService = new();

        // QR share card state
        private bool _isQrCardVisible = false;
        private string _currentShareUrl = string.Empty;
        private DispatcherTimer? _copyResetTimer;

        // Real Data Collections for TopSheet Chart
        private System.Collections.ObjectModel.ObservableCollection<double> _realUpTraffic = new();
        private System.Collections.ObjectModel.ObservableCollection<double> _realDownTraffic = new();
        private DateTime _sessionStartTime;
        private int _trafficTickCount = 0;
        private bool _isTopSheetOpen = false;
        private bool _isLatencyProbing = false;

        public ServersPage()
        {
            ViewModel = App.Current.Services.GetRequiredService<ServersViewModel>();
            this.DataContext = ViewModel;

            this.InitializeComponent();
            ServersListView.ItemsSource = ViewModel.FilteredServers;
            
            CoreManager.Instance.RunningChanged += CoreManager_RunningChanged;
            CoreManager.Instance.TrafficUpdated += CoreManager_TrafficUpdated;
            this.Unloaded += (s, e) =>
            {
                CoreManager.Instance.RunningChanged -= CoreManager_RunningChanged;
                CoreManager.Instance.TrafficUpdated -= CoreManager_TrafficUpdated;
            };

            UpdateControlBarUI();
            UpdateProxyModeUI();
            UpdateRoutingModeUI();
        }

        private void UpdateProxyModeUI()
        {
            var mode = AppSession.Instance.ProxyModeIndex;
            if (mode == 0) {
                ProxyModeIcon.Glyph = "\uEC27";
                ToolTipService.SetToolTip(ProxyModeButton, "系统代理");
            } else if (mode == 1) {
                ProxyModeIcon.Glyph = "\uE839";
                ToolTipService.SetToolTip(ProxyModeButton, "TUN模式");
            } else {
                ProxyModeIcon.Glyph = "\uF384";
                ToolTipService.SetToolTip(ProxyModeButton, "仅手动代理");
            }
        }

        private void UpdateRoutingModeUI()
        {
            var mode = AppSession.Instance.RoutingMode;
            bool isTun = AppSession.Instance.ProxyModeIndex == 1;
            
            string modeName = mode == "direct" ? "直接连接" : mode == "global" ? "全局路由" : "智能分流";
            ToolTipService.SetToolTip(RoutingModeButton, isTun ? $"TUN ({modeName})" : modeName);

            switch (mode)
            {
                case "smart":
                    RoutingModeIcon.Glyph = "\uE909";
                    break;
                case "global":
                    RoutingModeIcon.Glyph = "\uE12B";
                    break;
                case "direct":
                    RoutingModeIcon.Glyph = "\uEC27";
                    break;
            }
        }

        private void CoreManager_RunningChanged(object? sender, bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (isRunning)
                {
                    _sessionStartTime = DateTime.Now;
                    _trafficTickCount = 0;
                    _realUpTraffic.Clear();
                    _realDownTraffic.Clear();
                    // 更新节点名称
                    var nodeName = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? "---";
                    TopSheetNodeNameText.Text = nodeName;
                }
                else
                {
                    // Bug 3: 停止后重置 TopSheet 所有显示值
                    TopSheetUptimeText.Text = "00:00:00";
                    TopSheetDownText.Text = "0 B/s";
                    TopSheetUpText.Text = "0 B/s";
                    TopSheetLatencyText.Text = "--- ms";
                    TopSheetNodeNameText.Text = "---";
                }

                string selectedId = NodesManager.Instance.SelectedNodeId;
                foreach (var s in AllServers)
                {
                    s.ActiveIndicatorVisibility = (s.Id == selectedId && isRunning) ? Visibility.Visible : Visibility.Collapsed;
                }
                UpdateControlBarUI();
            });
        }

        private void UpdateControlBarUI()
        {
            bool isRunning = CoreManager.Instance.IsRunning;
            StartStopButton.IsChecked = isRunning;
            if (isRunning)
            {
                if (Application.Current.Resources.TryGetValue("SystemFillColorSuccessBrush", out var sysSucc) && sysSucc is Microsoft.UI.Xaml.Media.Brush sucBrush)
                {
                    StatusDot.Fill = sucBrush;
                }
                else
                {
                    StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                }

                // 恢复显示“概览”按钮（应对切页面后再切回来的情况）
                if (!_isTopSheetOpen)
                {
                    ReopenTopSheetButton.Visibility = Visibility.Visible;
                }

                string selectedName = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? "未知节点";
                ActiveNodeText.Text = selectedName;

                if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var txtPri) && txtPri is Microsoft.UI.Xaml.Media.Brush priBrush)
                {
                    ActiveNodeText.Foreground = priBrush;
                }
                else
                {
                    ActiveNodeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
                }
                
                _ = TriggerAiAndIpChecksAsync();
            }
            else
            {
                if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var txtSec) && txtSec is Microsoft.UI.Xaml.Media.Brush secBrush)
                {
                    StatusDot.Fill = secBrush;
                    ActiveNodeText.Foreground = secBrush;
                }
                else
                {
                    var fallbackBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                    StatusDot.Fill = fallbackBrush;
                    ActiveNodeText.Foreground = fallbackBrush;
                }

                ActiveNodeText.Text = "未连接";
                
                _aiCheckCts?.Cancel();
                UpdateAiAndIpDisplay();
                DownSpeedText.Text = "0.0 KB/s";
                UpSpeedText.Text = "0.0 KB/s";
            }
        }

        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isConnected = CoreManager.Instance.IsRunning;

                if (!isConnected)
                {
                    // 优先使用 ListView 当前选中的节点，而不是上次持久化的旧节点
                    if (ServersListView.SelectedItem is ServerEntryItem selectedItem)
                    {
                        NodesManager.Instance.SelectedNodeId = selectedItem.Id;
                        NodesManager.Instance.Save();
                    }
                    else if (string.IsNullOrEmpty(NodesManager.Instance.SelectedNodeId) && NodesManager.Instance.Nodes.Count > 0)
                    {
                        NodesManager.Instance.SelectedNodeId = NodesManager.Instance.Nodes[0].Id;
                        NodesManager.Instance.Save();
                    }

                    if (AppSession.Instance.ProxyModeIndex == 1 && !Helpers.AdminHelper.IsAdministrator())
                    {
                        StartStopButton.IsChecked = false;
                        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                        var dispatcher = this.DispatcherQueue;
                        
                        dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                        {
                            try
                            {
                                var dialog = new ContentDialog
                                {
                                    Title = "需要管理员权限",
                                    Content = "开启 TUN 模式需要管理员权限以创建虚拟网卡和接管系统路由。\n即将请求 UAC 提权并以管理员身份重启客户端。",
                                    PrimaryButtonText = "确认提权",
                                    CloseButtonText = "取消",
                                    XamlRoot = this.XamlRoot
                                };

                                var result = await dialog.ShowAsync();
                                if (result != ContentDialogResult.Primary)
                                {
                                    tcs.SetResult(false);
                                    return;
                                }

                                bool restarted = Helpers.AdminHelper.RestartAsAdmin("--tun-start");
                                tcs.SetResult(restarted);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[TUN] Failed to show admin dialog in ServersPage: {ex.Message}");
                                tcs.SetResult(false);
                            }
                        });

                        await tcs.Task;
                        return;
                    }

                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    string realConfig = ConfigBuilder.Build(node);

                    bool success = await CoreManager.Instance.StartAsync(realConfig);
                    if (!success)
                    {
                        StartStopButton.IsChecked = false;
                        var dialog = new ContentDialog
                        {
                            Title = "启动失败",
                            Content = "核心启动失败，请检查配置或在日志页面查看详细信息。\n错误信息: " + CoreManager.Instance.LastError,
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        _ = dialog.ShowAsync();
                    }
                    else
                    {
                        // 打开下拉卡片
                        StartStopButton.IsChecked = true;
                        ReopenTopSheetButton.Visibility = Visibility.Collapsed;
                        OpenTopSheet();
                    }
                }
                else
                {
                    CloseTopSheet();
                    StartStopButton.IsChecked = false;
                    ReopenTopSheetButton.Visibility = Visibility.Collapsed;
                    await CoreManager.Instance.StopAsync();
                }
            }
            catch (Exception ex)
            {
                StartStopButton.IsChecked = false;
                var dialog = new ContentDialog
                {
                    Title = "配置生成失败",
                    Content = "生成节点配置时发生异常，可能配置缺失或不兼容。\n" + ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                _ = dialog.ShowAsync();
                System.Diagnostics.Debug.WriteLine($"Start error: {ex}");
            }
        }

        private async void SetRoutingMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string mode)
            {
                AppSession.Instance.RoutingMode = mode;
                Helpers.LocalSettingsHelper.SetValue("routingMode", mode);
                UpdateRoutingModeUI();

                if (CoreManager.Instance.IsRunning)
                {
                    CoreManager.Instance.AppendLog("[系统] 检测到路由模式更新，正在自动重启代理引擎以重新加载配置...");
                    
                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    if (node != null)
                    {
                        string realConfig = ConfigBuilder.Build(node);
                        await CoreManager.Instance.StartAsync(realConfig);
                    }
                }
            }
        }

        private void CoreManager_TrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DownSpeedText.Text = FormatBytes(e.DownSpeed) + "/s";
                UpSpeedText.Text = FormatBytes(e.UpSpeed) + "/s";
                SessionUsageText.Text = FormatBytes(e.Down + e.Up);

                if (CoreManager.Instance.IsRunning)
                {
                    TopSheetUptimeText.Text = (DateTime.Now - _sessionStartTime).ToString(@"hh\:mm\:ss");
                    
                    // Bug 4: 只有面板打开时才写入数据集合
                    if (_isTopSheetOpen)
                    {
                        TopSheetDownText.Text = DownSpeedText.Text;
                        TopSheetUpText.Text = UpSpeedText.Text;

                        _realDownTraffic.Add(e.DownSpeed);
                        _realUpTraffic.Add(e.UpSpeed);
                        
                        if (_realDownTraffic.Count > 60) _realDownTraffic.RemoveAt(0);
                        if (_realUpTraffic.Count > 60) _realUpTraffic.RemoveAt(0);
                    }

                    // Bug 5: 防重入保护
                    _trafficTickCount++;
                    if (_trafficTickCount % 10 == 0 && _isTopSheetOpen && !_isLatencyProbing)
                    {
                        _ = RefreshTopSheetLatencyAsync();
                    }
                }
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

        private void LoadServersList()
        {
            ViewModel.LoadServersList();
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            ViewModel.SearchQuery = ServerSearchBox.Text;
            ViewModel.ShowFavoritesOnly = FavoriteFilterButton.IsChecked == true;
            ViewModel.LoadServersList(); // Temporary: trigger filtering since ApplyFilters is private in VM right now. Wait, I can just update the properties.

            ServersListView.ItemsSource = ViewModel.FilteredServers;

            // Restore selection or select default
            var match = FilteredServers.FirstOrDefault(s => s.Id == _lastSelectedNodeId);
            if (match == null)
            {
                match = FilteredServers.FirstOrDefault(s => s.Id == NodesManager.Instance.SelectedNodeId);
            }
            if (match == null && FilteredServers.Count > 0)
            {
                match = FilteredServers[0];
            }
            
            ServersListView.SelectedItem = match;
            
            if (match == null)
            {
                ShowPanel("empty");
            }
        }

        // ================= Sidebar Actions =================

        private void ServerSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim().ToLower();
                if (string.IsNullOrEmpty(query))
                {
                    sender.ItemsSource = null;
                }
                else
                {
                    sender.ItemsSource = AllServers
                        .Where(s => s.Name.ToLower().Contains(query) || s.Host.ToLower().Contains(query))
                        .Take(20)
                        .ToList();
                }
            }
            ApplyFilters();
        }

        private void ServerSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is ServerEntryItem match)
            {
                ServersListView.SelectedItem = match;
                sender.Text = match.Name;
            }
        }

        private void ServerSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is ServerEntryItem match)
            {
                ServersListView.SelectedItem = match;
                sender.Text = match.Name;
            }
            else if (!string.IsNullOrEmpty(args.QueryText))
            {
                var query = args.QueryText.Trim().ToLower();
                var firstMatch = AllServers.FirstOrDefault(s => s.Name.ToLower().Contains(query) || s.Host.ToLower().Contains(query));
                if (firstMatch != null)
                {
                    ServersListView.SelectedItem = firstMatch;
                    sender.Text = firstMatch.Name;
                }
            }
        }

        private void FavoriteFilterButton_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void NodeItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                element.CenterPoint = new System.Numerics.Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0f);
                element.Scale = new System.Numerics.Vector3(1.03f, 1.03f, 1f);
            }
        }

        private void NodeItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                element.Scale = new System.Numerics.Vector3(1.0f, 1.0f, 1f);
            }
        }

        private void Button_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                element.CenterPoint = new System.Numerics.Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0f);
                element.Scale = new System.Numerics.Vector3(1.05f, 1.05f, 1f);
            }
        }

        private void Button_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement element)
            {
                element.Scale = new System.Numerics.Vector3(1.0f, 1.0f, 1f);
            }
        }

        // ================= Top Sheet Animation =================
        
        private void OpenTopSheet()
        {
            TopSheetOverlay.Visibility = Visibility.Visible;
            
            var visual = ElementCompositionPreview.GetElementVisual(TopSheetOverlay);
            var compositor = visual.Compositor;

            // 初始化位置（隐藏在上方）
            ElementCompositionPreview.SetIsTranslationEnabled(TopSheetOverlay, true);
            var properties = visual.Properties;
            
            if (properties.TryGetVector3("Translation", out var currentTranslation) == Microsoft.UI.Composition.CompositionGetValueStatus.NotFound || currentTranslation.Y == 0)
            {
                properties.InsertVector3("Translation", new System.Numerics.Vector3(0, -1000f, 0));
            }

            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.Target = "Translation";
            springAnim.FinalValue = new System.Numerics.Vector3(0, 0, 0);
            springAnim.DampingRatio = 0.75f;
            springAnim.Period = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Translation", springAnim);

            TopSheetMockChart.UploadTraffic = _realUpTraffic;
            TopSheetMockChart.DownloadTraffic = _realDownTraffic;

            _isTopSheetOpen = true;

            // 打开时更新节点名称
            var nodeName = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? "---";
            TopSheetNodeNameText.Text = nodeName;

            // 打开时立刻探测一次延迟
            if (!_isLatencyProbing)
                _ = RefreshTopSheetLatencyAsync();
        }

        private async Task RefreshTopSheetLatencyAsync()
        {
            // Bug 5: 防重入保护
            if (_isLatencyProbing) return;
            _isLatencyProbing = true;
            DispatcherQueue.TryEnqueue(() => TopSheetLatencyText.Text = "--- ms");
            try
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                if (node == null) { _isLatencyProbing = false; return; }

                // Host field format is "host:port"
                string host = node.Host;
                int port = 443;
                var colonIdx = node.Host.LastIndexOf(':');
                if (colonIdx > 0 && int.TryParse(node.Host.Substring(colonIdx + 1), out int parsedPort))
                {
                    host = node.Host.Substring(0, colonIdx);
                    port = parsedPort;
                }

                var result = await _latencyProbeService.ProbeAsync(
                    node.Protocol, host, port,
                    TimeSpan.FromSeconds(5));

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (result.Status == LatencyProbeStatus.Success && result.Milliseconds.HasValue)
                    {
                        int ms = result.Milliseconds.Value;
                        TopSheetLatencyText.Text = $"{ms} ms";
                        TopSheetLatencyText.Foreground = ms < 100
                            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                            : ms < 300
                                ? new SolidColorBrush(Color.FromArgb(255, 251, 146, 60))
                                : new SolidColorBrush(Color.FromArgb(255, 248, 113, 113));
                    }
                    else
                    {
                        TopSheetLatencyText.Text = "超时";
                        TopSheetLatencyText.Foreground = new SolidColorBrush(Colors.Gray);
                    }
                });
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => TopSheetLatencyText.Text = "--- ms");
            }
            finally
            {
                _isLatencyProbing = false;
            }
        }

        private void CloseTopSheet()
        {
            _isTopSheetOpen = false;

            var visual = ElementCompositionPreview.GetElementVisual(TopSheetOverlay);
            var compositor = visual.Compositor;

            ElementCompositionPreview.SetIsTranslationEnabled(TopSheetOverlay, true);
            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.Target = "Translation";
            springAnim.FinalValue = new System.Numerics.Vector3(0, -1000f, 0);
            springAnim.DampingRatio = 0.95f;
            springAnim.Period = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("Translation", springAnim);
        }

        private void CloseTopSheet_Click(object sender, RoutedEventArgs e)
        {
            CloseTopSheet();
            ReopenTopSheetButton.Visibility = Visibility.Visible;
        }

        private void ReopenTopSheetButton_Click(object sender, RoutedEventArgs e)
        {
            ReopenTopSheetButton.Visibility = Visibility.Collapsed;
            OpenTopSheet();
        }



        // ================= QR Share Card =================

        private async void QrShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isQrCardVisible)
            {
                HideQrCard();
                return;
            }

            // Resolve share URL from currently selected node
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
            _currentShareUrl = node != null ? NodesManager.ToShareUrl(node) : string.Empty;

            // Reset copy button state
            CopyLinkButtonText.Text = "复制分享链接";
            var grad = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint   = new Windows.Foundation.Point(1, 0.5)
            };
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 99, 102, 241), Offset = 0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 139, 92, 246), Offset = 1 });
            CopyLinkButton.Background = grad;

            // Position card above the QR button before making visible
            PositionQrCard();

            // Show overlay
            QrOverlayCanvas.Visibility = Visibility.Visible;
            _isQrCardVisible = true;

            // Start spring animation after a layout pass
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                AnimateQrCardIn);

            // Generate QR code
            if (!string.IsNullOrEmpty(_currentShareUrl))
                await GenerateQrCodeAsync(_currentShareUrl);
            else
                QrCodeImage.Source = null;
        }

        private void PositionQrCard()
        {
            try
            {
                var transform = QrShareButton.TransformToVisual(RootGrid);
                var btnPos    = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                const double cardWidth           = 230;
                const double estimatedCardHeight = 320;

                // Center card horizontally over button, appear above it
                double left = btnPos.X + (QrShareButton.ActualWidth  / 2) - (cardWidth / 2);
                double top  = btnPos.Y - estimatedCardHeight - 12;

                // Clamp to avoid clipping at edges
                left = Math.Max(8, left);
                top  = Math.Max(8, top);

                Canvas.SetLeft(QrCardBorder, left);
                Canvas.SetTop(QrCardBorder,  top);
            }
            catch { }
        }

        private void AnimateQrCardIn()
        {
            try
            {
                var visual     = ElementCompositionPreview.GetElementVisual(QrCardBorder);
                var compositor = visual.Compositor;

                // Origin at bottom-center of card (card grows upward from button)
                float w = (float)QrCardBorder.ActualWidth;
                float h = (float)QrCardBorder.ActualHeight;
                visual.CenterPoint = new System.Numerics.Vector3(w / 2f, h, 0f);

                // Spring scale — DampingRatio < 1 = underdamped = natural bounce
                var spring = compositor.CreateSpringVector3Animation();
                spring.InitialValue = new System.Numerics.Vector3(0.82f, 0.82f, 1f);
                spring.FinalValue   = new System.Numerics.Vector3(1f, 1f, 1f);
                spring.DampingRatio = 0.55f;
                spring.Period       = TimeSpan.FromMilliseconds(280);

                visual.Scale = new System.Numerics.Vector3(0.82f, 0.82f, 1f);
                visual.StartAnimation("Scale", spring);

                // Fade in
                visual.Opacity = 0f;
                var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                fadeIn.InsertKeyFrame(0f, 0f);
                fadeIn.InsertKeyFrame(1f, 1f);
                fadeIn.Duration = TimeSpan.FromMilliseconds(180);
                visual.StartAnimation("Opacity", fadeIn);
            }
            catch { }
        }

        private void HideQrCard()
        {
            if (!_isQrCardVisible) return;
            try
            {
                var visual     = ElementCompositionPreview.GetElementVisual(QrCardBorder);
                var compositor = visual.Compositor;

                var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                fadeOut.InsertKeyFrame(0f, 1f);
                fadeOut.InsertKeyFrame(1f, 0f);
                fadeOut.Duration = TimeSpan.FromMilliseconds(140);

                var scaleOut = compositor.CreateVector3KeyFrameAnimation();
                scaleOut.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
                scaleOut.InsertKeyFrame(
                    1f, new System.Numerics.Vector3(0.9f, 0.9f, 1f),
                    compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.4f, 0f),
                        new System.Numerics.Vector2(1f, 1f)));
                scaleOut.Duration = TimeSpan.FromMilliseconds(140);

                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                visual.StartAnimation("Opacity", fadeOut);
                visual.StartAnimation("Scale",   scaleOut);
                batch.End();

                batch.Completed += (_, _) =>
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        QrOverlayCanvas.Visibility = Visibility.Collapsed;
                        _isQrCardVisible = false;
                    });
            }
            catch
            {
                QrOverlayCanvas.Visibility = Visibility.Collapsed;
                _isQrCardVisible = false;
            }
        }

        private void QrDismissRect_PointerPressed(object sender, PointerRoutedEventArgs e)
            => HideQrCard();

        private async Task GenerateQrCodeAsync(string url)
        {
            try
            {
                byte[] pngBytes = await Task.Run(() =>
                {
                    using var gen    = new QRCodeGenerator();
                    var       data   = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                    using var qrCode = new PngByteQRCode(data);
                    return qrCode.GetGraphic(10); // 10 px per module, black on white
                });

                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(pngBytes);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                QrCodeImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QR] Generation failed: {ex.Message}");
            }
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentShareUrl)) return;

            try
            {
                var pkg = new DataPackage();
                pkg.SetText(_currentShareUrl);
                Clipboard.SetContent(pkg);
            }
            catch { }

            // Visual feedback: green + "已复制！"
            CopyLinkButtonText.Text   = "已复制！";
            CopyLinkButton.Background = new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));

            // Auto-revert after 1.5 s
            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _copyResetTimer.Tick += (_, _) =>
            {
                _copyResetTimer?.Stop();
                _copyResetTimer = null;

                CopyLinkButtonText.Text = "复制分享链接";
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0.5),
                    EndPoint   = new Windows.Foundation.Point(1, 0.5)
                };
                grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 99, 102, 241), Offset = 0 });
                grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 139, 92, 246), Offset = 1 });
                CopyLinkButton.Background = grad;
            };
            _copyResetTimer.Start();
        }

        private void ServerItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            try
            {
                e.Handled = true;
                if (sender is FrameworkElement element && element.DataContext is ServerEntryItem server)
                {
                    ServersListView.SelectedItem = server;
                    _ = ViewModel.ConnectNodeCommand.ExecuteAsync(server);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerItem_DoubleTapped error: {ex}");
            }
        }

        private void ServerItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ServerEntryItem server)
            {
                args.Handled = true;
                return;
            }

            // Always select the item that was right-clicked
            ServersListView.SelectedItem = server;

            var flyout = new MenuFlyout();

            var editItem = new MenuFlyoutItem
            {
                Text = "编辑",
                Icon = new FontIcon { Glyph = "\uE70F" }
            };
            editItem.Click += EditManualServerDetail_Click;

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += DeleteServerDetail_Click;

            var shareItem = new MenuFlyoutItem
            {
                Text = "分享",
                Icon = new FontIcon { Glyph = "\uE72D" }
            };
            shareItem.Click += ShareServer_Click;

            var favoriteText = server.IsFavorite ? "取消收藏" : "加入收藏";
            var favoriteIcon = server.IsFavorite ? "\uE735" : "\uE734";
            var favoriteItem = new MenuFlyoutItem
            {
                Text = favoriteText,
                Icon = new FontIcon { Glyph = favoriteIcon }
            };
            favoriteItem.Click += (_, _) =>
            {
                server.IsFavorite = !server.IsFavorite;
                server.FavoriteVisibility = server.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
                if (ServersListView.SelectedItem == server && DetailFavoriteButton != null && DetailFavoriteIcon != null)
                {
                    DetailFavoriteButton.IsChecked = server.IsFavorite;
                    DetailFavoriteIcon.Glyph = server.IsFavorite ? "\uE735" : "\uE734";
                }

                var node = NodesManager.Instance.Nodes.Find(n => n.Id == server.Id);
                if (node != null)
                {
                    node.IsFavorite = server.IsFavorite;
                    NodesManager.Instance.Save();
                }
            };

            flyout.Items.Add(editItem);
            flyout.Items.Add(deleteItem);
            flyout.Items.Add(shareItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(favoriteItem);

            if (args.TryGetPosition(element, out Windows.Foundation.Point point))
            {
                flyout.ShowAt(element, new FlyoutShowOptions { Position = point });
            }
            else
            {
                flyout.ShowAt(element);
            }

            args.Handled = true;
        }

        // ================= Bottom Action Buttons =================

        private void AddManualServer_Click(object sender, RoutedEventArgs e)
        {
            _editingNodeId = string.Empty;
            ServerNameInput.Text = string.Empty;
            ServerHostInput.Text = string.Empty;
            ServerPortInput.Text = "443";
            ServerProtocolInput.SelectedIndex = 0;

            ServerPasswordInput.Password = string.Empty;
            ServerUuidInput.Text = string.Empty;
            ServerPathInput.Text = string.Empty;
            ServerWsHostInput.Text = string.Empty;
            ServerSniInput.Text = string.Empty;
            ServerFingerprintInput.SelectedIndex = 0;
            ServerAllowInsecureInput.IsChecked = false;
            ServerPublicKeyInput.Text = string.Empty;
            ServerShortIdInput.Text = string.Empty;
            if (ServerFlowInput != null) ServerFlowInput.SelectedIndex = 0;

            ServerNetworkInput.SelectedIndex = 0;
            ServerSecurityInput.SelectedIndex = 0;

            // Reset new fields
            ServerAlterIdInput.Text = "0";
            ServerObfsTypeInput.SelectedIndex = 0;
            ServerObfsPasswordInput.Text = string.Empty;
            ServerIsShadowTlsInput.IsChecked = false;
            ServerShadowTlsVersionInput.SelectedIndex = 0;
            ServerShadowTlsPasswordInput.Text = string.Empty;
            ServerHeaderTypeInput.SelectedIndex = 0;
            ServerAlpnInput.Text = string.Empty;
            ServerSpiderXInput.Text = string.Empty;

            PopulateProxyChainOptions(string.Empty);

            UpdateFormFieldsVisibility();

            EditFormTitle.Text = "手动添加服务器";
            ShowPanel("edit");
        }

        private async void ImportFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try to read initial text from clipboard to pre-populate
                string initialText = string.Empty;
                try
                {
                    var packageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (packageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    {
                        initialText = await packageView.GetTextAsync() ?? string.Empty;
                    }
                }
                catch { }

                // Create input dialog elements
                var textBox = new TextBox
                {
                    PlaceholderText = "在此粘贴您的订阅链接 (http/https) 或节点分享链接 (vmess://, vless://, ss:// 等)\n支持多行批量输入",
                    AcceptsReturn = true,
                    Height = 160,
                    Text = initialText,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var scrollViewer = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "将链接粘贴在下方，每行输入一个：",
                                FontSize = 12,
                                Opacity = 0.8
                            },
                            textBox
                        }
                    },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var dialog = new ContentDialog
                {
                    Title = "导入订阅或节点链接",
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                    Content = scrollViewer,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                string text = textBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return;

                var (addedNodesCount, addedSubsCount) = await ViewModel.ImportNodesFromTextAsync(text);

                if (addedNodesCount > 0 || addedSubsCount > 0)
                {

                    // Show success feedback
                    string msg = string.Empty;
                    if (addedSubsCount > 0 && addedNodesCount > 0)
                        msg = $"成功导入并同步了 {addedSubsCount} 个订阅，且添加了 {addedNodesCount} 个单节点。";
                    else if (addedSubsCount > 0)
                        msg = $"成功导入并同步了 {addedSubsCount} 个订阅。";
                    else
                        msg = $"成功添加了 {addedNodesCount} 个单节点。";

                    var successDialog = new ContentDialog
                    {
                        Title = "导入成功",
                        Content = msg,
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    var failDialog = new ContentDialog
                    {
                        Title = "导入失败",
                        Content = "未检测到有效的订阅链接或节点分享链接，请检查输入格式是否正确。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await failDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = "导入时发生错误",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                try { await errDialog.ShowAsync(); } catch { }
            }
        }

        private async void SetProxyMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string mode)
            {
                int index = mode == "tun" ? 1 : mode == "manual" ? 2 : 0;

                // If switching to TUN mode without admin, show UAC dialog first
                if (index == 1 && !Helpers.AdminHelper.IsAdministrator())
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                    {
                        try
                        {
                            await Task.Delay(100);
                            var dialog = new ContentDialog
                            {
                                Title = "需要管理员权限",
                                Content = "开启 TUN 模式需要管理员权限以创建虚拟网卡和接管系统路由。\n即将请求 UAC 提权并以管理员身份重启客户端。",
                                PrimaryButtonText = "确认提权",
                                CloseButtonText = "取消",
                                XamlRoot = this.XamlRoot
                            };
                            var result = await dialog.ShowAsync();
                            if (result != ContentDialogResult.Primary)
                            {
                                tcs.SetResult(false);
                                return;
                            }
                            bool wasRunning = CoreManager.Instance.IsRunning;
                            if (wasRunning) await CoreManager.Instance.StopAsync();
                            string arg = wasRunning ? "--tun-start" : "--tun";
                            Helpers.AdminHelper.RestartAsAdmin(arg);
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TUN] SetProxyMode_Click dialog failed: {ex.Message}");
                            tcs.SetResult(false);
                        }
                    });
                    await tcs.Task;
                    return;
                }

                AppSession.Instance.ProxyModeIndex = index;
                Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", index);
                Helpers.LocalSettingsHelper.SetValue("enableTunMode", index == 1);

                UpdateProxyModeUI();
                UpdateRoutingModeUI();

                if (CoreManager.Instance.IsRunning)
                {
                    CoreManager.Instance.AppendLog("[系统] 检测到代理模式更新，正在自动重启代理引擎以重新加载配置...");
                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    if (node != null)
                    {
                        string realConfig = ConfigBuilder.Build(node);
                        await CoreManager.Instance.StartAsync(realConfig);
                    }
                }
            }
        }

        private async void ManageSubscriptions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageSubscriptionsDialog(() =>
            {
                LoadServersList();
            })
            {
                XamlRoot = this.XamlRoot
            };

            // Intercept the "添加" primary button: run add+sync, cancel close on failure
            dialog.PrimaryButtonClick += async (d, args) =>
            {
                var deferral = args.GetDeferral();
                bool ok = await dialog.TryAddSubscriptionAsync();
                if (!ok) args.Cancel = true;   // keep dialog open if URL was empty
                deferral.Complete();
                if (ok) LoadServersList();
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Show subscription dialog failed: {ex.Message}");
            }
        }

        private async void PingAllServers_Click(object sender, RoutedEventArgs e)
        {
            PingAllButton.IsEnabled = false;
            PingAllButton.Content = new ProgressRing { IsActive = true, Width = 16, Height = 16 };

            try
            {
                await ViewModel.PingAllServersCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ping all error: {ex.Message}");
            }
            finally
            {
                PingAllButton.IsEnabled = true;
                PingAllButton.Content = new FontIcon { Glyph = "\uEC4A", FontSize = 16 };
            }
        }

        // ================= Selection & Navigation Switcher =================

        private void ServersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ServersListView.SelectedItem is ServerEntryItem selected)
                {
                    _lastSelectedNodeId = selected.Id;
                    if (DetailTitle == null || DetailSubtitle == null || DetailHostText == null ||
                        DetailPortText == null || DetailNetworkText == null || DetailFavoriteButton == null ||
                        DetailFavoriteIcon == null || DetailLatencyText == null)
                        return;

                    // Populate Detailed View
                    DetailTitle.Text = selected.Name;
                    DetailSubtitle.Text = selected.Protocol;
                    DetailHostText.Text = selected.Host;
                    DetailPortText.Text = selected.Port.ToString();
                    DetailNetworkText.Text = string.IsNullOrEmpty(selected.Network) ? "TCP" : selected.Network.ToUpper();

                    DetailFavoriteButton.IsChecked = selected.IsFavorite;
                    DetailFavoriteIcon.Glyph = selected.IsFavorite ? "\uE735" : "\uE734"; // Solid / Outline star

                    // Reset statuses
                    DetailLatencyText.Text = "未测试";
                    
                    try
                    {
                        DetailLatencyText.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                    }
                    catch
                    {
                        // Fallback
                    }

                    ResetAiLights();
                    ResetIpInfoText();

                    ShowPanel("detail");

                    // Start Latency checks in background
                    _ = ViewModel.TestSingleLatencyCommand.ExecuteAsync(selected);
                    
                    // Show cached AI/IP info if applicable
                    UpdateAiAndIpDisplay();
                }
                else
                {
                    ShowPanel("empty");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SelectionChanged: {ex}");
            }
        }

        private void ShowPanel(string name)
        {
            EmptyViewPanel.Visibility = (name == "empty") ? Visibility.Visible : Visibility.Collapsed;
            ServerDetailsPanel.Visibility = (name == "detail") ? Visibility.Visible : Visibility.Collapsed;
            EditFormPanel.Visibility = (name == "edit") ? Visibility.Visible : Visibility.Collapsed;
            SubscriptionPanel.Visibility = (name == "subscription") ? Visibility.Visible : Visibility.Collapsed;

            // Hide bottom connection/control bar when adding/editing nodes or managing subscriptions
            if (BottomControlBar != null)
            {
                BottomControlBar.Visibility = (name == "edit" || name == "subscription") ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // ================= Latency Speed Test =================

        private async void TestSingleLatency_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                await ViewModel.TestSingleLatencyCommand.ExecuteAsync(selected);

                // Update detail text manually for now until XAML bindings are fully migrated
                if (DetailLatencyText != null)
                {
                    DetailLatencyText.Text = selected.PingText;
                    DetailLatencyText.Foreground = selected.PingColor;
                }
            }
        }


        private AiUnlockStatus? _openAiStatus;
        private AiUnlockStatus? _claudeStatus;
        private AiUnlockStatus? _geminiStatus;
        private string _ipOrgCache = "-";
        private string _ipAsnCache = "-";
        private string _ipLocationCache = "-";

        private ServerEntryItem? GetActiveServerItem()
        {
            return AllServers.FirstOrDefault(s => s.Id == NodesManager.Instance.SelectedNodeId);
        }

        private void ResetAiLights()
        {
            try
            {
                Microsoft.UI.Xaml.Media.Brush neutral = null;
                if (Application.Current.Resources.TryGetValue("StateNeutralBrush", out var res) && res is Microsoft.UI.Xaml.Media.Brush b)
                    neutral = b;
                else
                    neutral = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

                if (OpenAiLight != null) OpenAiLight.Fill = neutral;
                if (ClaudeLight != null) ClaudeLight.Fill = neutral;
                if (GeminiLight != null) GeminiLight.Fill = neutral;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetAiLights failed: {ex.Message}");
            }
        }

        private void ResetIpInfoText()
        {
            try
            {
                if (IpAddressText != null) IpAddressText.Text = "-";
                if (IpOrgText != null) IpOrgText.Text = "-";
                if (IpAsnText != null) IpAsnText.Text = "-";
                if (IpLocationText != null) IpLocationText.Text = "-";
                if (IpLoadingRing != null) IpLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetIpInfoText failed: {ex.Message}");
            }
        }

        private void UpdateAiAndIpDisplay()
        {
            try
            {
                var activeItem = GetActiveServerItem();
                if (!CoreManager.Instance.IsRunning || ServersListView.SelectedItem as ServerEntryItem == null || ServersListView.SelectedItem as ServerEntryItem != activeItem)
                {
                    ResetAiLights();
                    ResetIpInfoText();
                    return;
                }

                Microsoft.UI.Xaml.Media.Brush successBrush = null;
                if (Application.Current.Resources.TryGetValue("StateSuccessBrush", out var sucRes) && sucRes is Microsoft.UI.Xaml.Media.Brush sucB)
                    successBrush = sucB;
                else
                    successBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);

                Microsoft.UI.Xaml.Media.Brush errorBrush = null;
                if (Application.Current.Resources.TryGetValue("StateErrorBrush", out var errRes) && errRes is Microsoft.UI.Xaml.Media.Brush errB)
                    errorBrush = errB;
                else
                    errorBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);

                if (OpenAiLight != null) OpenAiLight.Fill = _openAiStatus == AiUnlockStatus.Unlocked ? successBrush : errorBrush;
                if (ClaudeLight != null) ClaudeLight.Fill = _claudeStatus == AiUnlockStatus.Unlocked ? successBrush : errorBrush;
                if (GeminiLight != null) GeminiLight.Fill = _geminiStatus == AiUnlockStatus.Unlocked ? successBrush : errorBrush;

                UpdateIpAddressDisplay();
                if (IpOrgText != null) IpOrgText.Text = _ipOrgCache;
                if (IpAsnText != null) IpAsnText.Text = _ipAsnCache;
                if (IpLocationText != null) IpLocationText.Text = _ipLocationCache;
                if (IpLoadingRing != null) IpLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateAiAndIpDisplay failed: {ex}");
            }
        }

        private static bool IsDatacenter(string org)
        {
            if (string.IsNullOrEmpty(org) || org == "-") return false;
            
            string[] keywords = { 
                "Amazon", "Google", "Microsoft", "Azure", "DigitalOcean", 
                "Linode", "Vultr", "Oracle", "Cloudflare", "OVH", 
                "Hetzner", "Data Center", "Hosting", "Cloud", "Server",
                "Akamai", "Zenlayer", "Choopa", "Tencent", "Alibaba",
                "Baidu", "Huawei", "Equinix", "Leaseweb", "QuadraNet"
            };

            foreach (var kw in keywords)
            {
                if (org.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task TriggerAiAndIpChecksAsync()
        {
            _aiCheckCts?.Cancel();
            _aiCheckCts = new CancellationTokenSource();
            var token = _aiCheckCts.Token;

            _openAiStatus = null;
            _claudeStatus = null;
            _geminiStatus = null;
            _activeIpAddress = "-";
            _ipOrgCache = "-";
            _ipAsnCache = "-";
            _ipLocationCache = "-";

            var activeItem = GetActiveServerItem();
            if (ServersListView.SelectedItem as ServerEntryItem == activeItem && IpLoadingRing != null)
            {
                IpLoadingRing.IsActive = true;
            }

            try
            {
                var ipInfoService = new IpInfoService();
                var aiService = new AiUnlockCheckService();
                int proxyPort = 2080;

                var openAiTask = aiService.CheckOpenAiAsync(proxyPort, token);
                var claudeTask = aiService.CheckClaudeAsync(proxyPort, token);
                var geminiTask = aiService.CheckGeminiAsync(proxyPort, token);
                var ipInfoTask = ipInfoService.GetIpInfoAsync(proxyPort, token);

                await Task.WhenAll(openAiTask, claudeTask, geminiTask, ipInfoTask);

                if (token.IsCancellationRequested) return;

                _openAiStatus = await openAiTask;
                _claudeStatus = await claudeTask;
                _geminiStatus = await geminiTask;
                var ipInfo = await ipInfoTask;

                if (ipInfo != null)
                {
                    _activeIpAddress = ipInfo.ip ?? "-";
                    var org = ipInfo.asOrganization ?? "-";
                    if (IsDatacenter(org))
                    {
                        _ipOrgCache = $"{org} [机房]";
                    }
                    else
                    {
                        _ipOrgCache = org;
                    }
                    _ipAsnCache = ipInfo.asn?.ToString() ?? "-";
                    _ipLocationCache = $"{ipInfo.city}, {ipInfo.region}, {ipInfo.country}";
                }
                else
                {
                    _activeIpAddress = "-";
                    _ipOrgCache = "获取失败";
                    _ipAsnCache = "-";
                    _ipLocationCache = "-";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI check error: {ex.Message}");
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        UpdateAiAndIpDisplay();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating UI in TriggerAiAndIpChecksAsync finally: {ex}");
                    }
                });
            }
        }

        private void UpdateIpAddressDisplay()
        {
            if (_activeIpAddress == "-" || string.IsNullOrEmpty(_activeIpAddress))
            {
                IpAddressText.Text = "-";
                return;
            }

            if (!_isIpMasked)
            {
                IpAddressText.Text = _activeIpAddress;
                IpMaskIcon.Glyph = "\uED1A"; // Eye open
            }
            else
            {
                var parts = _activeIpAddress.Split('.');
                if (parts.Length == 4)
                {
                    IpAddressText.Text = $"{parts[0]}.{parts[1]}.*.*";
                }
                else
                {
                    IpAddressText.Text = _activeIpAddress;
                }
                IpMaskIcon.Glyph = "\uE890"; // Eye closed
            }
        }

        private void ToggleIpMask_Click(object sender, RoutedEventArgs e)
        {
            _isIpMasked = !_isIpMasked;
            UpdateIpAddressDisplay();
        }

        private void CopyIpStatus_Click(object sender, RoutedEventArgs e)
        {
            if (_activeIpAddress == "-" || string.IsNullOrEmpty(_activeIpAddress)) return;

            var sb = new StringBuilder();
            sb.AppendLine($"出口 IP: {_activeIpAddress}");
            sb.AppendLine($"ISP 运营商: {IpOrgText.Text}");
            sb.AppendLine($"ASN 编号: {IpAsnText.Text}");
            sb.AppendLine($"地理位置: {IpLocationText.Text}");

            var package = new DataPackage();
            package.SetText(sb.ToString());
            Clipboard.SetContent(package);
        }

        // ================= Edit/Add Form Operations =================

        private async void ShareServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == selected.Id);
                if (node != null)
                {
                    string link = NodesManager.ToShareUrl(node);
                    if (!string.IsNullOrEmpty(link))
                    {
                        // Copy to clipboard automatically first for user convenience
                        try
                        {
                            var package = new DataPackage();
                            package.SetText(link);
                            Clipboard.SetContent(package);
                        }
                        catch { }

                        // Build the gorgeous 1:1 ported dialog
                        var dialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot
                        };

                        // ── X close button ──
                        var closeBtn = new Button
                        {
                            Content = "\uE711",
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                            Width = 32,
                            Height = 32,
                            Padding = new Thickness(0),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle))
                            closeBtn.Style = (Style)subtleStyle;
                        closeBtn.Click += (_, _) => dialog.Hide();

                        // ── Header row (title + X) ──
                        var header = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var titleText = new TextBlock
                        {
                            Text = "分享节点",
                            FontSize = 20,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetColumn(titleText, 0);
                        Grid.SetColumn(closeBtn, 1);
                        header.Children.Add(titleText);
                        header.Children.Add(closeBtn);

                        // ── Link box ──
                        var linkBox = new TextBox
                        {
                            Text = link,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            AcceptsReturn = false,
                        };
                        // ── Name row (server name + copy icon button) ──
                        var nameCopyBtn = new Button
                        {
                            Content = "\uE8C8",
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                            Width = 28,
                            Height = 28,
                            Padding = new Thickness(0),
                            FontSize = 14,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle2))
                            nameCopyBtn.Style = (Style)subtleStyle2;
                        ToolTipService.SetToolTip(nameCopyBtn, "复制链接");

                        nameCopyBtn.Click += async (_, _) =>
                        {
                            try
                            {
                                var pkg = new DataPackage();
                                pkg.SetText(link);
                                Clipboard.SetContent(pkg);
                                nameCopyBtn.Content = "\uE73E"; // Checkmark icon
                                await Task.Delay(1500);
                                nameCopyBtn.Content = "\uE8C8"; // Back to copy icon
                            }
                            catch { }
                        };

                        var nameRow = new Grid { ColumnSpacing = 4 };
                        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var nameText = new TextBlock
                        {
                            Text = node.Name,
                            FontSize = 12,
                            Opacity = 0.65,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetColumn(nameText, 0);
                        Grid.SetColumn(nameCopyBtn, 1);
                        nameRow.Children.Add(nameText);
                        nameRow.Children.Add(nameCopyBtn);

                        dialog.Content = new StackPanel
                        {
                            Width = 360,
                            Spacing = 12,
                            Children =
                            {
                                header,
                                nameRow,
                                linkBox,
                            }
                        };

                        try { await dialog.ShowAsync(); } catch { }
                    }
                    else
                    {
                        var failDialog = new ContentDialog
                        {
                            Title = "分享失败",
                            Content = "该节点的协议配置暂时不支持生成分享链接。",
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        try { await failDialog.ShowAsync(); } catch { }
                    }
                }
            }
        }

        private async void ActivateNode_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                await ViewModel.ConnectNodeCommand.ExecuteAsync(selected);
            }
        }

        private void EditManualServerDetail_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == selected.Id);
                if (node == null) return;

                _editingNodeId = selected.Id;
                ServerNameInput.Text = node.Name;
                
                var (hostPart, portPart) = NodeLinkParser.SplitHostPort(node.Host);
                if (portPart == 0) portPart = 443;

                ServerHostInput.Text = hostPart;
                ServerPortInput.Text = portPart.ToString();

                int idx = node.Protocol.ToUpper() switch
                {
                    "VLESS" => 0,
                    "VMESS" => 1,
                    "SHADOWSOCKS" => 2,
                    "TROJAN" => 3,
                    "HYSTERIA 2" => 4,
                    "HYSTERIA" => 4,
                    "TUIC" => 5,
                    "NAIVEPROXY" => 6,
                    "NAIVE" => 6,
                    "SOCKS" => 7,
                    "ANYTLS" => 8,
                    "HTTP" => 9,
                    "HTTPS" => 9,
                    "WIREGUARD" => 10,
                    "WG" => 10,
                    "SNELL" => 11,
                    _ => 0
                };
                ServerProtocolInput.SelectedIndex = idx;

                // Load newly supported fields
                ServerPasswordInput.Password = node.Password ?? string.Empty;
                ServerUuidInput.Text = node.Uuid ?? string.Empty;
                ServerPathInput.Text = node.Path ?? string.Empty;
                ServerWsHostInput.Text = node.WsHost ?? string.Empty;
                ServerSniInput.Text = node.Sni ?? string.Empty;

                // WireGuard
                ServerWgPrivateKeyInput.Text = node.WgPrivateKey ?? string.Empty;
                ServerWgLocalAddressInput.Text = node.WgLocalAddress ?? string.Empty;
                ServerWgPreSharedKeyInput.Text = node.WgPreSharedKey ?? string.Empty;
                ServerWgMtuInput.Text = node.WgMtu.ToString();

                // Snell
                ServerSnellPskInput.Password = node.Password ?? string.Empty;
                int snellVerIdx = (node.SnellVersion > 0 ? node.SnellVersion : 4) switch
                {
                    4 => 0,
                    5 => 1,
                    3 => 2,
                    2 => 3,
                    1 => 4,
                    _ => 0
                };
                ServerSnellVersionInput.SelectedIndex = snellVerIdx;
                // Load Snell obfs
                string snellObfsMode = node.ObfsType?.ToLower() ?? "none";
                ServerSnellObfsInput.SelectedIndex = snellObfsMode == "http" ? 1 : 0;
                ServerSnellObfsHostInput.Text = node.WsHost ?? string.Empty;

                // Find index of selected fingerprint
                int fpIdx = 0;
                if (!string.IsNullOrEmpty(node.Fingerprint))
                {
                    string fpLower = node.Fingerprint.ToLower();
                    fpIdx = fpLower switch
                    {
                        "chrome" => 1,
                        "firefox" => 2,
                        "safari" => 3,
                        "edge" => 4,
                        "ios" => 5,
                        "android" => 6,
                        "random" => 7,
                        "randomized" => 8,
                        _ => 0
                    };
                }
                ServerFingerprintInput.SelectedIndex = fpIdx;

                ServerAllowInsecureInput.IsChecked = node.AllowInsecure;
                ServerPublicKeyInput.Text = node.PublicKey ?? string.Empty;
                ServerShortIdInput.Text = node.ShortId ?? string.Empty;

                // Load Flow setting
                int flowIdx = 0;
                if (!string.IsNullOrEmpty(node.Flow))
                {
                    string flowLower = node.Flow.ToLower();
                    flowIdx = flowLower switch
                    {
                        "xtls-rprx-vision" => 1,
                        "xtls-rprx-vision-udp443" => 2,
                        _ => 0
                    };
                }
                if (ServerFlowInput != null) ServerFlowInput.SelectedIndex = flowIdx;

                // Load Network setting
                int netIdx = 0;
                if (!string.IsNullOrEmpty(node.Network))
                {
                    string netLower = node.Network.ToLower();
                    netIdx = netLower switch
                    {
                        "tcp" => 0,
                        "ws" => 1,
                        "grpc" => 2,
                        "xhttp" => 3,
                        "kcp" => 4,
                        "quic" => 5,
                        "h2" => 6,
                        _ => 0
                    };
                }
                ServerNetworkInput.SelectedIndex = netIdx;

                // Load Security setting
                int secIdx = 0;
                if (!string.IsNullOrEmpty(node.Security))
                {
                    string secLower = node.Security.ToLower();
                    secIdx = secLower switch
                    {
                        "none" => 0,
                        "tls" => 1,
                        "reality" => 2,
                        _ => 0
                    };
                }
                ServerSecurityInput.SelectedIndex = secIdx;

                // Load new fields
                ServerAlterIdInput.Text = node.AlterId.ToString();
                
                int obfsIdx = 0;
                if (!string.IsNullOrEmpty(node.ObfsType))
                {
                    obfsIdx = node.ObfsType.ToLower() switch
                    {
                        "none" => 0,
                        "obfs" => 1,
                        _ => 0
                    };
                }
                ServerObfsTypeInput.SelectedIndex = obfsIdx;
                ServerObfsPasswordInput.Text = node.ObfsPassword ?? string.Empty;

                ServerIsShadowTlsInput.IsChecked = node.IsShadowTls;
                int stlsVersionIdx = node.ShadowTlsVersion switch
                {
                    3 => 0,
                    2 => 1,
                    1 => 2,
                    _ => 0
                };
                ServerShadowTlsVersionInput.SelectedIndex = stlsVersionIdx;
                ServerShadowTlsPasswordInput.Text = node.ShadowTlsPassword ?? string.Empty;

                int headerIdx = 0;
                if (!string.IsNullOrEmpty(node.HeaderType))
                {
                    headerIdx = node.HeaderType.ToLower() switch
                    {
                        "none" => 0,
                        "http" => 1,
                        _ => 0
                    };
                }
                ServerHeaderTypeInput.SelectedIndex = headerIdx;

                ServerAlpnInput.Text = node.Alpn ?? string.Empty;
                ServerSpiderXInput.Text = node.SpiderX ?? string.Empty;

                PopulateProxyChainOptions(node.Id);
                if (!string.IsNullOrEmpty(node.ProxyChainId))
                {
                    var chainItem = ServerProxyChainInput.Items
                        .Cast<ComboBoxItem>()
                        .FirstOrDefault(i => (i.Tag as string) == node.ProxyChainId);
                    if (chainItem != null)
                    {
                        ServerProxyChainInput.SelectedItem = chainItem;
                    }
                }

                UpdateFormFieldsVisibility();

                EditFormTitle.Text = "编辑节点参数";
                ShowPanel("edit");
            }
        }

        private async void DeleteServerDetail_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var dialog = new ContentDialog
                {
                    Title = "删除确认",
                    Content = $"确定要删除节点“{selected.Name}”吗？",
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ViewModel.DeleteServerCommand.Execute(selected);
                    ShowPanel("empty");
                }
            }
        }

        private void SaveServer_Click(object sender, RoutedEventArgs e)
        {
            string name = ServerNameInput.Text.Trim();
            string host = ServerHostInput.Text.Trim();
            string portStr = ServerPortInput.Text.Trim();
            if (string.IsNullOrEmpty(portStr)) portStr = "443";

            // Format host and port, wrapping IPv6 host in brackets for round-trip reliability
            string hostAndPort;
            if (host.Contains(':'))
            {
                string cleanHost = host.Trim('[', ']');
                hostAndPort = $"[{cleanHost}]:{portStr}";
            }
            else
            {
                hostAndPort = $"{host}:{portStr}";
            }

            var item = ServerProtocolInput.SelectedItem as ComboBoxItem;
            string protocol = item?.Content?.ToString() ?? "VLESS";
            string proto = protocol.ToUpper();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(host)) return;

            string password = ServerPasswordInput.Password.Trim();
            string uuid = ServerUuidInput.Text.Trim();
            
            var encItem = ServerEncryptionInput.SelectedItem as ComboBoxItem;
            string encryption = encItem?.Content?.ToString() ?? string.Empty;

            var netItem = ServerNetworkInput.SelectedItem as ComboBoxItem;
            string network = netItem?.Content?.ToString() ?? "tcp";

            string path = ServerPathInput.Text.Trim();
            string wsHost = ServerWsHostInput.Text.Trim();

            var secItem = ServerSecurityInput.SelectedItem as ComboBoxItem;
            string security = secItem?.Content?.ToString() ?? "none";

            string sni = ServerSniInput.Text.Trim();

            var fpItem = ServerFingerprintInput.SelectedItem as ComboBoxItem;
            string fingerprint = fpItem?.Content?.ToString() ?? "none";

            bool allowInsecure = ServerAllowInsecureInput.IsChecked == true;
            string publicKey = ServerPublicKeyInput.Text.Trim();
            string shortId = ServerShortIdInput.Text.Trim();
            var flowItem = ServerFlowInput != null ? ServerFlowInput.SelectedItem as ComboBoxItem : null;
            string flow = (flowItem?.Content?.ToString() != "none") ? (flowItem?.Content?.ToString() ?? string.Empty) : string.Empty;

            // Retrieve new field values
            int alterId = 0;
            if (int.TryParse(ServerAlterIdInput.Text.Trim(), out int alt))
            {
                alterId = alt;
            }

            var obfsItem = ServerObfsTypeInput.SelectedItem as ComboBoxItem;
            string obfsType = obfsItem?.Content?.ToString() ?? "none";
            string obfsPassword = ServerObfsPasswordInput.Text.Trim();

            bool isShadowTls = ServerIsShadowTlsInput.IsChecked == true;
            var stlsVerItem = ServerShadowTlsVersionInput.SelectedItem as ComboBoxItem;
            int shadowTlsVersion = 3;
            if (stlsVerItem != null && int.TryParse(stlsVerItem.Content.ToString(), out int stlsV))
            {
                shadowTlsVersion = stlsV;
            }
            string shadowTlsPassword = ServerShadowTlsPasswordInput.Text.Trim();

            var headerItem = ServerHeaderTypeInput.SelectedItem as ComboBoxItem;
            string headerType = headerItem?.Content?.ToString() ?? "none";

            string alpn = ServerAlpnInput.Text.Trim();
            string spiderX = ServerSpiderXInput.Text.Trim();

            var proxyChainItem = ServerProxyChainInput.SelectedItem as ComboBoxItem;
            string proxyChainId = proxyChainItem != null ? (proxyChainItem.Tag as string ?? string.Empty) : string.Empty;

            string wgPrivateKey = ServerWgPrivateKeyInput.Text.Trim();
            string wgLocalAddress = ServerWgLocalAddressInput.Text.Trim();
            string wgPreSharedKey = ServerWgPreSharedKeyInput.Text.Trim();
            int wgMtu = 0;
            if (int.TryParse(ServerWgMtuInput.Text.Trim(), out int parsedMtu))
            {
                wgMtu = parsedMtu;
            }

            // Snell fields
            string snellPsk = ServerSnellPskInput.Password.Trim();
            int snellVersion = 4;
            if (ServerSnellVersionInput.SelectedItem is ComboBoxItem snellVerItem &&
                int.TryParse(snellVerItem.Content.ToString(), out int parsedSnellVer))
            {
                snellVersion = parsedSnellVer;
            }
            string snellObfsMode = "none";
            if (ServerSnellObfsInput.SelectedItem is ComboBoxItem snellObfsItem)
            {
                string c = snellObfsItem.Content.ToString() ?? "";
                snellObfsMode = c.ToUpper() == "HTTP" ? "http" : "none";
            }
            string snellObfsHost = ServerSnellObfsHostInput.Text.Trim();

            if (string.IsNullOrEmpty(_editingNodeId))
            {
                // Create manual server
                var node = new PersistedNode
                {
                    Name = name,
                    Protocol = protocol,
                    Host = hostAndPort,
                    Password = proto == "SNELL" ? snellPsk : password,
                    Uuid = uuid,
                    Encryption = encryption,
                    Network = network,
                    Path = path,
                    WsHost = proto == "SNELL" ? snellObfsHost : wsHost,
                    Security = security,
                    Sni = sni,
                    Fingerprint = fingerprint,
                    AllowInsecure = allowInsecure,
                    PublicKey = publicKey,
                    ShortId = shortId,
                    Flow = flow,
                    AlterId = alterId,
                    ObfsType = proto == "SNELL" ? snellObfsMode : obfsType,
                    ObfsPassword = obfsPassword,
                    IsShadowTls = isShadowTls,
                    ShadowTlsVersion = shadowTlsVersion,
                    ShadowTlsPassword = shadowTlsPassword,
                    HeaderType = headerType,
                    Alpn = alpn,
                    SpiderX = spiderX,
                    ProxyChainId = proxyChainId,
                    WgPrivateKey = wgPrivateKey,
                    WgLocalAddress = wgLocalAddress,
                    WgPreSharedKey = wgPreSharedKey,
                    WgMtu = wgMtu,
                    SnellVersion = snellVersion
                };
                NodesManager.Instance.AddManualNode(node);
            }
            else
            {
                // Update manual server
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
                if (node != null)
                {
                    node.Name = name;
                    node.Protocol = protocol;
                    node.Host = hostAndPort;
                    node.Password = proto == "SNELL" ? snellPsk : password;
                    node.Uuid = uuid;
                    node.Encryption = encryption;
                    node.Network = network;
                    node.Path = path;
                    node.WsHost = proto == "SNELL" ? snellObfsHost : wsHost;
                    node.Security = security;
                    node.Sni = sni;
                    node.Fingerprint = fingerprint;
                    node.AllowInsecure = allowInsecure;
                    node.PublicKey = publicKey;
                    node.ShortId = shortId;
                    node.Flow = flow;
                    node.AlterId = alterId;
                    node.ObfsType = proto == "SNELL" ? snellObfsMode : obfsType;
                    node.ObfsPassword = obfsPassword;
                    node.IsShadowTls = isShadowTls;
                    node.ShadowTlsVersion = shadowTlsVersion;
                    node.ShadowTlsPassword = shadowTlsPassword;
                    node.HeaderType = headerType;
                    node.Alpn = alpn;
                    node.SpiderX = spiderX;
                    node.ProxyChainId = proxyChainId;
                    node.WgPrivateKey = wgPrivateKey;
                    node.WgLocalAddress = wgLocalAddress;
                    node.WgPreSharedKey = wgPreSharedKey;
                    node.WgMtu = wgMtu;
                    node.SnellVersion = snellVersion;
                    NodesManager.Instance.Save();
                }
            }

            LoadServersList();
            ShowPanel("empty");
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                ShowPanel("detail");
            }
            else
            {
                ShowPanel("empty");
            }
        }

        private void DetailFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                ViewModel.ToggleFavoriteCommand.Execute(selected);
                
                // Keep UI sync for now until XAML bindings are fully implemented
                DetailFavoriteButton.IsChecked = selected.IsFavorite;
                DetailFavoriteIcon.Glyph = selected.IsFavorite ? "\uE735" : "\uE734";
            }
        }

        // ================= Subscription Manager =================

        private void SubSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubSegmented == null || SubAddFormPanel == null || SubListFormPanel == null) return;

            if (SubSegmented.SelectedIndex == 0)
            {
                SubAddFormPanel.Visibility = Visibility.Visible;
                SubListFormPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SubAddFormPanel.Visibility = Visibility.Collapsed;
                SubListFormPanel.Visibility = Visibility.Visible;
            }
        }

        private async void SaveSub_Click(object sender, RoutedEventArgs e)
        {
            string name = SubNameInput.Text.Trim();
            string url = SubUrlInput.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

            SaveSubButton.IsEnabled = false;
            SaveSubButton.Content = "导入同步中...";

            await ViewModel.AddSubscriptionAsync(name, url);

            SaveSubButton.IsEnabled = true;
            SaveSubButton.Content = "导入并同步";

            CancelEdit_Click(sender, e);
        }

        private async void UpdateSub_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PersistedSubscription sub)
            {
                btn.IsEnabled = false;
                await ViewModel.UpdateSubscriptionCommand.ExecuteAsync(sub);
                btn.IsEnabled = true;
            }
        }

        private void DeleteSub_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PersistedSubscription sub)
            {
                ViewModel.DeleteSubscriptionCommand.Execute(sub);
            }
        }

        private void ServerProtocolInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormFieldsVisibility();
        }

        private void ServerNetworkInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormFieldsVisibility();
        }

        private void ServerSecurityInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormFieldsVisibility();
        }

        private void ServerObfsTypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerObfsTypeInput == null || ServerObfsPasswordInput == null) return;
            if (ServerObfsTypeInput.SelectedItem is ComboBoxItem item)
            {
                string obfs = item.Content.ToString() ?? "none";
                ServerObfsPasswordInput.Visibility = (obfs != "none") ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ServerSnellObfsInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerSnellObfsInput == null || ServerSnellObfsHostInput == null) return;
            if (ServerSnellObfsInput.SelectedItem is ComboBoxItem item)
            {
                bool isHttp = item.Content.ToString()?.ToUpper() == "HTTP";
                ServerSnellObfsHostInput.Visibility = isHttp ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ServerIsShadowTlsInput_Changed(object sender, RoutedEventArgs e)
        {
            if (ServerIsShadowTlsInput == null || ShadowTlsSubPanel == null) return;
            ShadowTlsSubPanel.Visibility = (ServerIsShadowTlsInput.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ServersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            ViewModel.ReorderServersCommand.Execute(null);
        }

        private void PopulateProxyChainOptions(string excludeId)
        {
            if (ServerProxyChainInput == null) return;
            ServerProxyChainInput.Items.Clear();

            // Add Direct Option
            var directItem = new ComboBoxItem { Content = "直连 (Direct)", Tag = "" };
            ServerProxyChainInput.Items.Add(directItem);
            ServerProxyChainInput.SelectedItem = directItem;

            foreach (var node in NodesManager.Instance.Nodes)
            {
                if (!string.IsNullOrEmpty(excludeId) && node.Id == excludeId)
                    continue;

                var item = new ComboBoxItem
                {
                    Content = $"{node.Name} ({node.Protocol})",
                    Tag = node.Id
                };
                ServerProxyChainInput.Items.Add(item);
            }
        }

        private void UpdateFormFieldsVisibility()
        {
            if (ServerProtocolInput == null || 
                PasswordPanel == null || 
                UuidPanel == null || 
                EncryptionPanel == null || 
                NetworkPanel == null || 
                PathHostPanel == null || 
                SecurityPanel == null || 
                TlsPanel == null || 
                RealityPanel == null ||
                FlowPanel == null ||
                ServerUuidInput == null ||
                ServerNetworkInput == null ||
                ServerSecurityInput == null ||
                ServerEncryptionInput == null ||
                AlterIdPanel == null ||
                ObfsPanel == null ||
                ShadowTlsPanel == null ||
                HeaderTypePanel == null ||
                WireGuardPanel == null ||
                SnellPanel == null ||
                ServerWgPrivateKeyInput == null) 
                return;

            string proto = "";
            if (ServerProtocolInput.SelectedItem is ComboBoxItem cbi)
            {
                proto = (cbi.Content?.ToString() ?? "").ToUpper();
            }

            // 1. Password Panel: visible for Shadowsocks, Trojan, Hysteria 2, TUIC, NaiveProxy, SOCKS, HTTP
            bool showPassword = proto == "SHADOWSOCKS" || proto == "TROJAN" || proto == "HYSTERIA 2" || 
                               proto == "TUIC" || proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP";
            PasswordPanel.Visibility = showPassword ? Visibility.Visible : Visibility.Collapsed;

            // 2. UUID Panel: visible for VLESS, VMess, TUIC, NaiveProxy, SOCKS, HTTP
            bool showUuid = proto == "VLESS" || proto == "VMESS" || proto == "TUIC" || 
                            proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP";
            UuidPanel.Visibility = showUuid ? Visibility.Visible : Visibility.Collapsed;
            
            // Customize Uuid label
            if (ServerUuidInput != null)
            {
                if (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP")
                {
                    ServerUuidInput.Header = "用户名 (Username)";
                }
                else if (proto == "TUIC")
                {
                    ServerUuidInput.Header = "UUID (TUIC)";
                }
                else
                {
                    ServerUuidInput.Header = "UUID (VMess / VLESS)";
                }
            }

            // 3. Encryption Panel: visible for Shadowsocks, VMess
            bool showEncryption = proto == "SHADOWSOCKS" || proto == "VMESS";
            EncryptionPanel.Visibility = showEncryption ? Visibility.Visible : Visibility.Collapsed;

            // Update encryption choices based on protocol
            UpdateEncryptionDropdown(proto);

            // 4. Network Panel: not applicable to Hysteria 2, TUIC, NaiveProxy
            bool showNetwork = proto != "HYSTERIA 2" && proto != "TUIC" && proto != "NAIVEPROXY";
            NetworkPanel.Visibility = showNetwork ? Visibility.Visible : Visibility.Collapsed;

            // 5. Security Panel: not applicable to Hysteria 2, TUIC, NaiveProxy, SOCKS
            bool showSecurity = proto != "HYSTERIA 2" && proto != "TUIC" && proto != "NAIVEPROXY" && proto != "SOCKS";
            SecurityPanel.Visibility = showSecurity ? Visibility.Visible : Visibility.Collapsed;

            // Get selected Network and Security
            string network = "TCP";
            if (ServerNetworkInput != null && ServerNetworkInput.SelectedItem is ComboBoxItem netItem)
            {
                network = (netItem.Content?.ToString() ?? "").ToUpper();
            }

            string security = "NONE";
            if (ServerSecurityInput != null && ServerSecurityInput.SelectedItem is ComboBoxItem secItem)
            {
                security = (secItem.Content?.ToString() ?? "").ToUpper();
            }

            // 6. Path & WsHost Panel: visible when Network is WS, GRPC, XHTTP, H2
            bool showPathHost = showNetwork && (network == "WS" || network == "GRPC" || network == "XHTTP" || network == "H2");
            PathHostPanel.Visibility = showPathHost ? Visibility.Visible : Visibility.Collapsed;

            // 7. TLS Panel: visible when Security is TLS/REALITY, or protocol is Hysteria 2, TUIC, NaiveProxy, AnyTLS
            bool isTls = (showSecurity && (security == "TLS" || security == "REALITY")) || 
                         proto == "HYSTERIA 2" || proto == "TUIC" || proto == "NAIVEPROXY" || proto == "ANYTLS";
            TlsPanel.Visibility = isTls ? Visibility.Visible : Visibility.Collapsed;

            // 8. Reality Panel: visible when Security is REALITY
            bool isReality = showSecurity && security == "REALITY";
            RealityPanel.Visibility = isReality ? Visibility.Visible : Visibility.Collapsed;

            // 9. Flow Panel: visible when VLESS and (Security is TLS or REALITY)
            bool isFlow = proto == "VLESS" && showSecurity && (security == "TLS" || security == "REALITY");
            FlowPanel.Visibility = isFlow ? Visibility.Visible : Visibility.Collapsed;

            // 10. AlterId Panel
            AlterIdPanel.Visibility = proto == "VMESS" ? Visibility.Visible : Visibility.Collapsed;

            // 11. Obfs Panel
            ObfsPanel.Visibility = (proto == "HYSTERIA 2" || proto == "HYSTERIA") ? Visibility.Visible : Visibility.Collapsed;

            // 12. Shadow-TLS Panel
            ShadowTlsPanel.Visibility = (proto == "SHADOWSOCKS" || proto == "SNELL") ? Visibility.Visible : Visibility.Collapsed;

            // 13. Header Type Panel
            bool showHeaderType = showNetwork && (network == "TCP" || network == "KCP" || network == "QUIC");
            HeaderTypePanel.Visibility = showHeaderType ? Visibility.Visible : Visibility.Collapsed;

            // 14. WireGuard Panel
            WireGuardPanel.Visibility = proto == "WIREGUARD" ? Visibility.Visible : Visibility.Collapsed;

            // 15. Snell Panel
            SnellPanel.Visibility = proto == "SNELL" ? Visibility.Visible : Visibility.Collapsed;

            // Hide standard panels that don't apply to Snell
            if (proto == "SNELL")
            {
                PasswordPanel.Visibility = Visibility.Collapsed;
                NetworkPanel.Visibility = Visibility.Collapsed;
                SecurityPanel.Visibility = Visibility.Collapsed;
                TlsPanel.Visibility = Visibility.Collapsed;
                UuidPanel.Visibility = Visibility.Collapsed;
            }

            // 16. Trigger sub-panel selection handlers
            ServerObfsTypeInput_SelectionChanged(null!, null!);
            ServerIsShadowTlsInput_Changed(null!, null!);
        }

        private void UpdateEncryptionDropdown(string proto)
        {
            if (ServerEncryptionInput == null) return;

            string currentSelected = "";
            if (ServerEncryptionInput.SelectedItem is ComboBoxItem currentItem)
            {
                currentSelected = currentItem.Content?.ToString() ?? "";
            }

            ServerEncryptionInput.Items.Clear();

            if (proto == "SHADOWSOCKS")
            {
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-256-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "chacha20-ietf-poly1305" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-aes-256-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-chacha20-poly1305" });
            }
            else if (proto == "VMESS")
            {
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "auto" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "none" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "chacha20-poly1305" });
            }

            // Restore selection or select first
            if (ServerEncryptionInput.Items.Count > 0)
            {
                var match = ServerEncryptionInput.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content.ToString() == currentSelected);
                if (match != null)
                {
                    ServerEncryptionInput.SelectedItem = match;
                }
                else
                {
                    ServerEncryptionInput.SelectedIndex = 0;
                }
            }
        }
    }
}
