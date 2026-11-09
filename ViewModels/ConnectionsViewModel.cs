using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;
using AnywhereWinUI.Services;
using Microsoft.UI.Dispatching;

namespace AnywhereWinUI.ViewModels
{
    public partial class ConnectionsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<ConnectionItem> connections = new();

        [ObservableProperty]
        private ObservableCollection<AppTrafficItem> appRankings = new();

        [ObservableProperty]
        private bool isCapturing = true;

        [ObservableProperty]
        private string captureButtonText = L.Conn_StopCapture;

        [ObservableProperty]
        private string captureButtonIcon = "\xE769"; // Pause icon

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedAppFilter = string.Empty;

        [ObservableProperty]
        private bool hasAppFilter;

        [ObservableProperty]
        private string activeAppFilterLabel = string.Empty;

        [ObservableProperty]
        private string activeAppCountText = string.Empty;

        [ObservableProperty]
        private bool hasAppRankings;

        [ObservableProperty]
        private bool isQuickRuleToastOpen;

        [ObservableProperty]
        private string quickRuleToastMessage = string.Empty;

        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly Dictionary<string, ConnectionItem> _trackedById = new(256);
        private ClashConnectionsMessage? _lastSnapshot;
        private CancellationTokenSource? _toastCts;
        private bool _isActivePage = false;
        private const int MaxConnections = 200;
        private const int MaxAppRankings = 12;

        public ConnectionsViewModel()
        {
            // BUG-2 fix: 在构造时缓存 DispatcherQueue，避免在背景线程回调时 GetForCurrentThread() 返回 null
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;
            SingboxApiClient.Instance.ConnectionsUpdated += OnConnectionsUpdated;
        }

        partial void OnSearchTextChanged(string value)
        {
            if (_isActivePage && _lastSnapshot != null)
            {
                ApplySnapshot(_lastSnapshot, recomputeRates: false);
            }
        }

        partial void OnSelectedAppFilterChanged(string value)
        {
            HasAppFilter = !string.IsNullOrWhiteSpace(value);
            ActiveAppFilterLabel = HasAppFilter
                ? string.Format(L.Conn_AppFilterPillFmt, value)
                : string.Empty;

            foreach (var app in AppRankings)
            {
                app.IsSelectedFilter = HasAppFilter &&
                    string.Equals(app.AppName, value, StringComparison.OrdinalIgnoreCase);
            }

            if (_isActivePage && _lastSnapshot != null)
            {
                ApplySnapshot(_lastSnapshot, recomputeRates: false);
            }
        }

        public async Task OnNavigatedToAsync()
        {
            _isActivePage = true;
            if (IsCapturing)
            {
                await SingboxApiClient.Instance.StartAsync();
            }
        }

        public Task OnNavigatedFromAsync()
        {
            // Mark page inactive and release row models while away; next navigation re-populates immediately.
            _isActivePage = false;
            _lastSnapshot = null;
            _trackedById.Clear();
            Connections.Clear();
            AppRankings.Clear();
            HasAppRankings = false;
            LocalProcessResolver.ClearCache();
            return Task.CompletedTask;
        }

