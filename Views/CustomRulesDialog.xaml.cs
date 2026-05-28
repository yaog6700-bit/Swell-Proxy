using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.Models;
using AnywhereWinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;

namespace AnywhereWinUI.Views
{
    public sealed partial class CustomRulesDialog : ContentDialog
    {
        // ─── State ───────────────────────────────────────────────────────
        private readonly ObservableCollection<CustomRule> _rules = new();

        // Tracks which rule is being edited (null = add mode)
        private CustomRule? _editingRule;
        private int _editingIndex = -1;

        // ─── Constructor ─────────────────────────────────────────────────

        public CustomRulesDialog()
        {
            this.InitializeComponent();

            // Load existing rules from AppSession
            foreach (var r in AppSession.Instance.CustomRules)
                _rules.Add(r.Clone());

            // Show warning if not in smart mode
            string mode = AppSession.Instance.RoutingMode;
            if (mode != "smart")
            {
                NonSmartModeBanner.Message = mode == "global"
                    ? "您正处于全局模式，自定义规则不会写入配置。切回智能分流模式后自动生效。"
                    : "您正处于直连模式，自定义规则不会写入配置。切回智能分流模式后自动生效。";
                NonSmartModeBanner.IsOpen = true;
            }

            RefreshListView();
            PopulateOutbounds();

            this.PrimaryButtonClick += CustomRulesDialog_PrimaryButtonClick;
        }

