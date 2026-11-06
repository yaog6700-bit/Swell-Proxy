using System;
using System.IO;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;
using AnywhereWinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AnywhereWinUI.Views
{
    public sealed partial class AddCustomRuleDialog : ContentDialog
    {
        public CustomRule? EditingRule { get; set; }
        public CustomRule? ResultRule { get; private set; }

        public AddCustomRuleDialog()
        {
            InitializeComponent();

            PopulateOutbounds();
            TypeComboBox.SelectionChanged += TypeComboBox_SelectionChanged;
            BrowseButton.Click += BrowseButton_Click;
            PrimaryButtonClick += AddCustomRuleDialog_PrimaryButtonClick;
            UpdateTypeHints(GetSelectedType());
        }

        public void LoadRule(CustomRule rule)
        {
            EditingRule = rule;
            Title = L.CustomRules_EditTitle;
            PrimaryButtonText = L.CustomRules_SaveBtn;

            SelectComboBoxItemByTag(TypeComboBox, rule.Type);
            MatchTextBox.Text = rule.Match;
            SelectComboBoxItemByTag(OutboundComboBox, rule.OutboundTag);
            UpdateTypeHints(GetSelectedType());
        }

        private void PopulateOutbounds()
        {
            OutboundComboBox.Items.Clear();
            OutboundComboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionProxy, Tag = "proxy" });
            OutboundComboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionDirect, Tag = "direct" });
            OutboundComboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionBlock, Tag = "block" });

            if (NodesManager.Instance.Nodes.Count > 0)
            {
                OutboundComboBox.Items.Add(new ComboBoxItem { Content = "----------", IsEnabled = false });
                foreach (var node in NodesManager.Instance.Nodes)
                {
                    OutboundComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = node.Name,
                        Tag = $"node:{node.Id}"
                    });
                }
            }

            SelectComboBoxItemByTag(OutboundComboBox, "proxy");
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTypeHints(GetSelectedType());
        }

        private void UpdateTypeHints(string type)
        {
            if (BrowsePanel == null || MatchTextBox == null || HintTextBlock == null) return;

            switch (type)
            {
                case "ip":
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhIp;
                    HintTextBlock.Text = L.CustomRules_HintIp;
                    break;

                case "process":
                    BrowsePanel.Visibility = Visibility.Visible;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhProcess;
                    HintTextBlock.Text = L.CustomRules_HintProcess;
                    break;

                case "mixed":
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = "domain_suffix:youtube.com, domain_keyword:google, geosite:youtube, ip_cidr:1.1.1.1/32";
                    HintTextBlock.Text = L.CustomRules_HintMixed;
                    break;

                default:
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhDomain;
                    HintTextBlock.Text = L.CustomRules_HintDomain;
                    break;
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(MainWindow.Instance!.AppWindow.Id)
                {
                    SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                string value = GetBrowseFormat() == "path"
                    ? file.Path
                    : Path.GetFileName(file.Path);

                string existing = MatchTextBox.Text.Trim();
                MatchTextBox.Text = string.IsNullOrEmpty(existing)
                    ? value
                    : $"{existing},{value}";
            }
            catch
            {
                // Picker cancellation is not an error.
            }
        }

        private string GetBrowseFormat()
        {
            if (BrowseFormatComboBox.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "name";

            return "name";
        }

        private void AddCustomRuleDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            string match = MatchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(match))
            {
                ErrorText.Text = L.CustomRules_EmptyMatchErr;
                ErrorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            ResultRule = new CustomRule
            {
                Type = GetSelectedType(),
                Match = match,
                OutboundTag = GetSelectedOutbound(),
                IsEnabled = EditingRule?.IsEnabled ?? true
            };
        }

        private string GetSelectedType()
            => (TypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "domain";

        private string GetSelectedOutbound()
            => (OutboundComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "proxy";

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string? tag)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if ((comboBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == tag)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }
    }
}
