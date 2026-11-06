using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.Globalization;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Represents a selectable UI language entry in Settings.
    /// Real language rows keep their native endonym ("简体中文", "English") so users
    /// can always recognize their own language regardless of the active locale,
    /// while the "Follow System" option resolves dynamically via <see cref="Loc"/>.
    /// </summary>
    public sealed class LanguageInfo
    {
        private readonly string _displayName;
        private readonly string? _resourceKey;

        public string? Tag { get; }

        public string DisplayName =>
            _resourceKey is null ? _displayName : Loc.GetString(_resourceKey);

        public LanguageInfo(string? tag, string displayName, string? resourceKey = null)
        {
            Tag = tag;
            _displayName = displayName;
            _resourceKey = resourceKey;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Manages UI language persistence and WinAppSDK <see cref="ApplicationLanguages.PrimaryLanguageOverride"/>.
    /// Must be initialized at the very start of <c>App()</c> before <c>InitializeComponent()</c>.
    /// </summary>
    public static class LanguageHelper
    {
        public const string SettingsKey = "appLanguage";

        public static readonly LanguageInfo[] SupportedLanguages =
        [
            new(null,    "跟随系统", resourceKey: "Language_FollowSystem"),
            new("zh-CN", "简体中文"),
            new("en-US", "English"),
        ];

        /// <summary>
        /// The concrete language code ("zh-CN" or "en-US") active for the current process session.
        /// </summary>
        public static string EffectiveLanguage { get; private set; } = "zh-CN";

        /// <summary>
        /// The persisted language tag at process startup (<c>null</c> = Follow System).
        /// Used by SettingsPage to detect whether a restart is required.
        /// </summary>
        public static string? StartupTag { get; private set; }

        public static string? GetSavedTag()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                return Normalize(raw);
            }
            return null;
        }

        public static void SaveTag(string? tag)
        {
            var normalized = Normalize(tag);
            LocalSettingsHelper.SetValue(SettingsKey, normalized ?? string.Empty);
        }

        public static string? Normalize(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            foreach (var lang in SupportedLanguages)
            {
                if (lang.Tag is not null &&
                    string.Equals(lang.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return lang.Tag;
                }
            }
            return null;
        }

        public static int IndexOf(string? tag)
        {
            var normalized = Normalize(tag);
            if (normalized is null) return 0;
            for (int i = 0; i < SupportedLanguages.Length; i++)
            {
                if (string.Equals(SupportedLanguages[i].Tag, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        public static string? TagAt(int index)
            => (uint)index < (uint)SupportedLanguages.Length
                ? SupportedLanguages[index].Tag
                : null;

        public static string ResolveSystemLanguage()
        {
            try
            {
                var osLanguages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
                if (osLanguages != null && osLanguages.Count > 0)
                {
                    var primary = osLanguages[0];
                    if (!string.IsNullOrEmpty(primary))
                    {
                        return primary.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
                    }
                }
            }
            catch { }

            try
            {
                var ui = CultureInfo.InstalledUICulture.Name;
                if (!string.IsNullOrEmpty(ui))
                {
                    return ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
                }
            }
            catch { }

            return "zh-CN";
        }

        /// <summary>
        /// Reads saved preference and applies <see cref="ApplicationLanguages.PrimaryLanguageOverride"/>
        /// before any XAML or MRT ResourceManager initialization occurs.
        /// </summary>
        public static void InitializeFromSettings()
        {
            StartupTag = GetSavedTag();
            ApplyOverride(StartupTag);
        }

        public static void ApplyOverride(string? tag)
        {
            var normalized = Normalize(tag);
            EffectiveLanguage = normalized ?? ResolveSystemLanguage();

            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = EffectiveLanguage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Language] Failed to set PrimaryLanguageOverride '{EffectiveLanguage}': {ex.Message}");
            }

            try
            {
                var culture = new CultureInfo(EffectiveLanguage);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch { }
        }
    }
}