        private void PopulateOutbounds()
        {
            FormOutboundComboBox.Items.Clear();
            FormOutboundComboBox.Items.Add(new ComboBoxItem { Content = "proxy (代理)", Tag = "proxy" });
            FormOutboundComboBox.Items.Add(new ComboBoxItem { Content = "direct (直连)", Tag = "direct" });
            FormOutboundComboBox.Items.Add(new ComboBoxItem { Content = "block (阻断)", Tag = "block" });

            var nodes = NodesManager.Instance.Nodes;
            if (nodes.Count > 0)
            {
                var sep = new ComboBoxItem { Content = "──────────", IsEnabled = false };
                FormOutboundComboBox.Items.Add(sep);

                foreach (var node in nodes)
                {
                    FormOutboundComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"[节点] {node.Name}",
                        Tag = $"node:{node.Id}"
                    });
                }
            }
        }

        // ─── List Panel ──────────────────────────────────────────────────

        private void RefreshListView()
        {
            RulesListView.ItemsSource = null;
            RulesListView.ItemsSource = _rules;

            bool hasRules = _rules.Count > 0;
            EmptyState.Visibility  = hasRules ? Visibility.Collapsed : Visibility.Visible;
            RulesListView.Visibility = hasRules ? Visibility.Visible : Visibility.Collapsed;
            RuleCountText.Text = $"共 {_rules.Count} 条规则";
        }

        // Build each ListView row purely in code-behind (no DataTemplate / Converter needed)
        private void RulesListView_ContainerContentChanging(
            ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is not CustomRule rule) return;

            var grid = new Grid { Padding = new Thickness(4, 6, 4, 6), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Enable toggle
            var toggle = new ToggleSwitch
            {
                IsOn = rule.IsEnabled,
                OffContent = string.Empty,
                OnContent = string.Empty,
                Width = 44,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Toggled += (s, _) => rule.IsEnabled = ((ToggleSwitch)s).IsOn;
            Grid.SetColumn(toggle, 0);

            // Type badge
            var typeBadge = MakeBadge(rule.Type, TypeBadgeColor(rule.Type));
            Grid.SetColumn(typeBadge, 1);

            // Match text
            var matchText = new TextBlock
            {
                Text = rule.Match,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(matchText, 2);

            // Outbound badge
            var outBadge = MakeBadge(rule.OutboundTag, OutboundBadgeColor(rule.OutboundTag));
            Grid.SetColumn(outBadge, 3);

            // Edit button
            var editBtn = new Button
            {
                Tag = rule,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon { Glyph = "\uE70F", FontSize = 13 }
            };
            ToolTipService.SetToolTip(editBtn, "编辑");
            editBtn.Click += EditRuleButton_Click;
            Grid.SetColumn(editBtn, 4);

            // Delete button
            var deleteBtn = new Button
            {
                Tag = rule,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
                }
            };
            ToolTipService.SetToolTip(deleteBtn, "删除");
            deleteBtn.Click += DeleteRuleButton_Click;
            Grid.SetColumn(deleteBtn, 5);

            grid.Children.Add(toggle);
            grid.Children.Add(typeBadge);
            grid.Children.Add(matchText);
            grid.Children.Add(outBadge);
            grid.Children.Add(editBtn);
            grid.Children.Add(deleteBtn);

            args.ItemContainer.ContentTemplate = null;
            args.ItemContainer.Content = grid;
            args.Handled = true;
        }

        private static Border MakeBadge(string text, Color color)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(color),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White)
                }
            };
        }

        private static Color TypeBadgeColor(string type) => type switch
        {
            "ip"      => Color.FromArgb(255, 0, 120, 215),
            "process" => Color.FromArgb(255, 107, 79, 187),
            _         => Color.FromArgb(255, 16, 124, 16)
        };

        private static Color OutboundBadgeColor(string outbound) => outbound switch
        {
            "direct" => Color.FromArgb(255, 0, 153, 92),
            "block"  => Color.FromArgb(255, 196, 43, 28),
            _        => Color.FromArgb(255, 0, 103, 192)
        };

        // ─── List Actions ────────────────────────────────────────────────

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            _editingRule  = null;
            _editingIndex = -1;
            OpenForm(isEdit: false);
        }

        private void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not CustomRule rule) return;
            _editingRule  = rule;
            _editingIndex = _rules.IndexOf(rule);
            OpenForm(isEdit: true, rule);
        }

        private void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not CustomRule rule) return;
            _rules.Remove(rule);
            RefreshListView();
        }

        // ─── Panel Switching ─────────────────────────────────────────────

        /// <summary>Switch from list panel to the inline edit/add form.</summary>
        private void OpenForm(bool isEdit, CustomRule? rule = null)
        {
            // Reset form state
            FormErrorText.Visibility = Visibility.Collapsed;
            FormMatchTextBox.Text    = rule?.Match ?? string.Empty;
            FormTitleText.Text       = isEdit ? "编辑规则" : "添加规则";

            // Type
            int typeIdx = rule?.Type switch
            {
                "ip"      => 1,
                "process" => 2,
                _         => 0
            };
            FormTypeComboBox.SelectedIndex = typeIdx;
            UpdateFormHints(rule?.Type ?? "domain");

            // Outbound
            FormOutboundComboBox.SelectedIndex = 0; // Default to proxy
            string targetOutbound = rule?.OutboundTag ?? "proxy";
            for (int i = 0; i < FormOutboundComboBox.Items.Count; i++)
            {
                if (FormOutboundComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == targetOutbound)
                {
                    FormOutboundComboBox.SelectedIndex = i;
                    break;
                }
            }

            // Hide dialog buttons while in form mode — the form has its own Confirm/Cancel
            IsPrimaryButtonEnabled = false;
            PrimaryButtonText      = string.Empty;
            CloseButtonText        = string.Empty;

            ListPanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility = Visibility.Visible;
            FormMatchTextBox.Focus(FocusState.Programmatic);
        }

        /// <summary>Return to the list panel.</summary>
        private void CloseForm()
        {
            FormPanel.Visibility = Visibility.Collapsed;
            ListPanel.Visibility = Visibility.Visible;

            IsPrimaryButtonEnabled = true;
            PrimaryButtonText      = "保存";
            CloseButtonText        = "取消";
        }

        private void FormBackButton_Click(object sender, RoutedEventArgs e) => CloseForm();
        private void FormCancelButton_Click(object sender, RoutedEventArgs e) => CloseForm();

        // ─── Form: Type change ───────────────────────────────────────────

        private void FormTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormTypeComboBox.SelectedItem is not ComboBoxItem item) return;
            UpdateFormHints(item.Tag?.ToString() ?? "domain");
        }

        private void UpdateFormHints(string type)
        {
            // Guard: controls may be null if called during InitializeComponent()
            if (FormBrowsePanel == null || FormMatchTextBox == null || FormHintText == null) return;

            switch (type)
            {
                case "domain":
                    FormBrowsePanel.Visibility   = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "youtube.com 或 geosite:cn";
                    FormHintText.Text            = "支持精确匹配、regex:、geosite: 前缀";
                    break;
                case "ip":
                    FormBrowsePanel.Visibility   = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "192.168.0.0/16 或 geoip:cn";
                    FormHintText.Text            = "支持 CIDR 格式，多个用逗号分隔";
                    break;
                case "process":
                    FormBrowsePanel.Visibility   = Visibility.Visible;
                    FormMatchTextBox.PlaceholderText = "chrome.exe 或完整路径";
                    FormHintText.Text            = "填写进程名或完整路径，多个用逗号分隔";
                    break;
            }
        }

        // ─── Form: Browse exe ────────────────────────────────────────────

        private async void FormBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                bool useFullPath = (FormBrowseFormatComboBox.SelectedItem as ComboBoxItem)
                                   ?.Tag?.ToString() == "path";

                string value = useFullPath ? file.Path : Path.GetFileName(file.Path);

                string existing = FormMatchTextBox.Text.Trim();
                FormMatchTextBox.Text = string.IsNullOrEmpty(existing)
                    ? value
                    : $"{existing},{value}";
            }
            catch { /* ignore picker cancel */ }
        }

        // ─── Form: Confirm ───────────────────────────────────────────────

        private void FormConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            FormErrorText.Visibility = Visibility.Collapsed;

            string match = FormMatchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(match))
            {
                FormErrorText.Text       = "请填写匹配内容";
                FormErrorText.Visibility = Visibility.Visible;
                return;
            }

            string type     = (FormTypeComboBox.SelectedItem    as ComboBoxItem)?.Tag?.ToString() ?? "domain";
            string outbound = (FormOutboundComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "proxy";

            if (type == "ip")
            {
                var entries = match.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var entry in entries)
                {
                    if (entry.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase)) continue;

                    string ipPart = entry;
                    string prefixPart = null;
                    int slashIdx = entry.IndexOf('/');
                    if (slashIdx >= 0)
                    {
                        ipPart = entry.Substring(0, slashIdx);
                        prefixPart = entry.Substring(slashIdx + 1);
                    }

                    if (!System.Net.IPAddress.TryParse(ipPart, out var ipAddress))
                    {
                        FormErrorText.Text = $"无效的 IP 地址: {ipPart}";
                        FormErrorText.Visibility = Visibility.Visible;
                        return;
                    }

                    if (prefixPart != null)
                    {
                        if (!int.TryParse(prefixPart, out int prefix) || prefix < 0)
                        {
                            FormErrorText.Text = $"无效的 CIDR 前缀: {entry}";
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && prefix > 32)
                        {
                            FormErrorText.Text = $"IPv4 掩码不能大于 32: {entry}";
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && prefix > 128)
                        {
                            FormErrorText.Text = $"IPv6 掩码不能大于 128: {entry}";
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
            }

            var newRule = new CustomRule
            {
                Type       = type,
                Match      = match,
                OutboundTag = outbound,
                IsEnabled  = _editingRule?.IsEnabled ?? true
            };

            if (_editingIndex >= 0 && _editingIndex < _rules.Count)
                _rules[_editingIndex] = newRule;
            else
                _rules.Add(newRule);

            CloseForm();
            RefreshListView();
        }

        // ─── Save ─────────────────────────────────────────────────────────

        private void CustomRulesDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // If somehow the form is still open, cancel save and close form instead
            if (FormPanel.Visibility == Visibility.Visible)
            {
                args.Cancel = true;
                CloseForm();
                return;
            }

            var list = _rules.ToList();
            AppSession.Instance.CustomRules = list;

            var json = JsonSerializer.Serialize(list, AnywhereWinUI.Models.AppJsonContext.Default.ListCustomRule);
            LocalSettingsHelper.SetValue("customRules", json);
        }
    }
}
