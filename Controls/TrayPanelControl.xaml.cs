using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;
using AnywhereWinUI.Views;

namespace AnywhereWinUI.Controls
{
    public sealed partial class TrayPanelControl : UserControl
    {
        private const int MaxQuickNodes = 8;

        private readonly Action _onCloseFlyout;
        private readonly Action _onOpenServersPage;
        private readonly Action _onOpenSettingsAbout;
        private readonly Func<Task> _onRequestTunAdminStartAsync;

        private bool _isAttached;
        private bool _suppressToggleEvent;
        private bool _suppressModeSegmentEvent;
        private bool _isBusySwitching;
        private bool _isPingingNodes;
        private string? _autoResolvedNodeId;
        private CancellationTokenSource? _panelCts;
        private readonly List<PersistedNode> _displayedNodes = new(MaxQuickNodes);

        public TrayPanelControl(
            Action onCloseFlyout,
            Action onOpenServersPage,
            Action onOpenSettingsAbout,
            Func<Task> onRequestTunAdminStartAsync)
        {
            _onCloseFlyout = onCloseFlyout;
            _onOpenServersPage = onOpenServersPage;
            _onOpenSettingsAbout = onOpenSettingsAbout;
            _onRequestTunAdminStartAsync = onRequestTunAdminStartAsync;

            this.InitializeComponent();
            ApplyStaticLocalizedLabels();
        }

        private void ApplyStaticLocalizedLabels()
        {
            NodesSectionHeaderText.Text = L.TrayPanel_NodesHeader;
            QuickPingBtnText.Text = L.TrayPanel_PingBtn;
            AutoTitleText.Text = L.TrayPanel_AutoTitle;
            AutoSubtitleText.Text = L.TrayPanel_AutoSubtitle;
            AllNodesLabelText.Text = L.TrayPanel_AllNodes;
            ModeLabelText.Text = L.TrayPanel_ModeLabel;
            ModeRuleItem.Content = L.TrayPanel_ModeRuleShort;
            ModeGlobalItem.Content = L.TrayPanel_ModeGlobalShort;
            ModeDirectItem.Content = L.TrayPanel_ModeDirectShort;
            RetakeProxyBtn.Content = L.Dashboard_ProxyRetakeBtn;
        }

        public void AttachAndRefresh()
        {
            ApplyStaticLocalizedLabels();

            _panelCts?.Cancel();
            _panelCts?.Dispose();
            _panelCts = new CancellationTokenSource();

            if (!_isAttached)
            {
                _isAttached = true;
                this.ActualThemeChanged += OnPanelThemeChanged;
                CoreManager.Instance.RunningChanged += OnCoreRunningChanged;
                CoreManager.Instance.TrafficUpdated += OnCoreTrafficUpdated;
                CoreManager.Instance.ActiveNodeChanged += OnCoreStateChanged;
                CoreManager.Instance.ModeStateChanged += OnCoreStateChanged;
                CoreManager.Instance.SystemProxyTampered += OnSystemProxyTampered;
            }

            // Temporarily wake 1s live speed polling ONLY while this quick panel is visibly open
            CoreManager.Instance.SetTrayPanelOpen(true);

            RefreshAllUI();

            if (CoreManager.Instance.IsRunning && AppSession.Instance.IsAutoNodeSelect)
            {
                _ = RefreshAutoResolvedNodeAsync(_panelCts.Token);
            }
        }

        public void DetachAndTrim()
        {
            // Return CoreManager immediately to 5s zero-allocation tray hibernation
            CoreManager.Instance.SetTrayPanelOpen(false);

            if (_isAttached)
            {
                _isAttached = false;
                this.ActualThemeChanged -= OnPanelThemeChanged;
                CoreManager.Instance.RunningChanged -= OnCoreRunningChanged;
                CoreManager.Instance.TrafficUpdated -= OnCoreTrafficUpdated;
                CoreManager.Instance.ActiveNodeChanged -= OnCoreStateChanged;
                CoreManager.Instance.ModeStateChanged -= OnCoreStateChanged;
                CoreManager.Instance.SystemProxyTampered -= OnSystemProxyTampered;
            }

            try
            {
                _panelCts?.Cancel();
                _panelCts?.Dispose();
                _panelCts = null;
            }
            catch { }

            _displayedNodes.Clear();
            ClearNodeButtons();
        }

