using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.Services
{
    public static class RoutingRulesService
    {
        private const string StorageKey = "routingRulesV1";
        private const int BuiltInMatchVersion = 2;

        public static List<RoutingRuleItem> LoadRules()
        {
            var defaults = CreateDefaultRules();

            if (LocalSettingsHelper.TryGetValue<string>(StorageKey, out var json) &&
                !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRoutingRuleItem);
                    if (saved != null && saved.Count > 0)
                    {
                        return MergeWithDefaults(saved, defaults);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load routing rules: {ex}");
                    // Fall back to defaults plus legacy custom rules.
                }
            }

            foreach (var rule in AppSession.Instance.CustomRules)
            {
                defaults.Add(new RoutingRuleItem
                {
                    Id = $"custom:{Guid.NewGuid():N}",
                    Name = string.IsNullOrWhiteSpace(rule.Remark) ? "自定义规则" : rule.Remark,
                    Description = "用户自定义分流规则",
                    Type = rule.Type,
                    Match = rule.Match,
                    OutboundTag = rule.OutboundTag,
                    IsEnabled = rule.IsEnabled,
                    IsBuiltIn = false,
                    MatchVersion = 0
                });
            }

            return defaults;
        }

        public static void SaveRules(IEnumerable<RoutingRuleItem> rules)
        {
            var list = rules.Select(r => r.Clone()).ToList();
            var json = JsonSerializer.Serialize(list, AppJsonContext.Default.ListRoutingRuleItem);
            LocalSettingsHelper.SetValue(StorageKey, json);

            foreach (var rule in list.Where(r => r.IsBuiltIn))
            {
                switch (rule.Id)
                {
                    case "builtin:youtube":
                        AppSession.Instance.RuleYouTubeAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleYouTubeAction", rule.OutboundTag);
                        break;
                    case "builtin:google":
                        AppSession.Instance.RuleGoogleAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleGoogleAction", rule.OutboundTag);
                        break;
                    case "builtin:telegram":
                        AppSession.Instance.RuleTelegramAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleTelegramAction", rule.OutboundTag);
                        break;
                    case "builtin:netflix":
                        AppSession.Instance.RuleNetflixAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleNetflixAction", rule.OutboundTag);
                        break;
                    case "builtin:tiktok":
                        AppSession.Instance.RuleTikTokAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleTikTokAction", rule.OutboundTag);
                        break;
                    case "builtin:chatgpt":
                        AppSession.Instance.RuleChatGPTAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleChatGPTAction", rule.OutboundTag);
                        break;
                    case "builtin:claude":
                        AppSession.Instance.RuleClaudeAction = rule.OutboundTag;
                        LocalSettingsHelper.SetValue("ruleClaudeAction", rule.OutboundTag);
                        break;
                }
            }

            var customRules = list
                .Where(r => !r.IsBuiltIn)
                .Select(r => new CustomRule
                {
                    Remark = r.Name == "自定义规则" ? string.Empty : r.Name,
                    Type = r.Type,
                    Match = r.Match,
                    OutboundTag = r.OutboundTag,
                    IsEnabled = r.IsEnabled
                })
                .ToList();

            AppSession.Instance.CustomRules = customRules;
            var customJson = JsonSerializer.Serialize(customRules, AppJsonContext.Default.ListCustomRule);
            LocalSettingsHelper.SetValue("customRules", customJson);
        }

        public static List<RoutingRuleItem> CreateDefaultRules()
        {
            var session = AppSession.Instance;
            return new List<RoutingRuleItem>
            {
                new()
                {
                    Id = "builtin:youtube",
                    Name = "YouTube / 油管",
                    Description = "YouTube 视频与媒体流量",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/YouTube.png",
                    Type = "mixed",
                    Match = "domain_suffix:youtube.com,domain_suffix:youtu.be,domain_suffix:ytimg.com,domain_suffix:ggpht.com,domain_suffix:googlevideo.com",
                    OutboundTag = session.RuleYouTubeAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleYouTubeAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:google",
                    Name = "Google / 谷歌服务",
                    Description = "Google 搜索、API、Play 与静态资源",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/Google.png",
                    Type = "mixed",
                    Match = "domain_suffix:google.com,domain_suffix:googleapis.com,domain_suffix:gstatic.com",
                    OutboundTag = session.RuleGoogleAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleGoogleAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:telegram",
                    Name = "Telegram / 电报",
                    Description = "Telegram 域名与专用 IP 段",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/Telegram.png",
                    Type = "mixed",
                    Match = "domain_suffix:telegram.org,domain_suffix:t.me,domain_suffix:tdesktop.com,ip_cidr:91.108.4.0/22,ip_cidr:91.108.8.0/22,ip_cidr:91.108.12.0/22,ip_cidr:91.108.16.0/22,ip_cidr:91.108.56.0/22,ip_cidr:149.154.160.0/20",
                    OutboundTag = session.RuleTelegramAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleTelegramAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:netflix",
                    Name = "Netflix / 奈飞",
                    Description = "Netflix 流媒体域名",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/Netflix.png",
                    Type = "mixed",
                    Match = "domain_suffix:netflix.com,domain_suffix:netflix.net,domain_suffix:nflximg.net,domain_suffix:nflxext.com,domain_suffix:nflxso.net,domain_suffix:nflxvideo.net",
                    OutboundTag = session.RuleNetflixAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleNetflixAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:tiktok",
                    Name = "TikTok",
                    Description = "TikTok 与字节海外服务",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/TikTok.png",
                    Type = "mixed",
                    Match = "domain_suffix:tiktok.com,domain_suffix:tiktokv.com,domain_suffix:tiktokcdn.com,domain_suffix:byteoversea.com",
                    OutboundTag = session.RuleTikTokAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleTikTokAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:chatgpt",
                    Name = "ChatGPT / OpenAI",
                    Description = "OpenAI 与 ChatGPT 服务",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/ChatGPT.png",
                    Type = "mixed",
                    Match = "domain_suffix:openai.com,domain_suffix:chatgpt.com,domain_suffix:ai.com,domain_suffix:oaistatic.com,domain_suffix:oaiusercontent.com",
                    OutboundTag = session.RuleChatGPTAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleChatGPTAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                },
                new()
                {
                    Id = "builtin:claude",
                    Name = "Claude / Anthropic",
                    Description = "Claude 与 Anthropic 服务",
                    IconUrl = "ms-appx:///Assets/RoutingIconsLegacy/Claude.png",
                    Type = "mixed",
                    Match = "domain_suffix:anthropic.com,domain_suffix:claude.ai",
                    OutboundTag = session.RuleClaudeAction,
                    IsEnabled = !string.IsNullOrEmpty(session.RuleClaudeAction),
                    IsBuiltIn = true,
                    MatchVersion = BuiltInMatchVersion
                }
            };
        }

        private static List<RoutingRuleItem> MergeWithDefaults(
            List<RoutingRuleItem> saved,
            List<RoutingRuleItem> defaults)
        {
            var defaultMap = defaults.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            var merged = new List<RoutingRuleItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in saved)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = $"custom:{Guid.NewGuid():N}";

                if (defaultMap.TryGetValue(item.Id, out var defaultItem))
                {
                    item.Name = defaultItem.Name;
                    item.Description = defaultItem.Description;
                    item.IconUrl = defaultItem.IconUrl;
                    item.IsBuiltIn = true;

                    if (item.MatchVersion < defaultItem.MatchVersion &&
                        RequiresBuiltInMatchUpgrade(item.Id))
                    {
                        item.Type = defaultItem.Type;
                        item.Match = defaultItem.Match;
                    }

                    item.MatchVersion = defaultItem.MatchVersion;
                }

                merged.Add(item);
                seen.Add(item.Id);
            }

            foreach (var defaultItem in defaults)
            {
                if (!seen.Contains(defaultItem.Id))
                    merged.Add(defaultItem);
            }

            return merged;
        }

        private static bool RequiresBuiltInMatchUpgrade(string id)
            => id.Equals("builtin:youtube", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("builtin:google", StringComparison.OrdinalIgnoreCase);
    }
}
