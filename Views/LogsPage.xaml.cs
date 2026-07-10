using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Views
{
    public sealed partial class LogsPage : Page
    {
        public LogsViewModel ViewModel { get; }

        public LogsPage()
        {
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(LogsViewModel)) as LogsViewModel;
            this.InitializeComponent();

            // Subscribe for page instance lifetime; NavigationCache reuses this page so do not
            // unsubscribe on Unloaded (visual detach is not final destroy).
            if (ViewModel != null)
                ViewModel.LogFlushed += OnLogFlushed;
        }

        private void OnLogFlushed(object? sender, (int Received, int PrevCount, int NewCount) args)
        {
            if (ViewModel == null) return;

            if (ViewModel.AutoScroll)
            {
                LogScrollViewer.ChangeView(null, double.MaxValue, null, disableAnimation: true);
            }
            else
            {
                // Scroll anchoring: Shift the scroll offset down by the height of evicted lines so visible content stays anchored.
                var prevOffset = LogScrollViewer.VerticalOffset;
                var prevExtent = LogScrollViewer.ExtentHeight;
                var evicted = Math.Max(0, args.Received - (args.NewCount - args.PrevCount));

                if (evicted > 0 && args.PrevCount > 0 && prevExtent > 0)
                {
                    var lineHeight = prevExtent / args.PrevCount;
                    var target = Math.Max(0, prevOffset - evicted * lineHeight);
                    LogScrollViewer.ChangeView(null, target, null, disableAnimation: true);
                }
            }
        }
    }
}
