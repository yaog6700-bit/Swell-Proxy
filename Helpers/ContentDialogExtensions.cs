using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Extension helpers for ContentDialog to ensure it always renders
    /// in the correct theme (WinUI 3 dialogs do not inherit parent theme).
    /// </summary>
    public static class ContentDialogExtensions
    {
        /// <summary>
        /// Sets the dialog's RequestedTheme to match the current app theme
        /// and returns the dialog for fluent chaining.
        /// </summary>
        public static ContentDialog WithAppTheme(this ContentDialog dialog)
        {
            var theme = MainWindow.Instance?.GetActiveTheme() ?? ElementTheme.Default;
            if (theme == ElementTheme.Default)
            {
                // Resolve Default → actual system theme
                theme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
            dialog.RequestedTheme = theme;
            return dialog;
        }
    }
}
