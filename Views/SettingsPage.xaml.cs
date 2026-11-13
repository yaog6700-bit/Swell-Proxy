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
using System.Collections.Generic;

namespace AnywhereWinUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        public ViewModels.SettingsViewModel ViewModel { get; }
        public ViewModels.MainViewModel MainViewModel { get; }

        private bool _isLoaded;
        private bool _suppressEngineSelectionChanged;

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
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(ViewModels.SettingsViewModel)) as ViewModels.SettingsViewModel;
            MainViewModel = ((App)Application.Current).Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
            this.InitializeComponent();
            InitializeSettings();
            _isLoaded = true;
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.Bindings.StopTracking();
        }

        private void InitializeSettings()
        {
            var mgr = NodesManager.Instance;

            // 0. Display language selection
            LanguageComboBox.ItemsSource = LanguageHelper.SupportedLanguages;
            LanguageComboBox.SelectedIndex = LanguageHelper.IndexOf(LanguageHelper.GetSavedTag());
            UpdateLanguageRestartBanner();

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

            // 3. Initialize Core engine selection, version & 1.15 capability state
            SetCoreEngineComboSelection(AppSession.Instance.CoreEngine);
            RefreshCoreVersionUI();
        }

        private void SetCoreEngineComboSelection(string engine)
        {
            if (CoreEngineComboBox == null) return;
            _suppressEngineSelectionChanged = true;
            try
            {
                string normalized = ShoesConfigBuilder.NormalizeEngine(engine);
                CoreEngineComboBox.SelectedIndex = string.Equals(normalized, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            finally
            {
                _suppressEngineSelectionChanged = false;
            }
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
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatAppearanceTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatAppearanceSub;
                        break;
                    case "Colors":
                        if (ColorsPanel != null) ColorsPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatColorsTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatColorsSub;
                        break;
                    case "Routing":
                        if (RoutingPanel != null) RoutingPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatRoutingTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatRoutingSub;
                        break;
                    case "DnsSettings":
                        if (DnsSettingsPanel != null) DnsSettingsPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatDnsTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatDnsSub;
                        break;
                    case "Tailscale":
                        if (TailscalePanel != null) TailscalePanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatTailscaleTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatTailscaleSub;
                        break;
                    case "Backup":
                        if (BackupPanel != null) BackupPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatBackupTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatBackupSub;
                        break;
                    case "Autostart":
                        if (AutostartPanel != null) AutostartPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatAutostartTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatAutostartSub;
                        break;
                    case "About":
                        if (AboutPanel != null) AboutPanel.Visibility = Visibility.Visible;
                        if (DetailCategoryTitle != null) DetailCategoryTitle.Text = L.Settings_CatAboutTitle;
                        if (DetailCategorySubtitle != null) DetailCategorySubtitle.Text = L.Settings_CatAboutSub;
                        break;
                }
            }
        }

        private void UpdateLanguageRestartBanner()
        {
            if (LanguageRestartInfoBar == null) return;
            var currentSaved = LanguageHelper.GetSavedTag();
            LanguageRestartInfoBar.IsOpen = !string.Equals(currentSaved, LanguageHelper.StartupTag, StringComparison.OrdinalIgnoreCase);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var tag = LanguageHelper.TagAt(LanguageComboBox.SelectedIndex);
            LanguageHelper.SaveTag(tag);
            UpdateLanguageRestartBanner();
        }

        private async void LanguageRestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Control btn) btn.IsEnabled = false;
            await App.RestartAsync();
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

        private void RefreshCoreVersionUI()
        {
            var verText = GetLocalSingboxVersionText();
            var numVer = Services.CoreUpdateService.GetLocalSingboxNumericVersion();
            bool is115OrAbove = numVer != null && numVer >= new Version(1, 15, 0);
            bool isShoes = string.Equals(AppSession.Instance.CoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase);

            if (ActiveCoreTitleText != null)
            {
                ActiveCoreTitleText.Text = isShoes ? L.Settings_ShoesCoreTitle : L.Settings_CoreTitleText;
            }

            if (SingboxVersionText != null)
            {
                if (isShoes)
                {
                    var shoesVer = Services.CoreUpdateService.GetLocalShoesVersionText();
                    SingboxVersionText.Text = string.Format(L.Settings_ShoesCoreVerFmt, shoesVer);
                }
                else
                {
                    SingboxVersionText.Text = is115OrAbove
                        ? string.Format(L.Settings_CoreVer115Fmt, verText)
                        : string.Format(L.Settings_CoreVerFmt, verText);
                }
            }

            if (RestoreCoreButton != null)
            {
                RestoreCoreButton.Visibility = (!isShoes && Services.CoreUpdateService.HasBundledBackup)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ReplaceCoreButton != null)
            {
                ToolTipService.SetToolTip(ReplaceCoreButton, isShoes
                    ? L.Settings_ReplaceShoesTooltip
                    : L.Settings_ReplaceSingboxTooltip);
            }

            if (CheckUpdateButton != null && CheckUpdateButton.IsEnabled)
            {
                CheckUpdateButton.Content = (isShoes && !Services.CoreUpdateService.IsShoesInstalled)
                    ? L.Settings_DownloadShoesBtnText
                    : L.Settings_CheckUpdateBtnText;
            }

            if (TunStackComboBox != null)
            {
                TunStackComboBox.IsEnabled = !is115OrAbove;
            }

            if (TunStackDescText != null)
            {
                TunStackDescText.Text = is115OrAbove
                    ? L.Settings_TunStackDesc115
                    : L.Settings_TunStackDescDefault;
            }
        }

        private void ResetColorsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetColorsCommand.Execute(null);
        }

        // ── Backup & Restore Handlers ────────────────────────────────────────

        private async void ExportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(MainWindow.Instance!.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"SwellProxy_Backup_{DateTime.Now:yyyyMMdd_HHmm}"
            };
            picker.FileTypeChoices.Add("Swell Proxy Backup", new List<string> { ".zip" });

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    ExportBackupButton.IsEnabled = false;
                    ExportBackupButton.Content = L.Settings_ExportingBtn;

                    await NodesManager.Instance.ExportBackupAsync(file.Path);

                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_ExportSuccessTitle,
                        Content = string.Format(L.Settings_ExportSuccessFmt, file.Path),
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                }
                catch (Exception ex)
                {
                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_ExportFailTitle,
                        Content = ex.Message,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                }
                finally
                {
                    ExportBackupButton.IsEnabled = true;
                    ExportBackupButton.Content = L.Settings_ExportBtnText;
                }
            }
        }

        private async void ImportBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmDialog = new ContentDialog
            {
                Title = L.Settings_ImportConfirmTitle,
                Content = L.Settings_ImportConfirmMsg,
                PrimaryButtonText = L.Settings_ImportConfirmPrimary,
                CloseButtonText = L.Dialog_Cancel,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var confirmResult = await confirmDialog.WithAppTheme().ShowAsync();
            if (confirmResult != ContentDialogResult.Primary) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            var backupFilePath = await Helpers.Win32FilePickerHelper.PickSingleFileAsync(
                hwnd,
                filters: [(L.Settings_BackupFilterName, "*.zip")],
                title: L.Settings_SelectBackupTitle);
            if (backupFilePath != null)
            {
                try
                {
                    ImportBackupButton.IsEnabled = false;
                    ImportBackupButton.Content = L.Settings_ImportingBtn;

                    await NodesManager.Instance.ImportBackupAsync(backupFilePath);

                    // Re-load UI states from newly imported config
                    ViewModel.LoadSettings();
                    InitializeSettings();

                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_ImportSuccessTitle,
                        Content = L.Settings_ImportSuccessMsg,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                }
                catch (Exception ex)
                {
                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_ImportFailTitle,
                        Content = ex.Message,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                }
                finally
                {
                    ImportBackupButton.IsEnabled = true;
                    ImportBackupButton.Content = L.Settings_ImportBtnText;
                }
            }
        }

        // ── Dual-Core & Sing-box / Shoes Core Management Handlers ────────────

        private async void CoreEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _suppressEngineSelectionChanged) return;

            string targetEngine = (CoreEngineComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == ShoesConfigBuilder.EngineShoes
                ? ShoesConfigBuilder.EngineShoes
                : ShoesConfigBuilder.EngineSingBox;

            if (string.Equals(AppSession.Instance.CoreEngine, targetEngine, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (targetEngine == ShoesConfigBuilder.EngineShoes && !Services.CoreUpdateService.IsShoesInstalled)
            {
                var promptDialog = new ContentDialog
                {
                    Title = L.Settings_ShoesDownloadPromptTitle,
                    Content = L.Settings_ShoesDownloadPromptMsg,
                    PrimaryButtonText = L.Settings_ShoesDownloadNowBtn,
                    SecondaryButtonText = L.Settings_ShoesPickLocalBtn,
                    CloseButtonText = L.Dialog_Cancel,
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var choice = await promptDialog.WithAppTheme().ShowAsync();
                bool ready = false;
                if (choice == ContentDialogResult.Primary)
                {
                    ready = await DownloadOrUpdateShoesAsync(forceInstall: true);
                }
                else if (choice == ContentDialogResult.Secondary)
                {
                    ready = await PickAndInstallLocalShoesAsync(showSuccessDialog: false);
                }

                if (!ready || !Services.CoreUpdateService.IsShoesInstalled)
                {
                    SetCoreEngineComboSelection(ShoesConfigBuilder.EngineSingBox);
                    RefreshCoreVersionUI();
                    return;
                }
            }

            AppSession.Instance.CoreEngine = targetEngine;
            LocalSettingsHelper.SetValue("coreEngine", targetEngine);
            RefreshCoreVersionUI();

            if (CoreManager.Instance.IsRunning)
            {
                try
                {
                    CoreManager.Instance.AppendLog(string.Format(L.Shoes_EngineSwitchedRestartFmt, targetEngine));
                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    string runtimeCfg = await ConfigBuilder.BuildAsync(node);
                    await CoreManager.Instance.StopAsync();
                    await CoreManager.Instance.StartAsync(runtimeCfg);
                }
                catch (Exception ex)
                {
                    CoreManager.Instance.AppendLog("[异常] 切换内核重启失败: " + ex.Message);
                }
            }
        }

        private async Task<bool> DownloadOrUpdateShoesAsync(bool forceInstall)
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.Content = L.Settings_CheckingBtn;

            try
            {
                var updater = new Services.CoreUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? $"socks5://127.0.0.1:{AppSession.Instance.MixedPort}" : null;
                var info = await updater.CheckShoesAsync(proxyUrl, forceInstallIfMissing: forceInstall, CancellationToken.None);

                if (info == null)
                {
                    if (!forceInstall && Services.CoreUpdateService.IsShoesInstalled)
                    {
                        var upToDateDialog = new ContentDialog
                        {
                            Title = L.Settings_CoreCheckTitle,
                            Content = string.Format(L.Settings_ShoesUpToDateFmt, Services.CoreUpdateService.GetLocalShoesVersionText()),
                            CloseButtonText = L.Dialog_OK,
                            XamlRoot = this.XamlRoot
                        };
                        await upToDateDialog.WithAppTheme().ShowAsync();
                        return true;
                    }

                    var missingDialog = new ContentDialog
                    {
                        Title = L.Settings_CheckFailTitle,
                        Content = L.Settings_ShoesReleaseNotFoundMsg,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await missingDialog.WithAppTheme().ShowAsync();
                    return false;
                }

                var proxyRunning = CoreManager.Instance.IsRunning;
                if (!forceInstall)
                {
                    var stopWarning = proxyRunning ? L.Settings_CoreStopWarning : string.Empty;
                    var confirmDialog = new ContentDialog
                    {
                        Title = L.Settings_NewVerTitle,
                        Content = string.Format(L.Settings_ShoesConfirmUpdateFmt, info.TagName, stopWarning),
                        PrimaryButtonText = L.Settings_ConfirmUpdateBtn,
                        CloseButtonText = L.Dialog_Cancel,
                        XamlRoot = this.XamlRoot
                    };

                    if (await confirmDialog.WithAppTheme().ShowAsync() != ContentDialogResult.Primary)
                    {
                        return false;
                    }
                }

                var progressText = new TextBlock { Text = L.Settings_PreparingDownload, Margin = new Thickness(0, 0, 0, 10) };
                var progressBar = new ProgressBar { IsIndeterminate = true, Width = 300 };
                var stackPanel = new StackPanel { Children = { progressText, progressBar } };

                var progressDialog = new ContentDialog
                {
                    Title = L.Settings_UpdatingShoesTitle,
                    Content = stackPanel,
                    XamlRoot = this.XamlRoot
                };

                var progress = new Progress<string>(msg =>
                {
                    DispatcherQueue.TryEnqueue(() => progressText.Text = msg);
                });

                var updateTask = updater.UpdateAsync(info, proxyUrl, progress, CancellationToken.None, async () =>
                {
                    if (proxyRunning && string.Equals(AppSession.Instance.ActiveCoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase))
                    {
                        await CoreManager.Instance.StopAsync();
                        await Task.Delay(400);
                    }
                    try
                    {
                        foreach (var p in Process.GetProcessesByName("shoes"))
                        {
                            try { p.Kill(); } catch { }
                        }
                    }
                    catch { }
                });

                _ = progressDialog.WithAppTheme().ShowAsync();

                try
                {
                    await updateTask;
                    progressDialog.Hide();
                    await Task.Delay(50);

                    RefreshCoreVersionUI();

                    if (!forceInstall)
                    {
                        var reconnectNote = (proxyRunning && !CoreManager.Instance.IsRunning) ? L.Settings_CoreReconnectNote : string.Empty;
                        var successDialog = new ContentDialog
                        {
                            Title = L.Settings_UpdateDoneTitle,
                            Content = string.Format(L.Settings_ShoesUpdateDoneFmt, info.TagName, reconnectNote),
                            CloseButtonText = L.Dialog_OK,
                            XamlRoot = this.XamlRoot
                        };
                        await successDialog.WithAppTheme().ShowAsync();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    progressDialog.Hide();
                    await Task.Delay(50);
                    var errDialog = new ContentDialog
                    {
                        Title = L.Settings_UpdateFailTitle,
                        Content = ex.Message,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await errDialog.WithAppTheme().ShowAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CheckFailTitle,
                    Content = string.Format(L.Settings_CheckFailFmt, ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
                return false;
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                RefreshCoreVersionUI();
            }
        }

        private async Task<bool> PickAndInstallLocalShoesAsync(bool showSuccessDialog)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            var filePath = await Helpers.Win32FilePickerHelper.PickSingleFileAsync(
                hwnd,
                filters: [(L.Settings_ExeFilterName, "*.exe")],
                title: L.Settings_SelectShoesTitle);
            if (filePath == null) return false;

            var probe = await Task.Run(() => Services.CoreUpdateService.ProbeShoesExecutable(filePath));
            if (!probe.IsValid)
            {
                var invalidDialog = new ContentDialog
                {
                    Title = L.Settings_InvalidShoesTitle,
                    Content = string.Format(L.Settings_InvalidShoesFmt, probe.VersionDisplay),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await invalidDialog.WithAppTheme().ShowAsync();
                return false;
            }

            var wasRunningShoes = CoreManager.Instance.IsRunning &&
                                  string.Equals(AppSession.Instance.ActiveCoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase);
            if (wasRunningShoes)
            {
                await CoreManager.Instance.StopAsync();
                await Task.Delay(400);
            }

            try
            {
                foreach (var p in Process.GetProcessesByName("shoes"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            try
            {
                Services.CoreUpdateService.InstallLocalShoesExecutable(filePath);
                RefreshCoreVersionUI();

                if (wasRunningShoes)
                {
                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    var cfg = await ConfigBuilder.BuildAsync(node);
                    await CoreManager.Instance.StartAsync(cfg);
                }

                if (showSuccessDialog)
                {
                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_CoreReplaceOkTitle,
                        Content = string.Format(L.Settings_ShoesReplaceOkFmt, probe.VersionDisplay, Path.GetFileName(filePath)),
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CoreReplaceFailTitle,
                    Content = string.Format(L.Settings_CoreReplaceFailFmt, ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
                return false;
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(AppSession.Instance.CoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase))
            {
                await DownloadOrUpdateShoesAsync(forceInstall: !Services.CoreUpdateService.IsShoesInstalled);
                return;
            }

            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.Content = L.Settings_CheckingBtn;

            try
            {
                var updater = new Services.CoreUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? $"socks5://127.0.0.1:{AppSession.Instance.MixedPort}" : null;
                var info = await updater.CheckSingboxAsync(proxyUrl, CancellationToken.None);

                if (info == null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_CoreCheckTitle,
                        Content = string.Format(L.Settings_CoreUpToDateFmt, GetLocalSingboxVersionText()),
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                    return;
                }

                var proxyRunning = CoreManager.Instance.IsRunning;
                var stopWarning = proxyRunning ? L.Settings_CoreStopWarning : string.Empty;

                var confirmDialog = new ContentDialog
                {
                    Title = L.Settings_NewVerTitle,
                    Content = string.Format(L.Settings_CoreConfirmUpdateFmt, info.TagName, stopWarning),
                    PrimaryButtonText = L.Settings_ConfirmUpdateBtn,
                    CloseButtonText = L.Dialog_Cancel,
                    XamlRoot = this.XamlRoot
                };

                if (await confirmDialog.WithAppTheme().ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var progressText = new TextBlock { Text = L.Settings_PreparingDownload, Margin = new Thickness(0, 0, 0, 10) };
                var progressBar = new ProgressBar { IsIndeterminate = true, Width = 300 };
                var stackPanel = new StackPanel { Children = { progressText, progressBar } };

                var progressDialog = new ContentDialog
                {
                    Title = L.Settings_UpdatingCoreTitle,
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
                _ = progressDialog.WithAppTheme().ShowAsync();
                
                try
                {
                    await updateTask;
                    progressDialog.Hide();
                    await Task.Delay(50);

                    RefreshCoreVersionUI();

                    var reconnectNote = proxyRunning ? L.Settings_CoreReconnectNote : string.Empty;
                    var successDialog = new ContentDialog
                    {
                        Title = L.Settings_UpdateDoneTitle,
                        Content = string.Format(L.Settings_UpdateDoneFmt, info.TagName, reconnectNote),
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.WithAppTheme().ShowAsync();
                }
                catch (Exception ex)
                {
                    progressDialog.Hide();
                    await Task.Delay(50);
                    var errDialog = new ContentDialog
                    {
                        Title = L.Settings_UpdateFailTitle,
                        Content = ex.Message,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await errDialog.WithAppTheme().ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CheckFailTitle,
                    Content = string.Format(L.Settings_CheckFailFmt, ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.Content = L.Settings_CheckUpdateBtnText;
            }
        }

        private async void CheckAppUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckAppUpdateButton.IsEnabled = false;
            CheckAppUpdateButton.Content = L.Settings_CheckingBtn;

            try
            {
                var updater = new AppUpdateService();
                var proxyUrl = CoreManager.Instance.IsRunning ? $"socks5://127.0.0.1:{AppSession.Instance.MixedPort}" : null;
                var info = await updater.CheckAsync(proxyUrl, CancellationToken.None);

                if (info == null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = L.Settings_AppCheckTitle,
                        Content = L.Settings_AppUpToDateMsg,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.WithAppTheme().ShowAsync();
                    return;
                }

                var confirmDialog = new ContentDialog
                {
                    Title = L.Settings_NewVerTitle,
                    Content = string.Format(L.Settings_AppConfirmUpdateFmt, info.TagName),
                    PrimaryButtonText = L.Settings_ConfirmUpdateBtn,
                    CloseButtonText = L.Dialog_Cancel,
                    XamlRoot = this.XamlRoot
                };

                if (await confirmDialog.WithAppTheme().ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var progressText = new TextBlock { Text = L.Settings_PreparingDownload, Margin = new Thickness(0, 0, 0, 10) };
                var progressBar = new ProgressBar { IsIndeterminate = true, Width = 300 };
                var stackPanel = new StackPanel { Children = { progressText, progressBar } };

                var progressDialog = new ContentDialog
                {
                    Title = L.Settings_UpdatingAppTitle,
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
                
                _ = progressDialog.WithAppTheme().ShowAsync();
                
                try
                {
                    var staging = await updateTask;
                    progressDialog.Hide();
                    await Task.Delay(50);

                    var successDialog = new ContentDialog
                    {
                        Title = L.Settings_AppReadyTitle,
                        Content = string.Format(L.Settings_AppReadyFmt, info.TagName),
                        PrimaryButtonText = L.Settings_RestartNowBtn,
                        XamlRoot = this.XamlRoot
                    };
                    
                    if (await successDialog.WithAppTheme().ShowAsync() == ContentDialogResult.Primary)
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
                        Title = L.Settings_UpdateFailTitle,
                        Content = ex.Message,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await errDialog.WithAppTheme().ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CheckFailTitle,
                    Content = string.Format(L.Settings_CheckFailFmt, ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            finally
            {
                CheckAppUpdateButton.IsEnabled = true;
                CheckAppUpdateButton.Content = L.Settings_CheckUpdateBtnText;
            }
        }

        private async void ReplaceCoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(AppSession.Instance.CoreEngine, ShoesConfigBuilder.EngineShoes, StringComparison.OrdinalIgnoreCase))
            {
                await PickAndInstallLocalShoesAsync(showSuccessDialog: true);
                return;
            }

            // 使用 Win32 IFileOpenDialog 替代 WinUI3 FileOpenPicker：
            // 当应用以管理员权限运行时（TUN 模式需要 elevated 权限），
            // FileOpenPicker 会抛出 COMException 0x80004005。
            // Win32FilePickerHelper 直接调用 Shell32 COM 接口，不受此限制。
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            var filePath = await Helpers.Win32FilePickerHelper.PickSingleFileAsync(
                hwnd,
                filters: [(L.Settings_ExeFilterName, "*.exe")],
                title: L.Settings_SelectCoreTitle);
            if (filePath == null) return;

            var targetPath = Services.CoreUpdateService.SingboxUserExePath;
            if (string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 替换前先校验所选 exe 是否为合法可运行的 sing-box 内核
            var probe = await Task.Run(() => Services.CoreUpdateService.ProbeSingboxExecutable(filePath));
            if (!probe.IsValid)
            {
                var invalidDialog = new ContentDialog
                {
                    Title = L.Settings_InvalidCoreTitle,
                    Content = string.Format(L.Settings_InvalidCoreFmt, probe.VersionDisplay),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await invalidDialog.WithAppTheme().ShowAsync();
                return;
            }

            var isRunning = CoreManager.Instance.IsRunning;
            if (isRunning)
            {
                await CoreManager.Instance.StopAsync();
                await Task.Delay(500);
            }

            // 确保无残留 sing-box 进程占用文件锁
            try
            {
                foreach (var p in Process.GetProcessesByName("sing-box"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            try
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(filePath, targetPath, overwrite: true);

                RefreshCoreVersionUI();

                var extra115Note = (probe.NumericVersion != null && probe.NumericVersion >= new Version(1, 15, 0))
                    ? L.Settings_CoreReplace115Note
                    : string.Empty;

                var dialog = new ContentDialog
                {
                    Title = L.Settings_CoreReplaceOkTitle,
                    Content = string.Format(L.Settings_CoreReplaceOkFmt, probe.VersionDisplay, Path.GetFileName(filePath), extra115Note),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CoreReplaceFailTitle,
                    Content = string.Format(L.Settings_CoreReplaceFailFmt, ex.Message),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
        }

        private async void RestoreCoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Services.CoreUpdateService.HasBundledBackup) return;

            var isRunning = CoreManager.Instance.IsRunning;
            if (isRunning)
            {
                await CoreManager.Instance.StopAsync();
                await Task.Delay(500);
            }

            try
            {
                foreach (var p in Process.GetProcessesByName("sing-box"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            try
            {
                if (File.Exists(Services.CoreUpdateService.SingboxUserExePath))
                {
                    File.Delete(Services.CoreUpdateService.SingboxUserExePath);
                }
                if (File.Exists(Services.CoreUpdateService.BundledBackupExePath))
                {
                    if (!File.Exists(Services.CoreUpdateService.SingboxBundledExePath))
                    {
                        File.Copy(Services.CoreUpdateService.BundledBackupExePath, Services.CoreUpdateService.SingboxBundledExePath, overwrite: true);
                    }
                    try { File.Delete(Services.CoreUpdateService.BundledBackupExePath); } catch { }
                }

                RefreshCoreVersionUI();

                var dialog = new ContentDialog
                {
                    Title = L.Settings_CoreRestoredTitle,
                    Content = string.Format(L.Settings_CoreRestoredFmt, GetLocalSingboxVersionText(), isRunning ? L.Settings_CoreRestoredReconnect : string.Empty),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Settings_CoreRestoreFailTitle,
                    Content = ex.Message,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
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
                    Title = L.Settings_ResetOnboardOkTitle,
                    Content = L.Settings_ResetOnboardOkMsg,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = L.Settings_ErrorTitle,
                    Content = ex.ToString(),
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await errDialog.WithAppTheme().ShowAsync();
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

                var proxyUrl = CoreManager.Instance.IsRunning
                    ? $"socks5://127.0.0.1:{AppSession.Instance.MixedPort}"
                    : null;

                using var http = CreateGeoUpdateHttpClient(proxyUrl);

                await DownloadGeoRuleAsync(
                    http,
                    "geosite-cn.srs",
                    "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-cn.srs",
                    Path.Combine(dir, "geosite-cn.srs"),
                    proxyUrl);

                await DownloadGeoRuleAsync(
                    http,
                    "geoip-cn.srs",
                    "https://raw.githubusercontent.com/SagerNet/sing-geoip/rule-set/geoip-cn.srs",
                    Path.Combine(dir, "geoip-cn.srs"),
                    proxyUrl);

                await DownloadGeoRuleAsync(
                    http,
                    "geosite-category-ads-all.srs",
                    "https://raw.githubusercontent.com/SagerNet/sing-geosite/rule-set/geosite-category-ads-all.srs",
                    Path.Combine(dir, "geosite-category-ads-all.srs"),
                    proxyUrl);

                var dialog = new ContentDialog
                {
                    Title = L.Settings_GeoOkTitle,
                    Content = L.Settings_GeoOkMsg,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = L.Settings_GeoFailTitle,
                    Content = L.Settings_GeoFailPrefix + ex.Message,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await errDialog.WithAppTheme().ShowAsync();
            }
            finally
            {
                UpdateGeoButton.Content = originalContent;
                UpdateGeoButton.IsEnabled = true;
            }
        }

        private static System.Net.Http.HttpClient CreateGeoUpdateHttpClient(string? proxyUrl)
        {
            var handler = new System.Net.Http.HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                handler.Proxy = new System.Net.WebProxy(proxyUrl);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            var client = new System.Net.Http.HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SwellProxy/GeoUpdater");
            return client;
        }

        private static async Task DownloadGeoRuleAsync(
            System.Net.Http.HttpClient http,
            string fileName,
            string url,
            string destinationPath,
            string? proxyUrl)
        {
            try
            {
                using var response = await http.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(destinationPath, bytes);
            }
            catch (Exception ex)
            {
                var proxyText = string.IsNullOrWhiteSpace(proxyUrl)
                    ? L.Settings_GeoNoProxy
                    : string.Format(L.Settings_GeoWithProxyFmt, proxyUrl);
                throw new InvalidOperationException(
                    string.Format(L.Settings_GeoDownloadErrFmt, fileName, url, proxyText, ex.Message),
                    ex);
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
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(MainWindow.Instance!.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };

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

        private void MixedPortInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is NumberBox nb && double.IsNaN(nb.Value))
            {
                nb.Value = ViewModel.MixedPort;
            }
        }
    }
}
