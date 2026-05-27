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
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }
    }
}
