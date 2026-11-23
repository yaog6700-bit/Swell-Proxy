using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using AnywhereWinUI.Views;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace AnywhereWinUI.ViewModels
{
    public sealed class ServerGroupFilterItem
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    public enum ServerSortMode
    {
        Default,
        Active,
        Protocol,
        Latency
    }

    public partial class ServersViewModel : ObservableObject
    {
        private const string AllFilterId = "__all__";
        private const string FavoritesFilterId = "__favorites__";
        private const string ManualFilterId = "__manual__";

        private readonly LatencyProbeService _latencyProbeService;
        private readonly DispatcherQueue _dispatcherQueue;
        private volatile bool _isBatchPinging;

        [ObservableProperty]
        private ObservableCollection<ServerEntryItem> _allServers = new();

        [ObservableProperty]
        private ObservableCollection<ServerEntryItem> _filteredServers = new();

        [ObservableProperty]
        private ObservableCollection<PersistedSubscription> _subscriptions = new();

        [ObservableProperty]
        private ObservableCollection<ServerGroupFilterItem> _groupFilters = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedGroupFilterId = AllFilterId;

        [ObservableProperty]
        private ServerSortMode _sortMode = ServerSortMode.Default;

        [ObservableProperty]
        private string _selectedNodeId = string.Empty;

        [ObservableProperty]
        private bool _isQrCardVisible = false;

        [ObservableProperty]
        private string _currentShareUrl = string.Empty;

        [ObservableProperty]
        private ServerEntryItem? _selectedServer;

        public ServersViewModel()
        {
            _latencyProbeService = new LatencyProbeService();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("SelectedGroupFilterId", out var savedId) && !string.IsNullOrEmpty(savedId))
            {
                _selectedGroupFilterId = savedId;
            }
            if (Helpers.LocalSettingsHelper.TryGetValue<int>("SelectedServerSortMode", out var savedSort) &&
                Enum.IsDefined(typeof(ServerSortMode), savedSort))
            {
                _sortMode = (ServerSortMode)savedSort;
            }

            // LoadSubscriptions() → RebuildGroupFilters() 第1次
            // LoadServersList()   → RebuildGroupFilters() + ApplyFilters() 第2次
            // 不需要在此处额外再调用，避免三次重复重建
            LoadSubscriptions();
            LoadServersList();

            ProtocolColorStore.ColorsChanged += (_, _) =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var s in AllServers)
                        s.NotifyProtocolColorChanged();
                });
            };

            CoreManager.Instance.ActiveNodeChanged += (_, _) =>
            {
                _dispatcherQueue?.TryEnqueue(SyncActiveNodeIndicators);
            };
        }

        public void SyncActiveNodeIndicators()
        {
            string currentSelectedId = NodesManager.Instance.SelectedNodeId;
            bool isRunning = CoreManager.Instance.IsRunning;
            bool isAuto = AppSession.Instance.IsAutoNodeSelect;
            SelectedNodeId = currentSelectedId;

            foreach (var s in AllServers)
            {
                s.ActiveIndicatorVisibility = (!isAuto && s.Id == currentSelectedId && isRunning)
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;
            }

            if (SortMode == ServerSortMode.Active)
            {
                ApplyFilters();
            }
        }

        public void LoadSubscriptions()
        {
            Subscriptions.Clear();
            foreach (var s in NodesManager.Instance.Subscriptions)
            {
                Subscriptions.Add(s);
            }

            RebuildGroupFilters();
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilters();
        }

        partial void OnSelectedGroupFilterIdChanged(string value)
        {
            Helpers.LocalSettingsHelper.SetValue("SelectedGroupFilterId", value ?? AllFilterId);
            ApplyFilters();
        }

        public string? ActiveGroupFilterDisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedGroupFilterId) || SelectedGroupFilterId == AllFilterId)
                    return null;
                return GroupFilters.FirstOrDefault(g => string.Equals(g.Id, SelectedGroupFilterId, StringComparison.Ordinal))?.Name;
            }
        }

        partial void OnSortModeChanged(ServerSortMode value)
        {
            Helpers.LocalSettingsHelper.SetValue("SelectedServerSortMode", (int)value);
            ApplyFilters();
        }

        public void LoadServersList()
        {
            var prevById = new System.Collections.Generic.Dictionary<string, ServerEntryItem>(AllServers.Count, StringComparer.Ordinal);
            foreach (var existing in AllServers)
            {
                if (!string.IsNullOrEmpty(existing.Id))
                    prevById[existing.Id] = existing;
            }

            AllServers.Clear();
            var nodes = NodesManager.Instance.Nodes;
            string currentSelectedId = NodesManager.Instance.SelectedNodeId;
            bool isRunning = CoreManager.Instance.IsRunning;
            bool isAuto = AppSession.Instance.IsAutoNodeSelect;

            foreach (var node in nodes)
            {
                bool isCustom = CustomConfigService.IsCustomNode(node);
                string hostPart;
                int portPart;

                if (isCustom)
                {
                    hostPart = string.IsNullOrWhiteSpace(node.Host) ? "sing-box JSON" : node.Host;
                    portPart = 0;
                }
                else
                {
                    if (!NodeLinkParser.TrySplitHostPort(node.Host, out hostPart, out portPart))
                    {
                        Debug.WriteLine($"[ServersViewModel] Skipping node with invalid host: {node.Name} ({node.Host})");
                        continue;
                    }

                    if (portPart == 0) portPart = 443;
                }

                string subId = node.SubscriptionId ?? string.Empty;
                string net = node.Network ?? string.Empty;
                var activeVis = (!isAuto && node.Id == currentSelectedId && isRunning)
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;

                if (prevById.TryGetValue(node.Id, out var prev) &&
                    string.Equals(prev.Name, node.Name, StringComparison.Ordinal) &&
                    string.Equals(prev.Host, hostPart, StringComparison.Ordinal) &&
                    prev.Port == portPart &&
                    string.Equals(prev.Protocol, node.Protocol, StringComparison.Ordinal) &&
                    string.Equals(prev.SubscriptionId, subId, StringComparison.Ordinal) &&
                    string.Equals(prev.Network, net, StringComparison.Ordinal))
                {
                    // Reuse unchanged item instance so tested LatencyMs/PingColor & ListView container survive!
                    prev.IsFavorite = node.IsFavorite;
                    prev.ActiveIndicatorVisibility = activeVis;
                    AllServers.Add(prev);
                    continue;
                }

                var item = new ServerEntryItem
                {
                    Id = node.Id,
                    Name = node.Name,
                    Host = hostPart,
                    Port = portPart,
                    Protocol = node.Protocol,
                    SubscriptionId = subId,
                    Network = net,
                    IsFavorite = node.IsFavorite,
                    PingText = isCustom ? L.CustomCfg_ListBadge : L.Servers_PingUntested,
                    ActiveIndicatorVisibility = activeVis
                };
                AllServers.Add(item);
            }
            // NOTE: RebuildGroupFilters() must be called AFTER AllServers is fully populated.
            // Calling it before (e.g. right after Clear()) means AllServers is empty, so
            // IsManualOrDetached checks always return false and "自建列表" is never added to
            // GroupFilters.  That causes the saved "__manual__" filter ID to be silently
            // reset to "__all__" every time the app restarts.
            RebuildGroupFilters();
            ApplyFilters();
        }

        public void ApplyFilters()
        {
            var query = AllServers.AsEnumerable();

            if (SelectedGroupFilterId == FavoritesFilterId)
            {
                query = query.Where(s => s.IsFavorite);
            }
            else if (SelectedGroupFilterId == ManualFilterId)
            {
                query = query.Where(s => IsManualOrDetached(s.SubscriptionId));
            }
            else if (!string.IsNullOrEmpty(SelectedGroupFilterId) && SelectedGroupFilterId != AllFilterId)
            {
                query = query.Where(s => s.SubscriptionId == SelectedGroupFilterId);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLower();
                query = query.Where(s => s.Name.ToLower().Contains(q) || s.Host.ToLower().Contains(q));
            }

            query = SortMode switch
            {
                ServerSortMode.Active => query.OrderBy(s => s.ActiveIndicatorVisibility == Microsoft.UI.Xaml.Visibility.Visible ? 0 : 1),
                ServerSortMode.Protocol => query.OrderBy(s => s.Protocol ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                ServerSortMode.Latency => query
                    .OrderBy(s => LatencySortBucket(s.LatencyMs))
                    .ThenBy(s => s.LatencyMs ?? int.MaxValue),
                _ => query
            };

            var desired = query.ToList();
            SyncFilteredServersInPlace(desired);
        }

        /// <summary>
        /// Incrementally synchronizes FilteredServers using Move/Insert/RemoveAt instead of Clear(),
        /// preserving ListView container virtualization and preventing SelectedItem drop / re-ping loops.
        /// </summary>
        private void SyncFilteredServersInPlace(System.Collections.Generic.List<ServerEntryItem> desired)
        {
            if (desired.Count == 0)
            {
                if (FilteredServers.Count > 0)
                    FilteredServers.Clear();
                return;
            }

            if (FilteredServers.Count == 0 || Math.Abs(FilteredServers.Count - desired.Count) > 120)
            {
                var previousSelectedId = SelectedServer?.Id;
                FilteredServers.Clear();
                foreach (var item in desired)
                    FilteredServers.Add(item);

                if (previousSelectedId != null)
                {
                    var restored = FilteredServers.FirstOrDefault(s => s.Id == previousSelectedId);
                    if (restored != null && SelectedServer != restored)
                        SelectedServer = restored;
                }
                return;
            }

            var desiredIds = new System.Collections.Generic.HashSet<string>(
                desired.Select(x => x.Id),
                StringComparer.Ordinal);
            var seenIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < FilteredServers.Count;)
            {
                string id = FilteredServers[i].Id;
                if (!desiredIds.Contains(id) || !seenIds.Add(id))
                {
                    FilteredServers.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            for (int i = 0; i < desired.Count; i++)
            {
                var targetItem = desired[i];
                if (i < FilteredServers.Count &&
                    string.Equals(FilteredServers[i].Id, targetItem.Id, StringComparison.Ordinal))
                {
                    if (!ReferenceEquals(FilteredServers[i], targetItem))
                    {
                        FilteredServers[i] = targetItem;
                    }
                    continue;
                }

                int existingIdx = -1;
                for (int j = i + 1; j < FilteredServers.Count; j++)
                {
                    if (string.Equals(FilteredServers[j].Id, targetItem.Id, StringComparison.Ordinal))
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx >= 0)
                {
                    FilteredServers.Move(existingIdx, i);
                    if (!ReferenceEquals(FilteredServers[i], targetItem))
                    {
                        FilteredServers[i] = targetItem;
                    }
                }
                else
                {
                    FilteredServers.Insert(i, targetItem);
                }
            }

            while (FilteredServers.Count > desired.Count)
            {
                FilteredServers.RemoveAt(FilteredServers.Count - 1);
            }
        }

        // 排序优先级：已测速(0) > 未测试(1) > 超时/失败(2)
        // 超时/失败的节点比从未测试的节点更差，排在最后
        private static int LatencySortBucket(int? latencyMs) => latencyMs switch
        {
            null => 1,  // 未测试 → 中间
            < 0  => 2,  // 超时/失败 → 最后
            _    => 0   // 有效延迟 → 最前
        };

        private void RebuildGroupFilters()
        {
            var selected = string.IsNullOrWhiteSpace(SelectedGroupFilterId) ? AllFilterId : SelectedGroupFilterId;

            GroupFilters.Clear();
            GroupFilters.Add(new ServerGroupFilterItem { Id = AllFilterId, Name = L.Servers_FilterAll });
            GroupFilters.Add(new ServerGroupFilterItem { Id = FavoritesFilterId, Name = L.Servers_FilterFavorites });

            if (AllServers.Any(s => IsManualOrDetached(s.SubscriptionId)))
            {
                GroupFilters.Add(new ServerGroupFilterItem { Id = ManualFilterId, Name = L.Servers_FilterManual });
            }

            foreach (var sub in Subscriptions)
            {
                if (string.IsNullOrEmpty(sub.Id)) continue;
                if (!AllServers.Any(s => s.SubscriptionId == sub.Id)) continue;

                GroupFilters.Add(new ServerGroupFilterItem
                {
                    Id = sub.Id,
                    Name = string.IsNullOrWhiteSpace(sub.Name) ? L.Servers_UnnamedSub : sub.Name
                });
            }

            // Only fall back to AllFilter when AllServers is fully loaded.
            // If AllServers is still empty (called from LoadSubscriptions() before
            // LoadServersList() has run), skip the reset so the user's saved filter
            // ID (如 __manual__) is not prematurely overwritten.
            if (AllServers.Count > 0 && !GroupFilters.Any(g => g.Id == selected))
            {
                selected = AllFilterId;
            }

            if (SelectedGroupFilterId != selected)
            {
                SelectedGroupFilterId = selected;
            }
        }

        private bool IsManualOrDetached(string? subscriptionId)
        {
            return string.IsNullOrEmpty(subscriptionId) ||
                   !Subscriptions.Any(sub => string.Equals(sub.Id, subscriptionId, StringComparison.Ordinal));
        }

        [RelayCommand]
        private async Task PingAllServersAsync()
        {
            var snapshot = FilteredServers.ToArray();
            _isBatchPinging = true;
            try
            {
                using var semaphore = new System.Threading.SemaphoreSlim(10);
                var tasks = snapshot.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await TestSingleLatencyAsync(item);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }
            finally
            {
                _isBatchPinging = false;
                if (SortMode == ServerSortMode.Latency)
                {
                    _dispatcherQueue?.TryEnqueue(ApplyFilters);
                }
            }
        }

        private static Brush ResolveSemanticPingBrush(string key, Windows.UI.Color fallback)
            => Helpers.ContentDialogExtensions.LookupThemeBrush(key, Helpers.ContentDialogExtensions.IsDarkThemeActive(), fallback);

        [RelayCommand]
        private async Task TestSingleLatencyAsync(ServerEntryItem item)
        {
            if (item == null || item.IsCustom) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                item.LatencyMs = null;
                item.PingText = L.Servers_PingTesting;
                item.PingColor = ResolveSemanticPingBrush("SystemFillColorCautionBrush", Colors.Orange);
            });

            int? trueUrlTestDelay = null;
            if (CoreManager.Instance.IsRunning &&
                string.Equals(AppSession.Instance.ActiveCoreEngine, ShoesConfigBuilder.EngineSingBox, StringComparison.OrdinalIgnoreCase) &&
                !AppSession.Instance.IsActiveNodeCustom &&
                AppSession.Instance.CanHotSwitchTo(item.Id))
            {
                trueUrlTestDelay = await SingboxApiClient.Instance.ProbeOutboundDelayAsync(item.Id, 3500);
            }

            LatencyProbeResult result;
            if (trueUrlTestDelay is > 0)
            {
                result = new LatencyProbeResult { Status = LatencyProbeStatus.Success, Milliseconds = trueUrlTestDelay.Value };
            }
            else
            {
                // Seamless fallback to direct TCP/ICMP entry-server probe (crucial for domestic IPv6 relays & non-gstatic exits)
                result = await _latencyProbeService.ProbeAsync(item.Protocol, item.Host, item.Port, TimeSpan.FromSeconds(3));
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Status != LatencyProbeStatus.Success)
                {
                    item.LatencyMs = -1;
                    item.PingText = result.Status == LatencyProbeStatus.Timeout ? L.Servers_PingTimeout : L.Servers_PingFailed;
                    item.PingColor = ResolveSemanticPingBrush("SystemFillColorCriticalBrush", Colors.Red);

                    if (SelectedServer == item)
                    {
                        OnPropertyChanged(nameof(SelectedServer));
                    }
                }
                else
                {
                    int delay = result.Milliseconds ?? 0;
                    item.LatencyMs = delay;
                    item.PingText = $"{delay} ms";

                    Brush colorBrush;
                    if (delay < 150) colorBrush = ResolveSemanticPingBrush("SystemFillColorSuccessBrush", Colors.Green);
                    else if (delay < 350) colorBrush = ResolveSemanticPingBrush("SystemFillColorCautionBrush", Colors.Orange);
                    else colorBrush = ResolveSemanticPingBrush("SystemFillColorCriticalBrush", Colors.Red);

                    item.PingColor = colorBrush;

                    if (SelectedServer == item)
                    {
                        OnPropertyChanged(nameof(SelectedServer));
                    }
                }

                if (!_isBatchPinging && SortMode == ServerSortMode.Latency)
                {
                    ApplyFilters();
                }
            });
        }

        [RelayCommand]
        private async Task ConnectNodeAsync(ServerEntryItem server)
        {
            if (server == null) return;

            string previousActiveId = NodesManager.Instance.SelectedNodeId;
            bool wasAuto = AppSession.Instance.IsAutoNodeSelect;
            bool isRunning = CoreManager.Instance.IsRunning;

            var node = NodesManager.Instance.Nodes.Find(n => n.Id == server.Id);
            if (node != null && isRunning && CustomConfigService.RequiresTunAdmin(node))
            {
                var xamlRoot = MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot != null)
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = L.Tun_AdminRequiredTitle,
                        Content = L.Tun_AdminRequiredMsg,
                        PrimaryButtonText = L.Tun_AdminConfirmBtn,
                        CloseButtonText = L.Dialog_Cancel,
                        XamlRoot = xamlRoot
                    };
                    var res = await dialog.WithAppTheme().ShowAsync();
                    if (res == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    {
                        NodesManager.Instance.SelectedNodeId = server.Id;
                        NodesManager.Instance.Save();
                        await CoreManager.Instance.StopAsync();
                        Helpers.AdminHelper.RestartAsAdmin("--auto-start-proxy");
                    }
                }
                return;
            }

            AppSession.Instance.IsAutoNodeSelect = false;
            Helpers.LocalSettingsHelper.SetValue("isAutoNodeSelect", false);

            // Set as active selected node in NodesManager
            NodesManager.Instance.SelectedNodeId = server.Id;
            SelectedNodeId = server.Id;
            NodesManager.Instance.Save();

            SyncActiveNodeIndicators();

            // Sync with AppSession active properties
            AppSession.Instance.SelectedNodeName = server.Name;
            AppSession.Instance.SelectedNodeProtocol = server.Protocol;
            AppSession.Instance.SelectedNodeHost = server.HostPortDisplay;

            if (isRunning && !wasAuto && previousActiveId == server.Id)
            {
                return;
            }

            if (node != null && isRunning)
            {
                try
                {
                    await CoreManager.Instance.SwitchToNodeAsync(node);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error switching node: {ex}");
                }
            }
        }

        [RelayCommand]
        private async Task DeleteServerAsync(ServerEntryItem server)
        {
            if (server == null) return;

            bool wasActiveNode = !AppSession.Instance.IsAutoNodeSelect &&
                                 string.Equals(NodesManager.Instance.SelectedNodeId, server.Id, StringComparison.Ordinal);
            bool hadSub = !string.IsNullOrEmpty(server.SubscriptionId);

            NodesManager.Instance.DeleteNode(server.Id);
            if (hadSub) LoadSubscriptions();
            LoadServersList();
            SelectedServer = null;

            if (CoreManager.Instance.IsRunning)
            {
                var remaining = NodesManager.Instance.Nodes;
                if (remaining.Count == 0)
                {
                    await CoreManager.Instance.StopAsync();
                }
                else if (wasActiveNode)
                {
                    var nextNode = remaining.Find(n => n.Id == NodesManager.Instance.SelectedNodeId) ?? remaining[0];
                    await CoreManager.Instance.SwitchToNodeAsync(nextNode);
                }
            }
            else if (wasActiveNode)
            {
                SyncActiveNodeIndicators();
            }
        }

        [RelayCommand]
        private void ToggleFavorite(ServerEntryItem server)
        {
            if (server != null)
            {
                server.IsFavorite = !server.IsFavorite;
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == server.Id);
                if (node != null)
                {
                    node.IsFavorite = server.IsFavorite;
                    NodesManager.Instance.Save();
                }

                if (SelectedGroupFilterId == FavoritesFilterId)
                {
                    ApplyFilters();
                }
            }
        }

        [RelayCommand]
        private void ReorderServers()
        {
            // Legacy path: reads from FilteredServers.
            // Prefer ApplyNodeOrder() which accepts an explicit order from sender.Items.
            var orderedIds = FilteredServers.Select(x => x.Id).ToList();
            ApplyNodeOrder(orderedIds);
        }

        /// <summary>
        /// Persists a new node ordering supplied by the view.
        /// The caller should extract IDs from <c>sender.Items</c> inside
        /// <c>DragItemsCompleted</c> so the order is authoritative regardless
        /// of when WinUI 3 updates the bound ObservableCollection.
        /// </summary>
        public void ApplyNodeOrder(System.Collections.Generic.IList<string> orderedIds)
        {
            var newNodesList = new System.Collections.Generic.List<PersistedNode>();

            // 1. Add nodes in the explicit new order (visible items only)
            foreach (var id in orderedIds)
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == id);
                if (node != null) newNodesList.Add(node);
            }

            // 2. Append any nodes that were filtered out (not visible in the list)
            foreach (var node in NodesManager.Instance.Nodes)
            {
                if (!newNodesList.Exists(n => n.Id == node.Id)) newNodesList.Add(node);
            }

            // 3. Commit to NodesManager and persist to disk
            NodesManager.Instance.Nodes.Clear();
            NodesManager.Instance.Nodes.AddRange(newNodesList);
            NodesManager.Instance.Save();

            // 4. Rebuild AllServers to reflect the new order in the UI
            var newAllServers = new System.Collections.Generic.List<ServerEntryItem>();
            foreach (var node in NodesManager.Instance.Nodes)
            {
                var item = AllServers.FirstOrDefault(s => s.Id == node.Id);
                if (item != null) newAllServers.Add(item);
            }
            AllServers.Clear();
            foreach (var item in newAllServers) AllServers.Add(item);

            // Reset sort mode to Default (custom manual order) so subsequent pings/filters preserve user's drag order!
            if (_sortMode != ServerSortMode.Default)
            {
                _sortMode = ServerSortMode.Default;
                Helpers.LocalSettingsHelper.SetValue("SelectedServerSortMode", (int)ServerSortMode.Default);
                OnPropertyChanged(nameof(SortMode));
            }
            ApplyFilters();
        }
        [RelayCommand]
        private async Task UpdateSubscriptionAsync(PersistedSubscription sub)
        {
            if (sub != null)
            {
                await NodesManager.Instance.UpdateSubscriptionAsync(sub.Id);
                LoadSubscriptions();
                LoadServersList();
            }
        }

        [RelayCommand]
        private async Task RestoreExcludedNodesAsync(PersistedSubscription sub)
        {
            if (sub != null)
            {
                await NodesManager.Instance.RestoreExcludedNodesAsync(sub.Id);
                LoadSubscriptions();
                LoadServersList();
            }
        }

        [RelayCommand]
        private void DeleteSubscription(PersistedSubscription sub)
        {
            if (sub != null)
            {
                NodesManager.Instance.DeleteSubscription(sub.Id);
                LoadSubscriptions();
                LoadServersList();
            }
        }

        public async Task<(int addedNodes, int addedSubs)> ImportNodesFromTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return (0, 0);

            var trimmedWhole = text.TrimStart('\uFEFF').TrimStart();
            bool startsWithHttpUrl =
                trimmedWhole.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmedWhole.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            // 若直接粘贴了整份 sing-box JSON 或 Clash YAML 配置文本，直接批量解析导入手动节点
            if (!startsWithHttpUrl &&
                (trimmedWhole.StartsWith("{", StringComparison.Ordinal) ||
                 trimmedWhole.StartsWith("[", StringComparison.Ordinal) ||
                 ClashSubscriptionParser.LooksLikeClashYaml(trimmedWhole)))
            {
                var docNodes = NodesManager.Instance.ParseSubscriptionContent(trimmedWhole, string.Empty);
                if (docNodes.Count > 0)
                {
                    NodesManager.Instance.Nodes.AddRange(docNodes);
                    NodesManager.Instance.Save();
                    LoadSubscriptions();
                    LoadServersList();
                    return (docNodes.Count, 0);
                }
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int addedNodesCount = 0;
            int addedSubsCount = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    // 作为订阅处理
                    string subName = $"{L.Servers_AutoSubPrefix}_{DateTime.Now:MMdd_HHmm}";
                    if (addedSubsCount > 0)
                    {
                        subName += $"_{addedSubsCount + 1}";
                    }
                    NodesManager.Instance.AddSubscription(subName, line);
                    
                    if (NodesManager.Instance.Subscriptions.Count > 0)
                    {
                        var lastSubId = NodesManager.Instance.Subscriptions.Last().Id;
                        string? err = await NodesManager.Instance.UpdateSubscriptionAsync(lastSubId);
                        if (err != null)
                        {
                            // 如果导入时立即刷新失败，就把它删了，避免污染列表
                            NodesManager.Instance.DeleteSubscription(lastSubId);
                        }
                        else
                        {
                            addedSubsCount++;
                        }
                    }
                }
                else
                {
                    // 尝试当作普通单节点链接解析
                    var parsedNode = NodesManager.ParseShareUrl(line);
                    if (parsedNode != null)
                    {
                        NodesManager.Instance.Nodes.Add(parsedNode);
                        addedNodesCount++;
                    }
                }
            }

            // 兜底：若用户直接粘贴了未解码的整段 Base64 订阅内容
            if (addedNodesCount == 0 && addedSubsCount == 0 && !startsWithHttpUrl)
            {
                var b64Nodes = NodesManager.Instance.ParseSubscriptionContent(trimmedWhole, string.Empty);
                if (b64Nodes.Count > 0)
                {
                    NodesManager.Instance.Nodes.AddRange(b64Nodes);
                    addedNodesCount = b64Nodes.Count;
                }
            }

            if (addedNodesCount > 0 || addedSubsCount > 0)
            {
                NodesManager.Instance.Save();
                LoadSubscriptions();
                LoadServersList();
            }

            return (addedNodesCount, addedSubsCount);
        }
        public async Task AddSubscriptionAsync(string name, string url)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

            NodesManager.Instance.AddSubscription(name, url);
            
            if (NodesManager.Instance.Subscriptions.Count > 0)
            {
                await NodesManager.Instance.UpdateSubscriptionAsync(NodesManager.Instance.Subscriptions.Last().Id);
            }
            
            LoadSubscriptions();
            LoadServersList();
        }
    }
}
