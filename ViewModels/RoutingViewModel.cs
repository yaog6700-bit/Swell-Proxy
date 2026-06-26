using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.ViewModels
{
    public class RoutingViewModel
    {
        public ObservableCollection<RuleSetActionItem> AvailableActions { get; } = new();

        public RoutingViewModel()
        {
            PopulateActions();
        }

        private void PopulateActions()
        {
            AvailableActions.Clear();
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE724", Label = "默认代理", Tag = "proxy" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE945", Label = "直连", Tag = "direct" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE733", Label = "拦截", Tag = "block" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE81C", Label = "自动优选", Tag = "urltest" });

            var nodes = NodesManager.Instance.Nodes;
            if (nodes.Count > 0)
            {
                // Separator could be handled in UI, but we'll add a dummy for now
                AvailableActions.Add(new RuleSetActionItem { Tag = "separator" });
                foreach (var node in nodes)
                {
                    AvailableActions.Add(new RuleSetActionItem
                    {
                        Glyph = "",
                        Label = node.Name,
                        Tag = $"node:{node.Id}"
                    });
                }
            }
        }

        public RuleSetActionItem ResolveItemForTag(string tag)
        {
            var match = AvailableActions.FirstOrDefault(i => i.Tag == tag);
            if (match != null) return match;

            if (tag != null && tag.StartsWith("node:"))
            {
                var id = tag.Substring(5);
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == id);
                if (node != null)
                    return new RuleSetActionItem { Glyph = "", Label = node.Name, Tag = tag };
            }

            return AvailableActions.FirstOrDefault() ?? new RuleSetActionItem();
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
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
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