        private void OnConnectionsUpdated(object? sender, ClashConnectionsMessage message)
        {
            if (!_isActivePage || !IsCapturing || !MainWindow.IsVisibleToUser) return;

            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    _lastSnapshot = message;
                    ApplySnapshot(message, recomputeRates: true);
                }
                catch (Exception ex)
                {
                    // 捕获 ObservableCollection 操作异常，防止它变为 UI 线程 fatal crash。
                    System.Diagnostics.Debug.WriteLine($"[ConnectionsViewModel] OnConnectionsUpdated error: {ex}");
                    try { Connections = new ObservableCollection<ConnectionItem>(); } catch { }
                }
            });
        }

        private void ApplySnapshot(ClashConnectionsMessage message, bool recomputeRates)
        {
            var incomingList = message.Connections ?? new List<ClashConnectionNode>();
            var activeIds = new HashSet<string>(incomingList.Count);
            var allActiveItems = new List<ConnectionItem>(incomingList.Count);

            foreach (var incoming in incomingList)
            {
                if (string.IsNullOrEmpty(incoming.Id)) continue;
                activeIds.Add(incoming.Id);

                string resolvedProc = LocalProcessResolver.ResolveProcessName(
                    incoming.Metadata.Process,
                    incoming.Metadata.ProcessPath,
                    incoming.Metadata.Network,
                    incoming.Metadata.SourcePort);

                if (_trackedById.TryGetValue(incoming.Id, out var existing))
                {
                    if (recomputeRates)
                    {
                        existing.UpdateFrom(incoming, resolvedProc);
                    }
                    allActiveItems.Add(existing);
                }
                else
                {
                    string status = L.Conn_StatusActive;
                    string rawHost = string.IsNullOrEmpty(incoming.Metadata.Host)
                        ? incoming.Metadata.DestinationIP
                        : incoming.Metadata.Host;
                    string targetHost = string.IsNullOrEmpty(rawHost) ? "Unknown" : rawHost;
                    if (!string.IsNullOrEmpty(incoming.Metadata.DestinationPort))
                        targetHost += $":{incoming.Metadata.DestinationPort}";

                    string chainTag = incoming.Chains.FirstOrDefault() ?? "Direct";
                    string node = chainTag;

                    if (chainTag.Equals("proxy", StringComparison.OrdinalIgnoreCase) ||
                        chainTag.Equals("urltest", StringComparison.OrdinalIgnoreCase))
                    {
                        node = AppSession.Instance.SelectedNodeName;
                        if (string.IsNullOrEmpty(node)) node = "Proxy";
                    }
                    else if (!chainTag.Equals("direct", StringComparison.OrdinalIgnoreCase) &&
                             !chainTag.Equals("block", StringComparison.OrdinalIgnoreCase))
                    {
                        var resolvedNode = NodesManager.Instance.Nodes.FirstOrDefault(n => n.Id == chainTag);
                        if (resolvedNode != null)
                        {
                            node = resolvedNode.Name;
                        }
                    }

                    string netType = (incoming.Metadata.Network ?? string.Empty).ToUpperInvariant();
                    if (netType == "TCP")
                    {
                        if (incoming.Metadata.DestinationPort == "443") netType = "HTTPS";
                        else if (incoming.Metadata.DestinationPort == "80") netType = "HTTP";
                    }

                    string rule = string.IsNullOrEmpty(incoming.RulePayload)
                        ? incoming.Rule
                        : $"{incoming.Rule} ({incoming.RulePayload})";

                    var newItem = new ConnectionItem(
                        id: incoming.Id,
                        type: netType,
                        processName: resolvedProc,
                        host: targetHost,
                        rawHost: incoming.Metadata.Host ?? string.Empty,
                        rawIp: incoming.Metadata.DestinationIP ?? string.Empty,
                        status: status,
                        rule: rule,
                        node: node,
                        duration: "0s",
                        size: "0 B"
                    );
                    newItem.UpdateFrom(incoming, resolvedProc);
                    _trackedById[incoming.Id] = newItem;
                    allActiveItems.Add(newItem);
                }
            }

            // Prune closed connections from _trackedById
            if (_trackedById.Count > activeIds.Count)
            {
                var staleIds = new List<string>();
                foreach (var kv in _trackedById)
                {
                    if (!activeIds.Contains(kv.Key))
                        staleIds.Add(kv.Key);
                }
                foreach (var id in staleIds)
                    _trackedById.Remove(id);
            }

            // 1. Update App Traffic Rankings (inspired by bowel's appsNormal / appsExpanded)
            UpdateAppRankings(allActiveItems);

            // 2. Filter and sort Connections table
            string query = SearchText?.Trim() ?? string.Empty;
            string appFilter = SelectedAppFilter?.Trim() ?? string.Empty;

            var filtered = allActiveItems.Where(c =>
            {
                if (!string.IsNullOrEmpty(appFilter) &&
                    !string.Equals(c.ProcessName, appFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(query))
                    return true;

                // Support bowel-style prefix tokens (app:, host:, node:, rule:) as well as free-text search
                if (query.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = query.Substring(4).Trim();
                    return c.ProcessName.Contains(sub, StringComparison.OrdinalIgnoreCase);
                }
                if (query.StartsWith("host:", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = query.Substring(5).Trim();
                    return c.Host.Contains(sub, StringComparison.OrdinalIgnoreCase);
                }
                if (query.StartsWith("node:", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = query.Substring(5).Trim();
                    return c.Node.Contains(sub, StringComparison.OrdinalIgnoreCase);
                }
                if (query.StartsWith("rule:", StringComparison.OrdinalIgnoreCase))
                {
                    var sub = query.Substring(5).Trim();
                    return c.Rule.Contains(sub, StringComparison.OrdinalIgnoreCase);
                }

                return c.Host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       c.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       c.RawIp.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       c.Rule.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       c.Node.Contains(query, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(c => c.CurrentRateBytes)
            .ThenByDescending(c => c.TotalBytes)
            .Take(MaxConnections)
            .ToList();

            SyncObservableCollection(Connections, filtered, c => c.Id);
        }

        private void UpdateAppRankings(List<ConnectionItem> allActiveItems)
        {
            if (allActiveItems.Count == 0)
            {
                AppRankings.Clear();
                HasAppRankings = false;
                ActiveAppCountText = string.Format(L.Conn_AppCountFmt, 0);
                return;
            }

            var groups = allActiveItems
                .GroupBy(c => string.IsNullOrWhiteSpace(c.ProcessName) ? L.Conn_UnknownApp : c.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    long sumRate = 0;
                    long sumBytes = 0;
                    string topHost = string.Empty;
                    long topScore = -1;

                    foreach (var item in g)
                    {
                        sumRate += item.CurrentRateBytes;
                        sumBytes += item.TotalBytes;
                        long score = item.CurrentRateBytes * 10 + item.TotalBytes;
                        if (score > topScore)
                        {
                            topScore = score;
                            topHost = !string.IsNullOrEmpty(item.SiteDomain)
                                ? item.SiteDomain
                                : (!string.IsNullOrEmpty(item.RawHost) ? item.RawHost : item.RawIp);
                        }
                    }

                    return new
                    {
                        AppName = g.First().ProcessName,
                        Count = g.Count(),
                        SumRate = sumRate,
                        SumBytes = sumBytes,
                        TopHost = topHost
                    };
                })
                .OrderByDescending(x => x.SumRate)
                .ThenByDescending(x => x.SumBytes)
                .ThenByDescending(x => x.Count)
                .Take(MaxAppRankings)
                .ToList();

            ActiveAppCountText = string.Format(L.Conn_AppCountFmt, groups.Count);
            HasAppRankings = groups.Count > 0;

            long maxRate = groups.Max(x => x.SumRate);
            long maxBytes = Math.Max(1L, groups.Max(x => x.SumBytes));

            var existingMap = AppRankings.ToDictionary(a => a.AppName, StringComparer.OrdinalIgnoreCase);
            var targetList = new List<AppTrafficItem>(groups.Count);

            foreach (var g in groups)
            {
                if (!existingMap.TryGetValue(g.AppName, out var appItem))
                {
                    appItem = new AppTrafficItem { AppName = g.AppName };
                }

                appItem.PrimaryHost = string.IsNullOrWhiteSpace(g.TopHost) ? "—" : g.TopHost;
                appItem.ConnectionCount = g.Count;
                appItem.ConnectionCountText = string.Format(L.Conn_AppConnCountFmt, g.Count);
                appItem.TotalBytes = g.SumBytes;
                appItem.TotalBytesText = ConnectionItem.FormatBytes(g.SumBytes);
                appItem.CurrentRateBytes = g.SumRate;
                appItem.RateText = g.SumRate > 0 ? $"{ConnectionItem.FormatBytes(g.SumRate)}/s" : "0 B/s";
                appItem.SharePercentage = maxRate > 0
                    ? Math.Clamp((double)g.SumRate / maxRate * 100.0, 4.0, 100.0)
                    : Math.Clamp((double)g.SumBytes / maxBytes * 100.0, 6.0, 100.0);
                appItem.IsSelectedFilter = HasAppFilter &&
                    string.Equals(g.AppName, SelectedAppFilter, StringComparison.OrdinalIgnoreCase);
                appItem.CanAddProcessRule = !string.IsNullOrWhiteSpace(g.AppName) &&
                    !string.Equals(g.AppName, L.Conn_UnknownApp, StringComparison.OrdinalIgnoreCase);

                targetList.Add(appItem);
            }

            SyncObservableCollection(AppRankings, targetList, a => a.AppName);
        }

        private static void SyncObservableCollection<T>(
            ObservableCollection<T> collection,
            List<T> target,
            Func<T, string> keySelector) where T : class
        {
            if (target.Count == 0)
            {
                if (collection.Count > 0)
                    collection.Clear();
                return;
            }

            if (Math.Abs(collection.Count - target.Count) > 50)
            {
                collection.Clear();
                foreach (var item in target)
                    collection.Add(item);
                return;
            }

            var targetKeys = new HashSet<string>(target.Select(keySelector), StringComparer.OrdinalIgnoreCase);
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (!targetKeys.Contains(keySelector(collection[i])))
                {
                    collection.RemoveAt(i);
                }
            }

            for (int i = 0; i < target.Count; i++)
            {
                var desired = target[i];
                string desiredKey = keySelector(desired);

                int existingIdx = -1;
                for (int j = 0; j < collection.Count; j++)
                {
                    if (string.Equals(keySelector(collection[j]), desiredKey, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx >= 0)
                {
                    if (existingIdx != i)
                    {
                        collection.Move(existingIdx, i);
                    }
                }
                else
                {
                    collection.Insert(i, desired);
                }
            }
        }

        public void ToggleAppFilter(string? appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                SelectedAppFilter = string.Empty;
                return;
            }

            if (string.Equals(SelectedAppFilter, appName, StringComparison.OrdinalIgnoreCase))
            {
                SelectedAppFilter = string.Empty;
            }
            else
            {
                SelectedAppFilter = appName;
            }
        }

        [RelayCommand]
        private void ClearAppFilter()
        {
            SelectedAppFilter = string.Empty;
        }

        public async Task ApplyQuickRuleAsync(string type, string match, string displayName, string outboundTag)
        {
            try
            {
                string msg = await RoutingRulesService.UpsertQuickRuleAsync(type, match, displayName, outboundTag);
                ShowToast(msg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConnectionsViewModel] ApplyQuickRuleAsync error: {ex}");
            }
        }

        public async Task CloseSingleConnectionAsync(ConnectionItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            string id = item.Id;
            _trackedById.Remove(id);
            Connections.Remove(item);
            await SingboxApiClient.Instance.CloseConnectionAsync(id);
        }

        public void ShowToast(string message)
        {
            _toastCts?.Cancel();
            var cts = new CancellationTokenSource();
            _toastCts = cts;

            QuickRuleToastMessage = message;
            IsQuickRuleToastOpen = true;

            _ = DismissToastAfterDelayAsync(cts.Token);
        }

        private async Task DismissToastAfterDelayAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(4000, token);
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        IsQuickRuleToastOpen = false;
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        }

        [RelayCommand]
        private async Task ToggleCaptureAsync()
        {
            IsCapturing = !IsCapturing;
            if (IsCapturing)
            {
                CaptureButtonText = L.Conn_StopCapture;
                CaptureButtonIcon = "\xE769"; // Pause
                if (_isActivePage)
                    await SingboxApiClient.Instance.StartAsync();
            }
            else
            {
                CaptureButtonText = L.Conn_StartCapture;
                CaptureButtonIcon = "\xE768"; // Play
                await SingboxApiClient.Instance.StopAsync();
            }
        }

        [RelayCommand]
        private async Task ClearConnectionsAsync()
        {
            _trackedById.Clear();
            Connections.Clear();
            AppRankings.Clear();
            HasAppRankings = false;
            await SingboxApiClient.Instance.CloseAllConnectionsAsync();
        }
    }
}
