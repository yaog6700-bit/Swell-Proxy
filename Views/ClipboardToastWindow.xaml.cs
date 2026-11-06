using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using System;

namespace AnywhereWinUI.Views
{
    public sealed partial class ClipboardToastWindow : Window
    {
        private readonly PersistedNode _node;
        private readonly AppWindow _appWindow;
        private readonly DispatcherTimer _timer;

        public ClipboardToastWindow(PersistedNode node)
        {
            this.InitializeComponent();
            _node = node;
            RootGrid.RequestedTheme = ContentDialogExtensions.ResolveAppTheme();
            NodeInfoText.Text = $"类型: {node.Protocol} | 名称: {node.Name}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // Configure borderless Fluent toast window without Win32 caption buttons or taskbar/Alt+Tab entry
            this.ExtendsContentIntoTitleBar = true;
            _appWindow.Resize(new Windows.Graphics.SizeInt32(340, 156));
            try { _appWindow.IsShownInSwitchers = false; } catch { }

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(true, false);
            }

            // Move to bottom right of work area
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            int x = displayArea.WorkArea.X + displayArea.WorkArea.Width - 340 - 20;
            int y = displayArea.WorkArea.Y + displayArea.WorkArea.Height - 156 - 20;
            _appWindow.Move(new Windows.Graphics.PointInt32(x, y));

            // Auto close after 6 seconds
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _timer.Tick += (_, _) => { _timer.Stop(); this.Close(); };
            _timer.Start();

            // Setup Enter / Escape key handling
            this.Content.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    ImportButton_Click(null!, null!);
                }
                else if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    CloseButton_Click(null!, null!);
                }
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            this.Close();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            NodesManager.Instance.AddManualNode(_node);

            // Try to refresh UI
            try
            {
                var vm = App.Current.Services.GetService(typeof(AnywhereWinUI.ViewModels.ServersViewModel)) as AnywhereWinUI.ViewModels.ServersViewModel;
                vm?.LoadServersList();
            }
            catch { }

            this.Close();
        }
    }
}
