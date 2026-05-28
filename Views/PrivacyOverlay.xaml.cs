using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AnywhereWinUI.Views
{
    public sealed partial class PrivacyOverlay : UserControl
    {
        public event EventHandler<string>? UnlockRequested;

        public PrivacyOverlay()
        {
            this.InitializeComponent();
        }

        private void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            Submit();
        }

        private void PasswordInput_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Submit();
            }
        }

        private void Submit()
        {
            var password = PasswordInput.Password;
            if (string.IsNullOrWhiteSpace(password)) return;

            UnlockRequested?.Invoke(this, password);
        }

        public void ShowError()
        {
            ErrorText.Visibility = Visibility.Visible;
            PasswordInput.Password = string.Empty;
            PasswordInput.Focus(FocusState.Programmatic);
        }

        public void Clear()
        {
            ErrorText.Visibility = Visibility.Collapsed;
            PasswordInput.Password = string.Empty;
        }
    }
}
