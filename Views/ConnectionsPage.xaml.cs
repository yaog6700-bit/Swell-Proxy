using Microsoft.UI.Xaml.Controls;
using AnywhereWinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AnywhereWinUI.Views
{
    public sealed partial class ConnectionsPage : Page
    {
        public ConnectionsViewModel ViewModel { get; }

        public ConnectionsPage()
        {
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();

            // Re-tint connection badges when app Light/Dark theme switches
            this.ActualThemeChanged += (_, _) =>
            {
                foreach (var item in ViewModel.Connections) item.RefreshThemeColors();
            };
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.OnNavigatedToAsync();
        }

        protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            await ViewModel.OnNavigatedFromAsync();
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.SearchText = sender.Text;
            }
        }
    }
}
