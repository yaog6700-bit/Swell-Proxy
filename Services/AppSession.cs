using System;
using System.Collections.Generic;
using System.Text.Json;
using AnywhereWinUI.Models;
namespace AnywhereWinUI.Services
{
    public sealed class AppSession
    {
        private static readonly Lazy<AppSession> _instance = new(() => new AppSession());
        public static AppSession Instance => _instance.Value;

        // Node Selection State
        public string SelectedNodeName { get; set; } = string.Empty;
        public string SelectedNodeProtocol { get; set; } = string.Empty;
        public string SelectedNodeHost { get; set; } = string.Empty;

        // Proxy Mode
        public int ProxyModeIndex { get; set; } = 0; // 0: System Proxy, 1: TUN, 2: Local Only
        public bool EnableTunMode => ProxyModeIndex == 1;
        public bool EnableSystemProxy => ProxyModeIndex == 0;
        public string? LastTunServerHost { get; set; }

        // Routing Rules State
        public bool BypassChina { get; set; } = true;
        public bool BlockAds { get; set; } = true;
        public bool BlockIPv6 { get; set; } = true;
        public bool FlushDNS { get; set; } = true;
        public string RoutingMode { get; set; } = "smart";
        public bool EnableAdvancedRouting { get; set; } = false;

        // UI Preferences
        public bool EnableClassicDashboard { get; set; } = false;
        
        // Privacy Mode
        public bool IsPrivacyModeActive { get; set; } = false;
        public string PrivacyPassword { get; set; } = string.Empty;

        // Application Rule Sets
        public string RuleNetflixAction { get; set; } = "proxy";
        public string RuleChatGPTAction { get; set; } = "proxy";
        public string RuleTelegramAction { get; set; } = "proxy";
        public string RuleGoogleAction { get; set; } = "proxy";
        public string RuleYouTubeAction { get; set; } = "proxy";
        public string RuleTikTokAction { get; set; } = "proxy";
        public string RuleClaudeAction { get; set; } = "proxy";

        // DNS Settings
        public string DirectDns { get; set; } = "223.5.5.5";
        public string ProxyDns { get; set; } = "https://1.1.1.1/dns-query";
        public string DnsStrategy { get; set; } = "prefer_ipv4";
        public bool EnableDnsCache { get; set; } = true;
        public bool EnableFakeDns { get; set; } = false;

        // Custom Routing Rules
        public List<CustomRule> CustomRules { get; set; } = new();

        private AppSession()
        {
            if (Helpers.LocalSettingsHelper.TryGetValue<int>("proxyModeIndex", out var pmi)) ProxyModeIndex = pmi;
            // Migrate legacy setting if exists and proxyModeIndex is missing
            else if (Helpers.LocalSettingsHelper.TryGetValue<bool>("enableTunMode", out var etm) && etm) ProxyModeIndex = 1;

            if (Helpers.LocalSettingsHelper.TryGetValue<string>("lastTunServerHost", out var ltsh) && !string.IsNullOrEmpty(ltsh)) LastTunServerHost = ltsh;

            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("bypassChina", out var bc)) BypassChina = bc;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("enableClassicDashboard", out var ecd)) EnableClassicDashboard = ecd;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("isPrivacyModeActive", out var ipm)) IsPrivacyModeActive = ipm;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("privacyPassword", out var pwd) && !string.IsNullOrEmpty(pwd)) PrivacyPassword = pwd;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("blockAds", out var ba)) BlockAds = ba;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("blockIPv6", out var bi)) BlockIPv6 = bi;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("flushDNS", out var fd)) FlushDNS = fd;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("enableAdvancedRouting", out var ear)) EnableAdvancedRouting = ear;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("routingMode", out var rm) && !string.IsNullOrEmpty(rm)) RoutingMode = rm;

            if (Helpers.LocalSettingsHelper.TryGetValue<string>("directDns", out var dd) && !string.IsNullOrEmpty(dd)) DirectDns = dd;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("proxyDns", out var pd) && !string.IsNullOrEmpty(pd)) ProxyDns = pd;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("dnsStrategy", out var ds) && !string.IsNullOrEmpty(ds)) DnsStrategy = ds;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("enableDnsCache", out var edc)) EnableDnsCache = edc;
            if (Helpers.LocalSettingsHelper.TryGetValue<bool>("enableFakeDns", out var efd)) EnableFakeDns = efd;

            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleNetflixAction", out var netflix) && !string.IsNullOrEmpty(netflix)) RuleNetflixAction = netflix;

            // Fallback for ChatGPT
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleChatGPTAction", out var chatgpt) && !string.IsNullOrEmpty(chatgpt))
                RuleChatGPTAction = chatgpt;
            else if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleOpenAIAction", out var openai) && !string.IsNullOrEmpty(openai))
                RuleChatGPTAction = openai;

            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleTelegramAction", out var telegram) && !string.IsNullOrEmpty(telegram)) RuleTelegramAction = telegram;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleGoogleAction", out var google) && !string.IsNullOrEmpty(google)) RuleGoogleAction = google;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleYouTubeAction", out var youtube) && !string.IsNullOrEmpty(youtube)) RuleYouTubeAction = youtube;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleTikTokAction", out var tiktok) && !string.IsNullOrEmpty(tiktok)) RuleTikTokAction = tiktok;
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("ruleClaudeAction", out var claude) && !string.IsNullOrEmpty(claude)) RuleClaudeAction = claude;

            // 加载自定义规则
            if (Helpers.LocalSettingsHelper.TryGetValue<string>("customRules", out var rulesJson) && !string.IsNullOrEmpty(rulesJson))
            {
                try
                {
                    var rules = JsonSerializer.Deserialize<List<CustomRule>>(rulesJson);
                    if (rules != null) CustomRules = rules;
                }
                catch { }
            }

            var manager = NodesManager.Instance;
            var node = manager.Nodes.Find(n => n.Id == manager.SelectedNodeId);
            if (node != null)
            {
                SelectedNodeName = node.Name;
                SelectedNodeProtocol = node.Protocol;
                SelectedNodeHost = node.Host;
            }
        }
    }
}
