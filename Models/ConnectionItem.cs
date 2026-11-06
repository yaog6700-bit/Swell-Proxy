using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using AnywhereWinUI.Helpers;

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
        private SolidColorBrush badgeBackground = _lightNeutralBg;

        [ObservableProperty]
        private SolidColorBrush badgeForeground = _lightNeutralFg;

        [ObservableProperty]
        private SolidColorBrush statusForeground = _lightNeutralFg;

        // Pre-allocated theme-aware semantic brushes (zero per-tick GC allocation)
        private static readonly SolidColorBrush _lightSuccessFg  = new(Windows.UI.Color.FromArgb(255,  15, 123,  15));
        private static readonly SolidColorBrush _lightSuccessBg  = new(Windows.UI.Color.FromArgb( 28,  15, 123,  15));
        private static readonly SolidColorBrush _darkSuccessFg   = new(Windows.UI.Color.FromArgb(255, 108, 203,  95));
        private static readonly SolidColorBrush _darkSuccessBg   = new(Windows.UI.Color.FromArgb( 38, 108, 203,  95));

        private static readonly SolidColorBrush _lightAccentFg   = new(Windows.UI.Color.FromArgb(255,   0,  95, 184));
        private static readonly SolidColorBrush _lightAccentBg   = new(Windows.UI.Color.FromArgb( 28,   0,  95, 184));
        private static readonly SolidColorBrush _darkAccentFg    = new(Windows.UI.Color.FromArgb(255,  96, 205, 255));
        private static readonly SolidColorBrush _darkAccentBg    = new(Windows.UI.Color.FromArgb( 38,  96, 205, 255));

        private static readonly SolidColorBrush _lightCautionFg  = new(Windows.UI.Color.FromArgb(255, 157,  93,   0));
        private static readonly SolidColorBrush _lightCautionBg  = new(Windows.UI.Color.FromArgb( 30, 157,  93,   0));
        private static readonly SolidColorBrush _darkCautionFg   = new(Windows.UI.Color.FromArgb(255, 252, 225,   0));
        private static readonly SolidColorBrush _darkCautionBg   = new(Windows.UI.Color.FromArgb( 36, 252, 225,   0));

        private static readonly SolidColorBrush _lightCriticalFg = new(Windows.UI.Color.FromArgb(255, 196,  43,  28));
        private static readonly SolidColorBrush _lightCriticalBg = new(Windows.UI.Color.FromArgb( 28, 196,  43,  28));
        private static readonly SolidColorBrush _darkCriticalFg  = new(Windows.UI.Color.FromArgb(255, 255, 153, 164));
        private static readonly SolidColorBrush _darkCriticalBg  = new(Windows.UI.Color.FromArgb( 38, 255, 153, 164));

        private static readonly SolidColorBrush _lightPurpleFg   = new(Windows.UI.Color.FromArgb(255, 109,  40, 217));
        private static readonly SolidColorBrush _lightPurpleBg   = new(Windows.UI.Color.FromArgb( 28, 109,  40, 217));
        private static readonly SolidColorBrush _darkPurpleFg    = new(Windows.UI.Color.FromArgb(255, 196, 181, 253));
        private static readonly SolidColorBrush _darkPurpleBg    = new(Windows.UI.Color.FromArgb( 38, 139,  92, 246));

        private static readonly SolidColorBrush _lightNeutralFg  = new(Windows.UI.Color.FromArgb(255,  96, 103, 112));
        private static readonly SolidColorBrush _lightNeutralBg  = new(Windows.UI.Color.FromArgb( 28,  96, 103, 112));
        private static readonly SolidColorBrush _darkNeutralFg   = new(Windows.UI.Color.FromArgb(255, 161, 168, 179));
        private static readonly SolidColorBrush _darkNeutralBg   = new(Windows.UI.Color.FromArgb( 38, 161, 168, 179));

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

            RefreshThemeColors();
        }

        public void RefreshThemeColors()
        {
            bool isDark = ContentDialogExtensions.IsDarkThemeActive();
            UpdateBadgeColors(Type ?? string.Empty, isDark);
            UpdateStatusColors(Status ?? string.Empty, isDark);
        }

        public void UpdateFrom(Services.ClashConnectionNode nodeData)
        {
            long totalBytes = nodeData.Download + nodeData.Upload;
            Size = FormatBytes(totalBytes);

            var durationSpan = DateTime.Now - nodeData.Start.ToLocalTime();
            Duration = $"{(int)durationSpan.TotalSeconds}s";
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

        private void UpdateBadgeColors(string type, bool isDark)
        {
            switch (type.ToUpperInvariant())
            {
                case "HTTP":
                case "GET":
                    BadgeForeground = isDark ? _darkSuccessFg : _lightSuccessFg;
                    BadgeBackground = isDark ? _darkSuccessBg : _lightSuccessBg;
                    break;
                case "HTTPS":
                case "POST":
                    BadgeForeground = isDark ? _darkAccentFg : _lightAccentFg;
                    BadgeBackground = isDark ? _darkAccentBg : _lightAccentBg;
                    break;
                case "TCP":
                case "PUT":
                    BadgeForeground = isDark ? _darkCautionFg : _lightCautionFg;
                    BadgeBackground = isDark ? _darkCautionBg : _lightCautionBg;
                    break;
                case "UDP":
                case "DELETE":
                    BadgeForeground = isDark ? _darkCriticalFg : _lightCriticalFg;
                    BadgeBackground = isDark ? _darkCriticalBg : _lightCriticalBg;
                    break;
                case "WS":
                case "WSS":
                    BadgeForeground = isDark ? _darkPurpleFg : _lightPurpleFg;
                    BadgeBackground = isDark ? _darkPurpleBg : _lightPurpleBg;
                    break;
                default:
                    BadgeForeground = isDark ? _darkNeutralFg : _lightNeutralFg;
                    BadgeBackground = isDark ? _darkNeutralBg : _lightNeutralBg;
                    break;
            }
        }

        private void UpdateStatusColors(string status, bool isDark)
        {
            if (status.Contains("进行中") || status.Contains("200") || status.Contains("201") || status.Contains("204"))
                StatusForeground = isDark ? _darkSuccessFg : _lightSuccessFg;
            else if (status.Contains("404") || status.Contains("Timeout"))
                StatusForeground = isDark ? _darkCautionFg : _lightCautionFg;
            else if (status.Contains("Error") || status.Contains("Reject") || status.Contains("500") || status.Contains("Block"))
                StatusForeground = isDark ? _darkCriticalFg : _lightCriticalFg;
            else
                StatusForeground = isDark ? _darkNeutralFg : _lightNeutralFg;
        }
    }
}
