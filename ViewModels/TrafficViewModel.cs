using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using AnywhereWinUI.Services;
using Microsoft.UI.Xaml;

namespace AnywhereWinUI.ViewModels
{
    // 每日流量记录
    public class DailyTraffic
    {
        public string Date { get; set; } = string.Empty; // yyyy-MM-dd
        public long ProxyUpload { get; set; }
        public long ProxyDownload { get; set; }
        public long Total => ProxyUpload + ProxyDownload;
    }

    // 柱状图 UI 绑定
    public class TrafficChartViewModel
    {
        public string DayLabel { get; set; } = string.Empty;
        public double UploadHeight { get; set; }
        public double DownloadHeight { get; set; }
        public string UploadTooltip { get; set; } = string.Empty;
        public string DownloadTooltip { get; set; } = string.Empty;
    }

    // 热力图视图模式
    public enum HeatmapViewMode { Daily, Weekly, Cumulative }

    // 热力图格子 UI 绑定
    public class HeatmapCellViewModel
    {
        public string Date { get; set; } = string.Empty;       // yyyy-MM-dd
        public long TotalBytes { get; set; }                   // 该格子代表的流量（因模式而异）
        public long UploadBytes { get; set; }
        public long DownloadBytes { get; set; }
        public int ColorLevel { get; set; }                    // 0~4，颜色深浅档位
        public string Tooltip { get; set; } = string.Empty;   // 悬停提示文字
        public bool IsPlaceholder { get; set; }               // 月份对齐用空格子
    }

    public partial class TrafficViewModel : ObservableObject, IDisposable
    {
        private List<DailyTraffic> _dailyRecords = new();
        private string _currentDateStr = string.Empty;

        // 当前会话净增量（相对于代理启动时的网卡基准）
        private long _sessionUpload = 0;
        private long _sessionDownload = 0;

        // 上一次记录的网卡读数（用于计算每秒差值）
        private long _lastNicDown = -1;
        private long _lastNicUp = -1;

        // 历史基准（今日已持久化的流量，用于跨会话累加）
        private long _todayBaseUpload = 0;
        private long _todayBaseDownload = 0;

        // 缓存 UI 线程 DispatcherQueue，避免事件回调时 GetForCurrentThread() 返回 null
        private readonly DispatcherQueue _dispatcherQueue;

        private const string DailyTrafficKey = "daily_traffic_records";

        [ObservableProperty]
        private string _sessionUploadText = "0.0 B";

        [ObservableProperty]
        private string _sessionDownloadText = "0.0 B";

        [ObservableProperty]
        private string _sessionTotalText = "0.0 B";

        [ObservableProperty]
        private string _activeConnectionsText = "0.0 B/s   ↑ 0.0 B/s";

        [ObservableProperty]
        private string _monthUploadText = "0.0 B";

        [ObservableProperty]
        private string _monthDownloadText = "0.0 B";

        [ObservableProperty]
        private string _monthTotalText = "0.0 B";

        [ObservableProperty]
        private Brush _statusDotFill = new SolidColorBrush(Colors.Gray);

        [ObservableProperty]
        private string _statusText = "未激活";

        // ── 热力图统计属性 ──────────────────────────────────────────────
        [ObservableProperty]
        private string _totalTrafficText = "0.0 B";

        [ObservableProperty]
        private string _peakDayText = "—";

        [ObservableProperty]
        private string _currentStreakText = "0 天";

        [ObservableProperty]
        private string _maxStreakText = "0 天";

        public ObservableCollection<TrafficChartViewModel> ChartItems { get; } = new();
        public ObservableCollection<HeatmapCellViewModel> HeatmapCells { get; } = new();
        public ObservableCollection<string> MonthLabels { get; } = new();

        private HeatmapViewMode _heatmapViewMode = HeatmapViewMode.Daily;

        public TrafficViewModel()
        {
            // 在 UI 线程构造时缓存 DispatcherQueue；若此时已在 UI 线程，直接获取，否则尝试 MainWindow
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;

            LoadRecords();
            _currentDateStr = DateTime.Now.ToString("yyyy-MM-dd");

            CoreManager.Instance.TrafficUpdated += OnTrafficUpdated;
            CoreManager.Instance.RunningChanged += OnRunningChanged;

            ResetSessionState();
            UpdateStatusUI(CoreManager.Instance.IsRunning);
            UpdateMonthAndChartUI();
            UpdateHeatmapUI();
        }

