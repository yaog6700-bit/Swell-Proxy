using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using AnywhereWinUI.Models;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Views
{
    public sealed partial class LogsPage : Page
    {
        public LogsViewModel ViewModel { get; }

        public LogsPage()
        {
            NavigationCacheMode = NavigationCacheMode.Disabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(LogsViewModel)) as LogsViewModel;
            this.InitializeComponent();

            // Re-tint level badges when app Light/Dark theme switches
            this.ActualThemeChanged += OnActualThemeChanged;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ViewModel != null)
            {
                ViewModel.LogFlushed -= OnLogFlushed;
                ViewModel.LogFlushed += OnLogFlushed;
                ViewModel.SetPageActive(true);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.ActualThemeChanged -= OnActualThemeChanged;
            if (ViewModel != null)
            {
                ViewModel.LogFlushed -= OnLogFlushed;
                ViewModel.SetPageActive(false);
            }
            this.Bindings.StopTracking();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            if (ViewModel == null) return;
            foreach (var entry in ViewModel.LogEntries) entry.RefreshTheme();
        }

        private void OnLogFlushed(object? sender, (int Received, int PrevCount, int NewCount) args)
        {
            if (ViewModel == null || !ViewModel.AutoScroll || ViewModel.LogEntries.Count == 0)
                return;

            LogListView.ScrollIntoView(ViewModel.LogEntries[^1]);
        }

        private void CopySingleLogLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is LogEntry entry)
            {
                var dp = new DataPackage();
                dp.SetText(string.IsNullOrEmpty(entry.Time)
                    ? $"[{entry.Level}] {entry.Message}"
                    : $"{entry.Time} [{entry.Level}] {entry.Message}");
                Clipboard.SetContent(dp);
            }
        }
    }
}
