using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using AnywhereWinUI.Helpers;

namespace AnywhereWinUI.Services
{
    public class ClipboardMonitorService
    {
        private static ClipboardMonitorService? _instance;
        public static ClipboardMonitorService Instance => _instance ??= new ClipboardMonitorService();

        private string _lastCopiedText = string.Empty;
        private DateTime _lastTriggerTime = DateTime.MinValue;

        public void Start()
        {
            try
            {
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
                if (dataPackageView.Contains(StandardDataFormats.Text))
                {
                    string text = await dataPackageView.GetTextAsync();
                    if (string.IsNullOrWhiteSpace(text)) return;
                    text = text.Trim();

                    // Prevent reacting to our own imports or duplicates
                    if (text == _lastCopiedText) return;

                    // Fast check for likely proxy links
                    if (text.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
                    {
                        var node = NodeLinkParser.Parse(text);
                        if (node != null)
                        {
                            _lastCopiedText = text;
                            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                            {
                                var toast = new Views.ClipboardToastWindow(node);
                                toast.Activate();
                            });
                        }
                    }
                }
            }
            catch
            {
                // Clipboard operations can fail if another app is holding the clipboard
            }
        }
    }
}
