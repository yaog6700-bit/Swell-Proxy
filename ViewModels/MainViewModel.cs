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
        [ObservableProperty]
        private ElementTheme _currentTheme = ElementTheme.Default;

        [ObservableProperty]
        private Visibility _updateBadgeVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private UpdateInfo? _availableUpdate;

        [ObservableProperty]
        private string _updateBannerText = "";

        public MainViewModel()
        {
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
                var proxyUrl = CoreManager.Instance.IsRunning ? "socks5://127.0.0.1:2080" : null;
                var info = await updater.CheckAsync(proxyUrl, CancellationToken.None);

                if (info != null)
                {
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() =>
                    {
                        AvailableUpdate = info;
                        UpdateBannerText = $"🎉 发现新版客户端 {info.TagName}，去点击下方“检查更新”按钮下载！";
                        UpdateBadgeVisibility = Visibility.Visible;
                    });
                }
            }
            catch
            {
                // Silent fail for background check
            }
        }
    }
}
