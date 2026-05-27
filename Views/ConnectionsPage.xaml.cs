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
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
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
