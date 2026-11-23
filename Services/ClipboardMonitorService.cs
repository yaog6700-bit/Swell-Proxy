using System;
using System.Runtime.InteropServices;
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
        private long _lastTriggerTick;
        private const int MaxCandidateLength = 8192;
        private const uint CF_UNICODETEXT = 13;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

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

        /// <summary>
        /// Marks a string just copied by Swell Proxy itself so OnClipboardContentChanged
        /// will not pop up an import toast for the user's own "Share / Copy Link" action.
        /// </summary>
        public void IgnoreText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ReadOnlySpan<char> span = text.AsSpan().Trim();
            if (span.IsEmpty) return;
            _lastCopiedLength = span.Length;
            _lastCopiedHash = string.GetHashCode(span, StringComparison.Ordinal);
        }

        private void OnClipboardContentChanged(object? sender, object e)
        {
            // Debounce to avoid multiple triggers for the same copy action
            long nowTick = Environment.TickCount64;
            if (nowTick - _lastTriggerTick < 500)
                return;
            _lastTriggerTick = nowTick;

            // Fast Win32 pre-check: 0 managed allocations and 0 LOH spikes when user copies images,
            // files, or multi-megabyte logs/spreadsheets in other applications!
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                return;

            string? candidate = TryExtractProxyCandidateZeroAlloc();
            if (candidate == null)
                return;

            var node = NodeLinkParser.Parse(candidate);
            if (node != null)
            {
                var queue = MainWindow.Instance?.DispatcherQueue
                            ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                queue?.TryEnqueue(() =>
                {
                    var toast = new Views.ClipboardToastWindow(node);
                    toast.Activate();
                });
            }
        }

        private unsafe string? TryExtractProxyCandidateZeroAlloc()
        {
            if (!OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                IntPtr hGlobal = GetClipboardData(CF_UNICODETEXT);
                if (hGlobal == IntPtr.Zero)
                    return null;

                ulong byteSize = GlobalSize(hGlobal).ToUInt64();
                // Reject empty or > 8192 UTF-16 chars BEFORE allocating any managed string!
                if (byteSize < 12 || byteSize > (ulong)((MaxCandidateLength + 1) * sizeof(char)))
                    return null;

                IntPtr ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero)
                    return null;

                try
                {
                    int maxChars = (int)(byteSize / sizeof(char));
                    ReadOnlySpan<char> raw = new ReadOnlySpan<char>((void*)ptr, maxChars);
                    int nullIdx = raw.IndexOf('\0');
                    if (nullIdx >= 0)
                        raw = raw.Slice(0, nullIdx);

                    ReadOnlySpan<char> span = raw.Trim();
                    if (span.IsEmpty || span.Length > MaxCandidateLength)
                        return null;

                    if (!HasSupportedProxyPrefix(span))
                        return null;

                    int hash = string.GetHashCode(span, StringComparison.Ordinal);
                    if (span.Length == _lastCopiedLength && hash == _lastCopiedHash)
                        return null;

                    _lastCopiedLength = span.Length;
                    _lastCopiedHash = hash;
                    return new string(span);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static bool HasSupportedProxyPrefix(ReadOnlySpan<char> span)
        {
            return span.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("naive+http2://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("naive+http://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("naive://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("http2://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("nowhere://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) ||
                   span.StartsWith("snell://", StringComparison.OrdinalIgnoreCase) ||
                   span.IndexOf("= snell,".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
