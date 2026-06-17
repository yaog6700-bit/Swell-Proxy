using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using AnywhereWinUI.Services;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public event EventHandler? PrivacyLocked;

        [ObservableProperty]
        private bool _isPrivacyLocked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PrivacyOverlayVisibility))]
        [NotifyPropertyChangedFor(nameof(MainContentVisibility))]
        private ElementTheme _currentTheme = ElementTheme.Default;

        public Visibility PrivacyOverlayVisibility => IsPrivacyLocked ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MainContentVisibility => IsPrivacyLocked ? Visibility.Collapsed : Visibility.Visible;

        [ObservableProperty]
        private Visibility _updateBadgeVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private UpdateInfo? _availableUpdate;

        [ObservableProperty]
        private string _updateBannerText = "";

        public MainViewModel()
        {
            var session = AppSession.Instance;
            if (session.IsPrivacyModeActive && !string.IsNullOrEmpty(session.PrivacyPassword))
            {
                IsPrivacyLocked = true;
            }

            // Background check for app updates on startup
            _ = CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                // Delay slightly to not block startup
                await Task.Delay(2000);
                
                var updater = new AppUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? $"socks5://127.0.0.1:{AppSession.Instance.MixedPort}" : null;
                var info = await updater.CheckAsync(proxyUrl, CancellationToken.None);

                if (info != null)
                {
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
                    {
                        AvailableUpdate = info;
                        UpdateBannerText = $"🎉 发现新版客户端 {info.TagName}！点击下方的“检查更新”按钮即可获取最新功能与修复体验。";
                        UpdateBadgeVisibility = Visibility.Visible;
                    });
                }
            }
            catch
            {
                // Silent fail for background check
            }
        }

        partial void OnIsPrivacyLockedChanged(bool value)
        {
            OnPropertyChanged(nameof(PrivacyOverlayVisibility));
            OnPropertyChanged(nameof(MainContentVisibility));
        }

        public async Task TogglePrivacyModeAsync()
        {
            var session = AppSession.Instance;
            
            if (IsPrivacyLocked)
            {
                return;
            }

            if (string.IsNullOrEmpty(session.PrivacyPassword))
            {
                // The MainWindow handles the dialog since there's no DialogService
                if (MainWindow.Instance != null)
                {
                    var newPwd = await MainWindow.Instance.ShowSetPasswordDialogAsync();
                    if (string.IsNullOrEmpty(newPwd)) return;

                    session.PrivacyPassword = newPwd;
                    Helpers.LocalSettingsHelper.SetValue("privacyPassword", newPwd);
                }
            }

            session.IsPrivacyModeActive = true;
            Helpers.LocalSettingsHelper.SetValue("isPrivacyModeActive", true);
            IsPrivacyLocked = true;
            PrivacyLocked?.Invoke(this, EventArgs.Empty);
        }

        public bool Unlock(string password)
        {
            var session = AppSession.Instance;
            if (session.PrivacyPassword == password)
            {
                IsPrivacyLocked = false;
                return true;
            }
            return false;
        }
    }
}
