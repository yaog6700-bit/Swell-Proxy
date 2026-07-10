using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using AnywhereWinUI.Models;

namespace AnywhereWinUI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public event EventHandler? PrivacyLocked;

        private readonly DispatcherQueue? _dispatcherQueue;

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
            // Cache UI dispatcher at construction time — after await, GetForCurrentThread() is often null.
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;

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
                    void Apply()
                    {
                        AvailableUpdate = info;
                        UpdateBannerText = $"🎉 发现新版客户端 {info.TagName}！点击下方的“检查更新”按钮即可获取最新功能与修复体验。";
                        UpdateBadgeVisibility = Visibility.Visible;
                    }

                    var dq = _dispatcherQueue ?? MainWindow.Instance?.DispatcherQueue;
                    if (dq != null)
                        dq.TryEnqueue(Apply);
                    else
                        Apply();
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

                    var hashed = PrivacyPasswordHelper.Hash(newPwd);
                    session.PrivacyPassword = hashed;
                    LocalSettingsHelper.SetValue("privacyPassword", hashed);
                }
            }

            session.IsPrivacyModeActive = true;
            LocalSettingsHelper.SetValue("isPrivacyModeActive", true);
            IsPrivacyLocked = true;
            PrivacyLocked?.Invoke(this, EventArgs.Empty);
        }

        public bool Unlock(string password)
        {
            var session = AppSession.Instance;
            if (PrivacyPasswordHelper.Verify(password, session.PrivacyPassword))
            {
                // If still legacy plaintext (edge case), migrate on successful unlock
                if (!PrivacyPasswordHelper.IsHashed(session.PrivacyPassword))
                {
                    var hashed = PrivacyPasswordHelper.Hash(password);
                    session.PrivacyPassword = hashed;
                    LocalSettingsHelper.SetValue("privacyPassword", hashed);
                }

                IsPrivacyLocked = false;
                return true;
            }
            return false;
        }
    }
}
