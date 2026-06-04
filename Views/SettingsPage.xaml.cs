using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;
using Windows.Storage.Pickers;
using System.Collections.Generic;

namespace AnywhereWinUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        public ViewModels.SettingsViewModel ViewModel { get; }
        public ViewModels.MainViewModel MainViewModel { get; }

        private bool _isLoaded;

        // ── Color Forwarding Properties for x:Bind ───────────────────────────
        public Color SsColor
        {
            get => ViewModel.SsColor;
            set => ViewModel.SsColor = value;
        }

        public Color VlessColor
        {
            get => ViewModel.VlessColor;
            set => ViewModel.VlessColor = value;
        }

        public Color VmessColor
        {
            get => ViewModel.VmessColor;
            set => ViewModel.VmessColor = value;
        }

        public Color Hysteria2Color
        {
            get => ViewModel.Hysteria2Color;
            set => ViewModel.Hysteria2Color = value;
        }

        public Color TrojanColor
        {
            get => ViewModel.TrojanColor;
            set => ViewModel.TrojanColor = value;
        }

        public Color FallbackColor
        {
            get => ViewModel.FallbackColor;
            set => ViewModel.FallbackColor = value;
        }

        public SettingsPage()
        {
            ViewModel = ((App)Application.Current).Services.GetService(typeof(ViewModels.SettingsViewModel)) as ViewModels.SettingsViewModel;
            MainViewModel = ((App)Application.Current).Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
            this.InitializeComponent();
            InitializeSettings();
            _isLoaded = true;
        }

        private void InitializeSettings()
        {
            var mgr = NodesManager.Instance;

            // 1. Theme selection
            var currentTheme = MainWindow.Instance?.GetActiveTheme() ?? ElementTheme.Default;
            string themeTag = currentTheme.ToString();
            for (int i = 0; i < ThemeSegmented.Items.Count; i++)
            {
                if (ThemeSegmented.Items[i] is CommunityToolkit.WinUI.Controls.SegmentedItem sItem &&
                    sItem.Tag?.ToString() == themeTag)
                {
                    ThemeSegmented.SelectedIndex = i;
                    break;
                }
            }

            // 2. Window backdrop selection
            var currentBackdrop = MainWindow.Instance?.ActiveBackdrop ?? "Mica";
            foreach (ComboBoxItem item in BackdropComboBox.Items)
            {
                if (item.Tag?.ToString() == currentBackdrop)
                {
                    BackdropComboBox.SelectedItem = item;
                    break;
                }
            }

            // 3. Initialize Core version text
            SingboxVersionText.Text = $"当前版本: {GetLocalSingboxVersionText()}";
        }

        private void CategoriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoriesListView == null) return;
            if (CategoriesListView.SelectedItem is ListViewItem selectedItem)
            {
                var tag = selectedItem.Tag?.ToString();

                if (AppearancePanel != null) AppearancePanel.Visibility = Visibility.Collapsed;
                if (ColorsPanel != null) ColorsPanel.Visibility = Visibility.Collapsed;
                if (RoutingPanel != null) RoutingPanel.Visibility = Visibility.Collapsed;
                if (DnsSettingsPanel != null) DnsSettingsPanel.Visibility = Visibility.Collapsed;
                if (TailscalePanel != null) TailscalePanel.Visibility = Visibility.Collapsed;
                if (BackupPanel != null) BackupPanel.Visibility = Visibility.Collapsed;
                if (AutostartPanel != null) AutostartPanel.Visibility = Visibility.Collapsed;
                if (AboutPanel != null) AboutPanel.Visibility = Visibility.Collapsed;

                switch (tag)
                {
                    case "Appearance":
                        if (AppearancePanel != null) AppearancePanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "个性化外观";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "自定义主题风格、背景材质与界面显示细节";
                        break;
                    case "Colors":
                        if (ColorsPanel != null) ColorsPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "协议标识";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "自定义节点协议在卡片和列表中的颜色分类";
                        break;
                    case "Routing":
                        if (RoutingPanel != null) RoutingPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "路由分流";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "配置系统级的路由绕过策略与广告拦截规则";
                        break;
                    case "DnsSettings":
                        if (DnsSettingsPanel != null) DnsSettingsPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "DNS与解析";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "配置代理环境下的远端与直连DNS防污染及FakeDNS";
                        break;
                    case "Tailscale":
                        if (TailscalePanel != null) TailscalePanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "Tailscale 组网";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "将 sing-box 接入 Tailscale 私有网络，实现内网穿透与多设备互联";
                        break;
                    case "Backup":
                        if (BackupPanel != null) BackupPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "备份与恢复";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "备份当前配置或从本地归档进行覆盖恢复";
                        break;
                    case "Autostart":
                        if (AutostartPanel != null) AutostartPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "开机与启动";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "管理客户端的自启动偏好与后台行为";
                        break;
                    case "About":
                        if (AboutPanel != null) AboutPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = "关于与内核";
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = "关于 Swell Proxy 原生客户端及代理内核管理";
                        break;
                }
            }
        }

        private void ThemeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ThemeSegmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem sItem &&
                Enum.TryParse<ElementTheme>(sItem.Tag?.ToString(), out var selectedTheme))
            {
                MainWindow.Instance?.SetTheme(selectedTheme);
                NodesManager.Instance.ThemeSetting = sItem.Tag?.ToString() ?? "Default";
                NodesManager.Instance.Save();
            }
        }

        private void BackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (BackdropComboBox.SelectedItem is ComboBoxItem item)
            {
                var backdrop = item.Tag?.ToString() ?? "Mica";
                MainWindow.Instance?.SetBackdrop(backdrop);
                NodesManager.Instance.BackdropSetting = backdrop;
                NodesManager.Instance.Save();
            }
        }

        private string GetLocalSingboxVersionText()
        {
            return Services.CoreUpdateService.GetLocalSingboxVersionText();
        }

        private void ResetColorsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetColorsCommand.Execute(null);
        }

        // ── Backup & Restore Handlers ────────────────────────────────────────

        private async void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Swell Proxy Backup", new List<string>() { ".zip" });
            picker.SuggestedFileName = $"SwellProxy_Backup_{DateTime.Now:yyyyMMdd_HHmm}";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    ExportBackupButton.IsEnabled = false;
                    ExportBackupButton.Content = "导出中...";

                    await NodesManager.Instance.ExportBackupAsync(file.Path);

                    var dialog = new ContentDialog
                    {
                        Title = "备份成功",
                        Content = $"备份文件已成功保存至：\n{file.Path}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "备份失败",
                        Content = ex.Message,
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                finally
                {
                    ExportBackupButton.IsEnabled = true;
                    ExportBackupButton.Content = "导出备份";
                }
            }
        }

        private async void ImportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmDialog = new ContentDialog
            {
                Title = "确认导入备份？",
                Content = "导入备份将彻底覆盖您当前的全部节点、订阅和个性化设置。此操作不可逆。\n\n是否继续？",
                PrimaryButtonText = "确认导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary) return;

            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".zip");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    ImportBackupButton.IsEnabled = false;
                    ImportBackupButton.Content = "导入中...";

                    await NodesManager.Instance.ImportBackupAsync(file.Path);

                    // Re-load UI states from newly imported config
                    ViewModel.LoadSettings();
                    InitializeSettings();

                    var dialog = new ContentDialog
                    {
                        Title = "恢复成功",
                        Content = "备份已成功导入！所有节点与设置已恢复至当前界面。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "恢复失败",
                        Content = ex.Message,
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                finally
                {
                    ImportBackupButton.IsEnabled = true;
                    ImportBackupButton.Content = "导入恢复";
                }
            }
        }

        // ── Sing-box Core Management Handlers ────────────────────────────────

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.Content = "正在检查...";

            try
            {
                var updater = new Services.CoreUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? "socks5://127.0.0.1:2080" : null;
                var info = await updater.CheckSingboxAsync(proxyUrl, CancellationToken.None);

                if (info == null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "内核检查更新",
                        Content = $"当前已是最新版本或网络无法访问。\n本地版本: {GetLocalSingboxVersionText()}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }

                var proxyRunning = CoreManager.Instance.IsRunning;
                var stopWarning = proxyRunning ? "\n\n⚠️ 当前代理将暂时停止，更新后请手动重新连接。" : string.Empty;

                var confirmDialog = new ContentDialog
                {
                    Title = "发现新版本",
                    Content = $"是否将内核更新至 {info.TagName}？{stopWarning}",
                    PrimaryButtonText = "确认更新",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var progressText = new TextBlock { Text = "准备下载...", Margin = new Thickness(0, 0, 0, 10) };
                var progressBar = new ProgressBar { IsIndeterminate = true, Width = 300 };
                var stackPanel = new StackPanel { Children = { progressText, progressBar } };

                var progressDialog = new ContentDialog
                {
                    Title = "正在更新内核",
                    Content = stackPanel,
                    XamlRoot = this.XamlRoot
                };

                var progress = new Progress<string>(msg =>
                {
                    DispatcherQueue.TryEnqueue(() => progressText.Text = msg);
                });

                var updateTask = updater.UpdateAsync(info, proxyUrl, progress, CancellationToken.None, async () => 
                {
                    if (proxyRunning)
                    {
                        await CoreManager.Instance.StopAsync();
                        await Task.Delay(500);
                    }
                });
                
                // Show dialog without awaiting it to allow background updates
                _ = progressDialog.ShowAsync();
                
                try
                {
                    await updateTask;
                    progressDialog.Hide();
                    await Task.Delay(50);

                    SingboxVersionText.Text = $"当前版本: {GetLocalSingboxVersionText()}";

                    var reconnectNote = proxyRunning ? "\n\n代理已停止，请返回主界面重新连接。" : string.Empty;
                    var successDialog = new ContentDialog
                    {
                        Title = "更新完成",
                        Content = $"已成功更新至 {info.TagName}。{reconnectNote}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    progressDialog.Hide();
                    await Task.Delay(50);
                    var errDialog = new ContentDialog
                    {
                        Title = "更新失败",
                        Content = ex.Message,
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "检查更新失败",
                    Content = $"错误信息: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.Content = "检查更新";
            }
        }

        private async void CheckAppUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckAppUpdateButton.IsEnabled = false;
            CheckAppUpdateButton.Content = "正在检查...";

            try
            {
                var updater = new AppUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? "socks5://127.0.0.1:2080" : null;
                var info = await updater.CheckAsync(proxyUrl, CancellationToken.None);

                if (info == null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "客户端检查更新",
                        Content = $"当前客户端已是最新版本或网络无法访问。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }

                var confirmDialog = new ContentDialog
                {
                    Title = "发现新版本",
                    Content = $"是否将客户端更新至 {info.TagName}？\n这将会下载最新版本并自动重启应用。",
                    PrimaryButtonText = "确认更新",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var progressText = new TextBlock { Text = "准备下载...", Margin = new Thickness(0, 0, 0, 10) };
                var progressBar = new ProgressBar { IsIndeterminate = true, Width = 300 };
                var stackPanel = new StackPanel { Children = { progressText, progressBar } };

                var progressDialog = new ContentDialog
                {
                    Title = "正在更新客户端",
                    Content = stackPanel,
                    XamlRoot = this.XamlRoot
                };

                var progress = new Progress<AnywhereWinUI.Models.ProgressDialogUpdate>(msg =>
                {
                    DispatcherQueue.TryEnqueue(() => {
                        progressText.Text = msg.StatusText;
                        if (msg.PercentComplete.HasValue)
                        {
                            progressBar.IsIndeterminate = false;
                            progressBar.Value = msg.PercentComplete.Value;
                        }
                    });
                });

                var updateTask = updater.DownloadVerifyAndExtractAsync(info, proxyUrl, progress, CancellationToken.None);
                
                _ = progressDialog.ShowAsync();
                
                try
                {
                    var staging = await updateTask;
                    progressDialog.Hide();
                    await Task.Delay(50);

                    var successDialog = new ContentDialog
                    {
                        Title = "更新准备就绪",
                        Content = $"新版本 {info.TagName} 下载并校验成功！\n\n点击“立即重启”后，当前应用将关闭并完成更新覆盖。",
                        PrimaryButtonText = "立即重启",
                        XamlRoot = this.XamlRoot
                    };
                    
                    if (await successDialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        if (CoreManager.Instance.IsRunning)
                        {
                            await CoreManager.Instance.StopAsync();
                        }
                        updater.LaunchUpdater(staging);
                        Application.Current.Exit();
                    }
                }
                catch (Exception ex)
                {
                    progressDialog.Hide();
                    await Task.Delay(50);
                    var errDialog = new ContentDialog
                    {
                        Title = "更新失败",
                        Content = ex.Message,
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "检查更新失败",
                    Content = $"错误信息: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                CheckAppUpdateButton.IsEnabled = true;
                CheckAppUpdateButton.Content = "检查更新";
            }
        }

        private async void ReplaceCoreButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var isRunning = CoreManager.Instance.IsRunning;
            if (isRunning)
            {
                await CoreManager.Instance.StopAsync();
                await Task.Delay(500);
            }

            try
            {
                var targetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sing-box.exe");

                var dir = Path.GetDirectoryName(targetPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(file.Path, targetPath, overwrite: true);

                var ver = GetLocalSingboxVersionText();
                SingboxVersionText.Text = $"当前版本: {ver}";

                var dialog = new ContentDialog
                {
                    Title = "内核替换成功",
                    Content = $"已成功替换内核驱动文件为:\n{file.Name}\n\n请返回仪表盘重新连接代理以应用最新内核。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "内核替换失败",
                    Content = $"覆盖内核文件时发生错误（可能文件被系统锁定占用）：\n{ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private async void ResetOnboardingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AnywhereWinUI.Helpers.LocalSettingsHelper.SetValue("onboardingCompleted", false);

                // 立即弹出引导页面
                MainWindow.Instance?.ShowOnboarding();

                var dialog = new ContentDialog
                {
                    Title = "重置成功",
                    Content = "新手引导已重置并唤起！",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = "发生错误",
                    Content = ex.ToString(),
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errDialog.ShowAsync();
            }
        }

        private async void UpdateGeoButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateGeoButton.IsEnabled = false;
            var originalContent = UpdateGeoButton.Content;
            UpdateGeoButton.Content = new ProgressRing { IsActive = true, Width = 16, Height = 16, Margin = new Thickness(0, 0, 0, 0) };

            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwellProxy");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var http = new System.Net.Http.HttpClient();
                
                var geositeUrl = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-cn.srs";
                var geositeBytes = await http.GetByteArrayAsync(geositeUrl);
                await File.WriteAllBytesAsync(Path.Combine(dir, "geosite-cn.srs"), geositeBytes);

                var geoipUrl = "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-cn.srs";
                var geoipBytes = await http.GetByteArrayAsync(geoipUrl);
                await File.WriteAllBytesAsync(Path.Combine(dir, "geoip-cn.srs"), geoipBytes);

                var adsUrl = "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ads-all.srs";
                var adsBytes = await http.GetByteArrayAsync(adsUrl);
                await File.WriteAllBytesAsync(Path.Combine(dir, "geosite-category-ads-all.srs"), adsBytes);

                var dialog = new ContentDialog
                {
                    Title = "Geo 更新成功",
                    Content = "最新路由数据 (geosite-cn.srs, geoip-cn.srs, geosite-category-ads-all.srs) 已经更新完成！\n若目前正在连接状态，请在仪表盘重新连接代理以使最新规则生效。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = "Geo 更新失败",
                    Content = "下载数据时发生错误 (请检查网络): \n" + ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errDialog.ShowAsync();
            }
            finally
            {
                UpdateGeoButton.Content = originalContent;
                UpdateGeoButton.IsEnabled = true;
            }
        }

        // ── Category Icons Micro-Animations ──────────────────────────────────────────────

        // 🎨 Appearance: Paintbrush Sway
        private void AppearanceItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAppearance == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAppearance);
            var compositor = visual.Compositor;

            float cx = IconAppearance.ActualWidth > 0 ? (float)(IconAppearance.ActualWidth / 2) : 8f;
            float cy = IconAppearance.ActualHeight > 0 ? (float)(IconAppearance.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var sway = compositor.CreateScalarKeyFrameAnimation();
            sway.InsertKeyFrame(0f, 0f);
            sway.InsertKeyFrame(0.25f, -22f); // Sway left
            sway.InsertKeyFrame(0.5f, 18f);  // Sway right
            sway.InsertKeyFrame(0.75f, -8f);
            sway.InsertKeyFrame(1.0f, 0f);
            sway.Duration = TimeSpan.FromMilliseconds(650);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0.5f, 1.25f);
            scaleX.InsertKeyFrame(1.0f, 1.15f);
            scaleX.Duration = TimeSpan.FromMilliseconds(650);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0.5f, 1.25f);
            scaleY.InsertKeyFrame(1.0f, 1.15f);
            scaleY.Duration = TimeSpan.FromMilliseconds(650);

            visual.StartAnimation("RotationAngleInDegrees", sway);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void AppearanceItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAppearance == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAppearance);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 0f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
            visual.StartAnimation("RotationAngleInDegrees", rot);
        }

        // 🎨 Colors: Palette Bounce & Pulse
        private void ColorsItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconColors == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconColors);
            var compositor = visual.Compositor;

            float cx = IconColors.ActualWidth > 0 ? (float)(IconColors.ActualWidth / 2) : 8f;
            float cy = IconColors.ActualHeight > 0 ? (float)(IconColors.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0f, 1f);
            scaleX.InsertKeyFrame(0.4f, 1.3f);
            scaleX.InsertKeyFrame(0.7f, 0.9f);
            scaleX.InsertKeyFrame(1.0f, 1.15f);
            scaleX.Duration = TimeSpan.FromMilliseconds(450);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0f, 1f);
            scaleY.InsertKeyFrame(0.4f, 0.8f);
            scaleY.InsertKeyFrame(0.7f, 1.2f);
            scaleY.InsertKeyFrame(1.0f, 1.15f);
            scaleY.Duration = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void ColorsItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconColors == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconColors);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        // 🛣 Routing: Animated Route
        private void RoutingItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconRouting == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconRouting);
            var compositor = visual.Compositor;

            float cx = IconRouting.ActualWidth > 0 ? (float)(IconRouting.ActualWidth / 2) : 8f;
            float cy = IconRouting.ActualHeight > 0 ? (float)(IconRouting.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(0f, 0f);
            rot.InsertKeyFrame(0.5f, 15f);
            rot.InsertKeyFrame(1.0f, 0f);
            rot.Duration = TimeSpan.FromMilliseconds(450);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0f, 1f);
            scaleX.InsertKeyFrame(0.5f, 1.2f);
            scaleX.InsertKeyFrame(1.0f, 1.15f);
            scaleX.Duration = TimeSpan.FromMilliseconds(450);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0f, 1f);
            scaleY.InsertKeyFrame(0.5f, 1.2f);
            scaleY.InsertKeyFrame(1.0f, 1.15f);
            scaleY.Duration = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
            visual.StartAnimation("RotationAngleInDegrees", rot);
        }

        private void RoutingItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconRouting == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconRouting);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 0f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
            visual.StartAnimation("RotationAngleInDegrees", rot);
        }

        // 💾 Backup: Cloud Storage Spring
        private void BackupItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconBackup == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconBackup);
            var compositor = visual.Compositor;

            float cx = IconBackup.ActualWidth > 0 ? (float)(IconBackup.ActualWidth / 2) : 8f;
            float cy = IconBackup.ActualHeight > 0 ? (float)(IconBackup.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0f, 1f);
            scaleX.InsertKeyFrame(0.4f, 1.12f);
            scaleX.InsertKeyFrame(0.7f, 0.95f);
            scaleX.InsertKeyFrame(1.0f, 1.15f);
            scaleX.Duration = TimeSpan.FromMilliseconds(450);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0f, 1f);
            scaleY.InsertKeyFrame(0.4f, 1.35f); // Stretch vertically like cloud floating up
            scaleY.InsertKeyFrame(0.7f, 0.88f);
            scaleY.InsertKeyFrame(1.0f, 1.15f);
            scaleY.Duration = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void BackupItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconBackup == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconBackup);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        // 🔌 Autostart: Plug/Power Spin
        private void AutostartItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAutostart == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAutostart);
            var compositor = visual.Compositor;

            float cx = IconAutostart.ActualWidth > 0 ? (float)(IconAutostart.ActualWidth / 2) : 8f;
            float cy = IconAutostart.ActualHeight > 0 ? (float)(IconAutostart.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeInOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.42f, 0f), new System.Numerics.Vector2(0.58f, 1f));

            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
            rotAnim.InsertKeyFrame(1f, 360f, easeInOut);
            rotAnim.Duration = TimeSpan.FromMilliseconds(700);

            var scaleAnimX = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimX.InsertKeyFrame(0.5f, 1.25f);
            scaleAnimX.InsertKeyFrame(1f, 1.15f);
            scaleAnimX.Duration = TimeSpan.FromMilliseconds(700);

            var scaleAnimY = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimY.InsertKeyFrame(0.5f, 1.25f);
            scaleAnimY.InsertKeyFrame(1f, 1.15f);
            scaleAnimY.Duration = TimeSpan.FromMilliseconds(700);

            visual.StartAnimation("RotationAngleInDegrees", rotAnim);
            visual.StartAnimation("Scale.X", scaleAnimX);
            visual.StartAnimation("Scale.Y", scaleAnimY);
        }

        private void DnsSettingsItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconDnsSettings == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconDnsSettings);
            var compositor = visual.Compositor;

            float cx = IconDnsSettings.ActualWidth > 0 ? (float)(IconDnsSettings.ActualWidth / 2) : 8f;
            float cy = IconDnsSettings.ActualHeight > 0 ? (float)(IconDnsSettings.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 15f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(400);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1.25f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(400);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1.25f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(400);

            visual.StartAnimation("RotationAngleInDegrees", rot);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void DnsSettingsItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconDnsSettings == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconDnsSettings);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 0f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(250);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("RotationAngleInDegrees", rot);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void AutostartItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAutostart == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAutostart);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
            rotAnim.InsertKeyFrame(1f, 0f, easeOut);
            rotAnim.Duration = TimeSpan.FromMilliseconds(300);

            var scaleAnimX = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimX.InsertKeyFrame(1f, 1f, easeOut);
            scaleAnimX.Duration = TimeSpan.FromMilliseconds(300);

            var scaleAnimY = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimY.InsertKeyFrame(1f, 1f, easeOut);
            scaleAnimY.Duration = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("RotationAngleInDegrees", rotAnim);
            visual.StartAnimation("Scale.X", scaleAnimX);
            visual.StartAnimation("Scale.Y", scaleAnimY);
        }

        // ℹ️ About: Info Circle Twist
        private void AboutItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAbout == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAbout);
            var compositor = visual.Compositor;

            float cx = IconAbout.ActualWidth > 0 ? (float)(IconAbout.ActualWidth / 2) : 8f;
            float cy = IconAbout.ActualHeight > 0 ? (float)(IconAbout.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 90f, easeOut); // Twist 90 degrees elegantly
            rot.Duration = TimeSpan.FromMilliseconds(400);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1.25f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(400);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1.25f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(400);

            visual.StartAnimation("RotationAngleInDegrees", rot);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void AboutItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconAbout == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconAbout);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 0f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(250);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("RotationAngleInDegrees", rot);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void SetDirectDnsAli_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DirectDns = "223.5.5.5";
        }

        private void SetDirectDnsTencent_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DirectDns = "119.29.29.29";
        }

        private void SetDirectDns114_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DirectDns = "114.114.114.114";
        }

        private void SetDirectDnsDoH_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DirectDns = "https://dns.alidns.com/dns-query";
        }

        private void SetProxyDnsGoogle_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProxyDns = "8.8.8.8";
        }

        private void SetProxyDnsCF_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProxyDns = "1.1.1.1";
        }

        private void SetProxyDnsQuad9_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProxyDns = "9.9.9.9";
        }

        private void SetProxyDnsDoH_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ProxyDns = "https://1.1.1.1/dns-query";
        }

        // ── Tailscale Handlers ────────────────────────────────────────────────

        private void SetTailscaleControlUrlOfficial_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TailscaleControlUrl = string.Empty;
        }

        private void ClearTailscaleControlUrl_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.TailscaleControlUrl = string.Empty;
        }

        private async void TailscaleStateDirBrowse_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.TailscaleStateDirectory = folder.Path;
            }
        }

        // 🌐 Tailscale: Pulse & Scale Animation
        private void TailscaleItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconTailscale == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconTailscale);
            var compositor = visual.Compositor;

            float cx = IconTailscale.ActualWidth > 0 ? (float)(IconTailscale.ActualWidth / 2) : 8f;
            float cy = IconTailscale.ActualHeight > 0 ? (float)(IconTailscale.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0f, 1f);
            scaleX.InsertKeyFrame(0.35f, 1.35f);
            scaleX.InsertKeyFrame(0.65f, 0.9f);
            scaleX.InsertKeyFrame(1.0f, 1.15f);
            scaleX.Duration = TimeSpan.FromMilliseconds(500);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0f, 1f);
            scaleY.InsertKeyFrame(0.35f, 1.35f);
            scaleY.InsertKeyFrame(0.65f, 0.9f);
            scaleY.InsertKeyFrame(1.0f, 1.15f);
            scaleY.Duration = TimeSpan.FromMilliseconds(500);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void TailscaleItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (IconTailscale == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(IconTailscale);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }
    }
}
