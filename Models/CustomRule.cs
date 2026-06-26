using System.Text.Json.Serialization;

namespace AnywhereWinUI.Models
{
    /// <summary>
    /// 自定义路由规则数据模型，兼容 sing-box 格式。
    /// </summary>
    public class CustomRule
    {
        /// <summary>
        /// 稳定标识符（"custom:xxxxxxxx..."），从 RoutingRuleItem.Id 传入并持久化，
        /// 避免每次保存时重新生成新 GUID，保证规则身份稳定。
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public string Remark { get; set; } = string.Empty;

        /// <summary>"domain" | "ip" | "process" | "mixed"</summary>
        public string Type { get; set; } = "domain";

        /// <summary>
        /// 匹配内容，支持逗号分隔多个值。
        /// domain: youtube.com, regexp:.*\.cn, geosite:cn
        /// ip: 192.168.0.0/16, geoip:cn
        /// process: chrome.exe, C:\Games\xxx.exe
        /// </summary>
        public string Match { get; set; } = string.Empty;

        /// <summary>"proxy" | "direct" | "block"</summary>
        public string OutboundTag { get; set; } = "proxy";

        public bool IsEnabled { get; set; } = true;

        public CustomRule Clone() => new()
        {
            Id          = Id,
            Remark      = Remark,
            Type        = Type,
            Match       = Match,
            OutboundTag = OutboundTag,
            IsEnabled   = IsEnabled,
        };
    }
}
