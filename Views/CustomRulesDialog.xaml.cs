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
        // Pre-allocated Fluent 2 Light/Dark adaptive tinted semantic badge brushes (zero per-row allocation)
        private static readonly SolidColorBrush LightBlueBg   = new(Color.FromArgb(28,   0,  95, 184));
        private static readonly SolidColorBrush LightBlueFg   = new(Color.FromArgb(255,  0,  95, 184));
        private static readonly SolidColorBrush DarkBlueBg    = new(Color.FromArgb(38,  96, 205, 255));
        private static readonly SolidColorBrush DarkBlueFg    = new(Color.FromArgb(255, 96, 205, 255));

        private static readonly SolidColorBrush LightPurpleBg = new(Color.FromArgb(28, 109,  40, 217));
        private static readonly SolidColorBrush LightPurpleFg = new(Color.FromArgb(255,109,  40, 217));
        private static readonly SolidColorBrush DarkPurpleBg  = new(Color.FromArgb(38, 139,  92, 246));
        private static readonly SolidColorBrush DarkPurpleFg  = new(Color.FromArgb(255,196, 181, 253));

        private static readonly SolidColorBrush LightAmberBg  = new(Color.FromArgb(30, 157,  93,   0));
        private static readonly SolidColorBrush LightAmberFg  = new(Color.FromArgb(255,157,  93,   0));
        private static readonly SolidColorBrush DarkAmberBg   = new(Color.FromArgb(36, 252, 225,   0));
        private static readonly SolidColorBrush DarkAmberFg   = new(Color.FromArgb(255,252, 225,   0));

        private static readonly SolidColorBrush LightGreenBg  = new(Color.FromArgb(28,  15, 123,  15));
        private static readonly SolidColorBrush LightGreenFg  = new(Color.FromArgb(255, 15, 123,  15));
        private static readonly SolidColorBrush DarkGreenBg   = new(Color.FromArgb(38, 108, 203,  95));
        private static readonly SolidColorBrush DarkGreenFg   = new(Color.FromArgb(255,108, 203,  95));

        private static readonly SolidColorBrush LightRedBg    = new(Color.FromArgb(28, 196,  43,  28));
        private static readonly SolidColorBrush LightRedFg    = new(Color.FromArgb(255,196,  43,  28));
        private static readonly SolidColorBrush DarkRedBg     = new(Color.FromArgb(38, 255, 153, 164));
        private static readonly SolidColorBrush DarkRedFg     = new(Color.FromArgb(255,255, 153, 164));

        private sealed class CustomRuleRowHolder
        {
            public required Grid Root { get; init; }
            public required ToggleSwitch Toggle { get; init; }
            public required Border TypeBadge { get; init; }
            public required TextBlock TypeText { get; init; }
            public required StackPanel TextPanel { get; init; }
            public required TextBlock RemarkText { get; init; }
            public required TextBlock MatchText { get; init; }
            public required Border OutBadge { get; init; }
            public required TextBlock OutText { get; init; }
            public required Button EditBtn { get; init; }
            public required Button DeleteBtn { get; init; }
            public required FontIcon DeleteIcon { get; init; }
            public CustomRule? CurrentRule { get; set; }
            public bool SuppressToggle { get; set; }
        }

        // ─── State ───────────────────────────────────────────────────────
        private readonly ObservableCollection<CustomRule> _rules = new();
        private readonly ObservableCollection<CustomRule> _filteredRules = new();
        private readonly System.Collections.Generic.Dictionary<string, string> _nodeNameMap = new(StringComparer.Ordinal);

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

            var nodes = NodesManager.Instance.GetNodesSnapshot();
            _nodeNameMap.Clear();
            foreach (var n in nodes)
            {
                _nodeNameMap[n.Id] = string.IsNullOrWhiteSpace(n.Name) ? L.CustomRules_NodeFallback : n.Name;
            }

            RefreshListView();
            PopulateOutbounds(nodes);
            PopulateTemplates();

            this.PrimaryButtonClick += CustomRulesDialog_PrimaryButtonClick;
        }

        private void PopulateOutbounds(System.Collections.Generic.List<PersistedNode> nodes)
        {
            PopulateOutboundComboBox(FormOutboundComboBox, nodes);
            PopulateOutboundComboBox(TemplateOutboundComboBox, nodes);
        }

        private static void PopulateOutboundComboBox(ComboBox comboBox, System.Collections.Generic.List<PersistedNode> nodes)
        {
            comboBox.Items.Clear();
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionProxy,  Tag = "proxy" });
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionDirect, Tag = "direct" });
            comboBox.Items.Add(new ComboBoxItem { Content = L.Routing_ActionBlock,  Tag = "block" });

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

        private bool RuleMatchesSearch(CustomRule rule, string query)
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

        private void RulesListView_ContainerContentChanging(
            ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer.Content is Grid rg && rg.Tag is CustomRuleRowHolder rh)
                    rh.CurrentRule = null;
                return;
            }
            if (args.Item is not CustomRule rule) return;

            CustomRuleRowHolder holder;
            if (args.ItemContainer.Content is Grid existingGrid && existingGrid.Tag is CustomRuleRowHolder existingHolder)
            {
                holder = existingHolder;
            }
            else
            {
                holder = CreateCustomRuleRowHolder();
                args.ItemContainer.ContentTemplate = null;
                args.ItemContainer.Content = holder.Root;
            }

            bool isDark = ContentDialogExtensions.IsElementDark(this);

            holder.CurrentRule = rule;
            holder.SuppressToggle = true;
            holder.Toggle.IsOn = rule.IsEnabled;
            holder.SuppressToggle = false;

            var (typeBg, typeFg) = TypeBadgeBrushes(rule.Type, isDark);
            holder.TypeBadge.Background = typeBg;
            holder.TypeText.Foreground = typeFg;
            holder.TypeText.Text = FormatTypeLabel(rule.Type);

            holder.RemarkText.Text = string.IsNullOrWhiteSpace(rule.Remark) ? L.CustomRules_Unnamed : rule.Remark;
            holder.MatchText.Text = rule.Match;
            holder.MatchText.Foreground = isDark
                ? LookupThemedBrush("TextFillColorSecondaryBrush", true, Colors.Gray)
                : LookupThemedBrush("TextFillColorSecondaryBrush", false, Colors.DimGray);
            ToolTipService.SetToolTip(holder.TextPanel, rule.Match);

            var (outBg, outFg) = OutboundBadgeBrushes(rule.OutboundTag, isDark);
            holder.OutBadge.Background = outBg;
            holder.OutText.Foreground = outFg;
            holder.OutText.Text = FormatOutboundLabel(rule.OutboundTag);

            holder.DeleteIcon.Foreground = isDark ? DarkRedFg : LightRedFg;
            holder.EditBtn.Tag = rule;
            holder.DeleteBtn.Tag = rule;
            args.Handled = true;
        }

        private CustomRuleRowHolder CreateCustomRuleRowHolder()
        {
            var grid = new Grid { Padding = new Thickness(10, 8, 10, 8), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var toggle = new ToggleSwitch
            {
                OffContent = string.Empty,
                OnContent  = string.Empty,
                MinWidth   = 0,
                Width      = 42,
                VerticalAlignment = VerticalAlignment.Center
            };
            var toggleHost = new Grid
            {
                Width = 50,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Center
            };
            toggleHost.Children.Add(toggle);
            Grid.SetColumn(toggleHost, 0);

            var (typeBadge, typeText) = CreateReusableBadge();
            Grid.SetColumn(typeBadge, 1);

            var remarkText = new TextBlock
            {
                FontSize     = 13,
                FontWeight   = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var matchText = new TextBlock
            {
                FontSize     = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var textPanel = new StackPanel
            {
                Spacing             = 2,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth            = 0
            };
            textPanel.Children.Add(remarkText);
            textPanel.Children.Add(matchText);
            Grid.SetColumn(textPanel, 2);

            var (outBadge, outText) = CreateReusableBadge(128);
            Grid.SetColumn(outBadge, 3);

            Style? subtleBtnStyle = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var sObj) ? sObj as Style : null;

            var editBtn = new Button
            {
                Width             = 32,
                Height            = 32,
                Padding           = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content           = new FontIcon { Glyph = "\uE70F", FontSize = 13 }
            };
            if (subtleBtnStyle != null) editBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(editBtn, L.CustomRules_EditTooltip);
            editBtn.Click += EditRuleButton_Click;
            Grid.SetColumn(editBtn, 4);

            var deleteIcon = new FontIcon
            {
                Glyph    = "\uE74D",
                FontSize = 13
            };
            var deleteBtn = new Button
            {
                Width             = 32,
                Height            = 32,
                Padding           = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Content           = deleteIcon
            };
            if (subtleBtnStyle != null) deleteBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(deleteBtn, L.CustomRules_DelTooltip);
            deleteBtn.Click += DeleteRuleButton_Click;
            Grid.SetColumn(deleteBtn, 5);

            grid.Children.Add(toggleHost);
            grid.Children.Add(typeBadge);
            grid.Children.Add(textPanel);
            grid.Children.Add(outBadge);
            grid.Children.Add(editBtn);
            grid.Children.Add(deleteBtn);

            var holder = new CustomRuleRowHolder
            {
                Root = grid,
                Toggle = toggle,
                TypeBadge = typeBadge,
                TypeText = typeText,
                TextPanel = textPanel,
                RemarkText = remarkText,
                MatchText = matchText,
                OutBadge = outBadge,
                OutText = outText,
                EditBtn = editBtn,
                DeleteBtn = deleteBtn,
                DeleteIcon = deleteIcon
            };

            toggle.Toggled += (s, _) =>
            {
                if (!holder.SuppressToggle && holder.CurrentRule != null)
                    holder.CurrentRule.IsEnabled = ((ToggleSwitch)s).IsOn;
            };

            grid.Tag = holder;
            return holder;
        }

        private static (Border Badge, TextBlock Label) CreateReusableBadge(double maxWidth = 0)
        {
            var label = new TextBlock
            {
                FontSize     = 11,
                FontWeight   = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var border = new Border
            {
                CornerRadius      = new CornerRadius(4),
                Padding           = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child             = label
            };
            if (maxWidth > 0)
                border.MaxWidth = maxWidth;

            return (border, label);
        }

        private static (SolidColorBrush Bg, SolidColorBrush Fg) TypeBadgeBrushes(string type, bool isDark) => type switch
        {
            "ip"      => isDark ? (DarkBlueBg, DarkBlueFg)     : (LightBlueBg, LightBlueFg),
            "process" => isDark ? (DarkPurpleBg, DarkPurpleFg) : (LightPurpleBg, LightPurpleFg),
            "mixed"   => isDark ? (DarkAmberBg, DarkAmberFg)   : (LightAmberBg, LightAmberFg),
            _         => isDark ? (DarkGreenBg, DarkGreenFg)   : (LightGreenBg, LightGreenFg)
        };

        private static (SolidColorBrush Bg, SolidColorBrush Fg) OutboundBadgeBrushes(string outbound, bool isDark) => outbound switch
        {
            "direct" => isDark ? (DarkGreenBg, DarkGreenFg) : (LightGreenBg, LightGreenFg),
            "block"  => isDark ? (DarkRedBg, DarkRedFg)     : (LightRedBg, LightRedFg),
            _        => isDark ? (DarkBlueBg, DarkBlueFg)   : (LightBlueBg, LightBlueFg)
        };

        private static Brush LookupThemedBrush(string key, bool wantDark, Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, wantDark, fallback);

        private static string FormatTypeLabel(string type) => type switch
        {
            "ip"      => "IP",
            "process" => L.Routing_TypeProcess,
            "mixed"   => L.Routing_TypeMixed,
            _         => L.Routing_TypeDomain
        };

        private string FormatOutboundLabel(string outbound) => outbound switch
        {
            "direct"  => L.Routing_ActionDirect,
            "block"   => L.Routing_ActionBlock,
            "urltest" => L.Routing_BadgeAuto,
            _ when TryResolveNodeName(outbound, out var nodeName) => nodeName,
            _         => L.Routing_BadgeProxy
        };

        private bool TryResolveNodeName(string outbound, out string nodeName)
        {
            var nodeId = outbound.StartsWith("node:", StringComparison.OrdinalIgnoreCase)
                ? outbound.Substring(5)
                : outbound;

            if (_nodeNameMap.TryGetValue(nodeId, out var resolved))
            {
                nodeName = resolved;
                return true;
            }

            nodeName = string.Empty;
            return false;
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

        private List<LocalProcessResolver.RunningAppChoice> _runningApps = new();
        private bool _runningAppsLoaded;
        private bool _runningAppsLoading;

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
                    if (FormRunningAppsPanel != null) FormRunningAppsPanel.Visibility = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhDomain;
                    FormHintText.Text                = L.CustomRules_HintDomain;
                    break;
                case "ip":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    if (FormRunningAppsPanel != null) FormRunningAppsPanel.Visibility = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhIp;
                    FormHintText.Text                = L.CustomRules_HintIp;
                    break;
                case "process":
                    FormBrowsePanel.Visibility       = Visibility.Visible;
                    if (FormRunningAppsPanel != null)
                    {
                        FormRunningAppsPanel.Visibility = Visibility.Visible;
                        if (FormRunningAppsHeaderText != null)
                            FormRunningAppsHeaderText.Text = L.AddRule_RunningAppsLabel;
                        _ = LoadFormRunningAppsAsync(forceRefresh: false);
                    }
                    FormMatchTextBox.PlaceholderText = L.CustomRules_PhProcess;
                    FormHintText.Text                = L.CustomRules_HintProcess;
                    break;
                case "mixed":
                    FormBrowsePanel.Visibility       = Visibility.Collapsed;
                    if (FormRunningAppsPanel != null) FormRunningAppsPanel.Visibility = Visibility.Collapsed;
                    FormMatchTextBox.PlaceholderText = "domain_suffix:youtube.com, geosite:youtube, ip_cidr:1.1.1.1/32";
                    FormHintText.Text                = L.CustomRules_HintMixed;
                    break;
            }
        }

        private void FormMatchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string type = (FormTypeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "domain";
            if (type == "process" && _runningAppsLoaded)
            {
                RenderFormRunningAppsList();
            }
        }

        private void FormRefreshRunningAppsButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadFormRunningAppsAsync(forceRefresh: true);
        }

        private async Task LoadFormRunningAppsAsync(bool forceRefresh)
        {
            if (_runningAppsLoading || (_runningAppsLoaded && !forceRefresh))
            {
                if (_runningAppsLoaded) RenderFormRunningAppsList();
                return;
            }

            _runningAppsLoading = true;
            if (FormRunningAppsStatusText != null)
            {
                FormRunningAppsStatusText.Text = L.AddRule_RunningAppsLoading;
                FormRunningAppsStatusText.Visibility = Visibility.Visible;
            }
            if (FormRunningAppsBorder != null)
            {
                FormRunningAppsBorder.Visibility = Visibility.Collapsed;
            }

            try
            {
                _runningApps = await Task.Run(() => LocalProcessResolver.GetRunningUserApps());
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

            string type = (FormTypeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "domain";
            if (type == "process")
            {
                RenderFormRunningAppsList();
            }
        }

        private void RenderFormRunningAppsList()
        {
            if (FormRunningAppsListPanel == null || FormRunningAppsBorder == null || FormRunningAppsStatusText == null)
                return;

            FormRunningAppsListPanel.Children.Clear();

            if (_runningApps.Count == 0)
            {
                FormRunningAppsStatusText.Text = L.AddRule_RunningAppsEmpty;
                FormRunningAppsStatusText.Visibility = Visibility.Visible;
                FormRunningAppsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            string raw = FormMatchTextBox?.Text ?? string.Empty;
            int lastComma = raw.LastIndexOf(',');
            string filter = (lastComma >= 0 ? raw[(lastComma + 1)..] : raw).Trim();

            var matched = string.IsNullOrEmpty(filter)
                ? _runningApps
                : _runningApps.Where(a =>
                    a.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(filter) &&
                _runningApps.Any(a => string.Equals(a.ProcessName, filter, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(a.ExecutablePath, filter, StringComparison.OrdinalIgnoreCase)))
            {
                matched = _runningApps;
            }

            if (matched.Count == 0)
            {
                FormRunningAppsStatusText.Text = string.Format(L.AddRule_RunningAppsNoMatchFmt, filter);
                FormRunningAppsStatusText.Visibility = Visibility.Visible;
                FormRunningAppsBorder.Visibility = Visibility.Collapsed;
                return;
            }

            FormRunningAppsStatusText.Visibility = Visibility.Collapsed;
            FormRunningAppsBorder.Visibility = Visibility.Visible;

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
                btn.Click += FormRunningAppItem_Click;

                FormRunningAppsListPanel.Children.Add(btn);
            }
        }

        private void FormRunningAppItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not LocalProcessResolver.RunningAppChoice app)
                return;

            bool useFullPath = (FormBrowseFormatComboBox.SelectedItem as ComboBoxItem)
                               ?.Tag?.ToString() == "path";
            string value = useFullPath ? app.ExecutablePath : app.ProcessName;
            string current = FormMatchTextBox.Text ?? string.Empty;

            var parts = current
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

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

            FormMatchTextBox.Text = string.Join(", ", parts);
            FormMatchTextBox.SelectionStart = FormMatchTextBox.Text.Length;
        }

        // ─── Form: Browse exe ────────────────────────────────────────────

        private async void FormBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainWindow.Instance == null) return;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                var selectedPath = await Helpers.Win32FilePickerHelper.PickSingleFileAsync(
                    hwnd,
                    filters: [("Executable (*.exe)", "*.exe"), (L.FilePicker_AllFiles, "*.*")]);
                if (string.IsNullOrWhiteSpace(selectedPath)) return;

                bool useFullPath = (FormBrowseFormatComboBox.SelectedItem as ComboBoxItem)
                                   ?.Tag?.ToString() == "path";

                string value    = useFullPath ? selectedPath : Path.GetFileName(selectedPath);
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
