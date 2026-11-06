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
        private readonly ObservableCollection<CustomRule> _filteredRules = new();

        private static RuleTemplate[] GetRuleTemplates() => new RuleTemplate[]
        {
            new("Microsoft", "mixed", "domain_suffix:microsoft.com,domain_suffix:windows.com,domain_suffix:office.com,domain_suffix:office365.com,domain_suffix:live.com,domain_suffix:msftconnecttest.com,domain_suffix:msftncsi.com,domain_suffix:azure.com,domain_suffix:visualstudio.com", L.CustomRules_TplMsDesc, "proxy"),
            new("Apple", "mixed", "domain_suffix:apple.com,domain_suffix:icloud.com,domain_suffix:cdn-apple.com,domain_suffix:apple-dns.net,domain_suffix:appstore.com", L.CustomRules_TplAppleDesc, "proxy"),
            new("GitHub", "mixed", "domain_suffix:github.com,domain_suffix:github.io,domain_suffix:githubusercontent.com,domain_suffix:githubassets.com", L.CustomRules_TplGithubDesc, "proxy"),
            new("Steam", "mixed", "domain_suffix:steampowered.com,domain_suffix:steamcommunity.com,domain_suffix:steamstatic.com,domain_suffix:steamserver.net,domain_suffix:steamcontent.com", L.CustomRules_TplSteamDesc, "proxy"),
            new("Discord", "mixed", "domain_suffix:discord.com,domain_suffix:discord.gg,domain_suffix:discordapp.com,domain_suffix:discordapp.net", L.CustomRules_TplDiscordDesc, "proxy"),
            new("Spotify", "mixed", "domain_suffix:spotify.com,domain_suffix:scdn.co,domain_suffix:spotifycdn.com", L.CustomRules_TplSpotifyDesc, "proxy"),
            new("Bilibili", "mixed", "domain_suffix:bilibili.com,domain_suffix:biliapi.com,domain_suffix:biliapi.net,domain_suffix:bilivideo.com,domain_suffix:hdslb.com", L.CustomRules_TplBiliDesc, "direct"),
            new(L.CustomRules_TplLanName, "ip", "ip_cidr:10.0.0.0/8,ip_cidr:172.16.0.0/12,ip_cidr:192.168.0.0/16,ip_cidr:127.0.0.0/8,ip_cidr:fc00::/7,ip_cidr:fe80::/10", L.CustomRules_TplLanDesc, "direct"),
        };

        // Tracks which rule is being edited (null = add mode)
        private CustomRule? _editingRule;
        private int _editingIndex = -1;

        // ─── Constructor ─────────────────────────────────────────────────

        public CustomRulesDialog()
        {
            this.InitializeComponent();

            // FIX: 加载时保留原有 Id，避免每次保存都给自定义规则生成新 GUID
            foreach (var r in RoutingRulesService.LoadRules().Where(rule => !rule.IsBuiltIn))
            {
                _rules.Add(new CustomRule
                {
                    Id          = r.Id,   // 保留稳定标识
                    Remark      = (r.Name == "自定义规则" || r.Name == "Custom Rule" || r.Name == L.CustomRules_DefaultName) ? string.Empty : r.Name,
                    Type        = r.Type,
                    Match       = r.Match,
                    OutboundTag = r.OutboundTag,
                    IsEnabled   = r.IsEnabled
                });
            }

            // Show warning if not in smart mode
            string mode = AppSession.Instance.RoutingMode;
            if (mode != "smart")
            {
                NonSmartModeBanner.Message = mode == "global"
                    ? L.CustomRules_GlobalWarn
                    : L.CustomRules_DirectWarn;
                NonSmartModeBanner.IsOpen = true;
            }

            RefreshListView();
            PopulateOutbounds();
            PopulateTemplates();

            this.PrimaryButtonClick += CustomRulesDialog_PrimaryButtonClick;
        }

        private void PopulateOutbounds()
        {
            PopulateOutboundComboBox(FormOutboundComboBox);
            PopulateOutboundComboBox(TemplateOutboundComboBox);
        }

        private static void PopulateOutboundComboBox(ComboBox comboBox)
        {
            comboBox.Items.Clear();
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionProxy,  Tag = "proxy" });
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionDirect, Tag = "direct" });
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionBlock,  Tag = "block" });

            var nodes = NodesManager.Instance.Nodes;
            if (nodes.Count > 0)
            {
                var sep = new ComboBoxItem { Content = "──────────", IsEnabled = false };
                comboBox.Items.Add(sep);

                foreach (var node in nodes)
                {
                    comboBox.Items.Add(new ComboBoxItem
                    {
                        Content = string.Format(L.CustomRules_NodePrefixFmt, node.Name),
                        Tag     = $"node:{node.Id}"
                    });
                }
            }

            SelectComboBoxItemByTag(comboBox, "proxy");
        }

        // ─── List Panel ──────────────────────────────────────────────────

        private void RefreshListView()
        {
            ApplyRuleSearch();
        }

        private void SearchRuleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyRuleSearch();
        }

        private void ApplyRuleSearch()
        {
            string query = SearchRuleTextBox?.Text?.Trim() ?? string.Empty;
            _filteredRules.Clear();

            foreach (var rule in _rules)
            {
                if (string.IsNullOrWhiteSpace(query) || RuleMatchesSearch(rule, query))
                    _filteredRules.Add(rule);
            }

            bool isSearching      = !string.IsNullOrWhiteSpace(query);
            RulesListView.ItemsSource = null;
            RulesListView.ItemsSource = isSearching ? _filteredRules : _rules;

            bool hasVisibleRules       = _filteredRules.Count > 0;
            EmptyState.Visibility      = hasVisibleRules ? Visibility.Collapsed : Visibility.Visible;
            RulesListView.Visibility   = hasVisibleRules ? Visibility.Visible   : Visibility.Collapsed;
            RulesListView.CanReorderItems = !isSearching;
            RulesListView.AllowDrop       = !isSearching;
            RuleCountText.Text = !isSearching
                ? string.Format(L.CustomRules_CountFmt, _rules.Count)
                : string.Format(L.CustomRules_CountFilteredFmt, _rules.Count, _filteredRules.Count);
        }

        private static bool RuleMatchesSearch(CustomRule rule, string query)
        {
            return Contains(rule.Remark, query) ||
                   Contains(rule.Match, query) ||
                   Contains(FormatTypeLabel(rule.Type), query) ||
                   Contains(rule.Type, query) ||
                   Contains(FormatOutboundLabel(rule.OutboundTag), query) ||
                   Contains(rule.OutboundTag, query);
        }

        private static bool Contains(string? value, string query)
            => !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

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

        private void PopulateTemplates()
        {
            TemplateComboBox.Items.Clear();
            foreach (var template in GetRuleTemplates())
            {
                TemplateComboBox.Items.Add(new ComboBoxItem
                {
                    Content = template.Name,
                    Tag     = template
                });
            }

            if (TemplateComboBox.Items.Count > 0)
                TemplateComboBox.SelectedIndex = 0;
        }

        private RuleTemplate? GetSelectedTemplate()
            => (TemplateComboBox.SelectedItem as ComboBoxItem)?.Tag as RuleTemplate;

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTemplatePreview();
        }

        private void UpdateTemplatePreview()
        {
            var template = GetSelectedTemplate();
            if (template == null)
            {
                TemplateDescriptionText.Text = string.Empty;
                TemplateMatchText.Text       = string.Empty;
                return;
            }

            TemplateDescriptionText.Text = template.Description;
            TemplateMatchText.Text       = $"{FormatTypeLabel(template.Type)} · {template.Match}";
            SelectComboBoxItemByTag(TemplateOutboundComboBox, template.DefaultOutboundTag);
        }

        // Build each ListView row purely in code-behind (no DataTemplate / Converter needed)
        private void RulesListView_ContainerContentChanging(
            ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            if (args.Item is not CustomRule rule) return;

            var grid = new Grid { Padding = new Thickness(10, 8, 10, 8), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Enable toggle
            var toggle = new ToggleSwitch
            {
                IsOn       = rule.IsEnabled,
                OffContent = string.Empty,
                OnContent  = string.Empty,
                MinWidth   = 0,
                Width      = 42,
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Toggled += (s, _) => rule.IsEnabled = ((ToggleSwitch)s).IsOn;
            var toggleHost = new Grid
            {
                Width = 50,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Center
            };
            toggleHost.Children.Add(toggle);
            Grid.SetColumn(toggleHost, 0);

            var typeBadge = MakeBadge(FormatTypeLabel(rule.Type), TypeBadgeColor(rule.Type));
            Grid.SetColumn(typeBadge, 1);

            var textPanel = new StackPanel
            {
                Spacing             = 2,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth            = 0
            };
            textPanel.Children.Add(new TextBlock
            {
                Text            = string.IsNullOrWhiteSpace(rule.Remark) ? L.CustomRules_Unnamed : rule.Remark,
                FontSize        = 13,
                FontWeight      = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming    = TextTrimming.CharacterEllipsis
            });
            textPanel.Children.Add(new TextBlock
            {
                Text         = rule.Match,
                FontSize     = 12,
                Opacity      = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            ToolTipService.SetToolTip(textPanel, rule.Match);
            Grid.SetColumn(textPanel, 2);

            // Outbound badge
            var outBadge = MakeBadge(FormatOutboundLabel(rule.OutboundTag), OutboundBadgeColor(rule.OutboundTag), 120);
            Grid.SetColumn(outBadge, 3);

            // Edit button
            var editBtn = new Button
            {
                Tag               = rule,
                Width             = 36,
                Height            = 32,
                Padding           = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content           = new FontIcon { Glyph = "\uE70F", FontSize = 13 }
            };
            ToolTipService.SetToolTip(editBtn, L.CustomRules_EditTooltip);
            editBtn.Click += EditRuleButton_Click;
            Grid.SetColumn(editBtn, 4);

            // Delete button
            var deleteBtn = new Button
            {
                Tag               = rule,
                Width             = 36,
                Height            = 32,
                Padding           = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content           = new FontIcon
                {
                    Glyph      = "\uE74D",
                    FontSize   = 13,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))
                }
            };
            ToolTipService.SetToolTip(deleteBtn, L.CustomRules_DelTooltip);
            deleteBtn.Click += DeleteRuleButton_Click;
            Grid.SetColumn(deleteBtn, 5);

            grid.Children.Add(toggleHost);
            grid.Children.Add(typeBadge);
            grid.Children.Add(textPanel);
            grid.Children.Add(outBadge);
            grid.Children.Add(editBtn);
            grid.Children.Add(deleteBtn);

            args.ItemContainer.ContentTemplate = null;
            args.ItemContainer.Content         = grid;
            args.Handled                       = true;
        }

        private static Border MakeBadge(string text, Color color, double maxWidth = 0)
        {
            var border = new Border
            {
                CornerRadius      = new CornerRadius(4),
                Padding           = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background        = new SolidColorBrush(color),
                Child             = new TextBlock
                {
                    Text         = text,
                    FontSize     = 11,
                    FontWeight   = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground   = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };

            if (maxWidth > 0)
                border.MaxWidth = maxWidth;

            return border;
        }

        private static Color TypeBadgeColor(string type) => type switch
        {
            "ip"      => Color.FromArgb(255, 0,   120, 215),
            "process" => Color.FromArgb(255, 107, 79,  187),
            "mixed"   => Color.FromArgb(255, 96,  74,  123),
            _         => Color.FromArgb(255, 16,  124, 16)
        };

        private static Color OutboundBadgeColor(string outbound) => outbound switch
        {
            "direct" => Color.FromArgb(255, 0,   153, 92),
            "block"  => Color.FromArgb(255, 196, 43,  28),
            _        => Color.FromArgb(255, 0,   103, 192)
        };

        private static string FormatTypeLabel(string type) => type switch
        {
            "ip"      => "IP",
            "process" => L.Routing_TypeProcess,
            "mixed"   => L.Routing_TypeMixed,
            _         => L.Routing_TypeDomain
        };

        private static string FormatOutboundLabel(string outbound) => outbound switch
        {
            "direct"  => L.Routing_ActionDirect,
            "block"   => L.Routing_ActionBlock,
            "urltest" => L.Routing_BadgeAuto,
            _ when TryResolveNodeName(outbound, out var nodeName) => nodeName,
            _         => L.Routing_BadgeProxy
        };

        private static bool TryResolveNodeName(string outbound, out string nodeName)
        {
            var nodeId = outbound.StartsWith("node:", StringComparison.OrdinalIgnoreCase)
                ? outbound.Substring(5)
                : outbound;

            var node = NodesManager.Instance.Nodes.Find(n => n.Id == nodeId);
            if (node == null)
            {
                nodeName = string.Empty;
                return false;
            }

            nodeName = string.IsNullOrWhiteSpace(node.Name) ? L.CustomRules_NodeFallback : node.Name;
            return true;
        }

        // ─── List Actions ────────────────────────────────────────────────

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            _editingRule  = null;
            _editingIndex = -1;
            OpenForm(isEdit: false);
        }

        private void TemplateRuleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTemplatePanel();
        }

        private void TemplateBackButton_Click(object sender, RoutedEventArgs e)
        {
            CloseTemplatePanel();
        }

        private void TemplateConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            TemplateErrorText.Visibility = Visibility.Collapsed;

            var template = GetSelectedTemplate();
            if (template == null)
            {
                TemplateErrorText.Text       = L.CustomRules_SelectTplErr;
                TemplateErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (_rules.Any(rule =>
                    string.Equals(rule.Type,  template.Type,  StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.Match, template.Match, StringComparison.OrdinalIgnoreCase)))
            {
                TemplateErrorText.Text       = L.CustomRules_DupTplErr;
                TemplateErrorText.Visibility = Visibility.Visible;
                return;
            }

            // FIX: 模板新建规则时立即分配 ID，后续保存不再重新生成
            _rules.Add(new CustomRule
            {
                Id          = $"custom:{Guid.NewGuid():N}",
                Remark      = template.Name,
                Type        = template.Type,
                Match       = template.Match,
                OutboundTag = (TemplateOutboundComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                              ?? template.DefaultOutboundTag,
                IsEnabled   = TemplateEnabledSwitch.IsOn
            });

            CloseTemplatePanel();
            RefreshListView();
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
            _filteredRules.Remove(rule);
            RefreshListView();
        }

        // ─── Panel Switching ─────────────────────────────────────────────

        /// <summary>Switch from list panel to the inline edit/add form.</summary>
        private void OpenForm(bool isEdit, CustomRule? rule = null)
        {
            // Reset form state
            FormErrorText.Visibility = Visibility.Collapsed;
            FormRemarkTextBox.Text   = rule?.Remark ?? string.Empty;
            FormMatchTextBox.Text    = rule?.Match  ?? string.Empty;
            FormTitleText.Text       = isEdit ? L.CustomRules_EditTitle : L.CustomRules_AddTitle;

            // Type
            int typeIdx = rule?.Type switch
            {
                "ip"      => 1,
                "process" => 2,
                "mixed"   => 3,
                _         => 0
            };
            FormTypeComboBox.SelectedIndex = typeIdx;
            UpdateFormHints(rule?.Type ?? "domain");

            // Outbound
            SelectComboBoxItemByTag(FormOutboundComboBox, rule?.OutboundTag ?? "proxy");

            // Hide dialog buttons while in form mode — the form has its own Confirm/Cancel
            IsPrimaryButtonEnabled = false;
            PrimaryButtonText      = string.Empty;
            CloseButtonText        = string.Empty;

            ListPanel.Visibility     = Visibility.Collapsed;
            TemplatePanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility     = Visibility.Visible;
            FormMatchTextBox.Focus(FocusState.Programmatic);
        }

        /// <summary>Return to the list panel.</summary>
        private void CloseForm()
        {
            FormPanel.Visibility     = Visibility.Collapsed;
            TemplatePanel.Visibility = Visibility.Collapsed;
            ListPanel.Visibility     = Visibility.Visible;

            IsPrimaryButtonEnabled = true;
            PrimaryButtonText      = L.CustomRules_SaveBtn;
            CloseButtonText        = L.Dialog_Cancel;
        }

        private void OpenTemplatePanel()
        {
            TemplateErrorText.Visibility = Visibility.Collapsed;
            TemplateEnabledSwitch.IsOn   = true;
            if (TemplateComboBox.SelectedIndex < 0 && TemplateComboBox.Items.Count > 0)
                TemplateComboBox.SelectedIndex = 0;

            UpdateTemplatePreview();

            IsPrimaryButtonEnabled = false;
            PrimaryButtonText      = string.Empty;
            CloseButtonText        = string.Empty;

            FormPanel.Visibility     = Visibility.Collapsed;
            ListPanel.Visibility     = Visibility.Collapsed;
            TemplatePanel.Visibility = Visibility.Visible;
        }

        private void CloseTemplatePanel()
        {
            TemplatePanel.Visibility = Visibility.Collapsed;
            FormPanel.Visibility     = Visibility.Collapsed;
            ListPanel.Visibility     = Visibility.Visible;

            IsPrimaryButtonEnabled = true;
            PrimaryButtonText      = L.CustomRules_SaveBtn;
            CloseButtonText        = L.Dialog_Cancel;
        }

        private void FormBackButton_Click(object sender, RoutedEventArgs e)   => CloseForm();
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
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhDomain;
                    FormHintText.Text                = L.CustomRules_HintDomain;
                    break;
                case "ip":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhIp;
                    FormHintText.Text                = L.CustomRules_HintIp;
                    break;
                case "process":
                    FormBrowsePanel.Visibility       = Visibility.Visible;
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhProcess;
                    FormHintText.Text                = L.CustomRules_HintProcess;
                    break;
                case "mixed":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "domain_suffix:youtube.com, geosite:youtube, ip_cidr:1.1.1.1/32";
                    FormHintText.Text                = L.CustomRules_HintMixed;
                    break;
            }
        }

        // ─── Form: Browse exe ────────────────────────────────────────────

        private async void FormBrowseButton_Click(object sender, RoutedEventArgs e)
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

                bool useFullPath = (FormBrowseFormatComboBox.SelectedItem as ComboBoxItem)
                                   ?.Tag?.ToString() == "path";

                string value    = useFullPath ? file.Path : Path.GetFileName(file.Path);
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

            string match  = FormMatchTextBox.Text.Trim();
            string remark = FormRemarkTextBox.Text.Trim();
            if (string.IsNullOrEmpty(match))
            {
                FormErrorText.Text       = L.CustomRules_EmptyMatchErr;
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
                    if (entry.StartsWith("ip_cidr:", StringComparison.OrdinalIgnoreCase))
                        ipPart = entry.Substring("ip_cidr:".Length).Trim();

                    string? prefixPart = null;
                    int slashIdx = ipPart.IndexOf('/');
                    if (slashIdx >= 0)
                    {
                        prefixPart = ipPart.Substring(slashIdx + 1);
                        ipPart     = ipPart.Substring(0, slashIdx);
                    }

                    if (!System.Net.IPAddress.TryParse(ipPart, out var ipAddress))
                    {
                        FormErrorText.Text       = string.Format(L.CustomRules_InvalidIpFmt, ipPart);
                        FormErrorText.Visibility = Visibility.Visible;
                        return;
                    }

                    if (prefixPart != null)
                    {
                        if (!int.TryParse(prefixPart, out int prefix) || prefix < 0)
                        {
                            FormErrorText.Text       = string.Format(L.CustomRules_InvalidCidrFmt, entry);
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && prefix > 32)
                        {
                            FormErrorText.Text       = string.Format(L.CustomRules_Ipv4MaskErrFmt, entry);
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && prefix > 128)
                        {
                            FormErrorText.Text       = string.Format(L.CustomRules_Ipv6MaskErrFmt, entry);
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
            }

            var newRule = new CustomRule
            {
                Remark      = remark,
                Type        = type,
                Match       = match,
                OutboundTag = outbound,
                IsEnabled   = _editingRule?.IsEnabled ?? true
            };

            if (_editingIndex >= 0 && _editingIndex < _rules.Count)
            {
                // FIX: 编辑模式保留原有 ID，不生成新 GUID
                newRule.Id               = _rules[_editingIndex].Id;
                _rules[_editingIndex]    = newRule;
            }
            else
            {
                // 新增模式：此处分配 ID，保存时不再重新生成
                newRule.Id = $"custom:{Guid.NewGuid():N}";
                _rules.Add(newRule);
            }

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

            if (TemplatePanel.Visibility == Visibility.Visible)
            {
                args.Cancel = true;
                CloseTemplatePanel();
                return;
            }

            var builtInRules = RoutingRulesService.LoadRules().Where(rule => rule.IsBuiltIn);
            var customRules  = _rules.Select(rule => new RoutingRuleItem
            {
                // FIX: 优先使用已有稳定 ID；仅当规则完全新增（Id 为空）时才生成新 GUID
                Id           = string.IsNullOrEmpty(rule.Id) ? $"custom:{Guid.NewGuid():N}" : rule.Id,
                Name         = string.IsNullOrWhiteSpace(rule.Remark) ? L.CustomRules_DefaultName : rule.Remark,
                Description  = L.CustomRules_DefaultDesc,
                Type         = rule.Type,
                Match        = rule.Match,
                OutboundTag  = rule.OutboundTag,
                IsEnabled    = rule.IsEnabled,
                IsBuiltIn    = false,
                MatchVersion = 0
            });

            RoutingRulesService.SaveRules(builtInRules.Concat(customRules));
        }

        private sealed record RuleTemplate(
            string Name,
            string Type,
            string Match,
            string Description,
            string DefaultOutboundTag);
    }
}
