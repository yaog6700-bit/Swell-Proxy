using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Views
{
    public sealed partial class TrafficPage : Page
    {
        public TrafficViewModel ViewModel { get; }

        public TrafficPage()
        {
            NavigationCacheMode = NavigationCacheMode.Enabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(TrafficViewModel)) as TrafficViewModel;
            this.InitializeComponent();

            // Re-render heatmap palette when app Light/Dark theme switches
            this.ActualThemeChanged += (_, _) => ViewModel?.OnPageActivated();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel?.OnPageActivated();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel?.OnPageDeactivated();
        }

        private void HeatmapModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
