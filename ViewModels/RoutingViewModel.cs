using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using AnywhereWinUI.Services;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.ViewModels
{
    public partial class RoutingViewModel : ObservableObject
    {
        public ObservableCollection<RuleSetActionItem> AvailableActions { get; } = new();

        [ObservableProperty]
        private string _ruleGoogleAction;

        [ObservableProperty]
        private string _ruleTelegramAction;

        [ObservableProperty]
        private string _ruleNetflixAction;

        [ObservableProperty]
        private string _ruleYouTubeAction;

        [ObservableProperty]
        private string _ruleTikTokAction;

        [ObservableProperty]
        private string _ruleChatGPTAction;

        [ObservableProperty]
        private string _ruleClaudeAction;

        public RoutingViewModel()
        {
            _ruleGoogleAction = AppSession.Instance.RuleGoogleAction;
            _ruleTelegramAction = AppSession.Instance.RuleTelegramAction;
            _ruleNetflixAction = AppSession.Instance.RuleNetflixAction;
            _ruleYouTubeAction = AppSession.Instance.RuleYouTubeAction;
            _ruleTikTokAction = AppSession.Instance.RuleTikTokAction;
            _ruleChatGPTAction = AppSession.Instance.RuleChatGPTAction;
            _ruleClaudeAction = AppSession.Instance.RuleClaudeAction;

            PopulateActions();
        }

        private void PopulateActions()
        {
            AvailableActions.Clear();
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE724", Label = "默认 (Proxy)", Tag = "proxy" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE945", Label = "直连 (Direct)", Tag = "direct" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE733", Label = "拦截 (Block)", Tag = "block" });
            AvailableActions.Add(new RuleSetActionItem { Glyph = "\uE81C", Label = "自动优选 (Auto)", Tag = "urltest" });

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

        public void UpdateRuleAction(string ruleName, string tag)
        {
            switch (ruleName)
            {
                case "RuleGoogle":
                    RuleGoogleAction = tag;
                    AppSession.Instance.RuleGoogleAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleGoogleAction", tag);
                    break;
                case "RuleTelegram":
                    RuleTelegramAction = tag;
                    AppSession.Instance.RuleTelegramAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleTelegramAction", tag);
                    break;
                case "RuleNetflix":
                    RuleNetflixAction = tag;
                    AppSession.Instance.RuleNetflixAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleNetflixAction", tag);
                    break;
                case "RuleYouTube":
                    RuleYouTubeAction = tag;
                    AppSession.Instance.RuleYouTubeAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleYouTubeAction", tag);
                    break;
                case "RuleTikTok":
                    RuleTikTokAction = tag;
                    AppSession.Instance.RuleTikTokAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleTikTokAction", tag);
                    break;
                case "RuleChatGPT":
                    RuleChatGPTAction = tag;
                    AppSession.Instance.RuleChatGPTAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleChatGPTAction", tag);
                    break;
                case "RuleClaude":
                    RuleClaudeAction = tag;
                    AppSession.Instance.RuleClaudeAction = tag;
                    Helpers.LocalSettingsHelper.SetValue("ruleClaudeAction", tag);
                    break;
            }

            _ = TriggerCoreRestartIfNeeded();
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
                string realConfig = node != null ? ConfigBuilder.Build(node) : ConfigBuilder.Build();

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
