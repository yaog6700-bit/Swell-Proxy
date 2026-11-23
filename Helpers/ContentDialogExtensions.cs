using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Extension helpers for ContentDialog, pages, and floating controls to ensure
    /// C#-constructed UI elements always resolve accurate Windows 11 Fluent 2 Light/Dark
    /// semantic brushes even when in-app theme differs from Windows OS startup theme.
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
        /// Safely resolves whether <paramref name="element"/> is rendering in Dark theme.
        /// Before a FrameworkElement is attached to the visual tree (<c>IsLoaded == false</c>),
        /// WinUI 3's <c>ActualTheme</c> reflects OS <c>Application.RequestedTheme</c> rather than
        /// <c>MainWindow</c>'s active theme, so we fall back to <see cref="IsDarkThemeActive"/> until loaded.
        /// </summary>
        public static bool IsElementDark(FrameworkElement? element)
        {
            if (element is { IsLoaded: true })
            {
                if (element.ActualTheme == ElementTheme.Dark) return true;
                if (element.ActualTheme == ElementTheme.Light) return false;
            }
            return IsDarkThemeActive();
        }

        /// <summary>
        /// Resolves a WinUI 3 Fluent 2 semantic <see cref="Brush"/> for the requested Light or Dark theme
        /// without accidentally leaking OS-theme brushes when the user toggles Light/Dark inside the app.
        /// </summary>
        public static Brush LookupThemeBrush(string key, bool wantDark, Color fallback)
        {
            var appResources = Application.Current?.Resources;
            if (appResources != null)
            {
                // 1. Check App.xaml's outer ThemeDictionaries ("Dark" = Dark, "Light"/"Default" = Light)
                if (appResources.ThemeDictionaries != null)
                {
                    if (TryReadBrushFromThemeDict(appResources.ThemeDictionaries, wantDark ? "Dark" : "Light", key, out var outerMatch) ||
                        (!wantDark && TryReadBrushFromThemeDict(appResources.ThemeDictionaries, "Default", key, out outerMatch)))
                    {
                        return outerMatch!;
                    }
                }

                // 2. Check MergedDictionaries (XamlControlsResources uses "Light" for Light, "Dark"/"Default" for Dark)
                if (appResources.MergedDictionaries != null)
                {
                    foreach (var merged in appResources.MergedDictionaries)
                    {
                        if (merged?.ThemeDictionaries == null) continue;
                        if (wantDark)
                        {
                            if (TryReadBrushFromThemeDict(merged.ThemeDictionaries, "Dark", key, out var mDark) ||
                                TryReadBrushFromThemeDict(merged.ThemeDictionaries, "Default", key, out mDark))
                            {
                                return mDark!;
                            }
                        }
                        else
                        {
                            if (TryReadBrushFromThemeDict(merged.ThemeDictionaries, "Light", key, out var mLight))
                            {
                                return mLight!;
                            }
                        }
                    }
                }

                // 3. Only consult Application.Current.Resources[key] when Application.RequestedTheme matches wantDark!
                // Otherwise WinUI returns the OS startup theme brush (e.g. White text in Light mode when OS is Dark).
                bool osStartupIsDark = Application.Current!.RequestedTheme == ApplicationTheme.Dark;
                if (osStartupIsDark == wantDark &&
                    appResources.TryGetValue(key, out var directObj) &&
                    TryCoerceBrush(directObj, out var directBrush))
                {
                    return directBrush!;
                }
            }

            // 4. Official Windows 11 WinUI 3 Fluent 2 Light/Dark semantic palette
            if (TryGetStandardFluentBrush(key, wantDark, out var fluentBrush))
            {
                return fluentBrush!;
            }

            return new SolidColorBrush(fallback);
        }

        private static bool TryReadBrushFromThemeDict(
            System.Collections.Generic.IDictionary<object, object> themeDicts,
            string themeKey,
            string brushKey,
            out Brush? brush)
        {
            brush = null;
            if (themeDicts.TryGetValue(themeKey, out var dictObj) &&
                dictObj is ResourceDictionary rd &&
                rd.TryGetValue(brushKey, out var val) &&
                TryCoerceBrush(val, out brush))
            {
                return true;
            }
            return false;
        }

        private static bool TryCoerceBrush(object? value, out Brush? brush)
        {
            if (value is Brush b)
            {
                brush = b;
                return true;
            }
            if (value is Color c)
            {
                brush = new SolidColorBrush(c);
                return true;
            }
            brush = null;
            return false;
        }

        private static bool TryGetStandardFluentBrush(string key, bool wantDark, out Brush? brush)
        {
            Color? c = key switch
            {
                "TextFillColorPrimaryBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0xE4, 0x00, 0x00, 0x00),
                "TextFillColorSecondaryBrush" => wantDark
                    ? Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x9E, 0x00, 0x00, 0x00),
                "TextFillColorTertiaryBrush" => wantDark
                    ? Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x72, 0x00, 0x00, 0x00),
                "TextFillColorDisabledBrush" => wantDark
                    ? Color.FromArgb(0x5D, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x5C, 0x00, 0x00, 0x00),
                "DividerStrokeColorDefaultBrush" => wantDark
                    ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x0F, 0x00, 0x00, 0x00),
                "CardStrokeColorDefaultBrush" => wantDark
                    ? Color.FromArgb(0x19, 0x00, 0x00, 0x00)
                    : Color.FromArgb(0x0F, 0x00, 0x00, 0x00),
                "CardBackgroundFillColorDefaultBrush" => wantDark
                    ? Color.FromArgb(0x80, 0x20, 0x20, 0x20)
                    : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF),
                "SubtleFillColorSecondaryBrush" => wantDark
                    ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x09, 0x00, 0x00, 0x00),
                "SystemFillColorSuccessBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F)
                    : Color.FromArgb(0xFF, 0x0F, 0x7B, 0x0F),
                "SystemFillColorCautionBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00)
                    : Color.FromArgb(0xFF, 0x9D, 0x5D, 0x00),
                "SystemFillColorCriticalBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4)
                    : Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C),
                "SystemFillColorNeutralBrush" => wantDark
                    ? Color.FromArgb(0x87, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x72, 0x00, 0x00, 0x00),
                "SystemFillColorAttentionBackgroundBrush" => wantDark
                    ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x80, 0xF6, 0xF6, 0xF6),
                "AccentFillColorDefaultBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0x60, 0xCD, 0xFF)
                    : Color.FromArgb(0xFF, 0x00, 0x5F, 0xB8),
                "AccentTextFillColorPrimaryBrush" => wantDark
                    ? Color.FromArgb(0xFF, 0x99, 0xEB, 0xFF)
                    : Color.FromArgb(0xFF, 0x00, 0x3E, 0x92),
                _ => null
            };

            if (c.HasValue)
            {
                brush = new SolidColorBrush(c.Value);
                return true;
            }

            brush = null;
            return false;
        }

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
