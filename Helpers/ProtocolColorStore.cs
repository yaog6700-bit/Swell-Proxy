using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Runtime mutable store for protocol badge colors.
    /// Loaded from NodesConfig at startup; updated in real-time by the Personalize/Settings UI.
    /// </summary>
    public static class ProtocolColorStore
    {
        // Tailwind 400 defaults — readable on both light & dark surfaces
        public static Color Ss        { get; set; } = Color.FromArgb(255,  96, 165, 250);
        public static Color Vless     { get; set; } = Color.FromArgb(255,  52, 211, 153);
        public static Color Vmess     { get; set; } = Color.FromArgb(255, 167, 139, 250);
        public static Color Hysteria2 { get; set; } = Color.FromArgb(255, 251, 146,  60);
        public static Color Trojan    { get; set; } = Color.FromArgb(255, 248, 113, 113);
        public static Color Custom    { get; set; } = Color.FromArgb(255,  34, 211, 238);
        public static Color Fallback  { get; set; } = Color.FromArgb(255, 148, 163, 184);

        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);

        public static event EventHandler? ColorsChanged;

        public static Color GetColor(string protocol) => (protocol ?? string.Empty).ToLowerInvariant() switch
        {
            "ss" or "shadowsocks"       => Ss,
            "vless"                     => Vless,
            "vmess"                     => Vmess,
            "hysteria2" or "hysteria 2" or "hysteria" => Hysteria2,
            "trojan"                    => Trojan,
            "custom"                    => Custom,
            _                           => Fallback
        };

        public static Brush GetBrush(string? protocol)
        {
            string key = string.IsNullOrWhiteSpace(protocol) ? "__fallback__" : protocol.Trim();
            if (_brushCache.TryGetValue(key, out var cached))
                return cached;

            var color = GetColor(key);
            var brush = new SolidColorBrush(color);
            _brushCache[key] = brush;
            return brush;
        }

        public static void NotifyColorsChanged()
        {
            _brushCache.Clear();
            ColorsChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void LoadFrom(NodesConfig s)
        {
            if (s.ColorSs        is not null) Ss        = ParseHex(s.ColorSs,        Ss);
            if (s.ColorVless     is not null) Vless     = ParseHex(s.ColorVless,     Vless);
            if (s.ColorVmess     is not null) Vmess     = ParseHex(s.ColorVmess,     Vmess);
            if (s.ColorHysteria2 is not null) Hysteria2 = ParseHex(s.ColorHysteria2, Hysteria2);
            if (s.ColorTrojan    is not null) Trojan    = ParseHex(s.ColorTrojan,    Trojan);
            if (s.ColorFallback  is not null) Fallback  = ParseHex(s.ColorFallback,  Fallback);
            _brushCache.Clear();
        }

        public static void SaveTo(NodesConfig s)
        {
            s.ColorSs        = ToHex(Ss);
            s.ColorVless     = ToHex(Vless);
            s.ColorVmess     = ToHex(Vmess);
            s.ColorHysteria2 = ToHex(Hysteria2);
            s.ColorTrojan    = ToHex(Trojan);
            s.ColorFallback  = ToHex(Fallback);
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], NumberStyles.HexNumber, null, out var b))
            {
                return Color.FromArgb(255, r, g, b);
            }
            return fallback;
        }

        private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
