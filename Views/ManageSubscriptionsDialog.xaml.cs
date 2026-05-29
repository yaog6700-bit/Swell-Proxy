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

        public ManageSubscriptionsDialog(Action onDataChanged)
        {
            this.InitializeComponent();
            _onDataChanged = onDataChanged;
            _initialized = true;

            // Default to Add page
            ApplyAddPage();
        }

        // ── Page switching ────────────────────────────────────────────────────

        private void PageSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initialized) return;

            if (PageSegmented.SelectedIndex == 0)
                ApplyAddPage();
            else
                ApplyManagePage();
        }

        private void ApplyAddPage()
        {
            DialogTitleText.Text       = "添加订阅";
            AddPagePanel.Visibility    = Visibility.Visible;
            ManagePagePanel.Visibility = Visibility.Collapsed;
            PrimaryButtonText          = "添加";
            CloseButtonText            = "取消";
            IsPrimaryButtonEnabled     = !string.IsNullOrWhiteSpace(UrlInput?.Text);
        }

        private void ApplyManagePage()
        {
            DialogTitleText.Text       = "管理订阅";
            AddPagePanel.Visibility    = Visibility.Collapsed;
            ManagePagePanel.Visibility = Visibility.Visible;
            PrimaryButtonText          = string.Empty;
            CloseButtonText            = "完成";
            IsPrimaryButtonEnabled     = false;
            LoadSubscriptionCards();
        }

        // ── URL input → enable/disable "添加" ──────────────────────────────────

        private void UrlInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_initialized) return;
            if (AddPagePanel.Visibility == Visibility.Visible)
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(UrlInput.Text);
        }

        // ── Add subscription (called from ServersPage PrimaryButtonClick) ──────

        internal async Task<bool> TryAddSubscriptionAsync()
        {
            string url  = (UrlInput?.Text ?? string.Empty).Trim();
            string name = (NameInput?.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url)) return false;

            // Use URL host as fallback name
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = new Uri(url).Host; }
                catch { name = url; }
                if (string.IsNullOrWhiteSpace(name)) name = "未命名订阅";
            }

            try
            {
                NodesManager.Instance.AddSubscription(name, url);
                var subs = NodesManager.Instance.Subscriptions;
                if (subs.Count > 0)
                {
                    string? err = await NodesManager.Instance.UpdateSubscriptionAsync(subs[^1].Id);
                    if (err != null)
                    {
                        // Remove the added sub if first update fails completely
                        NodesManager.Instance.DeleteSubscription(subs[^1].Id);
                        
                        var errDialog = new ContentDialog
                        {
                            Title = "添加订阅失败",
                            Content = err,
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        try { await errDialog.ShowAsync(); } catch { }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] Add failed: {ex.Message}");
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
                var subs = NodesManager.Instance.Subscriptions;

                if (subs.Count == 0)
                {
                    EmptyStateText.Visibility  = Visibility.Visible;
                    SubScrollViewer.Visibility = Visibility.Collapsed;
                    return;
                }

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
            // ── Left info ────────────────────────────────────────────────────
            var nameBlock = new TextBlock
            {
                Text         = sub.Name,
                FontSize     = 14,
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var urlBlock = new TextBlock
            {
                Text         = sub.Url,
                FontSize     = 12,
                Opacity      = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var updatedBlock = new TextBlock
            {
                Text     = $"更新时间：{sub.LastUpdated}",
                FontSize = 12,
                Opacity  = 0.7
            };
            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(nameBlock);
            infoPanel.Children.Add(urlBlock);
            infoPanel.Children.Add(updatedBlock);

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

            var refreshBtn = new Button
            {
                Padding         = new Thickness(6),
                Background      = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content         = iconGrid
            };
            ToolTipService.SetToolTip(refreshBtn, "刷新订阅");

            refreshBtn.Click += async (_, _) =>
            {
                refreshBtn.IsEnabled      = false;
                refreshIcon.Visibility    = Visibility.Collapsed;
                busyRing.Visibility       = Visibility.Visible;
                try
                {
                    string? err = await NodesManager.Instance.UpdateSubscriptionAsync(sub.Id);
                    if (err != null)
                    {
                        var errDialog = new ContentDialog
                        {
                            Title = "更新订阅失败",
                            Content = err,
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        try { await errDialog.ShowAsync(); } catch { }
                    }
                    else
                    {
                        // Refresh the updated-time label
                        var latest = NodesManager.Instance.Subscriptions.Find(s => s.Id == sub.Id);
                        if (latest != null) updatedBlock.Text = $"更新时间：{latest.LastUpdated}";
                    }
                    _onDataChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ManageSubDialog] Refresh failed: {ex.Message}");
                }
                busyRing.Visibility    = Visibility.Collapsed;
                refreshIcon.Visibility = Visibility.Visible;
                refreshBtn.IsEnabled   = true;
            };

            // ── Delete button with Flyout confirm ─────────────────────────────
            var confirmTitle = new TextBlock
            {
                Text         = "删除订阅？",
                TextWrapping = TextWrapping.Wrap,
                FontWeight   = FontWeights.SemiBold
            };
            var confirmDesc = new TextBlock
            {
                Text         = "将同时删除该订阅下的所有节点。",
                TextWrapping = TextWrapping.Wrap,
                FontSize     = 12,
                Opacity      = 0.7
            };
            var confirmDeleteBtn = new Button
            {
                Content             = "删除",
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

            var deleteBtn = new Button
            {
                Padding         = new Thickness(6),
                Background      = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content         = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
                Flyout          = flyout
            };
            ToolTipService.SetToolTip(deleteBtn, "删除订阅");

            confirmDeleteBtn.Click += (_, _) =>
            {
                flyout.Hide();
                try { NodesManager.Instance.DeleteSubscription(sub.Id); }
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

            // ── Themed card border ────────────────────────────────────────────
            Brush cardBg, cardBorder;
            try
            {
                cardBg     = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                cardBorder = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
            }
            catch
            {
                cardBg     = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(30, 128, 128, 128));
                cardBorder = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 128, 128, 128));
            }

            return new Border
            {
                CornerRadius    = new CornerRadius(6),
                Background      = cardBg,
                BorderBrush     = cardBorder,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(12, 10, 12, 10),
                Margin          = new Thickness(0, 0, 0, 8),
                Child           = cardGrid
            };
        }
    }
}
