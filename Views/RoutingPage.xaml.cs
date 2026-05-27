using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AnywhereWinUI.ViewModels;
using System;
using AnywhereWinUI.Services;

namespace AnywhereWinUI.Views
{
    public sealed partial class RoutingPage : Page
    {
        public RoutingViewModel ViewModel { get; }

        public RoutingPage()
        {
            ViewModel = ((App)Application.Current).Services.GetService(typeof(RoutingViewModel)) as RoutingViewModel;
            this.InitializeComponent();

            InitializeRuleButton(RuleGoogleButton, RuleGoogleEmoji, RuleGoogleText, "RuleGoogle", ViewModel.RuleGoogleAction);
            InitializeRuleButton(RuleTelegramButton, RuleTelegramEmoji, RuleTelegramText, "RuleTelegram", ViewModel.RuleTelegramAction);
            InitializeRuleButton(RuleNetflixButton, RuleNetflixEmoji, RuleNetflixText, "RuleNetflix", ViewModel.RuleNetflixAction);
            InitializeRuleButton(RuleYouTubeButton, RuleYouTubeEmoji, RuleYouTubeText, "RuleYouTube", ViewModel.RuleYouTubeAction);
            InitializeRuleButton(RuleTikTokButton, RuleTikTokEmoji, RuleTikTokText, "RuleTikTok", ViewModel.RuleTikTokAction);
            InitializeRuleButton(RuleChatGPTButton, RuleChatGPTEmoji, RuleChatGPTText, "RuleChatGPT", ViewModel.RuleChatGPTAction);
            InitializeRuleButton(RuleClaudeButton, RuleClaudeEmoji, RuleClaudeText, "RuleClaude", ViewModel.RuleClaudeAction);
        }

        private void InitializeRuleButton(
            Button button,
            FontIcon iconBlock,
            TextBlock textBlock,
            string ruleName,
            string currentTag)
        {
            var flyout = new MenuFlyout
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
            };

            void AddRadioItem(RuleSetActionItem item)
            {
                var menuItem = new RadioMenuFlyoutItem
                {
                    Text = item.Label,
                    GroupName = ruleName,
                    IsChecked = item.Tag == currentTag
                };
                if (!string.IsNullOrEmpty(item.Glyph))
                {
                    menuItem.Icon = new FontIcon { Glyph = item.Glyph };
                }
                menuItem.Click += (s, e) =>
                {
                    UpdateButtonDisplay(iconBlock, textBlock, item);
                    ViewModel.UpdateRuleAction(ruleName, item.Tag);
                };
                flyout.Items.Add(menuItem);
            }

            foreach (var item in ViewModel.AvailableActions)
            {
                if (item.Tag == "separator")
                {
                    flyout.Items.Add(new MenuFlyoutSeparator());
                }
                else
                {
                    AddRadioItem(item);
                }
            }

            button.Flyout = flyout;
            UpdateButtonDisplay(iconBlock, textBlock, ViewModel.ResolveItemForTag(currentTag));
        }

        private static void UpdateButtonDisplay(FontIcon iconBlock, TextBlock textBlock, RuleSetActionItem item)
        {
            iconBlock.Glyph = item.Glyph;
            textBlock.Text = item.Label;
        }

        private async void AdvancedRulesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomRulesDialog
            {
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Rules saved — trigger core restart to apply new config
                await ViewModel.ApplyCustomRulesAsync();
            }
        }
    }
}
