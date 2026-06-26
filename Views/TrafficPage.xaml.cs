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
            ViewModel = ((App)Application.Current).Services.GetService(typeof(TrafficViewModel)) as TrafficViewModel;
            this.InitializeComponent();
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

        private void HeatmapDailyBtn_Checked(object sender, RoutedEventArgs e)
            => ViewModel?.SetHeatmapMode(HeatmapViewMode.Daily);

        private void HeatmapWeeklyBtn_Checked(object sender, RoutedEventArgs e)
            => ViewModel?.SetHeatmapMode(HeatmapViewMode.Weekly);

        private void HeatmapCumulativeBtn_Checked(object sender, RoutedEventArgs e)
            => ViewModel?.SetHeatmapMode(HeatmapViewMode.Cumulative);
    }
}
