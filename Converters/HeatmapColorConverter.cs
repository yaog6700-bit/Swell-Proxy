using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Converters
{
    /// <summary>
    /// 将热力图颜色等级 (0~4) 转换为对应的 SolidColorBrush。
    /// 自动感知应用内深色/浅色主题（优先应用设置，Default 时跟随系统），分别使用不同色盘。
    /// </summary>
    public sealed class HeatmapColorConverter : IValueConverter
    {
        // ── 深色模式色盘 ────────────────────────────────────────────────
        private static readonly SolidColorBrush Dark0 = new(Windows.UI.Color.FromArgb( 45, 255, 255, 255)); // 空格子（通透微白）
        private static readonly SolidColorBrush Dark1 = new(Windows.UI.Color.FromArgb(255,  14,  68, 111)); // 极浅蓝
        private static readonly SolidColorBrush Dark2 = new(Windows.UI.Color.FromArgb(255,  10, 109, 173)); // 中蓝
        private static readonly SolidColorBrush Dark3 = new(Windows.UI.Color.FromArgb(255,   0, 122, 204)); // 标准蓝
        private static readonly SolidColorBrush Dark4 = new(Windows.UI.Color.FromArgb(255,  56, 182, 255)); // 亮蓝（峰值）

        // ── 浅色模式色盘 ────────────────────────────────────────────────
        private static readonly SolidColorBrush Light0 = new(Windows.UI.Color.FromArgb( 22,   0,   0,   0)); // 空格子（柔和淡灰）
        private static readonly SolidColorBrush Light1 = new(Windows.UI.Color.FromArgb(255, 203, 226, 254)); // 极淡蓝
        private static readonly SolidColorBrush Light2 = new(Windows.UI.Color.FromArgb(255, 140, 184, 254)); // 浅蓝
        private static readonly SolidColorBrush Light3 = new(Windows.UI.Color.FromArgb(255,  75, 141, 248)); // 中蓝
        private static readonly SolidColorBrush Light4 = new(Windows.UI.Color.FromArgb(255,  43, 102, 224)); // 深蓝

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isDark = ContentDialogExtensions.IsDarkThemeActive();
            if (value is int level)
            {
                if (isDark)
                {
                    return level switch
                    {
                        1 => Dark1,
                        2 => Dark2,
                        3 => Dark3,
                        4 => Dark4,
                        _ => Dark0
                    };
                }

                return level switch
                {
                    1 => Light1,
                    2 => Light2,
                    3 => Light3,
                    4 => Light4,
                    _ => Light0
                };
            }
            return isDark ? Dark0 : Light0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
