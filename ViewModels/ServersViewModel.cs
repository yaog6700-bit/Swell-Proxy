using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            // LoadSubscriptions() → RebuildGroupFilters() 第1次
            // LoadServersList()   → RebuildGroupFilters() + ApplyFilters() 第2次
            // 不需要在此处额外再调用，避免三次重复重建
            LoadSubscriptions();
            LoadServersList();
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
            ApplyFilters();
        }

        partial void OnSortModeChanged(ServerSortMode value)
        {
            ApplyFilters();
        }

        public void LoadServersList()
        {
            AllServers.Clear();
            var nodes = NodesManager.Instance.Nodes;
            string currentSelectedId = NodesManager.Instance.SelectedNodeId;
            bool isRunning = CoreManager.Instance.IsRunning;

            foreach (var node in nodes)
            {
                if (!NodeLinkParser.TrySplitHostPort(node.Host, out var hostPart, out var portPart))
                {
                    Debug.WriteLine($"[ServersViewModel] Skipping node with invalid host: {node.Name} ({node.Host})");
                    continue;
                }

                if (portPart == 0) portPart = 443;

                var item = new ServerEntryItem
                {
                    Id = node.Id,
                    Name = node.Name,
                    Host = hostPart,
                    Port = portPart,
                    Protocol = node.Protocol,
                    SubscriptionId = node.SubscriptionId ?? string.Empty,
                    Network = node.Network ?? string.Empty,
                    IsFavorite = node.IsFavorite,
                    PingText = "未测试",
                    ActiveIndicatorVisibility = (node.Id == currentSelectedId && isRunning) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed
                };
                AllServers.Add(item);
            }
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

            FilteredServers.Clear();
            foreach (var item in query)
            {
                FilteredServers.Add(item);
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
            GroupFilters.Add(new ServerGroupFilterItem { Id = AllFilterId, Name = "全部服务器" });
            GroupFilters.Add(new ServerGroupFilterItem { Id = FavoritesFilterId, Name = "收藏列表" });

            if (AllServers.Any(s => IsManualOrDetached(s.SubscriptionId)))
            {
                GroupFilters.Add(new ServerGroupFilterItem { Id = ManualFilterId, Name = "自建列表" });
            }

            foreach (var sub in Subscriptions)
            {
                if (string.IsNullOrEmpty(sub.Id)) continue;
                if (!AllServers.Any(s => s.SubscriptionId == sub.Id)) continue;

                GroupFilters.Add(new ServerGroupFilterItem
                {
                    Id = sub.Id,
                    Name = string.IsNullOrWhiteSpace(sub.Name) ? "未命名订阅" : sub.Name
                });
            }

            if (!GroupFilters.Any(g => g.Id == selected))
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
            // BUG-7 fix: 先快照，防止 ping 期间 ApplyFilters() 重建集合引发 "集合已被修改" 异常
            var snapshot = FilteredServers.ToArray();
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

        [RelayCommand]
        private async Task TestSingleLatencyAsync(ServerEntryItem item)
        {
            if (item == null) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                item.LatencyMs = null;
                item.PingText = "测试中...";
                item.PingColor = new SolidColorBrush(Colors.Orange);
            });

            var result = await _latencyProbeService.ProbeAsync(item.Protocol, item.Host, item.Port, TimeSpan.FromSeconds(3));

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Status != LatencyProbeStatus.Success)
                {
                    item.LatencyMs = -1;
                    item.PingText = result.Status == LatencyProbeStatus.Timeout ? "超时" : "失败";
                    item.PingColor = new SolidColorBrush(Colors.Red);

                    // Also update selected server property if this is the selected one, so UI bound to it can update
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
                    
                    SolidColorBrush colorBrush;
                    if (delay < 100) colorBrush = new SolidColorBrush(Colors.Green);
                    else if (delay < 300) colorBrush = new SolidColorBrush(Colors.Orange);
                    else colorBrush = new SolidColorBrush(Colors.Red);

                    item.PingColor = colorBrush;

                    if (SelectedServer == item)
                    {
                        OnPropertyChanged(nameof(SelectedServer));
                    }
                }

                if (SortMode == ServerSortMode.Latency)
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
            bool isRunning = CoreManager.Instance.IsRunning;

            // Set as active selected node in NodesManager
            NodesManager.Instance.SelectedNodeId = server.Id;
            SelectedNodeId = server.Id;
            NodesManager.Instance.Save();

            // Reload active indicators
            foreach (var s in AllServers)
            {
                s.ActiveIndicatorVisibility = (s.Id == server.Id && isRunning) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            }

            if (SortMode == ServerSortMode.Active)
            {
                ApplyFilters();
            }

            // Sync with AppSession active properties
            AppSession.Instance.SelectedNodeName = server.Name;
            AppSession.Instance.SelectedNodeProtocol = server.Protocol;
            AppSession.Instance.SelectedNodeHost = server.HostPortDisplay;

            // If the engine is already running and the selected node is exactly the previous active node,
            // we don't need to trigger a redundant restart.
            if (isRunning && previousActiveId == server.Id)
            {
                return;
            }

            // Trigger proxy config update if running and node has changed
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == server.Id);
            if (node != null && isRunning)
            {
                try
                {
                    string realConfig = await ConfigBuilder.BuildAsync(node);
                    await CoreManager.Instance.StartAsync(realConfig);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error starting core on node switch: {ex}");
                }
            }
        }

        [RelayCommand]
        private void DeleteServer(ServerEntryItem server)
        {
            if (server != null)
            {
                NodesManager.Instance.DeleteNode(server.Id);
                LoadServersList();
                SelectedServer = null; // Clear selection
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
                    string subName = $"订阅_{DateTime.Now:MMdd_HHmm}";
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
