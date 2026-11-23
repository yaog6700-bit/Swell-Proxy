using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.ViewModels
{
    public class RoutingViewModel
    {
        private List<RuleSetActionItem> _cachedActions = new();
        private Dictionary<string, RuleSetActionItem> _actionByTag = new(StringComparer.Ordinal);

        public IReadOnlyList<RuleSetActionItem> AvailableActions => _cachedActions;

        public RoutingViewModel()
        {
            RefreshActions();
        }

        public void RefreshActions()
        {
            var list = new List<RuleSetActionItem>(32)
            {
                new RuleSetActionItem { Glyph = "\uE724", Label = L.Routing_ActionProxy,  Tag = "proxy" },
                new RuleSetActionItem { Glyph = "\uE945", Label = L.Routing_ActionDirect, Tag = "direct" },
                new RuleSetActionItem { Glyph = "\uE733", Label = L.Routing_ActionBlock,  Tag = "block" },
                new RuleSetActionItem { Glyph = "\uE81C", Label = L.Routing_ActionAuto,   Tag = "urltest" }
            };

            var nodes = NodesManager.Instance.GetNodesSnapshot();
            if (nodes.Count > 0)
            {
                list.Add(new RuleSetActionItem { Tag = "separator" });
                foreach (var node in nodes)
                {
                    list.Add(new RuleSetActionItem
                    {
                        Glyph = string.Empty,
                        Label = node.Name,
                        Tag = $"node:{node.Id}"
                    });
                }
            }

            var map = new Dictionary<string, RuleSetActionItem>(list.Count, StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (item.Tag != "separator")
                    map[item.Tag] = item;
            }

            _cachedActions = list;
            _actionByTag = map;
        }

        public RuleSetActionItem ResolveItemForTag(string? tag)
        {
            if (!string.IsNullOrEmpty(tag) && _actionByTag.TryGetValue(tag, out var match))
                return match;

            if (!string.IsNullOrEmpty(tag) && tag.StartsWith("node:", StringComparison.Ordinal))
            {
                var id = tag.Substring(5);
                var node = NodesManager.Instance.GetNodesSnapshot().Find(n => n.Id == id);
                if (node != null)
                    return new RuleSetActionItem { Glyph = string.Empty, Label = node.Name, Tag = tag };
            }

            return _cachedActions.Count > 0 ? _cachedActions[0] : new RuleSetActionItem();
        }

        public async Task ApplyCustomRulesAsync()
        {
            await TriggerCoreRestartIfNeeded();
        }

        private async Task TriggerCoreRestartIfNeeded()
        {
            if (CoreManager.Instance.IsRunning)
            {
                CoreManager.Instance.AppendLog("[系统] 检测到路由分流规则更新，正在自动重启代理引擎以重新加载配置...");
                var selectedId = NodesManager.Instance.SelectedNodeId;
                var node = NodesManager.Instance.GetNodesSnapshot().Find(n => n.Id == selectedId);
                string realConfig = node != null ? await ConfigBuilder.BuildAsync(node) : await ConfigBuilder.BuildAsync();

                await CoreManager.Instance.StopAsync();
                await CoreManager.Instance.StartAsync(realConfig);
            }
        }
    }

    public class RuleSetActionItem
    {
        public string Glyph { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }
}
