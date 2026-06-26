using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AnywhereWinUI.Models;
using AnywhereWinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;

namespace AnywhereWinUI.ViewModels
{
    public partial class TopologyViewModel : ObservableObject
    {
        public ObservableCollection<TopologyNode> Nodes { get; } = new();
        public ObservableCollection<TopologyLink> Links { get; } = new();

        [ObservableProperty]
        private double _containerWidth = 800;
        
        private bool _isActive;
        public bool IsActive 
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    if (value && _lastConnections != null)
                    {
                        UpdateFromLiveConnections(_lastConnections);
                    }
                }
            }
        }
        
        private const double FixedHeight = 280;
        private const double PaddingY = 20;
        private const double PaddingLeft = 20;
        private const double NodeWidth = 6;
        private const double NodeGap = 12;

        private readonly DispatcherQueue _dispatcherQueue;
        private List<ClashConnectionNode> _lastConnections = new();
        private bool _isUpdating = false;

        // Pre-allocated static brushes — avoids creating new Brush objects on every
        // high-frequency connection update (which would accumulate rapidly in memory).
        private static readonly SolidColorBrush _sourceBrush   = new(ColorHelper.FromArgb(255, 99,  102, 241)); // Indigo-500
        private static readonly SolidColorBrush _middleBrush   = new(ColorHelper.FromArgb(255, 16,  185, 129)); // Emerald-500
        private static readonly SolidColorBrush _outboundBrush = new(ColorHelper.FromArgb(255, 245, 158,  11)); // Amber-500

        public TopologyViewModel()
        {
            // BUG fix: 与 ConnectionsViewModel 保持一致，构造时缓存 DispatcherQueue。
            // TopologyGraphControl 以属性初始化器直接 new 此 ViewModel，
            // 该时机可能早于 UI 线程完全就绪，GetForCurrentThread() 可能返回 null，
            // 导致后续 TryEnqueue 时抛出 NullReferenceException 闪退。
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;
            SingboxApiClient.Instance.ConnectionsUpdated += OnConnectionsUpdated;
        }

        public void Unsubscribe()
        {
            SingboxApiClient.Instance.ConnectionsUpdated -= OnConnectionsUpdated;
        }

        private void OnConnectionsUpdated(object sender, ClashConnectionsMessage e)
        {
            _lastConnections = e.Connections;
            if (IsActive && !_isUpdating)
            {
                _isUpdating = true;
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        UpdateFromLiveConnections(_lastConnections);
                    }
                    catch (Exception ex)
                    {
                        // 捕获所有异常（包括 XamlBindingHelper.ConvertValue 产生的 COMException），
                        // 防止未处理异常通过 UI 线程 fatal handler 导致闪退。
                        System.Diagnostics.Debug.WriteLine($"[TopologyViewModel] UpdateFromLiveConnections error: {ex}");
                        try { Nodes.Clear(); Links.Clear(); } catch { }
                    }
                    finally
                    {
                        _isUpdating = false;
                    }
                });
            }
        }

        public void UpdateWidth(double width)
        {
            if (width > 0 && Math.Abs(ContainerWidth - width) > 10)
            {
                ContainerWidth = width;
                if (IsActive && _lastConnections != null)
                {
                    UpdateFromLiveConnections(_lastConnections); // Recalculate layout
                }
            }
        }

        public void UpdateFromLiveConnections(List<ClashConnectionNode> connections)
        {
            if (ContainerWidth <= 0 || connections == null) return;

            // Step 1: Aggregate Data
            var middleNodesMap = new Dictionary<string, (long Value, Dictionary<string, long> Flows)>();
            var outboundsMap = new Dictionary<string, long>();
            long totalTraffic = 0;


            foreach (var conn in connections)

            {
                long traffic = conn.Upload + conn.Download;
                if (traffic < 1024) traffic = 1024; // Minimum weight for visibility

                // Host
                string host = !string.IsNullOrEmpty(conn.Metadata.Host) ? conn.Metadata.Host : conn.Metadata.DestinationIP;
                if (string.IsNullOrEmpty(host)) host = "Unknown";

                // Outbound
                string chainTag = conn.Chains != null && conn.Chains.Count > 0 ? conn.Chains[0] : conn.Rule;
                string outbound = chainTag;
                if (string.IsNullOrEmpty(outbound)) outbound = "Direct";
                
                if (chainTag.ToLower() == "proxy" || chainTag.ToLower() == "urltest")
                {
                    outbound = AppSession.Instance.SelectedNodeName;
                    if (string.IsNullOrEmpty(outbound)) outbound = "Proxy";
                }
                else if (chainTag.ToLower() != "direct" && chainTag.ToLower() != "block")
                {
                    var resolvedNode = NodesManager.Instance.Nodes.FirstOrDefault(n => n.Id == chainTag);
                    if (resolvedNode != null)
                    {
                        outbound = resolvedNode.Name;
                    }
                }

                totalTraffic += traffic;

                if (!middleNodesMap.ContainsKey(host))
                    middleNodesMap[host] = (0, new Dictionary<string, long>());
                
                var mid = middleNodesMap[host];
                mid.Value += traffic;
                if (!mid.Flows.ContainsKey(outbound)) mid.Flows[outbound] = 0;
                mid.Flows[outbound] += traffic;

                middleNodesMap[host] = mid;

                if (!outboundsMap.ContainsKey(outbound)) outboundsMap[outbound] = 0;
                outboundsMap[outbound] += traffic;
            }

            if (totalTraffic == 0)
            {
                Nodes.Clear();
                Links.Clear();
                return;
            }

            // Take top 10 hosts to avoid clutter, aggregate rest into '其他' (Others)
            var orderedHosts = middleNodesMap.OrderByDescending(x => x.Value.Value).ToList();
            var topHosts = orderedHosts.Take(10).ToList();
            var otherHosts = orderedHosts.Skip(10).ToList();

            var middleNodes = topHosts.Select(x => (Name: x.Key, Value: x.Value.Value, Flows: x.Value.Flows)).ToList();

            if (otherHosts.Any())
            {
                long otherValue = otherHosts.Sum(x => x.Value.Value);
                var otherFlows = new Dictionary<string, long>();
                foreach (var other in otherHosts)
                {
                    foreach (var flow in other.Value.Flows)
                    {
                        if (!otherFlows.ContainsKey(flow.Key)) otherFlows[flow.Key] = 0;
                        otherFlows[flow.Key] += flow.Value;
                    }
                }
                middleNodes.Add((Name: "其他", Value: otherValue, Flows: otherFlows));
            }

            // BUG fix: 出口节点数量不加限制时，outGapTotal 可超过 availableHeight，
            // 导致 maxContentHeight <= 0，scale <= 0，路径坐标出现 NaN/负值，
            // XamlBindingHelper.ConvertValue 解析非法路径时抛出 WinRT native 异常闪退。
            // 解决：出口节点最多保留 8 个（间距合计 84px < availableHeight 240px），其余聚合为"其他"。
            const int MaxOutbounds = 8;
            if (outboundsMap.Count > MaxOutbounds)
            {
                var topOutbounds = outboundsMap.OrderByDescending(o => o.Value).Take(MaxOutbounds).ToDictionary(o => o.Key, o => o.Value);
                long otherOut = outboundsMap.Where(o => !topOutbounds.ContainsKey(o.Key)).Sum(o => o.Value);
                if (otherOut > 0) topOutbounds["其他"] = otherOut;
                outboundsMap = topOutbounds;
                // Remap flows in middleNodes to merge overflowed outbounds into "其他"
                var validKeys = new System.Collections.Generic.HashSet<string>(topOutbounds.Keys);
                for (int mi = 0; mi < middleNodes.Count; mi++)
                {
                    var m = middleNodes[mi];
                    var newFlows = new Dictionary<string, long>();
                    foreach (var kv in m.Flows)
                    {
                        string key = validKeys.Contains(kv.Key) ? kv.Key : "其他";
                        if (!newFlows.ContainsKey(key)) newFlows[key] = 0;
                        newFlows[key] += kv.Value;
                    }
                    middleNodes[mi] = (m.Name, m.Value, newFlows);
                }
            }

            // Adjust total traffic for calculation based on top 10
            long visualTotalTraffic = middleNodes.Sum(m => m.Value);

            // Keep existing nodes/links instead of Clear() to preserve hover state, just rebuild lists
            var newNodes = new List<TopologyNode>();
            var newLinks = new List<TopologyLink>();

            double availableHeight = FixedHeight - 2 * PaddingY;
            double maxScale = 50; // Max height scaling

            int midGapTotal = Math.Max(0, (middleNodes.Count - 1) * (int)NodeGap);
            int outGapTotal = Math.Max(0, (outboundsMap.Count - 1) * (int)NodeGap);

            double maxContentHeight = availableHeight - Math.Max(midGapTotal, outGapTotal);
            // BUG fix: maxContentHeight <= 0 时（极端情况），清空图表安全退出。
            // 不能使用 scale=1.0 作为备用值：visualTotalTraffic 可达数百万，
            // scale*traffic 产生超出屏幕数量级的坐标，触发 DirectX / WinRT native crash。
            if (maxContentHeight <= 0)
            {
                Nodes.Clear();
                Links.Clear();
                return;
            }
            double scale = Math.Min(maxContentHeight / visualTotalTraffic, maxScale);

            double shiftRight = 20;

            // 1. Source Node
            var sourceNode = new TopologyNode
            {
                Id = "source-local",
                Name = "本机设备",
                Type = "source",
                Value = visualTotalTraffic,
                X = PaddingLeft + shiftRight,
                Height = Math.Max(4, visualTotalTraffic * scale),
                Color = _sourceBrush
            };
            sourceNode.Y = (FixedHeight - sourceNode.Height) / 2;
            newNodes.Add(sourceNode);

            // 2. Middle Nodes
            double midGroupHeight = middleNodes.Sum(m => Math.Max(2, m.Value * scale)) + midGapTotal;
            double currentY = (FixedHeight - midGroupHeight) / 2;
            double midX = (ContainerWidth / 2) - 50 + shiftRight;

            var midNodeParams = new Dictionary<string, TopologyNode>();
            
            // Unified color for all middle nodes (Emerald 500)
            var middleNodeColor = _middleBrush;

            foreach (var m in middleNodes)
            {
                var h = Math.Max(2, m.Value * scale);
                var node = new TopologyNode
                {
                    Id = $"mid-{m.Name}",
                    Name = m.Name,
                    Type = "domain",
                    Value = m.Value,
                    X = midX,
                    Y = currentY,
                    Height = h,
                    Color = middleNodeColor
                };
                newNodes.Add(node);
                midNodeParams[m.Name] = node;
                currentY += h + NodeGap;
            }

            // 3. Outbound Nodes
            double outGroupHeight = outboundsMap.Sum(o => Math.Max(2, o.Value * scale)) + outGapTotal;
            currentY = (FixedHeight - outGroupHeight) / 2;
            double outboundX = ContainerWidth - 120 + shiftRight;

            var outNodeParams = new Dictionary<string, TopologyNode>();
            var outYCursorMap = new Dictionary<string, double>();

            foreach (var o in outboundsMap)
            {
                var h = Math.Max(2, o.Value * scale);
                var node = new TopologyNode
                {
                    Id = $"out-{o.Key}",
                    Name = o.Key,
                    Type = "outbound",
                    Value = o.Value,
                    X = outboundX,
                    Y = currentY,
                    Height = h,
                    Color = _outboundBrush
                };
                newNodes.Add(node);
                outNodeParams[o.Key] = node;
                outYCursorMap[o.Key] = currentY;
                currentY += h + NodeGap;
            }

            // Links: Source -> Middle
            double sourceCursor = sourceNode.Y;
            foreach (var m in middleNodes)
            {
                var midNode = midNodeParams[m.Name];
                var h = (m.Value / (double)visualTotalTraffic) * sourceNode.Height;

                var pathData = GetSankeyPathData(sourceNode.X + NodeWidth, sourceCursor, midNode.X, midNode.Y, h, midNode.Height);
                
                newLinks.Add(new TopologyLink
                {
                    SourceId = sourceNode.Id,
                    TargetId = midNode.Id,
                    Value = m.Value,
                    PathGeometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), pathData),
                    Color = CreateGradient(sourceNode.Color, midNode.Color)
                });
                sourceCursor += h;
            }

            // Links: Middle -> Outbound
            foreach (var m in middleNodes)
            {
                var midNode = midNodeParams[m.Name];
                double midCursor = midNode.Y;

                foreach (var flow in m.Flows)
                {
                    if (!outNodeParams.ContainsKey(flow.Key)) continue;
                    var outNode = outNodeParams[flow.Key];
                    var midH = (flow.Value / (double)m.Value) * midNode.Height;
                    var outH = (flow.Value / (double)outNode.Value) * outNode.Height;
                    var outCursor = outYCursorMap[flow.Key];

                    var pathData = GetSankeyPathData(midNode.X + NodeWidth, midCursor, outNode.X, outCursor, midH, outH);

                    newLinks.Add(new TopologyLink
                    {
                        SourceId = midNode.Id,
                        TargetId = outNode.Id,
                        Value = flow.Value,
                        PathGeometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), pathData),
                        Color = CreateGradient(midNode.Color, outNode.Color)
                    });

                    midCursor += midH;
                    outYCursorMap[flow.Key] = outCursor + outH;
                }
            }

            // Merge into ObservableCollections to prevent flickering
            UpdateCollection(Nodes, newNodes, n => n.Id, CopyProperties);
            UpdateCollection(Links, newLinks, l => $"{l.SourceId}-{l.TargetId}", CopyProperties);
        }

        private void UpdateCollection<T>(ObservableCollection<T> target, List<T> source, Func<T, string> keySelector, Action<T, T> copyProperties)
        {
            // Remove items not in source
            var sourceKeys = new HashSet<string>(source.Select(keySelector));
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!sourceKeys.Contains(keySelector(target[i])))
                {
                    target.RemoveAt(i);
                }
            }

            // Update existing or add new
            for (int i = 0; i < source.Count; i++)
            {
                var s = source[i];
                var sKey = keySelector(s);
                var existingItem = target.FirstOrDefault(t => keySelector(t) == sKey);
                
                if (existingItem != null)
                {
                    // Copy properties manually to avoid full replacement which triggers UI redraw
                    copyProperties(s, existingItem);
                    
                    // Reorder if necessary
                    int currentIndex = target.IndexOf(existingItem);
                    if (currentIndex != i)
                    {
                        target.Move(currentIndex, i);
                    }
                }
                else
                {
                    target.Insert(i, s);
                }
            }
        }

        private void CopyProperties(TopologyNode source, TopologyNode target)
        {
            if (target.Name != source.Name) target.Name = source.Name;
            if (target.Value != source.Value) target.Value = source.Value;
            if (Math.Abs(target.X - source.X) > 0.1) target.X = source.X;
            if (Math.Abs(target.Y - source.Y) > 0.1) target.Y = source.Y;
            if (Math.Abs(target.Height - source.Height) > 0.1) target.Height = source.Height;
            // Don't override color so it doesn't flash
        }

        private void CopyProperties(TopologyLink source, TopologyLink target)
        {
            if (target.Value != source.Value) target.Value = source.Value;
            target.PathGeometry = source.PathGeometry;
        }

        private LinearGradientBrush CreateGradient(SolidColorBrush start, SolidColorBrush end)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0)
            };
            if (start != null) brush.GradientStops.Add(new GradientStop { Color = start.Color, Offset = 0.0 });
            if (end != null) brush.GradientStops.Add(new GradientStop { Color = end.Color, Offset = 1.0 });
            return brush;
        }

        private string GetSankeyPathData(double x0, double y0, double x1, double y1, double h0, double h1)
        {
            double xi = (x0 + x1) / 2;
            return $"M {x0},{y0} C {xi},{y0} {xi},{y1} {x1},{y1} L {x1},{y1 + h1} C {xi},{y1 + h1} {xi},{y0 + h0} {x0},{y0 + h0} Z";
        }

        public void HighlightNode(string nodeId)
        {
            var forwardNodes = new HashSet<string> { nodeId };
            var backwardNodes = new HashSet<string> { nodeId };
            var expandedLinks = new HashSet<TopologyLink>();
            
            // Forward trace
            bool changed = true;
            while(changed)
            {
                changed = false;
                foreach(var link in Links)
                {
                    if (!expandedLinks.Contains(link) && forwardNodes.Contains(link.SourceId))
                    {
                        expandedLinks.Add(link);
                        if (forwardNodes.Add(link.TargetId)) changed = true;
                    }
                }
            }

            // Backward trace
            changed = true;
            while(changed)
            {
                changed = false;
                foreach(var link in Links)
                {
                    if (!expandedLinks.Contains(link) && backwardNodes.Contains(link.TargetId))
                    {
                        expandedLinks.Add(link);
                        if (backwardNodes.Add(link.SourceId)) changed = true;
                    }
                }
            }
            
            var relatedNodes = new HashSet<string>(forwardNodes);
            relatedNodes.UnionWith(backwardNodes);

            ApplyHighlight(relatedNodes, expandedLinks);
        }

        public void HighlightLink(string sourceId, string targetId)
        {
            var forwardNodes = new HashSet<string> { targetId };
            var backwardNodes = new HashSet<string> { sourceId };
            var expandedLinks = new HashSet<TopologyLink>();

            var initialLink = Links.FirstOrDefault(l => l.SourceId == sourceId && l.TargetId == targetId);
            if (initialLink != null)
            {
                expandedLinks.Add(initialLink);
            }

            // Forward trace
            bool changed = true;
            while(changed)
            {
                changed = false;
                foreach(var link in Links)
                {
                    if (!expandedLinks.Contains(link) && forwardNodes.Contains(link.SourceId))
                    {
                        expandedLinks.Add(link);
                        if (forwardNodes.Add(link.TargetId)) changed = true;
                    }
                }
            }

            // Backward trace
            changed = true;
            while(changed)
            {
                changed = false;
                foreach(var link in Links)
                {
                    if (!expandedLinks.Contains(link) && backwardNodes.Contains(link.TargetId))
                    {
                        expandedLinks.Add(link);
                        if (backwardNodes.Add(link.SourceId)) changed = true;
                    }
                }
            }
            
            var relatedNodes = new HashSet<string>(forwardNodes);
            relatedNodes.UnionWith(backwardNodes);

            ApplyHighlight(relatedNodes, expandedLinks);
        }

        public void ClearHighlight()
        {
            foreach (var node in Nodes) node.Opacity = 1.0;
            foreach (var link in Links) link.Opacity = 0.15;
        }

        private void ApplyHighlight(HashSet<string> highlightedNodes, HashSet<TopologyLink> highlightedLinks)
        {
            foreach (var node in Nodes)
            {
                node.Opacity = highlightedNodes.Contains(node.Id) ? 1.0 : 0.1;
            }
            foreach (var link in Links)
            {
                link.Opacity = highlightedLinks.Contains(link) ? 0.6 : 0.05;
            }
        }
    }
}
