using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Views
{
    public sealed partial class TrafficPage : Page
    {
        public TrafficViewModel ViewModel { get; }

        private bool _isSyncingMode;

        public TrafficPage()
        {
            NavigationCacheMode = NavigationCacheMode.Disabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(TrafficViewModel)) as TrafficViewModel;
            this.InitializeComponent();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            ViewModel?.OnPageActivated();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.ActualThemeChanged += OnActualThemeChanged;

            if (ViewModel != null && HeatmapModeSegmented != null)
            {
                _isSyncingMode = true;
                HeatmapModeSegmented.SelectedIndex = (int)ViewModel.CurrentHeatmapMode;
                _isSyncingMode = false;
            }

            ViewModel?.OnPageActivated();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.ActualThemeChanged -= OnActualThemeChanged;
            ViewModel?.OnPageDeactivated();
            this.Bindings.StopTracking();
        }

        private void HeatmapModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingMode) return;
            if (sender is not CommunityToolkit.WinUI.Controls.Segmented seg) return;
            var mode = seg.SelectedIndex switch
            {
                1 => HeatmapViewMode.Weekly,
                2 => HeatmapViewMode.Cumulative,
                _ => HeatmapViewMode.Daily
            };
            ViewModel?.SetHeatmapMode(mode);
        }
    }
}
