using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace AnywhereWinUI.Views
{
    public sealed partial class RoutingPage : Page
    {
        private sealed class RuleRowHolder
        {
            public required Grid Root { get; init; }
            public required FontIcon FallbackIcon { get; init; }
            public required Image IconImage { get; init; }
            public required StackPanel TextPanel { get; init; }
            public required TextBlock NameBlock { get; init; }
            public required TextBlock SubtitleBlock { get; init; }
            public required Button OutboundButton { get; init; }
            public required FontIcon OutboundGlyph { get; init; }
            public required TextBlock OutboundLabel { get; init; }
            public required Button EditButton { get; init; }
            public required Button DeleteButton { get; init; }
            public required FontIcon DeleteIcon { get; init; }
            public string? CurrentIconUrl { get; set; }
            public RoutingRuleItem? CurrentRule { get; set; }
        }

        private readonly ObservableCollection<RoutingRuleItem> _rules = new();

        public RoutingViewModel ViewModel { get; }

        public RoutingPage()
        {
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;

            ViewModel = ((App)Application.Current).Services.GetService(typeof(RoutingViewModel)) as RoutingViewModel
                ?? new RoutingViewModel();
            ViewModel.RefreshActions();

            InitializeComponent();
            RouteSimHeaderText.Text = L.RouteSim_HeaderTitle;
            RouteSimInputBox.PlaceholderText = L.RouteSim_InputPlaceholder;
            RulesListView.ItemsSource = _rules;
            this.Loaded += OnPageLoaded;
            this.ActualThemeChanged += OnActualThemeChanged;
            LoadRules();
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.Loaded -= OnPageLoaded;
            this.ActualThemeChanged -= OnActualThemeChanged;
            _rules.Clear();
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e) => RefreshAllRowBrushes();

        private void OnActualThemeChanged(FrameworkElement sender, object args) => RefreshAllRowBrushes();

        private void RefreshAllRowBrushes()
        {
            RefreshSimulationResult();
            if (RulesListView?.ItemsPanelRoot == null) return;
            foreach (var child in RulesListView.ItemsPanelRoot.Children)
            {
                if (child is ListViewItem item &&
                    item.Content is Grid g &&
                    g.Tag is RuleRowHolder holder)
                {
                    ApplyRowThemeBrushes(holder);
                }
            }
        }

        private void LoadRules()
        {
            ViewModel.RefreshActions();
            _rules.Clear();
            foreach (var rule in RoutingRulesService.LoadRules().Where(r => r.IsBuiltIn))
                _rules.Add(rule);

            RefreshRuleCount();
            RefreshSimulationResult();
        }

        private void RefreshRuleCount()
        {
            RuleCountText.Text = string.Format(L.Routing_RuleCountFmt, _rules.Count);
        }

        private void RulesListView_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer.Content is Grid recycledGrid && recycledGrid.Tag is RuleRowHolder recycledHolder)
                {
                    recycledHolder.CurrentRule = null;
                }
                return;
            }

            if (args.Item is not RoutingRuleItem rule) return;

            RuleRowHolder holder;
            if (args.ItemContainer.Content is Grid existingGrid && existingGrid.Tag is RuleRowHolder existingHolder)
            {
                holder = existingHolder;
            }
            else
            {
                holder = CreateRowHolder();
                args.ItemContainer.ContentTemplate = null;
                args.ItemContainer.Content = holder.Root;
            }

            BindRowHolder(holder, rule);
            ApplyRowThemeBrushes(holder);
            args.Handled = true;
        }

        private RuleRowHolder CreateRowHolder()
        {
            var root = new Grid
            {
                Width = 680,
                Padding = new Thickness(0, 12, 0, 12),
                ColumnSpacing = 0,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fallbackIcon = new FontIcon
            {
                FontSize = 18,
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center
            };
            var iconImage = new Image
            {
                Width = 30,
                Height = 30,
                Stretch = Stretch.Uniform,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconImage.ImageOpened += (_, _) =>
            {
                iconImage.Visibility = Visibility.Visible;
                fallbackIcon.Visibility = Visibility.Collapsed;
            };
            iconImage.ImageFailed += (_, _) =>
            {
                iconImage.Visibility = Visibility.Collapsed;
                fallbackIcon.Visibility = Visibility.Visible;
            };

            var iconHost = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 16, 0) };
            iconHost.Children.Add(fallbackIcon);
            iconHost.Children.Add(iconImage);
            Grid.SetColumn(iconHost, 0);

            var nameBlock = new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            };
            var subtitleBlock = new TextBlock
            {
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var textPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(nameBlock);
            textPanel.Children.Add(subtitleBlock);
            Grid.SetColumn(textPanel, 1);

            var outboundGlyph = new FontIcon { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var outboundLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
            var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 9, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };

            var outboundContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { outboundGlyph, outboundLabel, chevron }
            };

            var outboundButton = new Button
            {
                MinWidth = 160,
                Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Center,
                Content = outboundContent
            };

            // Lazy-populate MenuFlyout items ONLY when user actually clicks the dropdown!
            var flyout = new MenuFlyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
            };
            outboundButton.Flyout = flyout;
            Grid.SetColumn(outboundButton, 2);

            Style? subtleBtnStyle = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var sObj) ? sObj as Style : null;

            var editButton = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 13 }
            };
            if (subtleBtnStyle != null) editButton.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(editButton, L.Routing_EditRuleTooltip);
            editButton.Click += EditRuleButton_Click;
            Grid.SetColumn(editButton, 3);

            var deleteIcon = new FontIcon
            {
                Glyph = "\uE74D",
                FontSize = 13
            };
            var deleteButton = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(0),
                Content = deleteIcon
            };
            if (subtleBtnStyle != null) deleteButton.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(deleteButton, L.Routing_DelRuleTooltip);
            deleteButton.Click += DeleteRuleButton_Click;
            Grid.SetColumn(deleteButton, 4);

            root.Children.Add(iconHost);
            root.Children.Add(textPanel);
            root.Children.Add(outboundButton);
            root.Children.Add(editButton);
            root.Children.Add(deleteButton);

            var holder = new RuleRowHolder
            {
                Root = root,
                FallbackIcon = fallbackIcon,
                IconImage = iconImage,
                TextPanel = textPanel,
                NameBlock = nameBlock,
                SubtitleBlock = subtitleBlock,
                OutboundButton = outboundButton,
                OutboundGlyph = outboundGlyph,
                OutboundLabel = outboundLabel,
                EditButton = editButton,
                DeleteButton = deleteButton,
                DeleteIcon = deleteIcon
            };

            ApplyRowThemeBrushes(holder);

            flyout.Opening += (_, _) => PopulateFlyoutOnDemand(flyout, holder);
            flyout.Closed += (_, _) => flyout.Items.Clear();

            root.Tag = holder;
            return holder;
        }

        private void ApplyRowThemeBrushes(RuleRowHolder holder)
        {
            bool wantDark = ContentDialogExtensions.IsElementDark(this);

            Brush primaryFg = LookupThemedBrush("TextFillColorPrimaryBrush", wantDark, wantDark ? Colors.White : Colors.Black);
            Brush secondaryFg = LookupThemedBrush("TextFillColorSecondaryBrush", wantDark, Colors.Gray);

            holder.Root.BorderBrush = LookupThemedBrush("DividerStrokeColorDefaultBrush", wantDark, Color.FromArgb(30, 128, 128, 128));
            holder.FallbackIcon.Foreground = primaryFg;
            holder.NameBlock.Foreground = primaryFg;
            holder.SubtitleBlock.Foreground = secondaryFg;
            holder.DeleteIcon.Foreground = LookupThemedBrush("SystemFillColorCriticalBrush", wantDark, Color.FromArgb(255, 196, 43, 28));
        }

        private static Brush LookupThemedBrush(string key, bool wantDark, Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, wantDark, fallback);

        private void PopulateFlyoutOnDemand(MenuFlyout flyout, RuleRowHolder holder)
        {
            flyout.Items.Clear();
            var rule = holder.CurrentRule;
            if (rule == null) return;

            foreach (var action in ViewModel.AvailableActions)
            {
                if (action.Tag == "separator")
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    continue;
                }

                var capturedAction = action;
                var menuItem = new RadioMenuFlyoutItem
                {
                    Text = capturedAction.Label,
                    GroupName = rule.Id,
                    IsChecked = capturedAction.Tag == rule.OutboundTag
                };
                if (!string.IsNullOrEmpty(capturedAction.Glyph))
                    menuItem.Icon = new FontIcon { Glyph = capturedAction.Glyph };

                menuItem.Click += async (_, _) =>
                {
                    if (rule.OutboundTag == capturedAction.Tag) return;

                    rule.OutboundTag = capturedAction.Tag;
                    UpdateOutboundVisual(holder, capturedAction);
                    await SaveAndApplyAsync();
                };

                flyout.Items.Add(menuItem);
            }
        }

        private void BindRowHolder(RuleRowHolder holder, RoutingRuleItem rule)
        {
            holder.CurrentRule = rule;
            holder.Root.Opacity = rule.IsEnabled ? 1 : 0.55;
            holder.FallbackIcon.Glyph = rule.IsBuiltIn ? "\uE774" : "\uE8A5";

            if (!string.Equals(holder.CurrentIconUrl, rule.IconUrl, StringComparison.Ordinal))
            {
                holder.CurrentIconUrl = rule.IconUrl;
                if (!string.IsNullOrWhiteSpace(rule.IconUrl) &&
                    Uri.TryCreate(rule.IconUrl, UriKind.Absolute, out var iconUri))
                {
                    holder.IconImage.Visibility = Visibility.Collapsed;
                    holder.FallbackIcon.Visibility = Visibility.Visible;
                    holder.IconImage.Source = new BitmapImage(iconUri);
                }
                else
                {
                    holder.IconImage.Source = null;
                    holder.IconImage.Visibility = Visibility.Collapsed;
                    holder.FallbackIcon.Visibility = Visibility.Visible;
                }
            }

            holder.NameBlock.Text = rule.Name;
            holder.SubtitleBlock.Text = GetRuleSubtitle(rule);
            ToolTipService.SetToolTip(holder.TextPanel, rule.Match);

            UpdateOutboundVisual(holder, ViewModel.ResolveItemForTag(rule.OutboundTag));

            holder.EditButton.Tag = rule;
            holder.DeleteButton.Tag = rule;
            holder.DeleteButton.Visibility = rule.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void UpdateOutboundVisual(RuleRowHolder holder, RuleSetActionItem item)
        {
            holder.OutboundGlyph.Glyph = item.Glyph;
            holder.OutboundGlyph.Visibility = string.IsNullOrEmpty(item.Glyph) ? Visibility.Collapsed : Visibility.Visible;
            holder.OutboundLabel.Text = item.Label;
        }

        private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomRulesDialog
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.WithAppTheme().ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            LoadRules();
            RefreshRuleCount();
            await ViewModel.ApplyCustomRulesAsync();
        }

        private async void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not RoutingRuleItem rule) return;

            var dialog = new AddCustomRuleDialog
            {
                XamlRoot = XamlRoot
            };
            dialog.LoadRule(new CustomRule
            {
                Type = rule.Type,
                Match = rule.Match,
                OutboundTag = rule.OutboundTag,
                IsEnabled = rule.IsEnabled
            });

            var result = await dialog.WithAppTheme().ShowAsync();
            if (result != ContentDialogResult.Primary || dialog.ResultRule == null) return;

            rule.Type = dialog.ResultRule.Type;
            rule.Match = dialog.ResultRule.Match;
            rule.OutboundTag = dialog.ResultRule.OutboundTag;
            rule.IsEnabled = dialog.ResultRule.IsEnabled;

            var index = _rules.IndexOf(rule);
            if (index >= 0)
                _rules[index] = rule;

            await SaveAndApplyAsync();
        }

        private async void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not RoutingRuleItem rule) return;
            if (rule.IsBuiltIn) return;

            _rules.Remove(rule);
            RefreshRuleCount();
            await SaveAndApplyAsync();
        }

        private async void ResetRulesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = L.Routing_ResetTitle,
                Content = L.Routing_ResetMsg,
                PrimaryButtonText = L.Routing_ResetConfirmBtn,
                CloseButtonText = L.Dialog_Cancel,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.WithAppTheme().ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rules.Clear();
            foreach (var rule in RoutingRulesService.CreateDefaultRules())
                _rules.Add(rule);

            RefreshRuleCount();
            await SaveAndApplyAsync();
        }

        private async void RulesListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            await SaveAndApplyAsync();
        }

        private async Task SaveAndApplyAsync()
        {
            var customRules = RoutingRulesService.LoadRules().Where(r => !r.IsBuiltIn);
            RoutingRulesService.SaveRules(_rules.Concat(customRules));
            RefreshSimulationResult();
            await ViewModel.ApplyCustomRulesAsync();
        }

        private void RouteSimInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshSimulationResult();
        }

        private void RefreshSimulationResult()
        {
            if (RouteSimInputBox == null || RouteSimResultPanel == null) return;

            string raw = RouteSimInputBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
            {
                RouteSimResultPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var customRules = RoutingRulesService.LoadRules().Where(r => !r.IsBuiltIn);
            var combined = customRules.Concat(_rules).ToList();
            var sim = RoutingRuleSimulator.Evaluate(raw, combined);
            if (sim == null)
            {
                RouteSimResultPanel.Visibility = Visibility.Collapsed;
                return;
            }

            bool isDark = ContentDialogExtensions.IsElementDark(this);

            (Color bg, Color fg) = sim.Kind switch
            {
                SimulatedOutboundKind.Direct => isDark
                    ? (Color.FromArgb(38, 108, 203, 95), Color.FromArgb(255, 108, 203, 95))
                    : (Color.FromArgb(28, 15, 123, 15), Color.FromArgb(255, 15, 123, 15)),
                SimulatedOutboundKind.Block => isDark
                    ? (Color.FromArgb(38, 255, 153, 164), Color.FromArgb(255, 255, 153, 164))
                    : (Color.FromArgb(28, 196, 43, 28), Color.FromArgb(255, 196, 43, 28)),
                _ => isDark
                    ? (Color.FromArgb(38, 96, 205, 255), Color.FromArgb(255, 96, 205, 255))
                    : (Color.FromArgb(28, 0, 95, 184), Color.FromArgb(255, 0, 95, 184))
            };

            RouteSimOutboundBadge.Background = new SolidColorBrush(bg);
            RouteSimOutboundText.Foreground = new SolidColorBrush(fg);
            RouteSimOutboundText.Text = sim.OutboundDisplay;
            RouteSimMatchedRuleText.Text = string.Format(L.RouteSim_MatchedRuleFmt, sim.MatchedRuleDisplay);
            RouteSimDnsText.Text = string.Format(L.RouteSim_DnsFmt, sim.DnsDisplay);
            RouteSimResultPanel.Visibility = Visibility.Visible;
        }

        private static string FormatRuleType(string type) => type switch
        {
            "ip" => "IP",
            "process" => L.Routing_TypeProcess,
            "mixed" => L.Routing_TypeMixed,
            _ => L.Routing_TypeDomain
        };

        private static string GetRuleSubtitle(RoutingRuleItem rule)
        {
            if (rule.IsBuiltIn)
                return rule.Description;

            return $"{FormatRuleType(rule.Type)} · {rule.Match}";
        }
    }
}
