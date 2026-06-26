using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ObservableCollection<RoutingRuleItem> _rules = new();

        public RoutingViewModel ViewModel { get; }

        public RoutingPage()
        {
            ViewModel = ((App)Application.Current).Services.GetService(typeof(RoutingViewModel)) as RoutingViewModel
                ?? new RoutingViewModel();

            InitializeComponent();
            RulesListView.ItemsSource = _rules;
            LoadRules();
        }

        private void LoadRules()
        {
            _rules.Clear();
            foreach (var rule in RoutingRulesService.LoadRules().Where(r => r.IsBuiltIn))
                _rules.Add(rule);

            RefreshRuleCount();
        }

        private void RefreshRuleCount()
        {
            RuleCountText.Text = $"共 {_rules.Count} 条规则，按列表顺序匹配";
        }

        private void RulesListView_ContainerContentChanging(
            ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is not RoutingRuleItem rule) return;

            var root = new Grid
            {
                Width = 680,
                Padding = new Thickness(0, 12, 0, 12),
                ColumnSpacing = 0,
                BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Opacity = rule.IsEnabled ? 1 : 0.55
            };

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = CreateRuleIcon(rule);
            icon.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(icon, 0);

            var textPanel = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = rule.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = GetRuleSubtitle(rule),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            ToolTipService.SetToolTip(textPanel, rule.Match);
            Grid.SetColumn(textPanel, 1);

            var outboundButton = CreateOutboundButton(rule);
            Grid.SetColumn(outboundButton, 2);

            var editButton = new Button
            {
                Tag = rule,
                Width = 32,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 13 }
            };
            ToolTipService.SetToolTip(editButton, "编辑匹配规则");
            editButton.Click += EditRuleButton_Click;
            Grid.SetColumn(editButton, 3);

            var deleteButton = new Button
            {
                Tag = rule,
                Width = 32,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Visibility = rule.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible,
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
                }
            };
            ToolTipService.SetToolTip(deleteButton, "删除规则");
            deleteButton.Click += DeleteRuleButton_Click;
            Grid.SetColumn(deleteButton, 4);

            root.Children.Add(icon);
            root.Children.Add(textPanel);
            root.Children.Add(outboundButton);
            root.Children.Add(editButton);
            root.Children.Add(deleteButton);

            args.ItemContainer.ContentTemplate = null;
            args.ItemContainer.Content = root;
            args.Handled = true;
        }

        private FrameworkElement CreateRuleIcon(RoutingRuleItem rule)
        {
            var fallbackIcon = new FontIcon
            {
                Glyph = rule.IsBuiltIn ? "\uE774" : "\uE8A5",
                FontSize = 18,
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrWhiteSpace(rule.IconUrl) &&
                Uri.TryCreate(rule.IconUrl, UriKind.Absolute, out var iconUri))
            {
                var image = new Image
                {
                    Width = 30,
                    Height = 30,
                    Stretch = Stretch.Uniform,
                    Visibility = Visibility.Collapsed,
                    VerticalAlignment = VerticalAlignment.Center
                };
                image.ImageOpened += (_, _) =>
                {
                    image.Visibility = Visibility.Visible;
                    fallbackIcon.Visibility = Visibility.Collapsed;
                };
                image.ImageFailed += (_, _) =>
                {
                    image.Visibility = Visibility.Collapsed;
                    fallbackIcon.Visibility = Visibility.Visible;
                };
                image.Source = new BitmapImage(iconUri);

                var grid = new Grid { Width = 30, Height = 30 };
                grid.Children.Add(fallbackIcon);
                grid.Children.Add(image);
                return grid;
            }

            return fallbackIcon;
        }

        private Button CreateOutboundButton(RoutingRuleItem rule)
        {
            var button = new Button
            {
                MinWidth = 160,
                Padding = new Thickness(12, 7, 12, 7),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center
            };
            SetOutboundButtonContent(button, ViewModel.ResolveItemForTag(rule.OutboundTag));

            var flyout = new MenuFlyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
            };

            foreach (var action in ViewModel.AvailableActions)
            {
                if (action.Tag == "separator")
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    continue;
                }

                var menuItem = new RadioMenuFlyoutItem
                {
                    Text = action.Label,
                    GroupName = rule.Id,
                    IsChecked = action.Tag == rule.OutboundTag
                };
                if (!string.IsNullOrEmpty(action.Glyph))
                    menuItem.Icon = new FontIcon { Glyph = action.Glyph };

                menuItem.Click += async (_, _) =>
                {
                    if (rule.OutboundTag == action.Tag) return;

                    rule.OutboundTag = action.Tag;
                    SetOutboundButtonContent(button, action);
                    await SaveAndApplyAsync();
                };

                flyout.Items.Add(menuItem);
            }

            button.Flyout = flyout;
            return button;
        }

        private static void SetOutboundButtonContent(Button button, RuleSetActionItem item)
        {
            button.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = item.Glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = item.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                    new FontIcon { Glyph = "\uE70D", FontSize = 9, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }

        private async void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomRulesDialog
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
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

            var result = await dialog.ShowAsync();
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
                Title = "恢复默认规则",
                Content = "这会恢复默认应用规则和默认顺序，不影响自定义规则。",
                PrimaryButtonText = "恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
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
            await ViewModel.ApplyCustomRulesAsync();
        }

        private static string FormatRuleType(string type) => type switch
        {
            "ip" => "IP",
            "process" => "进程",
            "mixed" => "混合",
            _ => "域名"
        };

        private static string GetRuleSubtitle(RoutingRuleItem rule)
        {
            if (rule.IsBuiltIn)
                return rule.Description;

            return $"{FormatRuleType(rule.Type)} · {rule.Match}";
        }
    }
}