        private void ResetSessionState()
        {
            _sessionUpload = 0;
            _sessionDownload = 0;
            _currentDateStr = DateTime.Now.ToString("yyyy-MM-dd");

            var todayRecord = _dailyRecords.FirstOrDefault(r => r.Date == _currentDateStr);
            _todayBaseUpload = todayRecord?.ProxyUpload ?? 0;
            _todayBaseDownload = todayRecord?.ProxyDownload ?? 0;

            _lastNicDown = -1;
            _lastNicUp = -1;
        }

        private void OnTrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            if (_lastNicDown == -1 || _lastNicUp == -1)
            {
                _lastNicDown = e.Down;
                _lastNicUp = e.Up;
                return; // 第一次仅初始化基准，无增量
            }

            long deltaDown = e.Down - _lastNicDown;
            long deltaUp   = e.Up - _lastNicUp;

            // 应对网卡断开重连导致的累计字节数清零问题
            if (deltaDown < 0) deltaDown = 0;
            if (deltaUp < 0) deltaUp = 0;

            _lastNicDown = e.Down;
            _lastNicUp = e.Up;

            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    _sessionUpload   += deltaUp;
                    _sessionDownload += deltaDown;

                    SessionUploadText   = FormatBytes(_sessionUpload);
                    SessionDownloadText = FormatBytes(_sessionDownload);
                    SessionTotalText    = FormatBytes(_sessionUpload + _sessionDownload);
                    ActiveConnectionsText = $"↓ {FormatBytes(e.DownSpeed)}/s   ↑ {FormatBytes(e.UpSpeed)}/s";

