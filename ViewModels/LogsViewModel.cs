using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

        private static readonly SolidColorBrush RunningBrush = new(ColorHelper.FromArgb(255, 34, 197, 94)); // green
        private static readonly SolidColorBrush StoppedBrush = new(ColorHelper.FromArgb(255, 156, 163, 175)); // grey

        private readonly DispatcherQueue _queue;
        private readonly DispatcherQueueTimer _flushTimer;
        private volatile bool _dirty;
        private int _linesReceivedSinceFlush;
        private int _prevBufferCount;

        [ObservableProperty]
        private ObservableCollection<LogEntry> _logEntries = new();

        [ObservableProperty]
        private string _logText = string.Empty;

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
            _queue = DispatcherQueue.GetForCurrentThread();

            CoreManager.Instance.LogReceived += OnLogReceived;
            CoreManager.Instance.RunningChanged += OnRunningChanged;

            RenderLog();
            UpdateStatus();

            _flushTimer = _queue.CreateTimer();
            _flushTimer.Interval = FlushInterval;
            _flushTimer.IsRepeating = true;
            _flushTimer.Tick += OnFlushTick;
            _flushTimer.Start();
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
            if (!_dirty) return;
            _dirty = false;

            int received = System.Threading.Interlocked.Exchange(ref _linesReceivedSinceFlush, 0);
            int prevCount = _prevBufferCount;

            RenderLog();
            
            int newCount = _prevBufferCount;

            LogFlushed?.Invoke(this, (received, prevCount, newCount));
        }

        private void RenderLog()
        {
            var lines = CoreManager.Instance.GetLogBuffer();
            
            // Only parse new lines if we're appending, to save UI thread performance
            if (lines.Count == 0)
            {
                LogEntries.Clear();
                LogText = string.Empty;
            }
            else
            {
                // Full rebuild for simplicity since it's capped at 500
                LogEntries.Clear();
                foreach (var line in lines)
                {
                    LogEntries.Add(LogEntry.Parse(line));
                }
                LogText = string.Join(Environment.NewLine, lines.Select(l => LogEntry.Parse(l).Message));
            }
            
            LineCountText = $"({lines.Count} 行)";
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
            var dp = new DataPackage();
            dp.SetText(LogText);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }

        [RelayCommand]
        private void ClearLogs()
        {
            CoreManager.Instance.ClearLogBuffer();
            RenderLog();
        }

        public void Dispose()
        {
            _flushTimer.Stop();
            CoreManager.Instance.LogReceived -= OnLogReceived;
            CoreManager.Instance.RunningChanged -= OnRunningChanged;
        }
    }
}
