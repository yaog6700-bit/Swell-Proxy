using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace AnywhereWinUI.Models
{
    public partial class ConnectionItem : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string type; // TCP, UDP, HTTP, HTTPS, WS

        [ObservableProperty]
        private string host; // Target URL or IP

        [ObservableProperty]
        private string status; // e.g. "进行中", "200 OK", "Timeout"

        [ObservableProperty]
        private string rule; // e.g. "Proxy", "Direct", "Reject"

        [ObservableProperty]
        private string node; // Node name, e.g. "🇺🇸 US-01"

        [ObservableProperty]
        private string duration; // e.g. "120ms"

        [ObservableProperty]
        private string size; // e.g. "1.2KB"

        [ObservableProperty]
        private SolidColorBrush badgeBackground;

        [ObservableProperty]
        private SolidColorBrush badgeForeground;

        [ObservableProperty]
        private SolidColorBrush statusForeground;

        public ConnectionItem(string id, string type, string host, string status, string rule, string node, string duration, string size)
        {
            Id = id;
            Type = type;
            Host = host;
            Status = status;
            Rule = rule;
            Node = node;
            Duration = duration;
            Size = size;

            // Set badge colors based on type
            UpdateBadgeColors(type);

            // Set status colors based on status
            UpdateStatusColors(status);
        }

        public void UpdateFrom(Services.ClashConnectionNode nodeData)
        {
            long totalBytes = nodeData.Download + nodeData.Upload;
            Size = FormatBytes(totalBytes);
            
            var durationSpan = DateTime.Now - nodeData.Start.ToLocalTime();
            Duration = $"{(int)durationSpan.TotalSeconds}s";

            // If you want to update other fields you can do it here
            // Note: Since we use [ObservableProperty], setting these automatically fires PropertyChanged.
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
                if (counter >= suffixes.Length - 1) break;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private void UpdateBadgeColors(string type)
        {
            // BUG-11/DESIGN-8 fix: 使用语义颜色 token，跟随深浅主题自动变化
            // 底色使用半透明 accent 语义色；前景使用对应强调色
            string fgKey;
            switch (type.ToUpper())
            {
                case "HTTP":
                case "GET":
                    fgKey = "SystemFillColorSuccessBrush"; // 绿色
                    break;
                case "HTTPS":
                case "POST":
                    fgKey = "AccentTextFillColorPrimaryBrush"; // 蓝色 accent
                    break;
                case "TCP":
                case "PUT":
                    fgKey = "SystemFillColorCautionBrush"; // 琥珀色
                    break;
                case "UDP":
                case "DELETE":
                    fgKey = "SystemFillColorCriticalBrush"; // 红色
                    break;
                case "WS":
                case "WSS":
                    fgKey = "SystemFillColorAttentionBrush"; // 紫色/关注色
                    break;
                default:
                    fgKey = "SystemFillColorNeutralBrush"; // 灰色
                    break;
            }

            var resources = Application.Current?.Resources;
            SolidColorBrush fgBrush;
            if (resources != null && resources.TryGetValue(fgKey, out var res) && res is SolidColorBrush sb)
                fgBrush = sb;
            else
                fgBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175));

            // 徽章背景：前景色 + 低透明度
            BadgeBackground = new SolidColorBrush(Windows.UI.Color.FromArgb(30, fgBrush.Color.R, fgBrush.Color.G, fgBrush.Color.B));
            BadgeForeground = fgBrush;
        }

        private void UpdateStatusColors(string status)
        {
            // BUG-11/DESIGN-8 fix: 使用 Fluent 语义颜色，适配深浅主题
            var resources = Application.Current?.Resources;

            string key;
            if (status.Contains("进行中") || status.Contains("200") || status.Contains("201") || status.Contains("204"))
                key = "SystemFillColorSuccessBrush";
            else if (status.Contains("404") || status.Contains("Timeout"))
                key = "SystemFillColorCautionBrush";
            else if (status.Contains("Error") || status.Contains("Reject") || status.Contains("500") || status.Contains("Block"))
                key = "SystemFillColorCriticalBrush";
            else
                key = "SystemFillColorNeutralBrush";

            if (resources != null && resources.TryGetValue(key, out var res) && res is SolidColorBrush sb)
                StatusForeground = sb;
            else
                StatusForeground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 156, 163, 175));
        }
    }
}
