using System;
using Windows.ApplicationModel.DataTransfer;

namespace AnywhereWinUI.Services
{
    public class ClipboardMonitorService
    {
        private static ClipboardMonitorService? _instance;
        public static ClipboardMonitorService Instance => _instance ??= new ClipboardMonitorService();

        // Store only length + hash so copied text is never retained in managed heap
        private int _lastCopiedLength;
        private int _lastCopiedHash;
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private const int MaxCandidateLength = 8192;

        public void Start()
        {
            try
            {
                Clipboard.ContentChanged -= OnClipboardContentChanged;
                Clipboard.ContentChanged += OnClipboardContentChanged;
            }
            catch
            {
                // Ignore if not supported
            }
        }

        public void Stop()
        {
            try
            {
                Clipboard.ContentChanged -= OnClipboardContentChanged;
            }
            catch { }
        }

        private async void OnClipboardContentChanged(object? sender, object e)
        {
            // Debounce to avoid multiple triggers for the same copy action
            if ((DateTime.Now - _lastTriggerTime).TotalMilliseconds < 500)
                return;
            _lastTriggerTime = DateTime.Now;

            try
            {
                var dataPackageView = Clipboard.GetContent();
                if (!dataPackageView.Contains(StandardDataFormats.Text))
                    return;

                string text = await dataPackageView.GetTextAsync();
                if (string.IsNullOrWhiteSpace(text) || text.Length > MaxCandidateLength)
                    return;

                ReadOnlySpan<char> span = text.AsSpan().Trim();
                if (span.IsEmpty)
                    return;

                // Fast prefix check on ReadOnlySpan<char> BEFORE allocating any trimmed string
                if (!span.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase) &&
                    !span.StartsWith("snell://", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                int hash = string.GetHashCode(span, StringComparison.Ordinal);
                if (span.Length == _lastCopiedLength && hash == _lastCopiedHash)
                    return;

                string candidate = span.Length == text.Length ? text : span.ToString();
                var node = NodeLinkParser.Parse(candidate);
                if (node != null)
                {
                    _lastCopiedLength = span.Length;
                    _lastCopiedHash = hash;

                    var queue = MainWindow.Instance?.DispatcherQueue
                                ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    queue?.TryEnqueue(() =>
                    {
                        var toast = new Views.ClipboardToastWindow(node);
                        toast.Activate();
                    });
                }
            }
            catch
            {
                // Clipboard operations can fail if another app is holding the clipboard
            }
        }
    }
}
