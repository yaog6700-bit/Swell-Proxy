using System;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Models
{
    public enum LogBadgeKind
    {
        Default,
        Sys,
        SysExit,
        Info,
        Warn,
        Error,
        Debug
    }

    public partial class LogEntry : ObservableObject
    {
        public string Time { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO";

        [ObservableProperty]
        private Brush _levelTextBrush = _lightTextDefault;

        [ObservableProperty]
        private Brush _levelBgBrush = _lightBgDefault;

        public LogBadgeKind BadgeKind { get; private set; } = LogBadgeKind.Default;
        public string Message { get; set; } = string.Empty;
        public string Raw { get; set; } = string.Empty;
        public bool IsSystem { get; set; } = false;
        public Visibility TimeVisibility => string.IsNullOrEmpty(Time) ? Visibility.Collapsed : Visibility.Visible;

        private static readonly Regex AnsiRegex = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

        // ── Pre-allocated Light & Dark static brushes (zero per-line GC allocation) ─────────
        private static readonly SolidColorBrush _lightTextDefault = new(ColorHelper.FromArgb(255,  75,  85,  99)); // #4B5563
        private static readonly SolidColorBrush _lightBgDefault   = new(ColorHelper.FromArgb(255, 243, 244, 246)); // #F3F4F6
        private static readonly SolidColorBrush _lightTextSys     = new(ColorHelper.FromArgb(255,  85,  33, 181)); // #5521B5
        private static readonly SolidColorBrush _lightBgSys       = new(ColorHelper.FromArgb(255, 237, 235, 254)); // #EDEBFE
        private static readonly SolidColorBrush _lightTextWarn    = new(ColorHelper.FromArgb(255, 114,  59,  19)); // #723B13
        private static readonly SolidColorBrush _lightBgWarn      = new(ColorHelper.FromArgb(255, 253, 246, 178)); // #FDF6B2
        private static readonly SolidColorBrush _lightTextInfo    = new(ColorHelper.FromArgb(255,   5, 122,  85)); // #057A55
        private static readonly SolidColorBrush _lightBgInfo      = new(ColorHelper.FromArgb(255, 230, 246, 236)); // #E6F6EC
        private static readonly SolidColorBrush _lightTextError   = new(ColorHelper.FromArgb(255, 200,  30,  30)); // #C81E1E
        private static readonly SolidColorBrush _lightBgError     = new(ColorHelper.FromArgb(255, 253, 232, 232)); // #FDE8E8
        private static readonly SolidColorBrush _lightTextDebug   = new(ColorHelper.FromArgb(255,  30,  66, 159)); // #1E429F
        private static readonly SolidColorBrush _lightBgDebug     = new(ColorHelper.FromArgb(255, 225, 239, 254)); // #E1EFFE

        // Dark mode: low-alpha semantic tint background + readable Fluent foreground
        private static readonly SolidColorBrush _darkTextDefault  = new(ColorHelper.FromArgb(255, 180, 188, 200));
        private static readonly SolidColorBrush _darkBgDefault    = new(ColorHelper.FromArgb( 36, 156, 163, 175));
        private static readonly SolidColorBrush _darkTextSys      = new(ColorHelper.FromArgb(255, 196, 181, 253));
        private static readonly SolidColorBrush _darkBgSys        = new(ColorHelper.FromArgb( 42, 139,  92, 246));
        private static readonly SolidColorBrush _darkTextWarn     = new(ColorHelper.FromArgb(255, 252, 211,  77));
        private static readonly SolidColorBrush _darkBgWarn       = new(ColorHelper.FromArgb( 42, 245, 158,  11));
        private static readonly SolidColorBrush _darkTextInfo     = new(ColorHelper.FromArgb(255, 110, 231, 183));
        private static readonly SolidColorBrush _darkBgInfo       = new(ColorHelper.FromArgb( 40,  16, 185, 129));
        private static readonly SolidColorBrush _darkTextError    = new(ColorHelper.FromArgb(255, 252, 165, 165));
        private static readonly SolidColorBrush _darkBgError      = new(ColorHelper.FromArgb( 44, 239,  68,  68));
        private static readonly SolidColorBrush _darkTextDebug    = new(ColorHelper.FromArgb(255, 147, 197, 253));
        private static readonly SolidColorBrush _darkBgDebug      = new(ColorHelper.FromArgb( 42,  59, 130, 246));

        public void RefreshTheme() => ApplyKind(BadgeKind, ContentDialogExtensions.IsDarkThemeActive());

        private void ApplyKind(LogBadgeKind kind, bool isDark)
        {
            BadgeKind = kind;
            switch (kind)
            {
                case LogBadgeKind.Sys:
                    LevelTextBrush = isDark ? _darkTextSys : _lightTextSys;
                    LevelBgBrush   = isDark ? _darkBgSys   : _lightBgSys;
                    break;
                case LogBadgeKind.SysExit:
                case LogBadgeKind.Warn:
                    LevelTextBrush = isDark ? _darkTextWarn : _lightTextWarn;
                    LevelBgBrush   = isDark ? _darkBgWarn   : _lightBgWarn;
                    break;
                case LogBadgeKind.Info:
                    LevelTextBrush = isDark ? _darkTextInfo : _lightTextInfo;
                    LevelBgBrush   = isDark ? _darkBgInfo   : _lightBgInfo;
                    break;
                case LogBadgeKind.Error:
                    LevelTextBrush = isDark ? _darkTextError : _lightTextError;
                    LevelBgBrush   = isDark ? _darkBgError   : _lightBgError;
                    break;
                case LogBadgeKind.Debug:
                    LevelTextBrush = isDark ? _darkTextDebug : _lightTextDebug;
                    LevelBgBrush   = isDark ? _darkBgDebug   : _lightBgDebug;
                    break;
                default:
                    LevelTextBrush = isDark ? _darkTextDefault : _lightTextDefault;
                    LevelBgBrush   = isDark ? _darkBgDefault   : _lightBgDefault;
                    break;
            }
        }

        public static LogEntry Parse(string line)
        {
            bool isDark = ContentDialogExtensions.IsDarkThemeActive();
            if (string.IsNullOrWhiteSpace(line))
            {
                var empty = new LogEntry { Raw = line, Message = line };
                empty.ApplyKind(LogBadgeKind.Default, isDark);
                return empty;
            }

            var entry = new LogEntry { Raw = line };
            string clean = AnsiRegex.Replace(line, "");

            if (clean.StartsWith("[系统]") || clean.StartsWith("[启动]") || clean.StartsWith("[配置]") ||
                clean.StartsWith("[错误]") || clean.StartsWith("[警告]") || clean.StartsWith("[异常]") ||
                clean.StartsWith("[已停止]") || clean.StartsWith("[SystemProxy]") || clean.StartsWith("[TUN]") || clean.StartsWith("[Local]"))
            {
                entry.IsSystem = true;
                entry.Level = "SYS";
                entry.ApplyKind(LogBadgeKind.Sys, isDark);
                entry.Message = clean;
                return entry;
            }

            if (clean.StartsWith("[sing-box 进程已退出]"))
            {
                entry.IsSystem = true;
                entry.Level = "SYS";
                entry.ApplyKind(LogBadgeKind.SysExit, isDark);
                entry.Message = clean;
                return entry;
            }

            if (clean.StartsWith("[sing-box] "))
            {
                clean = clean.Substring(11);

                var match = Regex.Match(clean, @"^(?:([\+\-0-9]{5}\s\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\s+)?(INFO|WARN|ERROR|FATAL|DEBUG|PANIC)(.*)$");
                if (match.Success)
                {
                    entry.Time = match.Groups[1].Value.Trim();
                    entry.Level = match.Groups[2].Value;
                    entry.Message = match.Groups[3].Value.Trim();

                    if (!string.IsNullOrEmpty(entry.Time) && entry.Time.Length >= 8)
                    {
                        entry.Time = entry.Time.Substring(entry.Time.Length - 8);
                    }
                }
                else
                {
                    entry.Level = "MSG";
                    entry.Message = clean;
                }

                var kind = entry.Level switch
                {
                    "INFO" => LogBadgeKind.Info,
                    "WARN" => LogBadgeKind.Warn,
                    "ERROR" or "FATAL" or "PANIC" => LogBadgeKind.Error,
                    "DEBUG" => LogBadgeKind.Debug,
                    "SYS" => LogBadgeKind.Sys,
                    _ => LogBadgeKind.Default
                };
                entry.ApplyKind(kind, isDark);
            }
            else
            {
                entry.Level = "MSG";
                entry.ApplyKind(LogBadgeKind.Default, isDark);
                entry.Message = clean;
            }

            return entry;
        }
    }
}
