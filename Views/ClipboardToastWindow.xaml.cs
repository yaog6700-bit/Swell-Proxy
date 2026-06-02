using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using AnywhereWinUI.Services;
using System;

namespace AnywhereWinUI.Views
{
    public sealed partial class ClipboardToastWindow : Window
    {
        private PersistedNode _node;
        private AppWindow _appWindow;
        private DispatcherTimer _timer;

        public ClipboardToastWindow(PersistedNode node)
        {
            this.InitializeComponent();
            _node = node;
            NodeInfoText.Text = $"类型: {node.Protocol} | 名称: {node.Name}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            // Set size and remove titlebar styling
            _appWindow.Resize(new Windows.Graphics.SizeInt32(340, 160));
            var presenter = _appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
            }

            this.ExtendsContentIntoTitleBar = true;

            // Move to bottom right
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            int x = displayArea.WorkArea.X + displayArea.WorkArea.Width - 340 - 20;
            int y = displayArea.WorkArea.Y + displayArea.WorkArea.Height - 160 - 20;
            _appWindow.Move(new Windows.Graphics.PointInt32(x, y));

            // Auto close after 6 seconds
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _timer.Tick += (s, e) => { _timer.Stop(); this.Close(); };
            _timer.Start();

            // Setup Enter key handling
            this.Content.KeyDown += (s, e) => 
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    ImportButton_Click(null, null);
                }
            };
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
