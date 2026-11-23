using AnywhereWinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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

        public bool IsCustom => CustomConfigService.IsCustomProtocol(Protocol);

        public string HostPortDisplay => IsCustom
            ? (string.IsNullOrWhiteSpace(Host) ? "sing-box JSON" : Host)
            : NodeLinkParser.FormatHostPortPublic(Host, Port);

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? HostPortDisplay : Name;
        }

        private string _pingText = L.Servers_PingUntested;
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

        private int? _latencyMs;
        public int? LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (_latencyMs != value)
                {
                    _latencyMs = value;
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
                FavoriteVisibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public string DisplayProtocol => Protocol.ToUpper();

        public Brush ProtocolBrush => ProtocolColorStore.GetBrush(Protocol);

        public void NotifyProtocolColorChanged() => OnPropertyChanged(nameof(ProtocolBrush));
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
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchDebounceTimer;
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

        private static readonly SolidColorBrush _topSheetWarnBrush = new(Color.FromArgb(255, 251, 146, 60));
        private static readonly SolidColorBrush _topSheetErrorBrush = new(Color.FromArgb(255, 248, 113, 113));
        private static readonly SolidColorBrush _topSheetTimeoutBrush = new(Colors.Gray);

        public ServersPage()
        {
            NavigationCacheMode = NavigationCacheMode.Required;

            ViewModel = App.Current.Services.GetRequiredService<ServersViewModel>();
            this.DataContext = ViewModel;

            this.InitializeComponent();
            ServersListView.ItemsSource = ViewModel.FilteredServers;
            GroupFilterComboBox.ItemsSource = ViewModel.GroupFilters;
            GroupFilterComboBox.SelectedValue = ViewModel.SelectedGroupFilterId;
            UpdateGroupFilterButtonToolTip();
            UpdateSortMenuCheckedState();

            // Auto-select persisted active node so detail panel & AI/IP checks match on launch
            var initialNode = ViewModel.FilteredServers.FirstOrDefault(s => s.Id == NodesManager.Instance.SelectedNodeId)
                           ?? ViewModel.FilteredServers.FirstOrDefault();
            if (initialNode != null)
            {
                ServersListView.SelectedItem = initialNode;
            }

            // Keep subscriptions for page lifetime (NavigationCacheMode.Required reuses this single instance)
            CoreManager.Instance.RunningChanged += CoreManager_RunningChanged;
            CoreManager.Instance.TrafficUpdated += CoreManager_TrafficUpdated;
            CoreManager.Instance.ActiveNodeChanged += CoreManager_ActiveNodeChanged;
            CoreManager.Instance.ModeStateChanged += CoreManager_ModeStateChanged;
            MainWindow.VisibilityToUserChanged += OnMainWindowVisibilityChanged;

            UpdateControlBarUI();
            UpdateProxyModeUI();
            UpdateRoutingModeUI();
        }

        private void CoreManager_ActiveNodeChanged(object? sender, EventArgs e)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                UpdateControlBarUI();
                if (_isTopSheetOpen && CoreManager.Instance.IsRunning)
                {
                    var nodeName = AppSession.Instance.IsAutoNodeSelect
                        ? $"{L.TrayPanel_AutoTitle} ({L.TrayPanel_AutoSubtitle})"
                        : (NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? "---");
                    TopSheetNodeNameText.Text = nodeName;
                    if (!_isLatencyProbing)
                        _ = RefreshTopSheetLatencyAsync();
                }
            });
        }

        private void CoreManager_ModeStateChanged(object? sender, EventArgs e)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                UpdateProxyModeUI();
                UpdateRoutingModeUI();
            });
        }

        private void UpdateProxyModeUI()
        {
            var mode = AppSession.Instance.ProxyModeIndex;
            if (mode == 0) {
                ProxyModeIcon.Glyph = "\uEC27";
                ToolTipService.SetToolTip(ProxyModeButton, L.ProxyMode_System);
            } else if (mode == 1) {
                ProxyModeIcon.Glyph = "\uE839";
                ToolTipService.SetToolTip(ProxyModeButton, L.ProxyMode_Tun);
            } else if (mode == 3) {
                ProxyModeIcon.Glyph = "\uE83D";
                ToolTipService.SetToolTip(ProxyModeButton, L.ProxyMode_TunSystem);
            } else {
                ProxyModeIcon.Glyph = "\uF384";
                ToolTipService.SetToolTip(ProxyModeButton, L.ProxyMode_Manual);
            }
        }

        private void UpdateRoutingModeUI()
        {
            var mode = AppSession.Instance.RoutingMode;
            bool isTun = AppSession.Instance.EnableTunMode;

            var currentNode = ServersListView?.SelectedItem is ServerEntryItem selItem
                ? NodesManager.Instance.Nodes.Find(n => n.Id == selItem.Id)
                : NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);

            if (CustomConfigService.IsCustomNode(currentNode))
            {
                ToolTipService.SetToolTip(RoutingModeButton, L.CustomCfg_RouteSelfManaged);
            }
            else
            {
                string modeName = mode == "direct" ? L.RouteMode_Direct : mode == "global" ? L.RouteMode_Global : L.RouteMode_Smart;
                ToolTipService.SetToolTip(RoutingModeButton, isTun ? $"TUN ({modeName})" : modeName);
            }

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
                if (ViewModel.SortMode == ServerSortMode.Active)
                {
                    ViewModel.ApplyFilters();
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

                string selectedName = AppSession.Instance.IsAutoNodeSelect
                    ? $"{L.TrayPanel_AutoTitle} · {L.TrayPanel_AutoSubtitle}"
                    : (NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? L.Common_UnknownNode);
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

                ActiveNodeText.Text = L.Common_Disconnected;
                
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

                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    bool isCustomNode = CustomConfigService.IsCustomNode(node);
                    bool needsGlobalTunAdmin = AppSession.Instance.EnableTunMode && !isCustomNode && !Helpers.AdminHelper.IsAdministrator();
                    bool needsCustomTunAdmin = CustomConfigService.RequiresTunAdmin(node);

                    if (needsGlobalTunAdmin || needsCustomTunAdmin)
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
                                    Title = L.Tun_AdminRequiredTitle,
                                    Content = L.Tun_AdminRequiredMsg,
                                    PrimaryButtonText = L.Tun_AdminConfirmBtn,
                                    CloseButtonText = L.Dialog_Cancel,
                                    XamlRoot = this.XamlRoot
                                };

                                var result = await dialog.WithAppTheme().ShowAsync();
                                if (result != ContentDialogResult.Primary)
                                {
                                    tcs.SetResult(false);
                                    return;
                                }

                                string arg = needsCustomTunAdmin
                                    ? "--auto-start-proxy"
                                    : (AppSession.Instance.ProxyModeIndex == 3 ? "--tun-system-start" : "--tun-start");
                                bool restarted = Helpers.AdminHelper.RestartAsAdmin(arg);
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

                    string realConfig = await ConfigBuilder.BuildAsync(node);

                    bool success = await CoreManager.Instance.StartAsync(realConfig);
                    if (!success)
                    {
                        StartStopButton.IsChecked = false;
                        var dialog = new ContentDialog
                        {
                            Title = L.Core_StartFailedTitle,
                            Content = Loc.Format("Core_StartFailedWithError", CoreManager.Instance.LastError),
                            CloseButtonText = L.Dialog_OK,
                            XamlRoot = this.XamlRoot
                        };
                        _ = dialog.WithAppTheme().ShowAsync();
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
                    Title = L.Core_ConfigBuildFailedTitle,
                    Content = Loc.Format("Core_ConfigBuildFailedMsg", ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                _ = dialog.WithAppTheme().ShowAsync();
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
                        string realConfig = await ConfigBuilder.BuildAsync(node);
                        await CoreManager.Instance.StopAsync();
                        await CoreManager.Instance.StartAsync(realConfig);
                    }
                }
            }
        }

        private void OnMainWindowVisibilityChanged(object? sender, bool isVisible)
        {
            if (!isVisible) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                var latest = CoreManager.Instance.LatestTrafficStats;
                DownSpeedText.Text = FormatBytes(latest.DownSpeed) + "/s";
                UpSpeedText.Text = FormatBytes(latest.UpSpeed) + "/s";
                SessionUsageText.Text = FormatBytes(latest.Down + latest.Up);
                if (CoreManager.Instance.IsRunning)
                {
                    TopSheetUptimeText.Text = (DateTime.Now - _sessionStartTime).ToString(@"hh\:mm\:ss");
                    if (_isTopSheetOpen)
                    {
                        TopSheetDownText.Text = DownSpeedText.Text;
                        TopSheetUpText.Text = UpSpeedText.Text;
                    }
                }
            });
        }

        private void CoreManager_TrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            // True tray hibernation: zero closure allocation, zero UI-thread wakeups,
            // and zero background latency probes while MainWindow is hidden to tray!
            if (!MainWindow.IsVisibleToUser)
                return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!MainWindow.IsVisibleToUser) return;

                DownSpeedText.Text = FormatBytes(e.DownSpeed) + "/s";
                UpSpeedText.Text = FormatBytes(e.UpSpeed) + "/s";
                SessionUsageText.Text = FormatBytes(e.Down + e.Up);

                if (CoreManager.Instance.IsRunning)
                {
                    TopSheetUptimeText.Text = (DateTime.Now - _sessionStartTime).ToString(@"hh\:mm\:ss");
                    
                    if (_isTopSheetOpen)
                    {
                        TopSheetDownText.Text = DownSpeedText.Text;
                        TopSheetUpText.Text = UpSpeedText.Text;

                        _realDownTraffic.Add(e.DownSpeed);
                        _realUpTraffic.Add(e.UpSpeed);
                        
                        if (_realDownTraffic.Count > 60) _realDownTraffic.RemoveAt(0);
                        if (_realUpTraffic.Count > 60) _realUpTraffic.RemoveAt(0);
                    }

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
            ViewModel.LoadSubscriptions();
            ViewModel.LoadServersList();
            GroupFilterComboBox.SelectedValue = ViewModel.SelectedGroupFilterId;
            UpdateGroupFilterButtonToolTip();
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            ViewModel.SearchQuery = ServerSearchBox.Text;
            if (GroupFilterComboBox.SelectedValue is string selectedGroupId &&
                !string.Equals(ViewModel.SelectedGroupFilterId, selectedGroupId, StringComparison.Ordinal))
            {
                ViewModel.SelectedGroupFilterId = selectedGroupId;
            }
            // NOTE: Do NOT call ViewModel.LoadServersList() here.
            // Setting SearchQuery / SelectedGroupFilterId above already triggers
            // OnSearchQueryChanged / OnSelectedGroupFilterIdChanged in the ViewModel,
            // which internally calls ApplyFilters() to rebuild FilteredServers.
            // Calling LoadServersList() here would reconstruct AllServers from
            // NodesManager.Instance.Nodes every time the user types in the search box
            // or toggles favourites, effectively discarding any drag-reorder that was
            // already applied in-memory (and saved to disk) by ReorderServers().

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
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ApplyFilters();
                return;
            }

            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = DispatcherQueue.CreateTimer();
                _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
                _searchDebounceTimer.IsRepeating = false;
                _searchDebounceTimer.Tick += (_, _) =>
                {
                    var query = ServerSearchBox.Text.Trim();
                    if (string.IsNullOrEmpty(query))
                    {
                        ServerSearchBox.ItemsSource = null;
                    }
                    else
                    {
                        ServerSearchBox.ItemsSource = AllServers
                            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                        s.Host.Contains(query, StringComparison.OrdinalIgnoreCase))
                            .Take(20)
                            .ToList();
                    }
                    ApplyFilters();
                };
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
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

        private void GroupFilterToggleButton_Changed(object sender, RoutedEventArgs e)
        {
            GroupFilterBar.Visibility = GroupFilterToggleButton.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 直接更新 ViewModel，触发 OnSelectedGroupFilterIdChanged → ViewModel.ApplyFilters()
            // 无需再调 View 的 ApplyFilters()，避免列表双重重建
            if (GroupFilterComboBox.SelectedValue is string selectedGroupId)
            {
                ViewModel.SelectedGroupFilterId = selectedGroupId;
            }
            UpdateGroupFilterButtonToolTip();
        }

        private void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item &&
                item.Tag is string tag &&
                Enum.TryParse<ServerSortMode>(tag, out var mode))
            {
                ViewModel.SortMode = mode;  // 自动触发 OnSortModeChanged → ApplyFilters()
                UpdateSortMenuCheckedState();
                // 无需手动再调 ApplyFilters()，避免列表闪烁重建两次
            }
        }

        private void UpdateGroupFilterButtonToolTip()
        {
            var selected = ViewModel.GroupFilters.FirstOrDefault(g =>
                string.Equals(g.Id, ViewModel.SelectedGroupFilterId, StringComparison.Ordinal));
            var name = selected?.Name ?? L.Servers_FilterAll;
            ToolTipService.SetToolTip(GroupFilterToggleButton, Loc.Format("Servers_FilterTooltipFmt", name));
        }

        private void UpdateSortMenuCheckedState()
        {
            SortDefaultItem.IsChecked = ViewModel.SortMode == ServerSortMode.Default;
            SortActiveItem.IsChecked = ViewModel.SortMode == ServerSortMode.Active;
            SortProtocolItem.IsChecked = ViewModel.SortMode == ServerSortMode.Protocol;
            SortLatencyItem.IsChecked = ViewModel.SortMode == ServerSortMode.Latency;

            var sortName = ViewModel.SortMode switch
            {
                ServerSortMode.Active => L.Servers_SortActive,
                ServerSortMode.Protocol => L.Servers_SortProtocol,
                ServerSortMode.Latency => L.Servers_SortLatency,
                _ => L.Servers_SortDefault
            };
            ToolTipService.SetToolTip(SortButton, Loc.Format("Servers_SortTooltipFmt", sortName));
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
    }
}
