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

        private static readonly RuleTemplate[] RuleTemplates =
        {
            new("Microsoft", "mixed", "domain_suffix:microsoft.com,domain_suffix:windows.com,domain_suffix:office.com,domain_suffix:office365.com,domain_suffix:live.com,domain_suffix:msftconnecttest.com,domain_suffix:msftncsi.com,domain_suffix:azure.com,domain_suffix:visualstudio.com", "Microsoft、Windows、Office、Azure、Visual Studio 等常用服务。", "proxy"),
            new("Apple", "mixed", "domain_suffix:apple.com,domain_suffix:icloud.com,domain_suffix:cdn-apple.com,domain_suffix:apple-dns.net,domain_suffix:appstore.com", "Apple、iCloud、App Store 与系统服务。", "proxy"),
            new("GitHub", "mixed", "domain_suffix:github.com,domain_suffix:github.io,domain_suffix:githubusercontent.com,domain_suffix:githubassets.com", "GitHub 网页、仓库、静态资源与 raw 内容。", "proxy"),
            new("Steam", "mixed", "domain_suffix:steampowered.com,domain_suffix:steamcommunity.com,domain_suffix:steamstatic.com,domain_suffix:steamserver.net,domain_suffix:steamcontent.com", "Steam 商店、社区与下载相关域名。", "proxy"),
            new("Discord", "mixed", "domain_suffix:discord.com,domain_suffix:discord.gg,domain_suffix:discordapp.com,domain_suffix:discordapp.net", "Discord 网页、邀请链接与语音/媒体服务。", "proxy"),
            new("Spotify", "mixed", "domain_suffix:spotify.com,domain_suffix:scdn.co,domain_suffix:spotifycdn.com", "Spotify 登录、播放与 CDN 域名。", "proxy"),
            new("Bilibili", "mixed", "domain_suffix:bilibili.com,domain_suffix:biliapi.com,domain_suffix:biliapi.net,domain_suffix:bilivideo.com,domain_suffix:hdslb.com", "哔哩哔哩网页、接口与视频 CDN。", "direct"),
            new("局域网 / 私有网段", "ip", "ip_cidr:10.0.0.0/8,ip_cidr:172.16.0.0/12,ip_cidr:192.168.0.0/16,ip_cidr:127.0.0.0/8,ip_cidr:fc00::/7,ip_cidr:fe80::/10", "常见局域网、回环与 IPv6 私有/链路本地地址。", "direct"),
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
                    Remark      = r.Name == "自定义规则" ? string.Empty : r.Name,
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
                    ? "您正处于全局模式，自定义规则不会写入配置。切回智能分流模式后自动生效。"
                    : "您正处于直连模式，自定义规则不会写入配置。切回智能分流模式后自动生效。";
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
            comboBox.Items.Add(new ComboBoxItem { Content = "默认代理", Tag = "proxy" });
            comboBox.Items.Add(new ComboBoxItem { Content = "直连",     Tag = "direct" });
            comboBox.Items.Add(new ComboBoxItem { Content = "拦截",     Tag = "block" });

            var nodes = NodesManager.Instance.Nodes;
            if (nodes.Count > 0)
            {
                var sep = new ComboBoxItem { Content = "──────────", IsEnabled = false };
                comboBox.Items.Add(sep);

                foreach (var node in nodes)
                {
                    comboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"节点：{node.Name}",
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
                ? $"共 {_rules.Count} 条规则"
                : $"共 {_rules.Count} 条规则，匹配 {_filteredRules.Count} 条";
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
            foreach (var template in RuleTemplates)
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
                Text            = string.IsNullOrWhiteSpace(rule.Remark) ? "未命名规则" : rule.Remark,
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
            ToolTipService.SetToolTip(editBtn, "编辑");
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
            ToolTipService.SetToolTip(deleteBtn, "删除");
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
            "process" => "进程",
            "mixed"   => "混合",
            _         => "域名"
        };

        private static string FormatOutboundLabel(string outbound) => outbound switch
        {
            "direct"  => "直连",
            "block"   => "拦截",
            "urltest" => "自动",
            _ when TryResolveNodeName(outbound, out var nodeName) => nodeName,
            _         => "代理"
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

            nodeName = string.IsNullOrWhiteSpace(node.Name) ? "节点" : node.Name;
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
                TemplateErrorText.Text       = "请选择规则模板";
                TemplateErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (_rules.Any(rule =>
                    string.Equals(rule.Type,  template.Type,  StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.Match, template.Match, StringComparison.OrdinalIgnoreCase)))
            {
                TemplateErrorText.Text       = "已存在相同模板规则，可直接编辑现有规则";
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
            FormTitleText.Text       = isEdit ? "编辑规则" : "添加规则";

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
            PrimaryButtonText      = "保存";
            CloseButtonText        = "取消";
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
            PrimaryButtonText      = "保存";
            CloseButtonText        = "取消";
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
                    FormMatchTextBox.PlaceholderText = "youtube.com 或 geosite:cn";
                    FormHintText.Text                = "支持精确匹配、regex:、geosite:、ruleset:https://.../*.srs 前缀";
                    break;
                case "ip":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "192.168.0.0/16 或 geoip:cn";
                    FormHintText.Text                = "支持 CIDR 格式，多个用逗号分隔";
                    break;
                case "process":
                    FormBrowsePanel.Visibility       = Visibility.Visible;
                    FormMatchTextBox.PlaceholderText = "chrome.exe 或完整路径";
                    FormHintText.Text                = "填写进程名或完整路径，多个用逗号分隔";
                    break;
                case "mixed":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "domain_suffix:youtube.com, geosite:youtube, ip_cidr:1.1.1.1/32";
                    FormHintText.Text                = "支持 domain_suffix:、domain:、domain_regex:、geosite:、geoip:、ip_cidr:、process_name:、process_path:、ruleset:https://.../*.srs";
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
                        FormErrorText.Text       = $"无效的 IP 地址: {ipPart}";
                        FormErrorText.Visibility = Visibility.Visible;
                        return;
                    }

                    if (prefixPart != null)
                    {
                        if (!int.TryParse(prefixPart, out int prefix) || prefix < 0)
                        {
                            FormErrorText.Text       = $"无效的 CIDR 前缀: {entry}";
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && prefix > 32)
                        {
                            FormErrorText.Text       = $"IPv4 掩码不能大于 32: {entry}";
                            FormErrorText.Visibility = Visibility.Visible;
                            return;
                        }
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && prefix > 128)
                        {
                            FormErrorText.Text       = $"IPv6 掩码不能大于 128: {entry}";
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
                Name         = string.IsNullOrWhiteSpace(rule.Remark) ? "自定义规则" : rule.Remark,
                Description  = "用户自定义分流规则",
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
