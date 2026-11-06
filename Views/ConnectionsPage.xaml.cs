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
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            this.InitializeComponent();
            ViewModel = App.Current.Services.GetRequiredService<ConnectionsViewModel>();
        }

        private void OnActualThemeChanged(Microsoft.UI.Xaml.FrameworkElement sender, object args)
        {
            foreach (var item in ViewModel.Connections) item.RefreshThemeColors();
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.ActualThemeChanged += OnActualThemeChanged;
            await ViewModel.OnNavigatedToAsync();
        }

        protected override async void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.ActualThemeChanged -= OnActualThemeChanged;
            await ViewModel.OnNavigatedFromAsync();
            this.Bindings.StopTracking();
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
