using System;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Models
{
    public partial class AppTrafficItem : ObservableObject
    {
        [ObservableProperty]
        private string appName = string.Empty;

        [ObservableProperty]
        private string primaryHost = string.Empty;

        [ObservableProperty]
        private int connectionCount;

        [ObservableProperty]
        private string connectionCountText = string.Empty;

        [ObservableProperty]
        private long totalBytes;

        [ObservableProperty]
        private string totalBytesText = "0 B";

        [ObservableProperty]
        private long currentRateBytes;

        [ObservableProperty]
        private string rateText = "—";

        [ObservableProperty]
        private double sharePercentage;

        [ObservableProperty]
        private bool isSelectedFilter;

        [ObservableProperty]
        private bool canAddProcessRule;
    }

    public partial class ConnectionItem : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string type = string.Empty; // TCP, UDP, HTTP, HTTPS, WS

        [ObservableProperty]
        private string processName = string.Empty; // e.g. "chrome.exe"

        [ObservableProperty]
        private string host = string.Empty; // Target URL or IP:Port

        [ObservableProperty]
        private string rawHost = string.Empty; // Domain without port

        [ObservableProperty]
        private string siteDomain = string.Empty; // Base registrable domain, e.g. "openai.com"

        [ObservableProperty]
        private string rawIp = string.Empty; // Destination IP

        [ObservableProperty]
        private string status = string.Empty; // e.g. "进行中", "Active"

        [ObservableProperty]
        private string rule = string.Empty; // e.g. "Proxy", "Direct", "Reject"

        [ObservableProperty]
        private string node = string.Empty; // Node name, e.g. "🇺🇸 US-01"

        [ObservableProperty]
        private string duration = string.Empty; // e.g. "12s"

        [ObservableProperty]
        private string size = string.Empty; // e.g. "1.2 KB"

        [ObservableProperty]
        private string rateText = "—"; // e.g. "1.4 MB/s"

        public long TotalBytes { get; private set; }
        public long CurrentRateBytes { get; private set; }
        private long _lastPollBytes = -1;
        private long _lastPollTickMs;

        public bool HasProcess =>
            !string.IsNullOrWhiteSpace(ProcessName) &&
            !string.Equals(ProcessName, "—", StringComparison.Ordinal) &&
            !string.Equals(ProcessName, L.Conn_UnknownApp, StringComparison.OrdinalIgnoreCase);

        public bool HasDistinctSubdomain =>
            !string.IsNullOrWhiteSpace(RawHost) &&
            !string.IsNullOrWhiteSpace(SiteDomain) &&
            !string.Equals(RawHost, SiteDomain, StringComparison.OrdinalIgnoreCase);

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

        public ConnectionItem(
            string id,
            string type,
            string processName,
            string host,
            string rawHost,
            string rawIp,
            string status,
            string rule,
            string node,
            string duration,
            string size)
        {
            Id = id;
            Type = type;
            ProcessName = string.IsNullOrWhiteSpace(processName) ? L.Conn_UnknownApp : processName;
            Host = host;
            RawHost = rawHost;
            SiteDomain = ExtractSiteDomain(rawHost);
            RawIp = rawIp;
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

        public void UpdateFrom(Services.ClashConnectionNode nodeData, string? resolvedProcessName = null)
        {
            if (!string.IsNullOrWhiteSpace(resolvedProcessName) &&
                (string.IsNullOrWhiteSpace(ProcessName) || ProcessName == L.Conn_UnknownApp))
            {
                ProcessName = resolvedProcessName;
            }

            long totalBytes = nodeData.Download + nodeData.Upload;
            long nowTick = Environment.TickCount64;

            if (_lastPollBytes >= 0 && nowTick > _lastPollTickMs)
            {
                long deltaBytes = Math.Max(0, totalBytes - _lastPollBytes);
                double elapsedSec = Math.Max(0.25, (nowTick - _lastPollTickMs) / 1000.0);
                CurrentRateBytes = (long)(deltaBytes / elapsedSec);
            }
            else
            {
                CurrentRateBytes = 0;
            }

            _lastPollBytes = totalBytes;
            _lastPollTickMs = nowTick;
            TotalBytes = totalBytes;

            Size = FormatBytes(totalBytes);
            RateText = CurrentRateBytes > 0 ? $"{FormatBytes(CurrentRateBytes)}/s" : "—";

            var durationSpan = DateTime.Now - nodeData.Start.ToLocalTime();
            int totalSec = Math.Max(0, (int)durationSpan.TotalSeconds);
            Duration = FormatDuration(totalSec);
        }

        public static string FormatDuration(int totalSeconds)
        {
            if (totalSeconds < 60)
                return $"{totalSeconds}s";
            if (totalSeconds < 3600)
                return $"{totalSeconds / 60}m {totalSeconds % 60:D2}s";
            return $"{totalSeconds / 3600}h {(totalSeconds % 3600) / 60:D2}m";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
                if (counter >= suffixes.Length - 1) break;
            }
            return counter == 0 ? $"{(long)number} B" : $"{number:n1} {suffixes[counter]}";
        }

        public static string ExtractSiteDomain(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return string.Empty;

            string clean = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (IPAddress.TryParse(clean, out _))
                return string.Empty;

            var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2)
                return clean;

            string sld = parts[^2];
            string tld = parts[^1];
            if (tld.Length == 2 && parts.Length >= 3 &&
                (sld == "co" || sld == "com" || sld == "org" || sld == "net" || sld == "edu" || sld == "gov" || sld == "ac"))
            {
                return $"{parts[^3]}.{sld}.{tld}";
            }

            return $"{sld}.{tld}";
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
            if (status.Contains("进行中") || status.Contains("Active", StringComparison.OrdinalIgnoreCase) || status.Contains("200") || status.Contains("201") || status.Contains("204"))
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