        private void ClearNodeButtons()
        {
            foreach (var child in NodesStackPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Click -= NodeRowBtn_Click;
                    btn.Click -= AllNodesBtn_Click;
                    btn.Tag = null;
                    btn.Content = null;
                }
            }
            NodesStackPanel.Children.Clear();
        }

        private void OnPanelThemeChanged(FrameworkElement sender, object args)
        {
            if (_isAttached)
            {
                RefreshAllUI();
            }
        }

        private void OnCoreRunningChanged(object? sender, bool isRunning)
        {
            if (!_isAttached) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isAttached) return;
                RefreshAllUI();
                if (isRunning && AppSession.Instance.IsAutoNodeSelect && _panelCts != null)
                {
                    _ = RefreshAutoResolvedNodeAsync(_panelCts.Token);
                }
            });
        }

        private void OnCoreTrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            if (!_isAttached) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isAttached) return;
                RefreshTelemetryUI(e.DownSpeed, e.UpSpeed);
            });
        }

        private void OnCoreStateChanged(object? sender, EventArgs e)
        {
            if (!_isAttached) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isAttached) return;
                RefreshAllUI();
            });
        }

        private void OnSystemProxyTampered(object? sender, SystemProxyTamperResult e)
        {
            if (!_isAttached) return;
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isAttached) return;
                RefreshHeaderAndBanners();
            });
        }

        private async Task RefreshAutoResolvedNodeAsync(CancellationToken ct)
        {
            var nowTag = await SingboxApiClient.Instance.GetGroupSelectedAsync("urltest", ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isAttached || ct.IsCancellationRequested) return;
                _autoResolvedNodeId = nowTag;
                RebuildNodeListUI();
            });
        }

        private void RefreshAllUI()
        {
            RefreshHeaderAndBanners();
            var stats = CoreManager.Instance.LatestTrafficStats;
            RefreshTelemetryUI(
                CoreManager.Instance.IsRunning ? stats.DownSpeed : 0,
                CoreManager.Instance.IsRunning ? stats.UpSpeed : 0);
            RebuildNodeListUI();
            RefreshModeSegmentUI();
        }

        private void RefreshHeaderAndBanners()
        {
            bool isRunning = CoreManager.Instance.IsRunning;
            var tamper = CoreManager.Instance.LastProxyTamperState;
            bool isTampered = isRunning && tamper.IsTampered;

            _suppressToggleEvent = true;
            MasterProxyToggle.IsOn = isRunning;
            _suppressToggleEvent = false;

            Brush successBrush = LookupBrush("SystemFillColorSuccessBrush", Color.FromArgb(255, 16, 185, 129));
            Brush cautionBrush = LookupBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 245, 158, 11));
            Brush neutralBrush = LookupBrush("SystemFillColorNeutralBrush", Color.FromArgb(255, 156, 163, 175));

            if (_isBusySwitching)
            {
                StatusDot.Fill = cautionBrush;
                StatusTitleText.Text = isRunning ? L.TrayPanel_StatusSwitching : L.TrayPanel_StatusStarting;
            }
            else if (isRunning)
            {
                StatusDot.Fill = isTampered ? cautionBrush : successBrush;
                StatusTitleText.Text = L.TrayPanel_StatusNormal;
            }
            else
            {
                StatusDot.Fill = neutralBrush;
                StatusTitleText.Text = L.TrayPanel_StatusStopped;
            }

            // Concise mode suffix (e.g. "· TUN", "· 全局", "· TUN · 全局")
            var suffixes = new List<string>(2);
            if (AppSession.Instance.EnableTunMode)
                suffixes.Add("TUN");
            string rMode = AppSession.Instance.RoutingMode;
            if (string.Equals(rMode, "global", StringComparison.OrdinalIgnoreCase))
                suffixes.Add(L.TrayPanel_ModeGlobalShort);
            else if (string.Equals(rMode, "direct", StringComparison.OrdinalIgnoreCase))
                suffixes.Add(L.TrayPanel_ModeDirectShort);

            StatusModeSuffixText.Text = suffixes.Count > 0
                ? "· " + string.Join(" · ", suffixes)
                : string.Empty;

            TamperBannerText.Text = L.TrayPanel_ProxyTamperedShort;
            TamperBanner.Visibility = isTampered ? Visibility.Visible : Visibility.Collapsed;

            if (AppUpdateService.HasPendingUpdate && !string.IsNullOrEmpty(AppUpdateService.PendingVersionTag))
            {
                UpdateBannerText.Text = string.Format(L.TrayPanel_UpdateReadyFmt, AppUpdateService.PendingVersionTag);
                UpdateActionBtn.Content = L.Settings_RestartNowBtn;
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else if (MainWindow.Instance?.ViewModel?.AvailableUpdate is { } avail && !string.IsNullOrEmpty(avail.TagName))
            {
                UpdateBannerText.Text = string.Format(L.TrayPanel_UpdateAvailFmt, avail.TagName);
                UpdateActionBtn.Content = L.TrayPanel_UpdateViewBtn;
                UpdateBanner.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshTelemetryUI(long downSpeed, long upSpeed)
        {
            LiveSpeedText.Text = $"↓ {FormatSpeed(downSpeed)}   ↑ {FormatSpeed(upSpeed)}";

            long todayBytes = 0;
            try
            {
                var trafficVm = ((App)Application.Current).Services.GetService(typeof(TrafficViewModel)) as TrafficViewModel;
                if (trafficVm != null)
                {
                    todayBytes = trafficVm.GetTodayTotalBytes();
                }
            }
            catch { }

            TodayUsageText.Text = string.Format(L.TrayPanel_TodayFmt, FormatCompactBytes(todayBytes));
        }

        private void RebuildNodeListUI()
        {
            ClearNodeButtons();
            _displayedNodes.Clear();

            var allNodes = NodesManager.Instance.GetNodesSnapshot();
            var allValidNodes = allNodes.Where(n => !IsPseudoInfoNode(n)).ToList();
            var nodeById = new Dictionary<string, PersistedNode>(allNodes.Count, StringComparer.Ordinal);
            foreach (var n in allNodes)
            {
                nodeById[n.Id] = n;
            }

            var serversVm = ((App)Application.Current).Services.GetService(typeof(ServersViewModel)) as ServersViewModel;

            var latencyMap = new Dictionary<string, int?>(StringComparer.Ordinal);
            List<PersistedNode> validNodes;

            if (serversVm != null)
            {
                int loadableCount = allNodes.Count(n =>
                    CustomConfigService.IsCustomNode(n) || NodeLinkParser.TrySplitHostPort(n.Host, out _, out _));
                if (serversVm.AllServers.Count != loadableCount)
                {
                    serversVm.LoadSubscriptions();
                    serversVm.LoadServersList();
                }

                foreach (var s in serversVm.AllServers)
                {
                    latencyMap[s.Id] = s.LatencyMs;
                }

                string? filterLabel = serversVm.ActiveGroupFilterDisplayName;
                NodesSectionHeaderText.Text = !string.IsNullOrWhiteSpace(filterLabel)
                    ? $"{L.TrayPanel_NodesHeader} · {filterLabel}"
                    : L.TrayPanel_NodesHeader;

                validNodes = new List<PersistedNode>(serversVm.FilteredServers.Count);
                foreach (var item in serversVm.FilteredServers)
                {
                    if (nodeById.TryGetValue(item.Id, out var persisted) && !IsPseudoInfoNode(persisted))
                    {
                        validNodes.Add(persisted);
                    }
                }
            }
            else
            {
                NodesSectionHeaderText.Text = L.TrayPanel_NodesHeader;
                validNodes = allValidNodes;
            }

            AllNodesCountText.Text = string.Format(L.TrayPanel_NodeCountFmt, validNodes.Count);

            bool isAuto = AppSession.Instance.IsAutoNodeSelect;
            string selectedId = NodesManager.Instance.SelectedNodeId;

            AutoSelectBtn.Visibility = allValidNodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            QuickPingBtn.Visibility = validNodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            QuickPingBtnText.Text = _isPingingNodes
                ? L.TrayPanel_Pinging
                : (_isBusySwitching ? L.TrayPanel_StatusSwitching : L.TrayPanel_PingBtn);

            Brush selectedRowBg = LookupBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(24, 128, 128, 128));
            AutoAccentBar.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
            AutoTitleText.FontWeight = isAuto
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            if (isAuto)
                AutoSelectBtn.Background = selectedRowBg;
            else
                AutoSelectBtn.ClearValue(Control.BackgroundProperty);

            if (isAuto && !string.IsNullOrEmpty(_autoResolvedNodeId) && nodeById.TryGetValue(_autoResolvedNodeId, out var resolved))
            {
                AutoResolvedNodeText.Text = resolved.Name ?? string.Empty;
            }
            else
            {
                AutoResolvedNodeText.Text = string.Empty;
            }

            Style rowStyle = (Style)Resources["TrayFluentItemButtonStyle"];
            Brush secondaryFg = LookupBrush("TextFillColorSecondaryBrush", Colors.Gray);

            if (validNodes.Count == 0)
            {
                var emptyBtn = new Button
                {
                    Style = rowStyle,
                    Content = new TextBlock
                    {
                        Text = L.TrayPanel_EmptyNodes,
                        FontSize = 12,
                        Foreground = secondaryFg,
                        Margin = new Thickness(16, 2, 0, 2)
                    }
                };
                emptyBtn.Click += AllNodesBtn_Click;
                NodesStackPanel.Children.Add(emptyBtn);
                return;
            }

            List<PersistedNode> ranked;
            if (serversVm != null && serversVm.SortMode != ServerSortMode.Default)
            {
                // Preserve user's explicit sort mode from ServersPage (Active / Protocol / Latency)
                ranked = validNodes;
            }
            else
            {
                ranked = validNodes
                    .OrderByDescending(n => n.IsFavorite)
                    .ThenBy(n =>
                    {
                        if (latencyMap.TryGetValue(n.Id, out var ms) && ms is > 0) return 0;
                        if (ms is < 0) return 2;
                        return 1;
                    })
                    .ThenBy(n => latencyMap.TryGetValue(n.Id, out var ms) && ms is > 0 ? ms.Value : int.MaxValue)
                    .ToList();
            }

            var picked = new List<PersistedNode>(MaxQuickNodes);
            var pickedIds = new HashSet<string>(StringComparer.Ordinal);

            void TryPinNode(string? id)
            {
                if (string.IsNullOrEmpty(id)) return;
                var match = validNodes.Find(n => n.Id == id);
                if (match != null && pickedIds.Add(match.Id))
                    picked.Add(match);
            }

            if (!isAuto)
                TryPinNode(selectedId);
            else if (!string.IsNullOrEmpty(_autoResolvedNodeId))
                TryPinNode(_autoResolvedNodeId);

            foreach (var candidate in ranked)
            {
                if (picked.Count >= MaxQuickNodes) break;
                if (pickedIds.Add(candidate.Id))
                    picked.Add(candidate);
            }

            Brush primaryFg = LookupBrush("TextFillColorPrimaryBrush", Colors.White);
            Brush tertiaryFg = LookupBrush("TextFillColorTertiaryBrush", Colors.Gray);
            Brush accentBarBrush = LookupBrush("AccentFillColorDefaultBrush", Color.FromArgb(255, 59, 130, 246));
            Brush accentTextBrush = LookupBrush("AccentTextFillColorPrimaryBrush", Color.FromArgb(255, 96, 165, 250));
            Brush badgeBgBrush = LookupBrush("SystemFillColorAttentionBackgroundBrush", Color.FromArgb(36, 59, 130, 246));
            Brush successBrush = LookupBrush("SystemFillColorSuccessBrush", Color.FromArgb(255, 16, 185, 129));
            Brush cautionBrush = LookupBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 245, 158, 11));
            Brush criticalBrush = LookupBrush("SystemFillColorCriticalBrush", Color.FromArgb(255, 239, 68, 68));

            foreach (var node in picked)
            {
                _displayedNodes.Add(node);
                bool isSelectedManual = !isAuto && string.Equals(node.Id, selectedId, StringComparison.Ordinal);
                bool isAutoCurrent = isAuto && !string.IsNullOrEmpty(_autoResolvedNodeId) &&
                                     string.Equals(node.Id, _autoResolvedNodeId, StringComparison.Ordinal);

                var rowGrid = new Grid { ColumnSpacing = 4 };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // WinUI 3 signature 3px rounded vertical Accent selection pill
                if (isSelectedManual)
                {
                    var pill = new Border
                    {
                        Width = 3,
                        Height = 16,
                        CornerRadius = new CornerRadius(1.5),
                        Background = accentBarBrush,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(pill, 0);
                    rowGrid.Children.Add(pill);
                }

                var nameText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(node.Name) ? node.Host : node.Name,
                    FontSize = 13,
                    FontWeight = isSelectedManual ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = primaryFg,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 1);
                rowGrid.Children.Add(nameText);

                if (isAutoCurrent)
                {
                    var badge = new Border
                    {
                        Background = badgeBgBrush,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = L.TrayPanel_CurrentBadge,
                            FontSize = 11,
                            Foreground = accentTextBrush
                        }
                    };
                    Grid.SetColumn(badge, 2);
                    rowGrid.Children.Add(badge);
                }

                var (latText, latBrush) = FormatLatencyBadge(node, latencyMap, tertiaryFg, successBrush, cautionBrush, criticalBrush);
                var pingBlock = new TextBlock
                {
                    Text = latText,
                    FontSize = 12,
                    Foreground = latBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Typography.SetNumeralAlignment(pingBlock, FontNumeralAlignment.Tabular);
                Grid.SetColumn(pingBlock, 3);
                rowGrid.Children.Add(pingBlock);

                var btn = new Button
                {
                    Style = rowStyle,
                    Tag = node,
                    Content = rowGrid
                };
                if (isSelectedManual)
                {
                    btn.Background = selectedRowBg;
                }
                btn.Click += NodeRowBtn_Click;
                NodesStackPanel.Children.Add(btn);
            }
        }

        private static (string Text, Brush Foreground) FormatLatencyBadge(
            PersistedNode node,
            Dictionary<string, int?> latencyMap,
            Brush untestedBrush,
            Brush successBrush,
            Brush cautionBrush,
            Brush criticalBrush)
        {
            if (CustomConfigService.IsCustomNode(node))
                return ("JSON", untestedBrush);

            if (!latencyMap.TryGetValue(node.Id, out var ms) || ms == null)
                return ("—", untestedBrush);

            if (ms < 0)
                return (L.Servers_PingTimeout, criticalBrush);

            Brush color = ms < 120 ? successBrush : (ms < 280 ? cautionBrush : criticalBrush);
            return ($"{ms} ms", color);
        }

        private void RefreshModeSegmentUI()
        {
            string mode = AppSession.Instance.RoutingMode ?? "smart";
            int targetIdx = string.Equals(mode, "global", StringComparison.OrdinalIgnoreCase)
                ? 1
                : (string.Equals(mode, "direct", StringComparison.OrdinalIgnoreCase) ? 2 : 0);

            _suppressModeSegmentEvent = true;
            ModeSegmented.SelectedIndex = targetIdx;
            _suppressModeSegmentEvent = false;
        }

        private async void MasterProxyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressToggleEvent) return;

            bool targetOn = MasterProxyToggle.IsOn;
            if (targetOn == CoreManager.Instance.IsRunning)
                return;

            if (!targetOn)
            {
                _isBusySwitching = true;
                RefreshHeaderAndBanners();
                try
                {
                    await CoreManager.Instance.StopAsync();
                }
                finally
                {
                    _isBusySwitching = false;
                    if (_isAttached) RefreshAllUI();
                }
                return;
            }

            var allNodes = NodesManager.Instance.GetNodesSnapshot();
            if (string.IsNullOrEmpty(NodesManager.Instance.SelectedNodeId) && allNodes.Count > 0)
            {
                NodesManager.Instance.SelectedNodeId = allNodes[0].Id;
                NodesManager.Instance.Save();
            }

            var activeNode = allNodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
            bool isCustom = CustomConfigService.IsCustomNode(activeNode);
            bool needsAdmin = (AppSession.Instance.EnableTunMode && !isCustom && !AdminHelper.IsAdministrator())
                           || CustomConfigService.RequiresTunAdmin(activeNode);

            if (needsAdmin)
            {
                _suppressToggleEvent = true;
                MasterProxyToggle.IsOn = false;
                _suppressToggleEvent = false;
                _onCloseFlyout();
                await _onRequestTunAdminStartAsync();
                return;
            }

            if (activeNode == null && allNodes.Count == 0)
            {
                _suppressToggleEvent = true;
                MasterProxyToggle.IsOn = false;
                _suppressToggleEvent = false;
                _onCloseFlyout();
                _onOpenServersPage();
                return;
            }

            _isBusySwitching = true;
            RefreshHeaderAndBanners();
            try
            {
                string configJson = await ConfigBuilder.BuildAsync(activeNode);
                bool ok = await CoreManager.Instance.StartAsync(configJson);
                if (!ok && _isAttached)
                {
                    _suppressToggleEvent = true;
                    MasterProxyToggle.IsOn = false;
                    _suppressToggleEvent = false;
                }
            }
            catch
            {
                if (_isAttached)
                {
                    _suppressToggleEvent = true;
                    MasterProxyToggle.IsOn = false;
                    _suppressToggleEvent = false;
                }
            }
            finally
            {
                _isBusySwitching = false;
                if (_isAttached) RefreshAllUI();
            }
        }

        private async void AutoSelectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusySwitching) return;
            _isBusySwitching = true;
            RefreshHeaderAndBanners();
            QuickPingBtnText.Text = L.TrayPanel_StatusSwitching;

            try
            {
                await CoreManager.Instance.EnableAutoNodeSelectAsync();
                if (_isAttached && CoreManager.Instance.IsRunning && _panelCts != null)
                {
                    await RefreshAutoResolvedNodeAsync(_panelCts.Token);
                }
            }
            finally
            {
                _isBusySwitching = false;
                if (_isAttached) RefreshAllUI();
            }
        }

        private async void NodeRowBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusySwitching) return;
            if (sender is not Button btn || btn.Tag is not PersistedNode node)
                return;

            if (CoreManager.Instance.IsRunning && CustomConfigService.RequiresTunAdmin(node))
            {
                NodesManager.Instance.SelectedNodeId = node.Id;
                NodesManager.Instance.Save();
                _onCloseFlyout();
                await _onRequestTunAdminStartAsync();
                return;
            }

            _isBusySwitching = true;
            _autoResolvedNodeId = null;
            RefreshHeaderAndBanners();
            QuickPingBtnText.Text = L.TrayPanel_StatusSwitching;

            try
            {
                await CoreManager.Instance.SwitchToNodeAsync(node);
            }
            finally
            {
                _isBusySwitching = false;
                if (_isAttached) RefreshAllUI();
            }
        }

        private async void QuickPingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPingingNodes || _displayedNodes.Count == 0) return;
            var serversVm = ((App)Application.Current).Services.GetService(typeof(ServersViewModel)) as ServersViewModel;
            if (serversVm == null) return;

            _isPingingNodes = true;
            QuickPingBtnText.Text = L.TrayPanel_Pinging;

            try
            {
                var targets = _displayedNodes
                    .Select(n => serversVm.AllServers.FirstOrDefault(s => s.Id == n.Id))
                    .Where(s => s != null && !s.IsCustom)
                    .Cast<ServerEntryItem>()
                    .ToList();

                var tasks = targets.Select(item => serversVm.TestSingleLatencyCommand.ExecuteAsync(item)).ToList();
                await Task.WhenAll(tasks);

                if (_isAttached && CoreManager.Instance.IsRunning && AppSession.Instance.IsAutoNodeSelect && _panelCts != null)
                {
                    await RefreshAutoResolvedNodeAsync(_panelCts.Token);
                }
            }
            catch { }
            finally
            {
                _isPingingNodes = false;
                if (_isAttached) RebuildNodeListUI();
            }
        }

        private async void ModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressModeSegmentEvent || !_isAttached) return;
            if (ModeSegmented.SelectedItem is not SegmentedItem segItem || segItem.Tag is not string mode)
                return;

            if (string.Equals(AppSession.Instance.RoutingMode, mode, StringComparison.OrdinalIgnoreCase))
                return;

            _isBusySwitching = CoreManager.Instance.IsRunning;
            RefreshHeaderAndBanners();

            try
            {
                await CoreManager.Instance.SetRoutingModeAsync(mode);
            }
            finally
            {
                _isBusySwitching = false;
                if (_isAttached) RefreshAllUI();
            }
        }

        private void RetakeProxyBtn_Click(object sender, RoutedEventArgs e)
        {
            CoreManager.Instance.RetakeSystemProxy();
            RefreshHeaderAndBanners();
        }

        private async void UpdateActionBtn_Click(object sender, RoutedEventArgs e)
        {
            _onCloseFlyout();
            if (AppUpdateService.HasPendingUpdate)
            {
                await AppUpdateService.ApplyPendingUpdateAndRestartAsync();
            }
            else
            {
                _onOpenSettingsAbout();
            }
        }

        private void AllNodesBtn_Click(object sender, RoutedEventArgs e)
        {
            _onCloseFlyout();
            _onOpenServersPage();
        }

        private static bool IsPseudoInfoNode(PersistedNode node)
        {
            if (CustomConfigService.IsCustomNode(node)) return false;
            if (!NodeLinkParser.TrySplitHostPort(node.Host, out var h, out var p) || string.IsNullOrWhiteSpace(h) || p <= 0)
                return true;

            string cleanH = h.Trim('[', ']');
            if (cleanH is "127.0.0.1" or "0.0.0.0" or "localhost" or "::1")
                return true;

            string name = node.Name ?? string.Empty;
            return name.Contains("剩余流量", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("套餐到期", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("到期时间", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("距离下次重置", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("官网", StringComparison.OrdinalIgnoreCase);
        }

        private Brush LookupBrush(string key, Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, ContentDialogExtensions.IsElementDark(this), fallback);

        private static string FormatSpeed(long bytesPerSec)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
            if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
            if (bytesPerSec < 1024L * 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):F2} MB/s";
            return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):F2} GB/s";
        }

        private static string FormatCompactBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0):F2} TB";
        }
    }
}