                    ProcessDailyTraffic(_sessionUpload, _sessionDownload);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TrafficViewModel OnTrafficUpdated error: {ex.Message}");
                }
            });
        }

        private void OnRunningChanged(object? sender, bool isRunning)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                UpdateStatusUI(isRunning);
                if (!isRunning)
                {
                    PersistToday(_sessionUpload, _sessionDownload);
                    ResetSessionState();
                    SessionUploadText     = "0.0 B";
                    SessionDownloadText   = "0.0 B";
                    SessionTotalText      = "0.0 B";
                    ActiveConnectionsText = "↓ 0.0 B/s   ↑ 0.0 B/s";
                }
            });
        }

        private void UpdateStatusUI(bool isRunning)
        {
            // DESIGN-9 fix: 使用 Fluent 语义颜色资源，跟随深浅主题自动变化
            Brush brush;
            string key = isRunning ? "SystemFillColorSuccessBrush" : "SystemFillColorNeutralBrush";
            if (Application.Current?.Resources?.TryGetValue(key, out var res) == true && res is Brush thBrush)
                brush = thBrush;
            else
                brush = new SolidColorBrush(isRunning ? Colors.Green : Colors.Gray);

            StatusDotFill = brush;
            StatusText    = isRunning ? "活跃" : "未激活";
        }

        private void ProcessDailyTraffic(long sessionUpload, long sessionDownload)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (today != _currentDateStr)
            {
                PersistToday(sessionUpload, sessionDownload);
                _currentDateStr = today;
                _todayBaseUpload = 0;
                _todayBaseDownload = 0;
            }

            PersistToday(sessionUpload, sessionDownload);
            UpdateMonthAndChartUI();
            UpdateHeatmapUI();
        }

        private void PersistToday(long sessionUpload, long sessionDownload)
        {
            long todayUp = _todayBaseUpload + sessionUpload;
            long todayDown = _todayBaseDownload + sessionDownload;

            var existingRecord = _dailyRecords.FirstOrDefault(r => r.Date == _currentDateStr);
            if (existingRecord != null)
            {
                existingRecord.ProxyUpload = todayUp;
                existingRecord.ProxyDownload = todayDown;
            }
            else
            {
                _dailyRecords.Add(new DailyTraffic
                {
                    Date = _currentDateStr,
                    ProxyUpload = todayUp,
                    ProxyDownload = todayDown
                });
            }

            if (_dailyRecords.Count > 400)
                _dailyRecords = _dailyRecords.OrderByDescending(r => r.Date).Take(400).ToList();

            SaveRecords();
        }

        private void UpdateMonthAndChartUI()
        {
            string monthPrefix = DateTime.Now.ToString("yyyy-MM");
            var monthRecords = _dailyRecords.Where(r => r.Date.StartsWith(monthPrefix)).ToList();

            long monthUp = monthRecords.Sum(r => r.ProxyUpload);
            long monthDown = monthRecords.Sum(r => r.ProxyDownload);

            MonthUploadText = FormatBytes(monthUp);
            MonthDownloadText = FormatBytes(monthDown);
            MonthTotalText = FormatBytes(monthUp + monthDown);

            if (!_dailyRecords.Any()) return;

            long maxVal = _dailyRecords.Max(r => Math.Max(r.ProxyUpload, r.ProxyDownload));
            if (maxVal == 0) maxVal = 1;

            ChartItems.Clear();
            foreach (var record in _dailyRecords.OrderBy(r => r.Date))
            {
                string dayLabel = record.Date.Length > 5
                    ? record.Date.Substring(record.Date.Length - 5)
                    : record.Date;

                if (DateTime.TryParseExact(record.Date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                {
                    dayLabel = dt.ToString("M/d");
                }

                ChartItems.Add(new TrafficChartViewModel
                {
                    DayLabel = dayLabel,
                    UploadHeight = Math.Max((double)record.ProxyUpload / maxVal * 130, 2),
                    DownloadHeight = Math.Max((double)record.ProxyDownload / maxVal * 130, 2),
                    UploadTooltip = $"上传: {FormatBytes(record.ProxyUpload)}",
                    DownloadTooltip = $"下载: {FormatBytes(record.ProxyDownload)}"
                });
            }
        }

        // ── 热力图逻辑 ────────────────────────────────────────────────────

        public void SetHeatmapMode(HeatmapViewMode mode)
        {
            _heatmapViewMode = mode;
            UpdateHeatmapUI();
        }

        private void UpdateHeatmapUI()
        {
            // 构建过去 365 天的日期序列（从最早到今天）
            var today = DateTime.Today;
            var startDay = today.AddDays(-364);

            // 从 startDay 所在周的周日（或周一）对齐
            // WinUI 显示：列 = 周，行 = 周几（0=周日 … 6=周六）
            int startDow = (int)startDay.DayOfWeek; // 0=Sun
            var gridStart = startDay.AddDays(-startDow); // 对齐到该周周日

            // 建立日期→记录快速查找
            var recordMap = _dailyRecords.ToDictionary(r => r.Date, r => r);

            // 按周计算周汇总（供 Weekly 模式使用）
            Dictionary<int, long> weekTotals = new();
            if (_heatmapViewMode == HeatmapViewMode.Weekly)
            {
                int weekIdx = 0;
                for (var d = gridStart; d <= today; d = d.AddDays(1))
                {
                    if (d.DayOfWeek == DayOfWeek.Sunday && d != gridStart) weekIdx++;
                    var key = d.ToString("yyyy-MM-dd");
                    if (recordMap.TryGetValue(key, out var rec))
                    {
                        weekTotals[weekIdx] = weekTotals.GetValueOrDefault(weekIdx) + rec.Total;
                    }
                }
            }

            // 累计模式：计算每天的累计值
            Dictionary<string, long> cumulativeMap = new();
            if (_heatmapViewMode == HeatmapViewMode.Cumulative)
            {
                long running = 0;
                for (var d = gridStart; d <= today; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd");
                    if (recordMap.TryGetValue(key, out var rec))
                        running += rec.Total;
                    cumulativeMap[key] = running;
                }
            }

            // 计算颜色最大值（用于分档）
            long maxVal = 1;
            if (_heatmapViewMode == HeatmapViewMode.Daily)
                maxVal = _dailyRecords.Count > 0 ? _dailyRecords.Max(r => r.Total) : 1;
            else if (_heatmapViewMode == HeatmapViewMode.Weekly)
                maxVal = weekTotals.Count > 0 ? weekTotals.Values.Max() : 1;
            else // Cumulative
                maxVal = cumulativeMap.Count > 0 ? cumulativeMap.Values.Max() : 1;
            if (maxVal == 0) maxVal = 1;

            // 生成格子列表
            var cells = new List<HeatmapCellViewModel>();
            var monthLabelList = new List<string>();
            string lastMonth = string.Empty;
            int weekIndex = 0;

            for (var d = gridStart; d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Sunday && d != gridStart) weekIndex++;

                // 月份标签（每列首格记录）
                if (d.DayOfWeek == DayOfWeek.Sunday)
                {
                    string monthStr = d.Month != today.Month || d.Year != today.Year
                        ? d.ToString("M月")
                        : string.Empty;
                    if (monthStr != lastMonth)
                    {
                        monthLabelList.Add(monthStr);
                        lastMonth = monthStr;
                    }
                    else
                    {
                        monthLabelList.Add(string.Empty);
                    }
                }

                bool isFuture = d > today;
                bool isBeforeData = d < startDay;

                var dateKey = d.ToString("yyyy-MM-dd");
                recordMap.TryGetValue(dateKey, out var dayRec);

                long cellValue = 0;
                if (!isFuture)
                {
                    if (_heatmapViewMode == HeatmapViewMode.Daily)
                        cellValue = dayRec?.Total ?? 0;
                    else if (_heatmapViewMode == HeatmapViewMode.Weekly)
                        cellValue = weekTotals.GetValueOrDefault(weekIndex);
                    else
                        cellValue = cumulativeMap.GetValueOrDefault(dateKey);
                }

                int level = 0;
                if (!isFuture && cellValue > 0)
                {
                    double ratio = (double)cellValue / maxVal;
                    level = ratio switch
                    {
                        > 0.75 => 4,
                        > 0.50 => 3,
                        > 0.25 => 2,
                        _      => 1
                    };
                }

                string tooltip;
                if (isFuture || isBeforeData)
                    tooltip = d.ToString("yyyy/M/d");
                else if (dayRec == null)
                    tooltip = $"{d:yyyy/M/d}  无流量";
                else
                    tooltip = $"{d:yyyy/M/d}\n↑ {FormatBytes(dayRec.ProxyUpload)}  ↓ {FormatBytes(dayRec.ProxyDownload)}";

                cells.Add(new HeatmapCellViewModel
                {
                    Date = dateKey,
                    TotalBytes = cellValue,
                    UploadBytes = dayRec?.ProxyUpload ?? 0,
                    DownloadBytes = dayRec?.ProxyDownload ?? 0,
                    ColorLevel = (isFuture || isBeforeData) ? 0 : level,
                    Tooltip = tooltip,
                    IsPlaceholder = isFuture
                });
            }

            // 更新统计卡
            long grandTotal = _dailyRecords.Sum(r => r.Total);
            TotalTrafficText = FormatBytes(grandTotal);

            var peakRec = _dailyRecords.OrderByDescending(r => r.Total).FirstOrDefault();
            if (peakRec != null)
            {
                if (DateTime.TryParseExact(peakRec.Date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var peakDate))
                    PeakDayText = $"{peakDate:M月d日}  {FormatBytes(peakRec.Total)}";
                else
                    PeakDayText = FormatBytes(peakRec.Total);
            }
            else
            {
                PeakDayText = "—";
            }

            // 计算连续天数（有流量记录的天）
            int currentStreak = 0, maxStreak = 0, streak = 0;
            for (var d = today; d >= startDay; d = d.AddDays(-1))
            {
                var key = d.ToString("yyyy-MM-dd");
                if (recordMap.TryGetValue(key, out var r2) && r2.Total > 0)
                {
                    streak++;
                    if (currentStreak == 0) currentStreak = streak; // 连续到今天
                }
                else
                {
                    if (currentStreak == 0) currentStreak = 0; // 还没遇到今天的
                    maxStreak = Math.Max(maxStreak, streak);
                    streak = 0;
                }
            }
            maxStreak = Math.Max(maxStreak, streak);
            // 若今天没有数据，当前连续为0
            if (!recordMap.TryGetValue(today.ToString("yyyy-MM-dd"), out var todayR) || todayR.Total == 0)
                currentStreak = 0;

            CurrentStreakText = $"{currentStreak} 天";
            MaxStreakText = $"{maxStreak} 天";

            // 刷新集合
            HeatmapCells.Clear();
            foreach (var cell in cells) HeatmapCells.Add(cell);

            MonthLabels.Clear();
            foreach (var label in monthLabelList) MonthLabels.Add(label);
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

        private void LoadRecords()
        {
            try
            {
                if (Helpers.LocalSettingsHelper.TryGetValue<string>(DailyTrafficKey, out var json)
                    && !string.IsNullOrEmpty(json))
                {
                    _dailyRecords = JsonSerializer.Deserialize(json, AnywhereWinUI.Models.AppJsonContext.Default.ListDailyTraffic)
                        ?? new List<DailyTraffic>();
                }
            }
            catch
            {
                _dailyRecords = new List<DailyTraffic>();
            }
        }

        private void SaveRecords()
        {
            try
            {
                Helpers.LocalSettingsHelper.SetValue(DailyTrafficKey,
                    JsonSerializer.Serialize(_dailyRecords, AnywhereWinUI.Models.AppJsonContext.Default.ListDailyTraffic));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save records: {ex.Message}");
            }
        }

        public void Dispose()
        {
            CoreManager.Instance.TrafficUpdated -= OnTrafficUpdated;
            CoreManager.Instance.RunningChanged -= OnRunningChanged;
        }
    }
}
