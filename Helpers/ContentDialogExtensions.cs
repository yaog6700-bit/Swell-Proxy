using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Extension helpers for ContentDialog and floating windows/converters to ensure
    /// they always render in the active app theme (WinUI 3 dialogs/windows do not inherit parent theme).
    /// </summary>
    public static class ContentDialogExtensions
    {
        /// <summary>
        /// Resolves the effective Light or Dark <see cref="ElementTheme"/> currently active in the app.
        /// </summary>
        public static ElementTheme ResolveAppTheme()
        {
            var theme = MainWindow.Instance?.GetActiveTheme() ?? ElementTheme.Default;
            if (theme == ElementTheme.Default)
            {
                theme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
            return theme;
        }

        /// <summary>
        /// Returns true if the effective active app theme is Dark.
        /// </summary>
        public static bool IsDarkThemeActive() => ResolveAppTheme() == ElementTheme.Dark;

        /// <summary>
        /// Sets the dialog's RequestedTheme to match the current app theme
        /// and returns the dialog for fluent chaining.
        /// </summary>
        public static ContentDialog WithAppTheme(this ContentDialog dialog)
        {
            dialog.RequestedTheme = ResolveAppTheme();
            return dialog;
        }
    }
}
