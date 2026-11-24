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
using AnywhereWinUI.Helpers;
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
    public partial class TrafficChartViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _dayLabel = string.Empty;

        [ObservableProperty]
        private double _uploadHeight;

        [ObservableProperty]
        private double _downloadHeight;

        [ObservableProperty]
        private string _uploadTooltip = string.Empty;

        [ObservableProperty]
        private string _downloadTooltip = string.Empty;
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
        private readonly object _recordsLock = new();
        private List<DailyTraffic> _dailyRecords = new();
        private DailyTraffic? _cachedTodayRecord;
        private string _currentDateStr = string.Empty;
        private bool _hasUnsavedTraffic = false;
        private DateTime _lastDiskSaveUtc = DateTime.UtcNow;

        // 当前会话净增量（仅代理出站；由 CoreManager 的 ProxyTrafficTracker 累计）
        private long _sessionUpload = 0;
        private long _sessionDownload = 0;

        // 上一次上报的代理会话累计（用于差分）
        private long _lastTotalDown = -1;
        private long _lastTotalUp = -1;

        // 历史基准（今日已持久化的流量，用于跨会话累加）
        private long _todayBaseUpload = 0;
        private long _todayBaseDownload = 0;

        // 缓存 UI 线程 DispatcherQueue，避免事件回调时 GetForCurrentThread() 返回 null
        private readonly DispatcherQueue _dispatcherQueue;

        // Chart / heatmap rebuild guard:
        //   - Only rebuild when the Traffic page is actually visible to avoid
        //     O(N) collection rebuilds every second while the page is hidden.
        //   - Heatmap is throttled to once every 30 traffic ticks (~30 s) because
        //     it iterates 365 days even when nothing meaningful has changed.
        private bool _isPageActive = false;
        private int  _heatmapThrottleCount = 0;
        private const int HeatmapThrottleInterval = 30;

        // Disk write throttle: persist at most once every 60 s while the core is running.
        private int  _saveThrottleCount = 0;
        private const int SaveThrottleInterval = 60;

        private const string DailyTrafficKey = "daily_traffic_records";

        [ObservableProperty]
        private string _sessionUploadText = "0.0 B";

        [ObservableProperty]
        private string _sessionDownloadText = "0.0 B";

        [ObservableProperty]
        private string _sessionTotalText = "0.0 B";

        [ObservableProperty]
        private string _activeConnectionsText = "↓ 0.0 B/s   ↑ 0.0 B/s";

        [ObservableProperty]
        private string _monthUploadText = "0.0 B";

        [ObservableProperty]
        private string _monthDownloadText = "0.0 B";

        [ObservableProperty]
        private string _monthTotalText = "0.0 B";

        [ObservableProperty]
        private Brush _statusDotFill = new SolidColorBrush(Colors.Gray);

        [ObservableProperty]
        private string _statusText = L.Traffic_StatusInactive;

        // ── 热力图统计属性 ──────────────────────────────────────────────
        [ObservableProperty]
        private string _totalTrafficText = "0.0 B";

        [ObservableProperty]
        private string _peakDayText = "—";

        [ObservableProperty]
        private string _currentStreakText = string.Format(L.Traffic_DaysFmt, 0);

        [ObservableProperty]
        private string _maxStreakText = string.Format(L.Traffic_DaysFmt, 0);

        public ObservableCollection<TrafficChartViewModel> ChartItems { get; } = new();
        public IReadOnlyList<HeatmapCellViewModel> HeatmapCellList { get; private set; } = Array.Empty<HeatmapCellViewModel>();
        public IReadOnlyList<string> MonthLabelList { get; private set; } = Array.Empty<string>();
        public event EventHandler? HeatmapUpdated;

        private HeatmapViewMode _heatmapViewMode = HeatmapViewMode.Daily;
        public HeatmapViewMode CurrentHeatmapMode => _heatmapViewMode;

        /// <summary>
        /// Thread-safe snapshot of today's cumulative proxy upload + download bytes.
        /// </summary>
        public long GetTodayTotalBytes()
        {
            lock (_recordsLock)
            {
                string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
                if (string.Equals(_currentDateStr, todayStr, StringComparison.Ordinal))
                {
                    return Math.Max(0, _todayBaseUpload + _sessionUpload)
                         + Math.Max(0, _todayBaseDownload + _sessionDownload);
                }

                var rec = _dailyRecords.FirstOrDefault(r => r.Date == todayStr);
                return rec?.Total ?? 0;
            }
        }

        public TrafficViewModel()
        {
            // 在 UI 线程构造时缓存 DispatcherQueue；若此时已在 UI 线程，直接获取，否则尝试 MainWindow
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;

            LoadRecords();
            _currentDateStr = DateTime.Now.ToString("yyyy-MM-dd");

            CoreManager.Instance.TrafficUpdated += OnTrafficUpdated;
            CoreManager.Instance.RunningChanged += OnRunningChanged;
            MainWindow.VisibilityToUserChanged += OnWindowVisibilityChanged;

            ResetSessionState();
            UpdateStatusUI(CoreManager.Instance.IsRunning);
        }

        private void OnWindowVisibilityChanged(object? sender, bool isVisible)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (!isVisible)
                {
                    ChartItems.Clear();
                    HeatmapCellList = Array.Empty<HeatmapCellViewModel>();
                    MonthLabelList = Array.Empty<string>();
                    HeatmapUpdated?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    UpdateStatusUI(CoreManager.Instance.IsRunning);
                    if (_isPageActive)
                    {
                        var latest = CoreManager.Instance.LatestTrafficStats;
                        RefreshSessionStrings(latest.DownSpeed, latest.UpSpeed, includeSpeed: CoreManager.Instance.IsRunning);
                        UpdateMonthAndChartUI();
                        UpdateHeatmapUI();
                    }
                }
            });
        }

        private void RefreshSessionStrings(long downSpeed, long upSpeed, bool includeSpeed)
        {
            SessionUploadText = FormatBytes(_sessionUpload);
            SessionDownloadText = FormatBytes(_sessionDownload);
            SessionTotalText = FormatBytes(_sessionUpload + _sessionDownload);
            if (includeSpeed)
            {
                ActiveConnectionsText = $"↓ {FormatBytes(downSpeed)}/s   ↑ {FormatBytes(upSpeed)}/s";
            }
            else
            {
                ActiveConnectionsText = "↓ 0.0 B/s   ↑ 0.0 B/s";
            }
        }

        private void ResetSessionState()
        {
            lock (_recordsLock)
            {
                _sessionUpload = 0;
                _sessionDownload = 0;
                _currentDateStr = DateTime.Now.ToString("yyyy-MM-dd");

                _cachedTodayRecord = _dailyRecords.FirstOrDefault(r => r.Date == _currentDateStr);
                _todayBaseUpload = _cachedTodayRecord?.ProxyUpload ?? 0;
                _todayBaseDownload = _cachedTodayRecord?.ProxyDownload ?? 0;

                _lastTotalDown = -1;
                _lastTotalUp = -1;
                _hasUnsavedTraffic = false;
            }
        }

        private void OnTrafficUpdated(object? sender, (long Down, long Up, long DownSpeed, long UpSpeed) e)
        {
            // Accumulate session & daily counters directly on the background thread (0 UI thread wakeups when hidden!)
            long sessionUpSnapshot;
            long sessionDownSnapshot;

            lock (_recordsLock)
            {
                if (_lastTotalDown == -1 || _lastTotalUp == -1)
                {
                    _lastTotalDown = e.Down;
                    _lastTotalUp = e.Up;
                    return;
                }

                long deltaDown = e.Down - _lastTotalDown;
                long deltaUp   = e.Up - _lastTotalUp;
                if (deltaDown < 0) deltaDown = 0;
                if (deltaUp < 0) deltaUp = 0;

                _lastTotalDown = e.Down;
                _lastTotalUp = e.Up;

                _sessionUpload   += deltaUp;
                _sessionDownload += deltaDown;
                sessionUpSnapshot = _sessionUpload;
                sessionDownSnapshot = _sessionDownload;

                if (deltaDown > 0 || deltaUp > 0)
                {
                    _hasUnsavedTraffic = true;
                }

                var nowLocal = DateTime.Now;
                // Fast day rollover check: only format string if day-of-month doesn't match last 2 chars of _currentDateStr
                int day = nowLocal.Day;
                bool sameDay = _currentDateStr.Length == 10 &&
                               (_currentDateStr[8] - '0') * 10 + (_currentDateStr[9] - '0') == day &&
                               (_currentDateStr[5] - '0') * 10 + (_currentDateStr[6] - '0') == nowLocal.Month;

                if (!sameDay)
                {
                    PersistTodayInMemoryLocked(sessionUpSnapshot, sessionDownSnapshot);
                    SaveRecordsLocked();
                    _currentDateStr = nowLocal.ToString("yyyy-MM-dd");
                    _cachedTodayRecord = null;
                    _todayBaseUpload = 0;
                    _todayBaseDownload = 0;
                    _sessionUpload = 0;
                    _sessionDownload = 0;
                    sessionUpSnapshot = 0;
                    sessionDownSnapshot = 0;
                    _hasUnsavedTraffic = false;
                    _lastDiskSaveUtc = DateTime.UtcNow;
                }
                else if (deltaDown > 0 || deltaUp > 0)
                {
                    PersistTodayInMemoryLocked(sessionUpSnapshot, sessionDownSnapshot);
                }

                // Throttle disk writes: 60s when visible, 300s (5 min) when hibernating in tray, and ONLY if traffic actually flowed
                if (_hasUnsavedTraffic)
                {
                    double saveIntervalSec = MainWindow.IsVisibleToUser ? 60.0 : 300.0;
                    if ((DateTime.UtcNow - _lastDiskSaveUtc).TotalSeconds >= saveIntervalSec)
                    {
                        SaveRecordsLocked();
                        _hasUnsavedTraffic = false;
                        _lastDiskSaveUtc = DateTime.UtcNow;
                    }
                }
            }

            // ZERO DispatcherQueue.TryEnqueue when hidden to tray or viewing another page!
            if (!_isPageActive || !MainWindow.IsVisibleToUser)
                return;

            _dispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    if (!_isPageActive || !MainWindow.IsVisibleToUser) return;
                    RefreshSessionStrings(e.DownSpeed, e.UpSpeed, includeSpeed: true);
                    UpdateMonthAndChartUI();

                    _heatmapThrottleCount++;
                    if (_heatmapThrottleCount >= HeatmapThrottleInterval)
                    {
                        _heatmapThrottleCount = 0;
                        UpdateHeatmapUI();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TrafficViewModel OnTrafficUpdated error: {ex.Message}");
                }
            });
        }

        private void OnRunningChanged(object? sender, bool isRunning)
        {
            if (!isRunning)
            {
                lock (_recordsLock)
                {
                    if (_hasUnsavedTraffic || _sessionUpload > 0 || _sessionDownload > 0)
                    {
                        PersistTodayInMemoryLocked(_sessionUpload, _sessionDownload);
                        SaveRecordsLocked();
                    }
                    _saveThrottleCount = 0;
                    ResetSessionState();
                }
            }

            if (!MainWindow.IsVisibleToUser)
                return;

            _dispatcherQueue?.TryEnqueue(() =>
            {
                UpdateStatusUI(isRunning);
                if (!isRunning)
                {
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
            StatusText    = isRunning ? L.Traffic_StatusActive : L.Traffic_StatusInactive;
        }

        // Called by TrafficPage when it navigates to/from this ViewModel
        public void OnPageActivated()
        {
            _isPageActive = true;
            bool running = CoreManager.Instance.IsRunning;
            UpdateStatusUI(running);
            var latest = CoreManager.Instance.LatestTrafficStats;
            RefreshSessionStrings(latest.DownSpeed, latest.UpSpeed, includeSpeed: running);
            UpdateMonthAndChartUI();
            UpdateHeatmapUI();
        }

        public void OnPageDeactivated()
        {
            _isPageActive = false;
            ChartItems.Clear();
            HeatmapCellList = Array.Empty<HeatmapCellViewModel>();
            MonthLabelList = Array.Empty<string>();
            HeatmapUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void PersistTodayInMemoryLocked(long sessionUpload, long sessionDownload)
        {
            long todayUp   = _todayBaseUpload   + sessionUpload;
            long todayDown = _todayBaseDownload + sessionDownload;

            if (_cachedTodayRecord == null || _cachedTodayRecord.Date != _currentDateStr)
            {
                _cachedTodayRecord = _dailyRecords.FirstOrDefault(r => r.Date == _currentDateStr);
            }

            if (_cachedTodayRecord != null)
            {
                _cachedTodayRecord.ProxyUpload   = todayUp;
                _cachedTodayRecord.ProxyDownload = todayDown;
            }
            else
            {
                _cachedTodayRecord = new DailyTraffic
                {
                    Date          = _currentDateStr,
                    ProxyUpload   = todayUp,
                    ProxyDownload = todayDown
                };
                _dailyRecords.Add(_cachedTodayRecord);
            }

            if (_dailyRecords.Count > 400)
                _dailyRecords = _dailyRecords.OrderByDescending(r => r.Date).Take(400).ToList();
        }

        private void SaveRecordsLocked()
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

        private void UpdateMonthAndChartUI()
        {
            List<DailyTraffic> snapshot;
            lock (_recordsLock)
            {
                snapshot = _dailyRecords.ToList();
            }

            string monthPrefix = DateTime.Now.ToString("yyyy-MM");
            long monthUp = 0;
            long monthDown = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var r = snapshot[i];
                if (r.Date.StartsWith(monthPrefix, StringComparison.Ordinal))
                {
                    monthUp += r.ProxyUpload;
                    monthDown += r.ProxyDownload;
                }
            }

            MonthUploadText = FormatBytes(monthUp);
            MonthDownloadText = FormatBytes(monthDown);
            MonthTotalText = FormatBytes(monthUp + monthDown);

            if (snapshot.Count == 0)
            {
                ChartItems.Clear();
                return;
            }

            // Bug #3 fix: show only the most recent 30 days matching the card header ("近 30 天")
            var recentRecords = snapshot.OrderBy(r => r.Date).TakeLast(30).ToList();

            long maxVal = recentRecords.Max(r => Math.Max(r.ProxyUpload, r.ProxyDownload));
            if (maxVal == 0) maxVal = 1;

            bool updateInPlace = ChartItems.Count == recentRecords.Count;
            if (!updateInPlace)
            {
                ChartItems.Clear();
            }

            for (int i = 0; i < recentRecords.Count; i++)
            {
                var record = recentRecords[i];
                string dayLabel = record.Date.Length > 5
                    ? record.Date.Substring(record.Date.Length - 5)
                    : record.Date;

                if (DateTime.TryParseExact(record.Date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                {
                    dayLabel = dt.ToString("M/d");
                }

                double upHeight = Math.Max((double)record.ProxyUpload / maxVal * 130, 2);
                double downHeight = Math.Max((double)record.ProxyDownload / maxVal * 130, 2);
                string upTip = string.Format(L.Traffic_UpTooltipFmt, FormatBytes(record.ProxyUpload));
                string downTip = string.Format(L.Traffic_DownTooltipFmt, FormatBytes(record.ProxyDownload));

                if (updateInPlace)
                {
                    var item = ChartItems[i];
                    item.DayLabel = dayLabel;
                    item.UploadHeight = upHeight;
                    item.DownloadHeight = downHeight;
                    item.UploadTooltip = upTip;
                    item.DownloadTooltip = downTip;
                }
                else
                {
                    ChartItems.Add(new TrafficChartViewModel
                    {
                        DayLabel = dayLabel,
                        UploadHeight = upHeight,
                        DownloadHeight = downHeight,
                        UploadTooltip = upTip,
                        DownloadTooltip = downTip
                    });
                }
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
            List<DailyTraffic> snapshot;
            lock (_recordsLock)
            {
                snapshot = _dailyRecords.ToList();
            }

            // 构建过去 365 天的日期序列（从最早到今天）
            var today = DateTime.Today;
            var startDay = today.AddDays(-364);

            // 从 startDay 所在周的周日（或周一）对齐
            // WinUI 显示：列 = 周，行 = 周几（0=周日 … 6=周六）
            int startDow = (int)startDay.DayOfWeek; // 0=Sun
            var gridStart = startDay.AddDays(-startDow); // 对齐到该周周日

            // 建立日期→记录快速查找
            var recordMap = snapshot.ToDictionary(r => r.Date, r => r);

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
                maxVal = snapshot.Count > 0 ? snapshot.Max(r => r.Total) : 1;
            else if (_heatmapViewMode == HeatmapViewMode.Weekly)
                maxVal = weekTotals.Count > 0 ? weekTotals.Values.Max() : 1;
            else // Cumulative
                maxVal = cumulativeMap.Count > 0 ? cumulativeMap.Values.Max() : 1;
            if (maxVal == 0) maxVal = 1;

            // 生成格子列表
            bool isEn = LanguageHelper.EffectiveLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
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
                        ? (isEn ? d.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("M月"))
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
                    tooltip = string.Format(L.Traffic_NoTrafficTipFmt, d);
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
            long grandTotal = snapshot.Sum(r => r.Total);
            TotalTrafficText = FormatBytes(grandTotal);

            var peakRec = snapshot.OrderByDescending(r => r.Total).FirstOrDefault();
            if (peakRec != null)
            {
                if (DateTime.TryParseExact(peakRec.Date, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var peakDate))
                    PeakDayText = isEn
                        ? $"{peakDate.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture)}  {FormatBytes(peakRec.Total)}"
                        : $"{peakDate:M月d日}  {FormatBytes(peakRec.Total)}";
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

            CurrentStreakText = string.Format(L.Traffic_DaysFmt, currentStreak);
            MaxStreakText = string.Format(L.Traffic_DaysFmt, maxStreak);

            // 更新轻量只读快照并通知矢量画布一次性合并渲染（0 ObservableCollection 逐项开销）
            HeatmapCellList = cells;
            MonthLabelList = monthLabelList;
            HeatmapUpdated?.Invoke(this, EventArgs.Empty);
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

        public void FlushPendingToDisk()
        {
            lock (_recordsLock)
            {
                if (_hasUnsavedTraffic)
                {
                    PersistTodayInMemoryLocked(_sessionUpload, _sessionDownload);
                    SaveRecordsLocked();
                    _hasUnsavedTraffic = false;
                }
            }
        }

        public void Dispose()
        {
            FlushPendingToDisk();
            CoreManager.Instance.TrafficUpdated -= OnTrafficUpdated;
            CoreManager.Instance.RunningChanged -= OnRunningChanged;
            MainWindow.VisibilityToUserChanged -= OnWindowVisibilityChanged;
        }
    }
}
