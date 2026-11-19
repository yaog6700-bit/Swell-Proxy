using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        private List<LocalProcessResolver.RunningAppChoice> _runningApps = new();
        private bool _runningAppsLoaded;
        private bool _runningAppsLoading;

        public AddCustomRuleDialog()
        {
            InitializeComponent();

            PopulateOutbounds();
            RunningAppsHeaderText.Text = L.AddRule_RunningAppsLabel;
            TypeComboBox.SelectionChanged += TypeComboBox_SelectionChanged;
            BrowseButton.Click += BrowseButton_Click;
            RefreshRunningAppsButton.Click += (_, _) => _ = LoadRunningAppsAsync(forceRefresh: true);
            MatchTextBox.TextChanged += (_, _) =>
            {
                if (GetSelectedType() == "process" && _runningAppsLoaded)
                {
                    RenderRunningAppsList();
                }
            };
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
                    if (RunningAppsPanel != null) RunningAppsPanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhIp;
                    HintTextBlock.Text = L.CustomRules_HintIp;
                    break;

                case "process":
                    BrowsePanel.Visibility = Visibility.Visible;
                    if (RunningAppsPanel != null) RunningAppsPanel.Visibility = Visibility.Visible;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhProcess;
                    HintTextBlock.Text = L.CustomRules_HintProcess;
                    _ = LoadRunningAppsAsync(forceRefresh: false);
                    break;

                case "mixed":
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    if (RunningAppsPanel != null) RunningAppsPanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = "domain_suffix:youtube.com, domain_keyword:google, geosite:youtube, ip_cidr:1.1.1.1/32";
                    HintTextBlock.Text = L.CustomRules_HintMixed;
                    break;

                default:
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    if (RunningAppsPanel != null) RunningAppsPanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = L.CustomRules_PhDomain;
                    HintTextBlock.Text = L.CustomRules_HintDomain;
                    break;
            }
        }

        private async Task LoadRunningAppsAsync(bool forceRefresh)
        {
            if (_runningAppsLoading || (_runningAppsLoaded && !forceRefresh))
            {
                if (_runningAppsLoaded) RenderRunningAppsList();
                return;
            }

            _runningAppsLoading = true;
            if (RunningAppsStatusText != null)
            {
                RunningAppsStatusText.Text = L.AddRule_RunningAppsLoading;
                RunningAppsStatusText.Visibility = Visibility.Visible;
            }
            if (RunningAppsBorder != null)
            {
                RunningAppsBorder.Visibility = Visibility.Collapsed;
            }

            try
            {
                var apps = await Task.Run(() => LocalProcessResolver.GetRunningUserApps());
                _runningApps = apps;
                _runningAppsLoaded = true;
            }
            catch
            {
                _runningApps = new List<LocalProcessResolver.RunningAppChoice>();
                _runningAppsLoaded = true;
            }
            finally
            {
                _runningAppsLoading = false;
            }

            if (GetSelectedType() == "process")
            {
                RenderRunningAppsList();
            }
        }

        private void RenderRunningAppsList()
        {
            if (RunningAppsListPanel == null || RunningAppsBorder == null || RunningAppsStatusText == null)
                return;

            RunningAppsListPanel.Children.Clear();

            if (_runningApps.Count == 0)
            {
                RunningAppsStatusText.Text = L.AddRule_RunningAppsEmpty;
                RunningAppsStatusText.Visibility = Visibility.Visible;
                RunningAppsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            string filter = GetCurrentSearchToken();
            var matched = string.IsNullOrEmpty(filter)
                ? _runningApps
                : _runningApps.Where(a =>
                    a.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            // If user already picked an exact match, show all apps so they can easily add another.
            if (!string.IsNullOrEmpty(filter) &&
                _runningApps.Any(a => string.Equals(a.ProcessName, filter, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(a.ExecutablePath, filter, StringComparison.OrdinalIgnoreCase)))
            {
                matched = _runningApps;
            }

            if (matched.Count == 0)
            {
                RunningAppsStatusText.Text = string.Format(L.AddRule_RunningAppsNoMatchFmt, filter);
                RunningAppsStatusText.Visibility = Visibility.Visible;
                RunningAppsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            RunningAppsStatusText.Visibility = Visibility.Collapsed;
            RunningAppsBorder.Visibility = Visibility.Visible;

            foreach (var app in matched)
            {
                var rowGrid = new Grid { ColumnSpacing = 8 };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleBlock = new TextBlock
                {
                    Text = app.DisplayName,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(titleBlock, 0);

                var procBlock = new TextBlock
                {
                    Text = app.ProcessName,
                    FontSize = 11,
                    Opacity = 0.65,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(procBlock, 1);

                rowGrid.Children.Add(titleBlock);
                rowGrid.Children.Add(procBlock);

                var btn = new Button
                {
                    Content = rowGrid,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 5, 8, 5),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(4),
                    Tag = app
                };
                ToolTipService.SetToolTip(btn, app.ExecutablePath);
                btn.Click += RunningAppItem_Click;

                RunningAppsListPanel.Children.Add(btn);
            }
        }

        private string GetCurrentSearchToken()
        {
            string raw = MatchTextBox?.Text ?? string.Empty;
            int lastComma = raw.LastIndexOf(',');
            return (lastComma >= 0 ? raw[(lastComma + 1)..] : raw).Trim();
        }

        private void RunningAppItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not LocalProcessResolver.RunningAppChoice app)
                return;

            string value = GetBrowseFormat() == "path" ? app.ExecutablePath : app.ProcessName;
            string current = MatchTextBox.Text ?? string.Empty;

            var parts = current
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            // If the last token was a partial search query (not an already-selected app), replace it.
            if (parts.Count > 0 && !current.TrimEnd().EndsWith(','))
            {
                string last = parts[^1];
                bool isAlreadyExact = _runningApps.Any(a =>
                    string.Equals(a.ProcessName, last, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.ExecutablePath, last, StringComparison.OrdinalIgnoreCase));

                if (!isAlreadyExact &&
                    (app.ProcessName.Contains(last, StringComparison.OrdinalIgnoreCase) ||
                     app.DisplayName.Contains(last, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }

            if (!parts.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(value);
            }

            MatchTextBox.Text = string.Join(", ", parts);
            MatchTextBox.SelectionStart = MatchTextBox.Text.Length;
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
