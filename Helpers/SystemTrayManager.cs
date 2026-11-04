using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using WinUIEx;
using WinUIEx.Messaging;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Reliable Windows 10 / 11 system tray manager with genuine WinUI 3 MenuFlyout support.
    /// - Uses a dedicated top-level Win32 hidden window + owned HICON handles for Shell_NotifyIconW,
    ///   completely isolated from AppWindow.SetIcon / WM_SETICON handle destruction.
    /// - Allows Explorer's "TaskbarCreated" broadcast across UIPI when running elevated (TUN mode)
    ///   and self-heals via NIM_MODIFY return-value checking + a lightweight 10s watchdog.
    /// - Hosts genuine WinUI 3 MenuFlyout popups via a safe 0x0 Popup-styled XAML host window
    ///   (with Win32 WS_EX_TOOLWINDOW fallback so Windows 10 never crashes on IsShownInSwitchers).
    /// </summary>
    public sealed class SystemTrayManager : IDisposable
    {
        public const string ShowMainWindowMessageName = "SwellProxy_ShowMainWindow";

        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_GETMINMAXINFO = 0x0024;
        private const uint WM_DISPLAYCHANGE = 0x007E;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_DPICHANGED = 0x02E0;
        private const uint WM_TRAYCALLBACK = 0x8000 + 0x42; // WM_APP + 0x42

        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WS_EX_APPWINDOW = 0x00040000;
        private const uint WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;
        private const int IDI_APPLICATION = 32512;

        private const uint MSGFLT_ALLOW = 1;
        private const uint TRAY_ICON_ID = 1001;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly Action _onOpenRequested;
        private readonly Func<FlyoutBase> _contextMenuFactory;

        // Pinned delegate reference tied to this manager's lifetime so GC never collects WndProc
        private readonly WndProcDelegate _wndProcDelegate;
        private readonly string _windowClassName;
        private readonly uint _wmTaskbarCreated;
        private readonly uint _wmShowMainWindow;
        private readonly DispatcherQueueTimer _watchdogTimer;

        private IntPtr _hwnd;
        private IntPtr _activeIcon;
        private IntPtr _inactiveIcon;
        private int _loadedIconWidth;
        private int _loadedIconHeight;
        private bool _isActive;
        private bool _isAdded;
        private bool _disposed;

        // WinUI 3 Flyout host window (0x0 tool-popup window, safe on all Win10 & Win11 builds)
        private Window? _flyoutHostWindow;
        private Grid? _flyoutHostRoot;
        private IntPtr _flyoutHostHwnd;
        private WindowMessageMonitor? _flyoutMessageMonitor;
        private FlyoutBase? _currentFlyout;

        public SystemTrayManager(
            DispatcherQueue dispatcherQueue,
            bool initialActiveState,
            Action onOpenRequested,
            Func<FlyoutBase> contextMenuFactory)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            _isActive = initialActiveState;
            _onOpenRequested = onOpenRequested ?? throw new ArgumentNullException(nameof(onOpenRequested));
            _contextMenuFactory = contextMenuFactory ?? throw new ArgumentNullException(nameof(contextMenuFactory));

            _wndProcDelegate = WndProc;
            _windowClassName = $"SwellProxyTrayWnd_{Environment.ProcessId}";
            _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            _wmShowMainWindow = RegisterWindowMessageW(ShowMainWindowMessageName);

            CreateHiddenTopLevelWindow();
            LoadIcons();
            AddOrUpdateTrayIcon();
            InitializeFlyoutHostWindow();

            // Self-healing watchdog: verifies every 10s that Explorer still holds our tray icon
            // (covers boot autostart before Explorer tray init, Win10 silent notification area resets, etc.)
            _watchdogTimer = _dispatcherQueue.CreateTimer();
            _watchdogTimer.Interval = TimeSpan.FromSeconds(10);
            _watchdogTimer.IsRepeating = true;
            _watchdogTimer.Tick += (_, _) => EnsureVisible();
            _watchdogTimer.Start();
        }

        public void SetActiveState(bool isActive)
        {
            if (_disposed) return;
            _isActive = isActive;
            AddOrUpdateTrayIcon();
        }

        public void EnsureVisible()
        {
            if (_disposed || _hwnd == IntPtr.Zero) return;
            ReloadIconsIfMetricsChanged();
            AddOrUpdateTrayIcon();
        }

        public static void NotifyExistingInstanceToShow()
        {
            try
            {
                uint msg = RegisterWindowMessageW(ShowMainWindowMessageName);
                if (msg != 0)
                {
                    // HWND_BROADCAST = 0xFFFF
                    PostMessageW((IntPtr)0xFFFF, msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch
            {
                // Best effort only
            }
        }

        private void InitializeFlyoutHostWindow()
        {
            try
            {
                _flyoutHostWindow = new Window();
                _flyoutHostRoot = new Grid();
                _flyoutHostWindow.Content = _flyoutHostRoot;
                _flyoutHostHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_flyoutHostWindow);

                // Safe on Windows 10: wrap IsShownInSwitchers in try/catch + apply Win32 WS_EX_TOOLWINDOW
                try
                {
                    _flyoutHostWindow.AppWindow.IsShownInSwitchers = false;
                }
                catch { }

                WindowExtensions.SetWindowStyle(_flyoutHostWindow, WindowStyle.Popup);
                WindowExtensions.SetIsAlwaysOnTop(_flyoutHostWindow, true);

                if (_flyoutHostHwnd != IntPtr.Zero)
                {
                    long exStyle = GetWindowLongPtrW(_flyoutHostHwnd, GWL_EXSTYLE).ToInt64();
                    exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                    SetWindowLongPtrW(_flyoutHostHwnd, GWL_EXSTYLE, new IntPtr(exStyle));

                    _flyoutMessageMonitor = new WindowMessageMonitor(_flyoutHostHwnd);
                    _flyoutMessageMonitor.WindowMessageReceived += FlyoutHostWindow_MessageReceived;
                }

                _flyoutHostWindow.AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 0, 0));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SystemTrayManager] Failed to init WinUI flyout host: {ex.Message}");
            }
        }

        private void FlyoutHostWindow_MessageReceived(object? sender, WindowMessageEventArgs e)
        {
            if (_disposed) return;

            if (e.Message.MessageId == WM_GETMINMAXINFO && e.Message.LParam != IntPtr.Zero)
            {
                // Allow 0x0 physical window size so no empty window border ever appears
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(e.Message.LParam);
                mmi.ptMinTrackSize.X = 0;
                mmi.ptMinTrackSize.Y = 0;
                Marshal.StructureToPtr(mmi, e.Message.LParam, false);
            }
            else if (e.Message.MessageId == WM_ACTIVATE)
            {
                if (((ulong)e.Message.WParam & 0xFFFF) == 0)
                {
                    // Host window lost focus (user clicked outside the menu) — dismiss WinUI Flyout
                    _dispatcherQueue.TryEnqueue(CloseFlyout);
                }
            }
        }

        private void CreateHiddenTopLevelWindow()
        {
            IntPtr hInstance = GetModuleHandleW(null);

            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = _windowClassName,
                hIconSm = IntPtr.Zero
            };

            RegisterClassExW(ref wndClass);

            // IMPORTANT: hWndParent MUST be IntPtr.Zero (top-level hidden popup), NOT HWND_MESSAGE,
            // because Windows only delivers HWND_BROADCAST ("TaskbarCreated") to top-level windows.
            _hwnd = CreateWindowExW(
                WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                _windowClassName,
                "SwellProxyTrayHiddenWindow",
                WS_POPUP,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (_hwnd != IntPtr.Zero)
            {
                // Allow Medium-IL explorer.exe (and non-elevated shortcut launches) to send messages
                // to this window even when Swell Proxy runs elevated as Administrator (TUN Mode).
                if (_wmTaskbarCreated != 0)
                {
                    ChangeWindowMessageFilterEx(_hwnd, _wmTaskbarCreated, MSGFLT_ALLOW, IntPtr.Zero);
                }
                if (_wmShowMainWindow != 0)
                {
                    ChangeWindowMessageFilterEx(_hwnd, _wmShowMainWindow, MSGFLT_ALLOW, IntPtr.Zero);
                }
                ChangeWindowMessageFilterEx(_hwnd, WM_TRAYCALLBACK, MSGFLT_ALLOW, IntPtr.Zero);
            }
        }

        private void LoadIcons()
        {
            int cx = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
            int cy = Math.Max(16, GetSystemMetrics(SM_CYSMICON));

            IntPtr newActive = LoadSingleIcon("output.ico", cx, cy);
            IntPtr newInactive = LoadSingleIcon("output_grey.ico", cx, cy);

            IntPtr oldActive = _activeIcon;
            IntPtr oldInactive = _inactiveIcon;

            _activeIcon = newActive;
            _inactiveIcon = newInactive;
            _loadedIconWidth = cx;
            _loadedIconHeight = cy;

            if (oldActive != IntPtr.Zero && oldActive != _activeIcon)
            {
                DestroyIcon(oldActive);
            }
            if (oldInactive != IntPtr.Zero && oldInactive != _inactiveIcon)
            {
                DestroyIcon(oldInactive);
            }
        }

        private static IntPtr LoadSingleIcon(string fileName, int cx, int cy)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(iconPath))
            {
                IntPtr hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }

            return LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        private void ReloadIconsIfMetricsChanged()
        {
            int cx = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
            int cy = Math.Max(16, GetSystemMetrics(SM_CYSMICON));
            if (cx != _loadedIconWidth || cy != _loadedIconHeight || _activeIcon == IntPtr.Zero || _inactiveIcon == IntPtr.Zero)
            {
                LoadIcons();
            }
        }

        private NOTIFYICONDATAW CreateNotifyIconData()
        {
            IntPtr iconHandle = _isActive ? _activeIcon : _inactiveIcon;
            if (iconHandle == IntPtr.Zero)
            {
                iconHandle = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
            }

            string tooltip = _isActive ? "Swell Proxy - 已连接" : "Swell Proxy - 未连接";

            return new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYCALLBACK,
                hIcon = iconHandle,
                szTip = tooltip,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        private void AddOrUpdateTrayIcon()
        {
            if (_disposed || _hwnd == IntPtr.Zero) return;

            var nid = CreateNotifyIconData();

            if (_isAdded)
            {
                if (Shell_NotifyIconW(NIM_MODIFY, ref nid))
                {
                    return;
                }

                // Explorer lost our tray icon (e.g. Win10 tray reset or missed broadcast) — clean & re-add
                Shell_NotifyIconW(NIM_DELETE, ref nid);
                _isAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
            }
            else
            {
                if (Shell_NotifyIconW(NIM_ADD, ref nid))
                {
                    _isAdded = true;
                }
                else if (Shell_NotifyIconW(NIM_MODIFY, ref nid))
                {
                    _isAdded = true;
                }
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (!_disposed)
            {
                if (msg == WM_TRAYCALLBACK)
                {
                    uint trayEvent = (uint)(lParam.ToInt64() & 0xFFFF);
                    switch (trayEvent)
                    {
                        case WM_LBUTTONUP:
                        case WM_LBUTTONDBLCLK:
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                CloseFlyout();
                                _onOpenRequested();
                            });
                            return IntPtr.Zero;

                        case WM_RBUTTONUP:
                            var targetRect = GetTrayAnchorRect();
                            _dispatcherQueue.TryEnqueue(() => ShowWinUIFlyout(targetRect));
                            return IntPtr.Zero;
                    }
                }
                else if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
                {
                    // Explorer restarted — force fresh NIM_ADD registration
                    _isAdded = false;
                    ReloadIconsIfMetricsChanged();
                    AddOrUpdateTrayIcon();
                    return IntPtr.Zero;
                }
                else if (_wmShowMainWindow != 0 && msg == _wmShowMainWindow)
                {
                    EnsureVisible();
                    _dispatcherQueue.TryEnqueue(() => _onOpenRequested());
                    return IntPtr.Zero;
                }
                else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED || msg == WM_SETTINGCHANGE)
                {
                    ReloadIconsIfMetricsChanged();
                    AddOrUpdateTrayIcon();
                }
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private RECT GetTrayAnchorRect()
        {
            var identifier = new NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID,
                guidItem = Guid.Empty
            };

            if (Shell_NotifyIconGetRect(ref identifier, out RECT iconRect) == 0)
            {
                return iconRect;
            }

            GetCursorPos(out POINT pt);
            return new RECT
            {
                Left = pt.X,
                Top = pt.Y,
                Right = pt.X,
                Bottom = pt.Y
            };
        }

        private void ShowWinUIFlyout(RECT targetRect)
        {
            if (_disposed) return;

            if (_flyoutHostWindow == null || _flyoutHostRoot == null)
            {
                InitializeFlyoutHostWindow();
                if (_flyoutHostWindow == null || _flyoutHostRoot == null) return;
            }

            CloseFlyout();

            var flyout = _contextMenuFactory();
            if (flyout == null) return;

            // Sync WinUI 3 MenuFlyout theme with main window (Light / Dark / System)
            var activeTheme = MainWindow.Instance?.GetActiveTheme() ?? ElementTheme.Default;
            _flyoutHostRoot.RequestedTheme = activeTheme;

            flyout.ShouldConstrainToRootBounds = false;
            _currentFlyout = flyout;
            _flyoutHostRoot.ContextFlyout = flyout;

            var displayArea = DisplayArea.GetFromPoint(
                new PointInt32(targetRect.Left, targetRect.Top),
                DisplayAreaFallback.Primary);

            _flyoutHostWindow.AppWindow.MoveAndResize(
                new RectInt32(targetRect.Left, targetRect.Top, 0, 0),
                displayArea);

            _flyoutHostWindow.Activate();
            WindowExtensions.Show(_flyoutHostWindow);
            if (_flyoutHostHwnd != IntPtr.Zero)
            {
                SetForegroundWindow(_flyoutHostHwnd);
            }

            void PresentFlyout()
            {
                if (_disposed || _currentFlyout != flyout || _flyoutHostRoot?.XamlRoot == null) return;

                double scale = _flyoutHostRoot.XamlRoot.RasterizationScale > 0
                    ? _flyoutHostRoot.XamlRoot.RasterizationScale
                    : 1.0;
                double w = Math.Max(0, (targetRect.Right - targetRect.Left) / scale);
                double h = Math.Max(0, (targetRect.Bottom - targetRect.Top) / scale);

                flyout.Closed += CurrentFlyout_Closed;
                flyout.ShowAt(_flyoutHostRoot, new FlyoutShowOptions
                {
                    Position = new Point(0, 0),
                    ExclusionRect = new Rect(0, 0, w, h)
                });
            }

            if (_flyoutHostRoot.XamlRoot != null)
            {
                PresentFlyout();
            }
            else
            {
                RoutedEventHandler? onLoaded = null;
                onLoaded = (_, _) =>
                {
                    if (_flyoutHostRoot != null)
                        _flyoutHostRoot.Loaded -= onLoaded;
                    PresentFlyout();
                };
                _flyoutHostRoot.Loaded += onLoaded;
            }
        }

        private void CurrentFlyout_Closed(object? sender, object e)
        {
            if (sender is FlyoutBase flyout)
            {
                flyout.Closed -= CurrentFlyout_Closed;
                if (_currentFlyout == flyout)
                {
                    _currentFlyout = null;
                }
            }

            if (!_disposed && _flyoutHostWindow != null)
            {
                try { WindowExtensions.Hide(_flyoutHostWindow); } catch { }
            }
        }

        public void CloseFlyout()
        {
            if (_currentFlyout != null)
            {
                var flyout = _currentFlyout;
                _currentFlyout = null;
                flyout.Closed -= CurrentFlyout_Closed;
                try
                {
                    if (flyout.IsOpen)
                    {
                        flyout.Hide();
                    }
                }
                catch { }
            }

            if (_flyoutHostWindow != null)
            {
                try { WindowExtensions.Hide(_flyoutHostWindow); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _watchdogTimer.Stop(); } catch { }

            try
            {
                if (_currentFlyout != null)
                {
                    _currentFlyout.Closed -= CurrentFlyout_Closed;
                    if (_currentFlyout.IsOpen)
                        _currentFlyout.Hide();
                    _currentFlyout = null;
                }
            }
            catch { }

            try
            {
                if (_flyoutMessageMonitor != null)
                {
                    _flyoutMessageMonitor.WindowMessageReceived -= FlyoutHostWindow_MessageReceived;
                    _flyoutMessageMonitor.Dispose();
                    _flyoutMessageMonitor = null;
                }
            }
            catch { }

            try
            {
                _flyoutHostWindow?.Close();
                _flyoutHostWindow = null;
                _flyoutHostRoot = null;
                _flyoutHostHwnd = IntPtr.Zero;
            }
            catch { }

            if (_hwnd != IntPtr.Zero)
            {
                if (_isAdded)
                {
                    var nid = new NOTIFYICONDATAW
                    {
                        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                        hWnd = _hwnd,
                        uID = TRAY_ICON_ID,
                        szTip = string.Empty,
                        szInfo = string.Empty,
                        szInfoTitle = string.Empty
                    };
                    Shell_NotifyIconW(NIM_DELETE, ref nid);
                    _isAdded = false;
                }

                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            UnregisterClassW(_windowClassName, GetModuleHandleW(null));

            if (_activeIcon != IntPtr.Zero)
            {
                DestroyIcon(_activeIcon);
                _activeIcon = IntPtr.Zero;
            }
            if (_inactiveIcon != IntPtr.Zero)
            {
                DestroyIcon(_inactiveIcon);
                _inactiveIcon = IntPtr.Zero;
            }
        }

        #region Win32 Interop

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("shell32.dll")]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        #endregion
    }
}
