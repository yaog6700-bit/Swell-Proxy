using System;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AnywhereWinUI.Models
{
    public class LogEntry
    {
        public string Time { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO";
        public Brush LevelTextBrush { get; set; } = _brushTextDefault;
        public Brush LevelBgBrush { get; set; } = _brushBgDefault;
        public string Message { get; set; } = string.Empty;
        public string Raw { get; set; } = string.Empty;
        public bool IsSystem { get; set; } = false;
        public Visibility TimeVisibility => string.IsNullOrEmpty(Time) ? Visibility.Collapsed : Visibility.Visible;

        private static readonly Regex AnsiRegex = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

        // ── Pre-allocated static brushes ─────────────────────────────────────
        // LogEntry is created for every log line (up to 500 on every 500ms flush),
        // so allocating new brushes per-instance would generate thousands of GC objects.
        // Each pair (text + bg) corresponds to a log level.
        private static readonly SolidColorBrush _brushTextDefault  = new(ColorHelper.FromArgb(255,  75,  85,  99)); // #4B5563
        private static readonly SolidColorBrush _brushBgDefault    = new(ColorHelper.FromArgb(255, 243, 244, 246)); // #F3F4F6
        private static readonly SolidColorBrush _brushTextSys      = new(ColorHelper.FromArgb(255,  85,  33, 181)); // #5521B5
        private static readonly SolidColorBrush _brushBgSys        = new(ColorHelper.FromArgb(255, 237, 235, 254)); // #EDEBFE
        private static readonly SolidColorBrush _brushTextSysExit  = new(ColorHelper.FromArgb(255, 114,  59,  19)); // #723B13
        private static readonly SolidColorBrush _brushBgSysExit    = new(ColorHelper.FromArgb(255, 253, 246, 178)); // #FDF6B2
        private static readonly SolidColorBrush _brushTextInfo     = new(ColorHelper.FromArgb(255,   5, 122,  85)); // #057A55
        private static readonly SolidColorBrush _brushBgInfo       = new(ColorHelper.FromArgb(255, 230, 246, 236)); // #E6F6EC
        private static readonly SolidColorBrush _brushTextWarn     = new(ColorHelper.FromArgb(255, 114,  59,  19)); // #723B13
        private static readonly SolidColorBrush _brushBgWarn       = new(ColorHelper.FromArgb(255, 253, 246, 178)); // #FDF6B2
        private static readonly SolidColorBrush _brushTextError    = new(ColorHelper.FromArgb(255, 200,  30,  30)); // #C81E1E
        private static readonly SolidColorBrush _brushBgError      = new(ColorHelper.FromArgb(255, 253, 232, 232)); // #FDE8E8
        private static readonly SolidColorBrush _brushTextDebug    = new(ColorHelper.FromArgb(255,  30,  66, 159)); // #1E429F
        private static readonly SolidColorBrush _brushBgDebug      = new(ColorHelper.FromArgb(255, 225, 239, 254)); // #E1EFFE
        // ─────────────────────────────────────────────────────────────────────

        public static LogEntry Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return new LogEntry { Raw = line, Message = line };

            var entry = new LogEntry { Raw = line };
            string clean = AnsiRegex.Replace(line, "");

            if (clean.StartsWith("[系统]") || clean.StartsWith("[启动]") || clean.StartsWith("[配置]") || 
                clean.StartsWith("[错误]") || clean.StartsWith("[警告]") || clean.StartsWith("[异常]") ||
                clean.StartsWith("[已停止]") || clean.StartsWith("[SystemProxy]") || clean.StartsWith("[TUN]") || clean.StartsWith("[Local]"))
            {
                entry.IsSystem = true;
                entry.Level = "SYS";
                entry.LevelTextBrush = _brushTextSys;
                entry.LevelBgBrush = _brushBgSys;
                entry.Message = clean;
                return entry;
            }

            if (clean.StartsWith("[sing-box 进程已退出]"))
            {
                entry.IsSystem = true;
                entry.Level = "SYS";
                entry.LevelTextBrush = _brushTextSysExit;
                entry.LevelBgBrush = _brushBgSysExit;
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

                    // Extract HH:mm:ss
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

                switch (entry.Level)
                {
                    case "INFO": 
                        entry.LevelTextBrush = _brushTextInfo;
                        entry.LevelBgBrush   = _brushBgInfo;
                        break; 
                    case "WARN": 
                        entry.LevelTextBrush = _brushTextWarn;
                        entry.LevelBgBrush   = _brushBgWarn;
                        break; 
                    case "ERROR":
                    case "FATAL":
                    case "PANIC": 
                        entry.LevelTextBrush = _brushTextError;
                        entry.LevelBgBrush   = _brushBgError;
                        break; 
                    case "DEBUG": 
                        entry.LevelTextBrush = _brushTextDebug;
                        entry.LevelBgBrush   = _brushBgDebug;
                        break; 
                    case "SYS": 
                        entry.LevelTextBrush = _brushTextSys;
                        entry.LevelBgBrush   = _brushBgSys;
                        break; 
                    default: 
                        entry.LevelTextBrush = _brushTextDefault;
                        entry.LevelBgBrush   = _brushBgDefault;
                        break;
                }
            }
            else
            {
                entry.Level = "MSG";
                entry.LevelTextBrush = _brushTextDefault;
                entry.LevelBgBrush   = _brushBgDefault;
                entry.Message = clean;
            }

            return entry;
        }
    }
}
