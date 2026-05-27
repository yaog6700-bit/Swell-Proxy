using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace AnywhereWinUI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ElementTheme _currentTheme = ElementTheme.Default;

        public MainViewModel()
        {
            // Initial state
        }
    }
}
