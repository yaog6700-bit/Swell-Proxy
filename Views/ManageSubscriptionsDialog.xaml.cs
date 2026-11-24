using AnywhereWinUI.Helpers;
using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.Views
{
    /// <summary>
    /// Subscription management dialog — ported 1:1 from XrayUI-dev-net10.
    /// Tab 0 = "添加订阅": URL first, optional name, hint text.
    /// Tab 1 = "管理订阅": scrollable card list with refresh + delete.
    /// </summary>
    public sealed partial class ManageSubscriptionsDialog : ContentDialog
    {
        private readonly Action _onDataChanged;
        private bool _initialized;
        private bool _isLoadingAutoRefreshSwitch;
        private bool _isAddingSubscription;

        public ManageSubscriptionsDialog(Action onDataChanged)
        {
            this.InitializeComponent();
            _onDataChanged = onDataChanged;
            DialogErrorBar.Closed += (_, _) => DialogErrorBar.Visibility = Visibility.Collapsed;
            _initialized = true;

            // Default to Add page
            ApplyAddPage();
        }

        private void ShowInlineError(string title, string message)
        {
            DialogErrorBar.Title = title;
            DialogErrorBar.Message = message;
            DialogErrorBar.Visibility = Visibility.Visible;
            DialogErrorBar.IsOpen = true;
        }

        private void HideInlineError()
        {
            DialogErrorBar.IsOpen = false;
            DialogErrorBar.Visibility = Visibility.Collapsed;
        }

        // ── Page switching ────────────────────────────────────────────────────

        private void PageSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;
            HideInlineError();

            if (PageSegmented.SelectedIndex == 0)
                ApplyAddPage();
            else
                ApplyManagePage();
        }

        private void ApplyAddPage()
        {
            DialogTitleText.Text       = L.SubDialog_AddTitle;
            AddPagePanel.Visibility    = Visibility.Visible;
            ManagePagePanel.Visibility = Visibility.Collapsed;
            PrimaryButtonText          = L.SubDialog_AddBtn;
            CloseButtonText            = L.Dialog_Cancel;
            IsPrimaryButtonEnabled     = !_isAddingSubscription && !string.IsNullOrWhiteSpace(UrlInput?.Text);
        }

        private void ApplyManagePage()
        {
            DialogTitleText.Text       = L.SubDialog_ManageTitle;
            AddPagePanel.Visibility    = Visibility.Collapsed;
            ManagePagePanel.Visibility = Visibility.Visible;
            PrimaryButtonText          = string.Empty;
            CloseButtonText            = L.SubDialog_DoneBtn;
            IsPrimaryButtonEnabled     = false;
            _isLoadingAutoRefreshSwitch = true;
            AutoRefreshSwitch.IsOn = AppSession.Instance.EnableSubscriptionAutoRefresh;
            _isLoadingAutoRefreshSwitch = false;
            LoadSubscriptionCards();
        }

        private void AutoRefreshSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_initialized || _isLoadingAutoRefreshSwitch) return;

            AppSession.Instance.EnableSubscriptionAutoRefresh = AutoRefreshSwitch.IsOn;
            LocalSettingsHelper.SetValue("enableSubscriptionAutoRefresh", AutoRefreshSwitch.IsOn);
            LoadSubscriptionCards();
        }

        // ── URL input → enable/disable "添加" ──────────────────────────────────

        private void UrlInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized) return;
            if (AddPagePanel.Visibility == Visibility.Visible)
                IsPrimaryButtonEnabled = !_isAddingSubscription && !string.IsNullOrWhiteSpace(UrlInput.Text);
        }

        // ── Add subscription (called from ServersPage PrimaryButtonClick) ──────

        internal async Task<bool> TryAddSubscriptionAsync()
        {
            if (_isAddingSubscription) return false;

            string url  = (UrlInput?.Text ?? string.Empty).Trim();
            string name = (NameInput?.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url)) return false;

            // Use URL host as fallback name
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = new Uri(url).Host; }
                catch { name = url; }
                if (string.IsNullOrWhiteSpace(name)) name = L.Servers_UnnamedSub;
            }

            _isAddingSubscription = true;
            IsPrimaryButtonEnabled = false;
            HideInlineError();

            try
            {
                NodesManager.Instance.AddSubscription(name, url);
                var subs = NodesManager.Instance.GetSubscriptionsSnapshot();
                if (subs.Count > 0)
                {
                    string subId = subs[^1].Id;
                    string? err = await NodesManager.Instance.UpdateSubscriptionAsync(subId);
                    if (err != null)
                    {
                        // Remove the added sub if first update fails completely
                        NodesManager.Instance.DeleteSubscription(subId);
                        ShowInlineError(L.SubDialog_AddFailTitle, err);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] Add failed: {ex.Message}");
                ShowInlineError(L.SubDialog_AddFailTitle, ex.Message);
                return false;
            }
            finally
            {
                _isAddingSubscription = false;
                if (AddPagePanel.Visibility == Visibility.Visible)
                    IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(UrlInput?.Text);
            }

            _onDataChanged?.Invoke();
            return true;
        }

        // ── Manage page: build subscription cards ─────────────────────────────

        private void LoadSubscriptionCards()
        {
            try
            {
                SubsListPanel.Children.Clear();
                var subs = NodesManager.Instance.GetSubscriptionsSnapshot();

                if (subs.Count == 0)
                {
                    AutoRefreshPanel.Visibility = Visibility.Collapsed;
                    EmptyStateText.Visibility  = Visibility.Visible;
                    SubScrollViewer.Visibility = Visibility.Collapsed;
                    return;
                }

                AutoRefreshPanel.Visibility = Visibility.Visible;
                EmptyStateText.Visibility  = Visibility.Collapsed;
                SubScrollViewer.Visibility = Visibility.Visible;

                foreach (var sub in subs)
                    SubsListPanel.Children.Add(BuildSubscriptionCard(sub));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] LoadCards failed: {ex.Message}");
            }
        }

        private UIElement BuildSubscriptionCard(PersistedSubscription sub)
        {
            bool isDark = ContentDialogExtensions.IsElementDark(this);
            Brush primaryFg = LookupDialogThemeBrush("TextFillColorPrimaryBrush", isDark, isDark ? Colors.White : Colors.Black);
            Brush secondaryFg = LookupDialogThemeBrush("TextFillColorSecondaryBrush", isDark, Colors.Gray);
            Brush tertiaryFg = LookupDialogThemeBrush("TextFillColorTertiaryBrush", isDark, Colors.Gray);

            // ── Left info ────────────────────────────────────────────────────
            var nameBlock = new TextBlock
            {
                Text         = sub.Name,
                FontSize     = 14,
                FontWeight   = FontWeights.SemiBold,
                Foreground   = primaryFg,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var urlBlock = new TextBlock
            {
                Text         = sub.Url,
                FontSize     = 12,
                Foreground   = tertiaryFg,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var updatedBlock = new TextBlock
            {
                Text       = Loc.Format("SubDialog_LastSyncFmt", sub.LastUpdated),
                FontSize   = 12,
                Foreground = secondaryFg
            };
            var nextUpdateBlock = new TextBlock
            {
                Text       = GetAutoRefreshStatusText(sub),
                FontSize   = 12,
                Foreground = secondaryFg
            };
            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(nameBlock);
            infoPanel.Children.Add(urlBlock);
            infoPanel.Children.Add(updatedBlock);
            infoPanel.Children.Add(nextUpdateBlock);

            // ── Refresh button (icon ↔ ProgressRing) ─────────────────────────
            var refreshIcon = new FontIcon { Glyph = "\uE895", FontSize = 14 };
            var busyRing = new ProgressRing
            {
                Width      = 16,
                Height     = 16,
                IsActive   = true,
                Visibility = Visibility.Collapsed
            };
            var iconGrid = new Grid { Width = 16, Height = 16 };
            iconGrid.Children.Add(refreshIcon);
            iconGrid.Children.Add(busyRing);

            Style? subtleBtnStyle = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var sbObj) ? sbObj as Style : null;

            var refreshBtn = new Button
            {
                Width   = 32,
                Height  = 32,
                Padding = new Thickness(0),
                Content = iconGrid
            };
            if (subtleBtnStyle != null) refreshBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(refreshBtn, L.SubDialog_RefreshTooltip);

            refreshBtn.Click += async (_, _) =>
            {
                refreshBtn.IsEnabled      = false;
                refreshIcon.Visibility    = Visibility.Collapsed;
                busyRing.Visibility       = Visibility.Visible;
                HideInlineError();
                try
                {
                    string? err = await NodesManager.Instance.UpdateSubscriptionAsync(sub.Id);
                    if (err != null)
                    {
                        ShowInlineError(L.SubDialog_UpdateFailTitle, err);
                    }
                    else
                    {
                        // Refresh the updated-time label
                        var latest = NodesManager.Instance.GetSubscriptionsSnapshot().Find(s => s.Id == sub.Id);
                        if (latest != null)
                        {
                            updatedBlock.Text = Loc.Format("SubDialog_LastSyncFmt", latest.LastUpdated);
                            nextUpdateBlock.Text = GetAutoRefreshStatusText(latest);
                        }
                    }
                    _onDataChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] Refresh failed: {ex.Message}");
                    ShowInlineError(L.SubDialog_UpdateFailTitle, ex.Message);
                }
                busyRing.Visibility    = Visibility.Collapsed;
                refreshIcon.Visibility = Visibility.Visible;
                refreshBtn.IsEnabled   = true;
            };

            // ── Delete button with Flyout confirm ─────────────────────────────
            var confirmTitle = new TextBlock
            {
                Text         = L.SubDialog_DelConfirmTitle,
                TextWrapping = TextWrapping.Wrap,
                FontWeight   = FontWeights.SemiBold
            };
            var confirmDesc = new TextBlock
            {
                Text         = L.SubDialog_DelConfirmDesc,
                TextWrapping = TextWrapping.Wrap,
                FontSize     = 12,
                Opacity      = 0.7
            };
            var confirmDeleteBtn = new Button
            {
                Content             = L.Servers_MenuDelete,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            // Try to apply danger accent style
            try
            {
                if (Application.Current.Resources.TryGetValue(
                    "DangerAccentButtonStyle", out var obj) && obj is Style s)
                    confirmDeleteBtn.Style = s;
                else
                    confirmDeleteBtn.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28));
            }
            catch { /* ignore style lookup failures */ }

            var flyoutPanel = new StackPanel { Spacing = 12, MaxWidth = 240 };
            flyoutPanel.Children.Add(confirmTitle);
            flyoutPanel.Children.Add(confirmDesc);
            flyoutPanel.Children.Add(confirmDeleteBtn);

            var flyout = new Flyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
                Content   = flyoutPanel
            };

            var deleteIcon = new FontIcon
            {
                Glyph      = "\uE74D",
                FontSize   = 14,
                Foreground = LookupDialogThemeBrush("SystemFillColorCriticalBrush", isDark, Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28))
            };
            var deleteBtn = new Button
            {
                Width   = 32,
                Height  = 32,
                Padding = new Thickness(0),
                Content = deleteIcon,
                Flyout  = flyout
            };
            if (subtleBtnStyle != null) deleteBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(deleteBtn, L.SubDialog_DelTooltip);

            confirmDeleteBtn.Click += async (_, _) =>
            {
                flyout.Hide();
                try
                {
                    await ViewModels.ServersViewModel.DeleteSubscriptionAndSyncCoreAsync(sub.Id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] Delete failed: {ex.Message}");
                }
                _onDataChanged?.Invoke();
                LoadSubscriptionCards();   // rebuild list
            };

            // ── Card grid ────────────────────────────────────────────────────
            var cardGrid = new Grid { ColumnSpacing = 4 };
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(infoPanel,  0);
            Grid.SetColumn(refreshBtn, 1);
            Grid.SetColumn(deleteBtn,  2);
            cardGrid.Children.Add(infoPanel);
            cardGrid.Children.Add(refreshBtn);
            cardGrid.Children.Add(deleteBtn);

            // ── Themed card border (resolves active Light/Dark dictionary) ──
            Brush cardBg = LookupDialogThemeBrush(
                "CardBackgroundFillColorDefaultBrush",
                isDark,
                Microsoft.UI.ColorHelper.FromArgb(20, 128, 128, 128));
            Brush cardBorder = LookupDialogThemeBrush(
                "CardStrokeColorDefaultBrush",
                isDark,
                Microsoft.UI.ColorHelper.FromArgb(36, 128, 128, 128));

            return new Border
            {
                CornerRadius    = new CornerRadius(8),
                Background      = cardBg,
                BorderBrush     = cardBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(12, 10, 12, 10),
                Margin          = new Thickness(0, 0, 0, 8),
                Child           = cardGrid
            };
        }

        private static Brush LookupDialogThemeBrush(string key, bool wantDark, Windows.UI.Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, wantDark, fallback);

        private static string GetAutoRefreshStatusText(PersistedSubscription sub)
        {
            return AppSession.Instance.EnableSubscriptionAutoRefresh
                ? NodesManager.GetSubscriptionNextAutoRefreshText(sub)
                : L.SubDialog_AutoRefreshOff;
        }
    }
}
