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
        private int _failedUnlockAttempts;
        private DateTime _unlockCooldownUntilUtc = DateTime.MinValue;

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

        private int _isCheckingUpdates;
        private DateTime _lastUpdateCheckUtc = DateTime.MinValue;

        public MainViewModel()
        {
            // Cache UI dispatcher at construction time — after await, GetForCurrentThread() is often null.
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                               ?? MainWindow.Instance?.DispatcherQueue;

            var session = AppSession.Instance;
            if (session.IsPrivacyModeActive && !string.IsNullOrWhiteSpace(session.PrivacyPassword))
            {
                IsPrivacyLocked = true;
            }

            // Re-check once through the proxy if startup direct check ran before proxy was connected
            CoreManager.Instance.RunningChanged += (_, isRunning) =>
            {
                if (isRunning && AvailableUpdate == null && (DateTime.UtcNow - _lastUpdateCheckUtc).TotalSeconds >= 10)
                {
                    _ = CheckForUpdatesAsync(delayMs: 1500);
                }
            };

            // Background check for app updates on startup + periodic check for long-running tray sessions
            _ = CheckForUpdatesAsync(delayMs: 2000);
            _ = RunPeriodicUpdateCheckLoopAsync();
        }

        private async Task RunPeriodicUpdateCheckLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync())
            {
                if (AvailableUpdate == null && !AppUpdateService.HasPendingUpdate)
                {
                    await CheckForUpdatesAsync(delayMs: 0);
                }
            }
        }

        private async Task CheckForUpdatesAsync(int delayMs = 0)
        {
            if (Interlocked.CompareExchange(ref _isCheckingUpdates, 1, 0) != 0)
                return;

            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                _lastUpdateCheckUtc = DateTime.UtcNow;
                var updater = new AppUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning && AppSession.Instance.EffectiveHttpProxyPort is int p && p > 0
                    ? $"http://127.0.0.1:{p}"
                    : null;
                var info = await updater.CheckAsync(proxyUrl, CancellationToken.None);

                if (info != null)
                {
                    AppUpdateService.SetAvailableUpdate(info);

                    void Apply()
                    {
                        AvailableUpdate = info;
                        UpdateBannerText = string.Format(L.Update_BannerFmt, info.TagName);
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
            finally
            {
                Interlocked.Exchange(ref _isCheckingUpdates, 0);
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

            if (string.IsNullOrWhiteSpace(session.PrivacyPassword))
            {
                // The MainWindow handles the dialog since there's no DialogService
                if (MainWindow.Instance != null)
                {
                    var newPwd = await MainWindow.Instance.ShowSetPasswordDialogAsync();
                    if (string.IsNullOrWhiteSpace(newPwd)) return;

                    var hashed = PrivacyPasswordHelper.Hash(newPwd);
                    session.PrivacyPassword = hashed;
                    LocalSettingsHelper.SetValue("privacyPassword", hashed);
                }
                else
                {
                    return;
                }
            }

            session.IsPrivacyModeActive = true;
            LocalSettingsHelper.SetValue("isPrivacyModeActive", true);
            IsPrivacyLocked = true;
            PrivacyLocked?.Invoke(this, EventArgs.Empty);
        }

        public bool Unlock(string password)
        {
            if (DateTime.UtcNow < _unlockCooldownUntilUtc)
                return false;

            var session = AppSession.Instance;
            if (PrivacyPasswordHelper.Verify(password, session.PrivacyPassword))
            {
                _failedUnlockAttempts = 0;
                _unlockCooldownUntilUtc = DateTime.MinValue;

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

            _failedUnlockAttempts++;
            if (_failedUnlockAttempts >= 5)
            {
                _unlockCooldownUntilUtc = DateTime.UtcNow.AddSeconds(15);
                _failedUnlockAttempts = 0;
            }
            return false;
        }
    }
}
