using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        private bool isCapturing = true;

        [ObservableProperty]
        private string captureButtonText = "停止捕获";

        [ObservableProperty]
        private string captureButtonIcon = "\xE769"; // Pause icon

        [ObservableProperty]
        private string searchText = string.Empty;

        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isActivePage = false;
        private const int MaxConnections = 200;

        public ConnectionsViewModel()
        {
            // BUG-2 fix: 在构造时缓存 DispatcherQueue，避免在背景线程回调时 GetForCurrentThread() 返回 null
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;
            SingboxApiClient.Instance.ConnectionsUpdated += OnConnectionsUpdated;
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
            // BUG-6 fix: 不停止全局单例 WebSocket，仅标记页面已离开，让事件回调短路
            // （用户手动点击“停止捕获”时才真正停止 WebSocket）
            _isActivePage = false;
            return Task.CompletedTask;
        }

        private void OnConnectionsUpdated(object? sender, ClashConnectionsMessage message)
        {
            if (!_isActivePage || !IsCapturing) return;

            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                var filteredConnections = message.Connections.Where(c => 
                    string.IsNullOrEmpty(SearchText) ||
                    (c.Metadata.Host != null && c.Metadata.Host.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Metadata.DestinationIP != null && c.Metadata.DestinationIP.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Rule != null && c.Rule.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                var newConnections = new System.Collections.Generic.List<ConnectionItem>();

                // Build new list
                foreach (var incoming in filteredConnections)
                {
                    var existing = Connections.FirstOrDefault(c => c.Id == incoming.Id);
                    if (existing != null)
                    {
                        existing.UpdateFrom(incoming);
                        newConnections.Add(existing);
                    }
                    else
                    {
                        // Parse status and rule visually
                        string status = "进行中"; // Since it's in the list, it's active
                        string targetHost = string.IsNullOrEmpty(incoming.Metadata.Host) 
                                            ? incoming.Metadata.DestinationIP 
                                            : incoming.Metadata.Host;

                        if (string.IsNullOrEmpty(targetHost)) 
                            targetHost = "Unknown";
                        if (!string.IsNullOrEmpty(incoming.Metadata.DestinationPort))
                            targetHost += $":{incoming.Metadata.DestinationPort}";

                        // Resolve Node Name
                        string chainTag = incoming.Chains.FirstOrDefault() ?? "Direct";
                        string node = chainTag;
                        
                        if (chainTag.ToLower() == "proxy" || chainTag.ToLower() == "urltest")
                        {
                            node = AppSession.Instance.SelectedNodeName;
                            if (string.IsNullOrEmpty(node)) node = "Proxy";
                        }
                        else if (chainTag.ToLower() != "direct" && chainTag.ToLower() != "block")
                        {
                            var resolvedNode = NodesManager.Instance.Nodes.FirstOrDefault(n => n.Id == chainTag);
                            if (resolvedNode != null)
                            {
                                node = resolvedNode.Name;
                            }
                        }

                        // Map network to visual type
                        string netType = incoming.Metadata.Network.ToUpper(); // TCP, UDP
                        if (netType == "TCP")
                        {
                            if (incoming.Metadata.DestinationPort == "443") netType = "HTTPS";
                            else if (incoming.Metadata.DestinationPort == "80") netType = "HTTP";
                        }

                        string rule = incoming.Rule;

                        var newItem = new ConnectionItem(
                            id: incoming.Id,
                            type: netType,
                            host: targetHost,
                            status: status,
                            rule: rule,
                            node: node,
                            duration: "0s",
                            size: "0B"
                        );
                        newItem.UpdateFrom(incoming);

                        newConnections.Add(newItem);
                    }
                }

                // Cap max items
                if (newConnections.Count > MaxConnections)
                {
                    newConnections = newConnections.Take(MaxConnections).ToList();
                }

                // Sync to UI list safely
                bool isMassiveChange = Math.Abs(Connections.Count - newConnections.Count) > 50 || newConnections.Count == 0;
                
                if (isMassiveChange)
                {
                    Connections = new ObservableCollection<ConnectionItem>(newConnections);
                }
                else
                {
                    var sourceKeys = new System.Collections.Generic.HashSet<string>(newConnections.Select(c => c.Id));
                    
                    // Remove old items
                    for (int i = Connections.Count - 1; i >= 0; i--)
                    {
                        if (!sourceKeys.Contains(Connections[i].Id))
                        {
                            Connections.RemoveAt(i);
                        }
                    }

                    // Insert or move
                    for (int i = 0; i < newConnections.Count; i++)
                    {
                        var s = newConnections[i];
                        var existingItem = Connections.FirstOrDefault(c => c.Id == s.Id);
                        
                        if (existingItem != null)
                        {
                            int currentIndex = Connections.IndexOf(existingItem);
                            if (currentIndex != i)
                            {
                                Connections.Move(currentIndex, i);
                            }
                        }
                        else
                        {
                            Connections.Insert(i, s);
                        }
                    }
                }
                }
                catch (Exception ex)
                {
                    // 捕获 ObservableCollection 操作异常，防止它变为 UI 线程 fatal crash。
                    System.Diagnostics.Debug.WriteLine($"[ConnectionsViewModel] OnConnectionsUpdated error: {ex}");
                    try { Connections = new ObservableCollection<ConnectionItem>(); } catch { }
                }
            });
        }

        [RelayCommand]
        private async Task ToggleCaptureAsync()
        {
            IsCapturing = !IsCapturing;
            if (IsCapturing)
            {
                CaptureButtonText = "停止捕获";
                CaptureButtonIcon = "\xE769"; // Pause
                if (_isActivePage)
                    await SingboxApiClient.Instance.StartAsync();
            }
            else
            {
                CaptureButtonText = "开始捕获";
                CaptureButtonIcon = "\xE768"; // Play
                await SingboxApiClient.Instance.StopAsync();
            }
        }

        [RelayCommand]
        private void ClearConnections()
        {
            Connections.Clear();
        }
    }
}
