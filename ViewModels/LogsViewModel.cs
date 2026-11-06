using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using AnywhereWinUI.Services;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.ViewModels
{
    public partial class LogsViewModel : ObservableObject, IDisposable
    {
        private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);
        private const int MaxBufferLines = 500;

        private static readonly SolidColorBrush RunningBrush = new(ColorHelper.FromArgb(255, 34, 197, 94)); // green
        private static readonly SolidColorBrush StoppedBrush = new(ColorHelper.FromArgb(255, 156, 163, 175)); // grey

        private readonly DispatcherQueue _queue;
        private readonly DispatcherQueueTimer _flushTimer;
        private volatile bool _dirty;
        private bool _isPageActive;
        private int _linesReceivedSinceFlush;
        private int _prevBufferCount;

        [ObservableProperty]
        private ObservableCollection<LogEntry> _logEntries = new();

        [ObservableProperty]
        private string _lineCountText = "(0 行)";

        [ObservableProperty]
        private string _statusText = "核心已停止";

        [ObservableProperty]
        private Brush _statusDotFill = StoppedBrush;

        [ObservableProperty]
        private bool _autoScroll = true;

        public event EventHandler<(int Received, int PrevCount, int NewCount)>? LogFlushed;

        public LogsViewModel()
        {
            _queue = DispatcherQueue.GetForCurrentThread()
                     ?? MainWindow.Instance?.DispatcherQueue!;

            CoreManager.Instance.LogReceived += OnLogReceived;
            CoreManager.Instance.RunningChanged += OnRunningChanged;
            MainWindow.VisibilityToUserChanged += OnMainWindowVisibilityChanged;

            UpdateStatus();

            _flushTimer = _queue.CreateTimer();
            _flushTimer.Interval = FlushInterval;
            _flushTimer.IsRepeating = true;
            _flushTimer.Tick += OnFlushTick;
        }

        public void SetPageActive(bool active)
        {
            _isPageActive = active;
            if (active)
            {
                System.Threading.Interlocked.Exchange(ref _linesReceivedSinceFlush, 0);
                _dirty = false;
                RebuildAllEntries();
                UpdateTimerState();
                LogFlushed?.Invoke(this, (_prevBufferCount, 0, _prevBufferCount));
            }
            else
            {
                _flushTimer.Stop();
                LogEntries.Clear();
                _prevBufferCount = 0;
            }
        }

        private void OnMainWindowVisibilityChanged(object? sender, bool isVisible)
        {
            _queue?.TryEnqueue(() =>
            {
                UpdateTimerState();
                if (isVisible && _isPageActive && _dirty)
                {
                    OnFlushTick(_flushTimer, EventArgs.Empty);
                }
            });
        }

        private void UpdateTimerState()
        {
            if (_isPageActive && MainWindow.IsVisibleToUser)
            {
                if (!_flushTimer.IsRunning)
                    _flushTimer.Start();
            }
            else
            {
                if (_flushTimer.IsRunning)
                    _flushTimer.Stop();
            }
        }

        private void OnLogReceived(object? sender, string line)
        {
            System.Threading.Interlocked.Increment(ref _linesReceivedSinceFlush);
            _dirty = true;
        }

        private void OnRunningChanged(object? sender, bool running)
        {
            _queue.TryEnqueue(UpdateStatus);
        }

        private void OnFlushTick(DispatcherQueueTimer sender, object args)
        {
            if (!_isPageActive || !MainWindow.IsVisibleToUser || !_dirty) return;
            _dirty = false;

            int received = System.Threading.Interlocked.Exchange(ref _linesReceivedSinceFlush, 0);
            int prevCount = _prevBufferCount;

            var lines = CoreManager.Instance.GetLogBuffer();
            if (lines.Count == 0)
            {
                LogEntries.Clear();
                _prevBufferCount = 0;
            }
            else if (LogEntries.Count == 0 || received >= MaxBufferLines || received <= 0 || received > lines.Count)
            {
                RebuildFromSnapshot(lines);
            }
            else
            {
                // Incremental append: parse only newly arrived lines instead of rebuilding all 500 items
                int startIdx = Math.Max(0, lines.Count - received);
                for (int i = startIdx; i < lines.Count; i++)
                {
                    LogEntries.Add(LogEntry.Parse(lines[i]));
                }
                while (LogEntries.Count > MaxBufferLines)
                {
                    LogEntries.RemoveAt(0);
                }
                _prevBufferCount = LogEntries.Count;
            }

            LineCountText = $"({lines.Count} 行)";
            LogFlushed?.Invoke(this, (received, prevCount, _prevBufferCount));
        }

        private void RebuildAllEntries()
        {
            var lines = CoreManager.Instance.GetLogBuffer();
            RebuildFromSnapshot(lines);
            LineCountText = $"({lines.Count} 行)";
        }

        private void RebuildFromSnapshot(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            LogEntries.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                LogEntries.Add(LogEntry.Parse(lines[i]));
            }
            _prevBufferCount = lines.Count;
        }

        private void UpdateStatus()
        {
            var running = CoreManager.Instance.IsRunning;
            StatusText = running ? "核心运行中" : "核心已停止";
            StatusDotFill = running ? RunningBrush : StoppedBrush;
        }

        [RelayCommand]
        private void CopyLogs()
        {
            var lines = CoreManager.Instance.GetLogBuffer();
            if (lines.Count == 0) return;

            // Build clipboard string lazily on demand instead of allocating ~100KB LOH strings every 500ms
            var sb = new StringBuilder(lines.Count * 96);
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(LogEntry.Parse(lines[i]).Message);
            }

            var dp = new DataPackage();
            dp.SetText(sb.ToString());
            Clipboard.SetContent(dp);
        }

        [RelayCommand]
        private void ClearLogs()
        {
            CoreManager.Instance.ClearLogBuffer();
            System.Threading.Interlocked.Exchange(ref _linesReceivedSinceFlush, 0);
            _dirty = false;
            LogEntries.Clear();
            _prevBufferCount = 0;
            LineCountText = "(0 行)";
        }

        public void Dispose()
        {
            _flushTimer.Stop();
            CoreManager.Instance.LogReceived -= OnLogReceived;
            CoreManager.Instance.RunningChanged -= OnRunningChanged;
            MainWindow.VisibilityToUserChanged -= OnMainWindowVisibilityChanged;
        }
    }
}
