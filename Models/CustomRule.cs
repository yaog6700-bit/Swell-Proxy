using System.Text.Json.Serialization;

namespace AnywhereWinUI.Models
{
    /// <summary>
    /// 自定义路由规则数据模型，兼容 sing-box 格式。
    /// </summary>
    public class CustomRule
    {
        /// <summary>"domain" | "ip" | "process"</summary>
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
            Type       = Type,
            Match      = Match,
            OutboundTag = OutboundTag,
            IsEnabled  = IsEnabled,
        };
    }
}
