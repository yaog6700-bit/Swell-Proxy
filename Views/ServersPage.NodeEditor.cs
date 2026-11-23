using AnywhereWinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using WinUIEx;
using AnywhereWinUI.Services;
using AnywhereWinUI.ViewModels;
using QRCoder;

namespace AnywhereWinUI.Views
{
    public sealed partial class ServersPage
    {
        // ================= Edit/Add Form Operations =================

        private async void ShareServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == selected.Id);
                if (node != null)
                {
                    if (CustomConfigService.IsCustomNode(node))
                    {
                        await CopyCustomConfigJsonWithDialogAsync(node);
                        return;
                    }

                    string link = NodesManager.ToShareUrl(node);
                    if (!string.IsNullOrEmpty(link))
                    {
                        // Copy to clipboard automatically first for user convenience
                        try
                        {
                            ClipboardMonitorService.Instance.IgnoreText(link);
                            var package = new DataPackage();
                            package.SetText(link);
                            Clipboard.SetContent(package);
                        }
                        catch { }

                        // Build the gorgeous 1:1 ported dialog
                        var dialog = new ContentDialog
                        {
                            XamlRoot = this.XamlRoot
                        };

                        // ── X close button (use FontIcon so Win10/11 automatically pick SymbolThemeFontFamily) ──
                        var closeBtn = new Button
                        {
                            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                            Width = 32,
                            Height = 32,
                            Padding = new Thickness(0),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle))
                            closeBtn.Style = (Style)subtleStyle;
                        closeBtn.Click += (_, _) => dialog.Hide();

                        // ── Header row (title + X) ──
                        var header = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var titleText = new TextBlock
                        {
                            Text = L.Servers_ShareDialogTitle,
                            FontSize = 20,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetColumn(titleText, 0);
                        Grid.SetColumn(closeBtn, 1);
                        header.Children.Add(titleText);
                        header.Children.Add(closeBtn);

                        // ── Link box ──
                        var linkBox = new TextBox
                        {
                            Text = link,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            AcceptsReturn = false,
                        };
                        // ── Name row (server name + copy icon button) ──
                        var copyIcon = new FontIcon { Glyph = "\uE8C8", FontSize = 14 };
                        var nameCopyBtn = new Button
                        {
                            Content = copyIcon,
                            Width = 28,
                            Height = 28,
                            Padding = new Thickness(0),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle2))
                            nameCopyBtn.Style = (Style)subtleStyle2;
                        ToolTipService.SetToolTip(nameCopyBtn, L.Servers_CopyLinkTooltip);

                        nameCopyBtn.Click += async (_, _) =>
                        {
                            try
                            {
                                var pkg = new DataPackage();
                                pkg.SetText(link);
                                Clipboard.SetContent(pkg);
                                copyIcon.Glyph = "\uE73E"; // Checkmark icon
                                await Task.Delay(1500);
                                copyIcon.Glyph = "\uE8C8"; // Back to copy icon
                            }
                            catch { }
                        };

                        var nameRow = new Grid { ColumnSpacing = 4 };
                        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        var nameText = new TextBlock
                        {
                            Text = node.Name,
                            FontSize = 12,
                            Opacity = 0.65,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetColumn(nameText, 0);
                        Grid.SetColumn(nameCopyBtn, 1);
                        nameRow.Children.Add(nameText);
                        nameRow.Children.Add(nameCopyBtn);

                        dialog.Content = new StackPanel
                        {
                            Width = 360,
                            Spacing = 12,
                            Children =
                            {
                                header,
                                nameRow,
                                linkBox,
                            }
                        };

                        try { await dialog.WithAppTheme().ShowAsync(); } catch { }
                    }
                    else
                    {
                        var failDialog = new ContentDialog
                        {
                            Title = L.Servers_ShareFailTitle,
                            Content = L.Servers_ShareFailMsg,
                            CloseButtonText = L.Dialog_OK,
                            XamlRoot = this.XamlRoot
                        };
                        try { await failDialog.WithAppTheme().ShowAsync(); } catch { }
                    }
                }
            }
        }

        private async void ActivateNode_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                await ViewModel.ConnectNodeCommand.ExecuteAsync(selected);
            }
        }

        private void EditManualServerDetail_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == selected.Id);
                if (node == null) return;

                if (CustomConfigService.IsCustomNode(node))
                {
                    _ = OpenCustomConfigEditorAsync(node);
                    return;
                }

                _editingNodeId = selected.Id;
                ServerNameInput.Text = node.Name;
                
                var (hostPart, portPart) = NodeLinkParser.SplitHostPort(node.Host);
                if (portPart == 0) portPart = 443;

                ServerHostInput.Text = hostPart;
                ServerPortInput.Text = portPart.ToString();

                int idx = node.Protocol.ToUpper() switch
                {
                    "VLESS" => 0,
                    "VMESS" => 1,
                    "SHADOWSOCKS" => 2,
                    "TROJAN" => 3,
                    "HYSTERIA 2" => 4,
                    "HYSTERIA" => 4,
                    "TUIC" => 5,
                    "NAIVEPROXY" => 6,
                    "NAIVE" => 6,
                    "SOCKS" => 7,
                    "ANYTLS" => 8,
                    "HTTP" => 9,
                    "HTTPS" => 9,
                    "WIREGUARD" => 10,
                    "WG" => 10,
                    "SNELL" => 11,
                    "NOWHERE" => 12,
                    _ => 0
                };
                // 暂存 Encryption 到 Tag 中，以便 UpdateFormFieldsVisibility 能够读取到要恢复的值
                ServerEncryptionInput.Tag = node.Encryption;
                ServerProtocolInput.SelectedIndex = idx;

                // Load newly supported fields
                ServerPasswordInput.Password = node.Password ?? string.Empty;
                string p = (node.Protocol ?? string.Empty).ToUpper();
                if (p == "NAIVEPROXY" || p == "NAIVE" || p == "SOCKS" || p == "HTTP" || p == "HTTPS") {
                    ServerUuidInput.Text = node.Username ?? string.Empty;
                } else {
                    ServerUuidInput.Text = node.Uuid ?? string.Empty;
                }
                ServerPathInput.Text = node.Path ?? string.Empty;
                ServerWsHostInput.Text = node.WsHost ?? string.Empty;
                ServerSniInput.Text = node.Sni ?? string.Empty;

                // WireGuard
                ServerWgPrivateKeyInput.Text = node.WgPrivateKey ?? string.Empty;
                ServerWgLocalAddressInput.Text = node.WgLocalAddress ?? string.Empty;
                ServerWgPreSharedKeyInput.Text = node.WgPreSharedKey ?? string.Empty;
                ServerWgMtuInput.Text = node.WgMtu.ToString();

                // Snell
                ServerSnellPskInput.Password = node.Password ?? string.Empty;
                int snellVerIdx = (node.SnellVersion > 0 ? node.SnellVersion : 4) switch
                {
                    6 => 0,
                    4 => 1,
                    5 => 2,
                    3 => 3,
                    2 => 4,
                    1 => 5,
                    _ => 1
                };
                ServerSnellVersionInput.SelectedIndex = snellVerIdx;
                
                string snellMode = node.SnellMode?.ToLower() ?? "default";
                ServerSnellModeInput.SelectedIndex = snellMode switch
                {
                    "unshaped" => 1,
                    "unsafe-raw" => 2,
                    _ => 0
                };
                // Load Snell obfs
                string snellObfsMode = node.ObfsType?.ToLower() ?? "none";
                ServerSnellObfsInput.SelectedIndex = snellObfsMode == "http" ? 1 : 0;
                ServerSnellObfsHostInput.Text = node.WsHost ?? string.Empty;

                // Find index of selected fingerprint
                int fpIdx = 0;
                if (!string.IsNullOrEmpty(node.Fingerprint))
                {
                    string fpLower = node.Fingerprint.ToLower();
                    fpIdx = fpLower switch
                    {
                        "chrome" => 1,
                        "firefox" => 2,
                        "safari" => 3,
                        "edge" => 4,
                        "ios" => 5,
                        "android" => 6,
                        "random" => 7,
                        "randomized" => 8,
                        _ => 0
                    };
                }
                ServerFingerprintInput.SelectedIndex = fpIdx;

                ServerAllowInsecureInput.IsChecked = node.AllowInsecure;
                ServerPublicKeyInput.Text = node.PublicKey ?? string.Empty;
                ServerShortIdInput.Text = node.ShortId ?? string.Empty;

                // Load Flow setting
                int flowIdx = 0;
                if (!string.IsNullOrEmpty(node.Flow))
                {
                    string flowLower = node.Flow.ToLower();
                    flowIdx = flowLower switch
                    {
                        "xtls-rprx-vision" => 1,
                        "xtls-rprx-vision-udp443" => 2,
                        _ => 0
                    };
                }
                if (ServerFlowInput != null) ServerFlowInput.SelectedIndex = flowIdx;

                // Load Network setting
                int netIdx = 0;
                if (!string.IsNullOrEmpty(node.Network))
                {
                    string netLower = node.Network.ToLower();
                    netIdx = netLower switch
                    {
                        "tcp" => 0,
                        "ws" => 1,
                        "httpupgrade" => 2,
                        "grpc" => 3,
                        "xhttp" => 4,
                        "kcp" => 5,
                        "quic" => 6,
                        "h2" => 7,
                        _ => 0
                    };
                }
                ServerNetworkInput.SelectedIndex = netIdx;

                // Load Security setting
                int secIdx = 0;
                if (!string.IsNullOrEmpty(node.Security))
                {
                    string secLower = node.Security.ToLower();
                    secIdx = secLower switch
                    {
                        "none" => 0,
                        "tls" => 1,
                        "reality" => 2,
                        _ => 0
                    };
                }
                ServerSecurityInput.SelectedIndex = secIdx;

                // Load new fields
                ServerAlterIdInput.Text = node.AlterId.ToString();
                
                int obfsIdx = 0;
                if (!string.IsNullOrEmpty(node.ObfsType))
                {
                    obfsIdx = node.ObfsType.ToLower() switch
                    {
                        "none" => 0,
                        "obfs" => 1,
                        _ => 0
                    };
                }
                ServerObfsTypeInput.SelectedIndex = obfsIdx;
                ServerObfsPasswordInput.Text = node.ObfsPassword ?? string.Empty;

                ServerIsShadowTlsInput.IsChecked = node.IsShadowTls;
                int stlsVersionIdx = node.ShadowTlsVersion switch
                {
                    3 => 0,
                    2 => 1,
                    1 => 2,
                    _ => 0
                };
                ServerShadowTlsVersionInput.SelectedIndex = stlsVersionIdx;
                ServerShadowTlsPasswordInput.Text = node.ShadowTlsPassword ?? string.Empty;
                // ShadowTLS SNI: only populate the dedicated field for Shadowsocks/Snell;
                // other protocols store SNI in ServerSniInput (TlsPanel)
                if ((node.Protocol?.ToUpper() == "SHADOWSOCKS" || node.Protocol?.ToUpper() == "SNELL") && node.IsShadowTls)
                    ServerShadowTlsSniInput.Text = node.Sni ?? string.Empty;

                int headerIdx = 0;
                if (!string.IsNullOrEmpty(node.HeaderType))
                {
                    headerIdx = node.HeaderType.ToLower() switch
                    {
                        "none" => 0,
                        "http" => 1,
                        _ => 0
                    };
                }
                ServerHeaderTypeInput.SelectedIndex = headerIdx;

                ServerAlpnInput.Text = node.Alpn ?? string.Empty;
                ServerSpiderXInput.Text = node.SpiderX ?? string.Empty;
                if (ServerNowhereSpecInput != null)
                    ServerNowhereSpecInput.Text = node.Spec ?? string.Empty;

                PopulateProxyChainOptions(node.Id);
                if (!string.IsNullOrEmpty(node.ProxyChainId))
                {
                    var chainItem = ServerProxyChainInput.Items
                        .Cast<ComboBoxItem>()
                        .FirstOrDefault(i => (i.Tag as string) == node.ProxyChainId);
                    if (chainItem != null)
                    {
                        ServerProxyChainInput.SelectedItem = chainItem;
                    }
                }

                UpdateFormFieldsVisibility();

                EditFormTitle.Text = L.Servers_EditNodeTitle;
                ShowPanel("edit");
            }
        }

        private async void DeleteServerDetail_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                var dialog = new ContentDialog
                {
                    Title = L.Servers_DeleteConfirmTitle,
                    Content = Loc.Format("Servers_DeleteConfirmFmt", selected.Name),
                    PrimaryButtonText = L.Dialog_OK,
                    CloseButtonText = L.Dialog_Cancel,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.WithAppTheme().ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteServerCommand.ExecuteAsync(selected);
                    ShowPanel("empty");
                }
            }
        }

        private void SaveServer_Click(object sender, RoutedEventArgs e)
        {
            string name = ServerNameInput.Text.Trim();
            string host = ServerHostInput.Text.Trim();
            string portStr = ServerPortInput.Text.Trim();
            if (string.IsNullOrEmpty(portStr)) portStr = "443";

            // Format host and port, wrapping IPv6 host in brackets for round-trip reliability
            string hostAndPort;
            if (host.Contains(':'))
            {
                string cleanHost = host.Trim('[', ']');
                hostAndPort = $"[{cleanHost}]:{portStr}";
            }
            else
            {
                hostAndPort = $"{host}:{portStr}";
            }

            var item = ServerProtocolInput.SelectedItem as ComboBoxItem;
            string protocol = item?.Content?.ToString() ?? "VLESS";
            string proto = protocol.ToUpper();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(host)) return;

            string password = ServerPasswordInput.Password.Trim();
            string uuid = ServerUuidInput.Text.Trim();
            
            var encItem = ServerEncryptionInput.SelectedItem as ComboBoxItem;
            string encryption = encItem?.Content?.ToString() ?? string.Empty;

            var netItem = ServerNetworkInput.SelectedItem as ComboBoxItem;
            string network = netItem?.Content?.ToString() ?? "tcp";

            string path = ServerPathInput.Text.Trim();
            string wsHost = ServerWsHostInput.Text.Trim();

            var secItem = ServerSecurityInput.SelectedItem as ComboBoxItem;
            string security = secItem?.Content?.ToString() ?? "none";

            string sni = ServerSniInput.Text.Trim();

            var fpItem = ServerFingerprintInput.SelectedItem as ComboBoxItem;
            string fingerprint = fpItem?.Content?.ToString() ?? "none";

            bool allowInsecure = ServerAllowInsecureInput.IsChecked == true;
            string publicKey = ServerPublicKeyInput.Text.Trim();
            string shortId = ServerShortIdInput.Text.Trim();
            var flowItem = ServerFlowInput != null ? ServerFlowInput.SelectedItem as ComboBoxItem : null;
            string flow = (flowItem?.Content?.ToString() != "none") ? (flowItem?.Content?.ToString() ?? string.Empty) : string.Empty;

            // Retrieve new field values
            int alterId = 0;
            if (int.TryParse(ServerAlterIdInput.Text.Trim(), out int alt))
            {
                alterId = alt;
            }

            var obfsItem = ServerObfsTypeInput.SelectedItem as ComboBoxItem;
            string obfsType = obfsItem?.Content?.ToString() ?? "none";
            string obfsPassword = ServerObfsPasswordInput.Text.Trim();

            bool isShadowTls = ServerIsShadowTlsInput.IsChecked == true;
            var stlsVerItem = ServerShadowTlsVersionInput.SelectedItem as ComboBoxItem;
            int shadowTlsVersion = 3;
            if (stlsVerItem != null && int.TryParse(stlsVerItem.Content.ToString(), out int stlsV))
            {
                shadowTlsVersion = stlsV;
            }
            string shadowTlsPassword = ServerShadowTlsPasswordInput.Text.Trim();
            // For Shadowsocks/Snell, SNI is entered in the ShadowTLS sub-panel;
            // for other protocols it comes from ServerSniInput in TlsPanel.
            string shadowTlsSni = ServerShadowTlsSniInput.Text.Trim();
            if ((proto == "SHADOWSOCKS" || proto == "SNELL") && isShadowTls && !string.IsNullOrEmpty(shadowTlsSni))
                sni = shadowTlsSni;

            var headerItem = ServerHeaderTypeInput.SelectedItem as ComboBoxItem;
            string headerType = headerItem?.Content?.ToString() ?? "none";

            string alpn = ServerAlpnInput.Text.Trim();
            string spiderX = ServerSpiderXInput.Text.Trim();
            string nowhereSpec = ServerNowhereSpecInput != null ? ServerNowhereSpecInput.Text.Trim() : string.Empty;

            var proxyChainItem = ServerProxyChainInput.SelectedItem as ComboBoxItem;
            string proxyChainId = proxyChainItem != null ? (proxyChainItem.Tag as string ?? string.Empty) : string.Empty;

            string wgPrivateKey = ServerWgPrivateKeyInput.Text.Trim();
            string wgLocalAddress = ServerWgLocalAddressInput.Text.Trim();
            string wgPreSharedKey = ServerWgPreSharedKeyInput.Text.Trim();
            int wgMtu = 0;
            if (int.TryParse(ServerWgMtuInput.Text.Trim(), out int parsedMtu))
            {
                wgMtu = parsedMtu;
            }

            // Snell fields
            string snellPsk = ServerSnellPskInput.Password.Trim();
            int snellVersion = 4;
            if (ServerSnellVersionInput.SelectedItem is ComboBoxItem snellVerItem &&
                int.TryParse(snellVerItem.Content.ToString(), out int parsedSnellVer))
            {
                snellVersion = parsedSnellVer;
            }
            string snellMode = "default";
            if (ServerSnellModeInput.SelectedItem is ComboBoxItem snellModeItem)
            {
                snellMode = snellModeItem.Content.ToString() ?? "default";
            }
            string snellObfsMode = "none";
            if (ServerSnellObfsInput.SelectedItem is ComboBoxItem snellObfsItem)
            {
                string c = snellObfsItem.Content.ToString() ?? "";
                snellObfsMode = c.ToUpper() == "HTTP" ? "http" : "none";
            }
            string snellObfsHost = ServerSnellObfsHostInput.Text.Trim();

            if (string.IsNullOrEmpty(_editingNodeId))
            {
                // Create manual server
                var node = new PersistedNode
                {
                    Name = name,
                    Protocol = protocol,
                    Host = hostAndPort,
                    Password = proto == "SNELL" ? snellPsk : password,
                    Uuid = (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP") ? string.Empty : uuid,
                    Username = (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP") ? uuid : string.Empty,
                    Encryption = encryption,
                    Network = network,
                    Path = path,
                    WsHost = proto == "SNELL" ? snellObfsHost : wsHost,
                    Security = security,
                    Sni = sni,
                    Fingerprint = fingerprint,
                    AllowInsecure = allowInsecure,
                    PublicKey = publicKey,
                    ShortId = shortId,
                    Flow = flow,
                    AlterId = alterId,
                    ObfsType = proto == "SNELL" ? snellObfsMode : obfsType,
                    ObfsPassword = obfsPassword,
                    IsShadowTls = isShadowTls,
                    ShadowTlsVersion = shadowTlsVersion,
                    ShadowTlsPassword = shadowTlsPassword,
                    HeaderType = headerType,
                    Alpn = alpn,
                    SpiderX = spiderX,
                    Spec = nowhereSpec,
                    ProxyChainId = proxyChainId,
                    WgPrivateKey = wgPrivateKey,
                    WgLocalAddress = wgLocalAddress,
                    WgPreSharedKey = wgPreSharedKey,
                    WgMtu = wgMtu,
                    SnellVersion = snellVersion,
                    SnellMode = proto == "SNELL" && snellVersion == 6 ? snellMode : null
                };
                NodesManager.Instance.AddManualNode(node);
            }
            else
            {
                // Update manual server
                var node = NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
                if (node != null)
                {
                    node.Name = name;
                    node.Protocol = protocol;
                    node.Host = hostAndPort;
                    node.Password = proto == "SNELL" ? snellPsk : password;
                    node.Uuid = (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP") ? string.Empty : uuid;
                    node.Username = (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP") ? uuid : string.Empty;
                    node.Encryption = encryption;
                    node.Network = network;
                    node.Path = path;
                    node.WsHost = proto == "SNELL" ? snellObfsHost : wsHost;
                    node.Security = security;
                    node.Sni = sni;
                    node.Fingerprint = fingerprint;
                    node.AllowInsecure = allowInsecure;
                    node.PublicKey = publicKey;
                    node.ShortId = shortId;
                    node.Flow = flow;
                    node.AlterId = alterId;
                    node.ObfsType = proto == "SNELL" ? snellObfsMode : obfsType;
                    node.ObfsPassword = obfsPassword;
                    node.IsShadowTls = isShadowTls;
                    node.ShadowTlsVersion = shadowTlsVersion;
                    node.ShadowTlsPassword = shadowTlsPassword;
                    // Persist ShadowTLS SNI for SS/Snell nodes
                    if ((proto == "SHADOWSOCKS" || proto == "SNELL") && isShadowTls && !string.IsNullOrEmpty(shadowTlsSni))
                        node.Sni = shadowTlsSni;
                    node.HeaderType = headerType;
                    node.Alpn = alpn;
                    node.SpiderX = spiderX;
                    node.Spec = nowhereSpec;
                    node.ProxyChainId = proxyChainId;
                    node.WgPrivateKey = wgPrivateKey;
                    node.WgLocalAddress = wgLocalAddress;
                    node.WgPreSharedKey = wgPreSharedKey;
                    node.WgMtu = wgMtu;
                    node.SnellVersion = snellVersion;
                    node.SnellMode = proto == "SNELL" && snellVersion == 6 ? snellMode : null;
                    NodesManager.Instance.Save();
                }
            }

            LoadServersList();
            ShowPanel("empty");
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                ShowPanel("detail");
            }
            else
            {
                ShowPanel("empty");
            }
        }

        private void DetailFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                ViewModel.ToggleFavoriteCommand.Execute(selected);
                
                // Keep UI sync for now until XAML bindings are fully implemented
                DetailFavoriteButton.IsChecked = selected.IsFavorite;
                DetailFavoriteIcon.Glyph = selected.IsFavorite ? "\uE735" : "\uE734";
            }
        }

        // ================= Subscription Manager =================

        private void SubSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubSegmented == null || SubAddFormPanel == null || SubListFormPanel == null) return;

            if (SubSegmented.SelectedIndex == 0)
            {
                SubAddFormPanel.Visibility = Visibility.Visible;
                SubListFormPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SubAddFormPanel.Visibility = Visibility.Collapsed;
                SubListFormPanel.Visibility = Visibility.Visible;
            }
        }

        private async void SaveSub_Click(object sender, RoutedEventArgs e)
        {
            string name = SubNameInput.Text.Trim();
            string url = SubUrlInput.Text.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

            SaveSubButton.IsEnabled = false;
            SaveSubButton.Content = L.Servers_SubSyncingBtn;

            await ViewModel.AddSubscriptionAsync(name, url);

            SaveSubButton.IsEnabled = true;
            SaveSubButton.Content = L.Servers_SubImportSyncBtn;

            CancelEdit_Click(sender, e);
        }

        public static string FormatExcludedSummary(int count)
            => count > 0 ? string.Format(L.Servers_SubExcludedCountFmt, count) : string.Empty;

        public static string RestoreExcludedButtonText
            => L.Servers_SubRestoreExcludedBtn;

        private async void RestoreSubExcluded_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PersistedSubscription sub)
            {
                if (fe is Control ctrl) ctrl.IsEnabled = false;
                try
                {
                    await ViewModel.RestoreExcludedNodesCommand.ExecuteAsync(sub);
                }
                finally
                {
                    if (fe is Control ctrl2) ctrl2.IsEnabled = true;
                }
            }
        }

        private async void UpdateSub_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PersistedSubscription sub)
            {
                btn.IsEnabled = false;
                await ViewModel.UpdateSubscriptionCommand.ExecuteAsync(sub);
                btn.IsEnabled = true;
            }
        }

        private void DeleteSub_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PersistedSubscription sub)
            {
                ViewModel.DeleteSubscriptionCommand.Execute(sub);
            }
        }

        private void ServerProtocolInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerProtocolInput?.SelectedItem is ComboBoxItem cbi &&
                string.Equals(cbi.Tag as string, "custom", StringComparison.OrdinalIgnoreCase))
            {
                string carriedName = ServerNameInput?.Text?.Trim() ?? string.Empty;
                ServerProtocolInput.SelectedIndex = 0;
                var existing = string.IsNullOrEmpty(_editingNodeId)
                    ? null
                    : NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
                _ = OpenCustomConfigEditorAsync(existing, carriedName);
                return;
            }
            UpdateFormFieldsVisibility();
        }

        private void ServerNetworkInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormFieldsVisibility();
        }

        private void ServerSecurityInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFormFieldsVisibility();
        }

        private void ServerObfsTypeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerObfsTypeInput == null || ServerObfsPasswordInput == null) return;
            if (ServerObfsTypeInput.SelectedItem is ComboBoxItem item)
            {
                string obfs = item.Content.ToString() ?? "none";
                ServerObfsPasswordInput.Visibility = (obfs != "none") ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ServerSnellVersionInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerSnellVersionInput == null || ServerSnellModeInput == null || ServerSnellObfsInput == null || ServerSnellObfsHostInput == null) return;
            
            bool isV6 = false;
            if (ServerSnellVersionInput.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out int ver))
            {
                isV6 = ver == 6;
            }

            ServerSnellModeInput.Visibility = isV6 ? Visibility.Visible : Visibility.Collapsed;
            ServerSnellObfsInput.Visibility = isV6 ? Visibility.Collapsed : Visibility.Visible;
            
            // If switching to v6, also hide the ObfsHostInput
            if (isV6)
            {
                ServerSnellObfsHostInput.Visibility = Visibility.Collapsed;
            }
            else
            {
                ServerSnellObfsInput_SelectionChanged(null!, null!);
            }
        }

        private void ServerSnellObfsInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerSnellObfsInput == null || ServerSnellObfsHostInput == null) return;
            if (ServerSnellObfsInput.SelectedItem is ComboBoxItem item)
            {
                bool isHttp = item.Content.ToString()?.ToUpper() == "HTTP";
                ServerSnellObfsHostInput.Visibility = isHttp ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ServerIsShadowTlsInput_Changed(object sender, RoutedEventArgs e)
        {
            if (ServerIsShadowTlsInput == null || ShadowTlsSubPanel == null) return;
            ShadowTlsSubPanel.Visibility = (ServerIsShadowTlsInput.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ServersListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    var orderedIds = ViewModel.FilteredServers
                        .Select(x => x.Id)
                        .ToList();
                    if (orderedIds.Count > 0)
                    {
                        ViewModel.ApplyNodeOrder(orderedIds);
                        UpdateSortMenuCheckedState();
                    }
                });
        }

        private void PopulateProxyChainOptions(string excludeId)
        {
            if (ServerProxyChainInput == null) return;
            ServerProxyChainInput.Items.Clear();

            // Add Direct Option
            var directItem = new ComboBoxItem { Content = L.Servers_ChainDirect, Tag = "" };
            ServerProxyChainInput.Items.Add(directItem);
            ServerProxyChainInput.SelectedItem = directItem;

            foreach (var node in NodesManager.Instance.Nodes)
            {
                if (!string.IsNullOrEmpty(excludeId) && node.Id == excludeId)
                    continue;
                if (CustomConfigService.IsCustomNode(node))
                    continue;

                var item = new ComboBoxItem
                {
                    Content = $"{node.Name} ({node.Protocol})",
                    Tag = node.Id
                };
                ServerProxyChainInput.Items.Add(item);
            }
        }

        private void UpdateFormFieldsVisibility()
        {
            if (ServerProtocolInput == null || 
                PasswordPanel == null || 
                UuidPanel == null || 
                EncryptionPanel == null || 
                NetworkPanel == null || 
                PathHostPanel == null || 
                SecurityPanel == null || 
                TlsPanel == null || 
                RealityPanel == null ||
                FlowPanel == null ||
                ServerUuidInput == null ||
                ServerNetworkInput == null ||
                ServerSecurityInput == null ||
                ServerEncryptionInput == null ||
                AlterIdPanel == null ||
                ObfsPanel == null ||
                ShadowTlsPanel == null ||
                HeaderTypePanel == null ||
                WireGuardPanel == null ||
                SnellPanel == null ||
                ServerWgPrivateKeyInput == null) 
                return;

            string proto = "";
            if (ServerProtocolInput.SelectedItem is ComboBoxItem cbi)
            {
                proto = (cbi.Content?.ToString() ?? "").ToUpper();
            }

            // 1. Password Panel: visible for Shadowsocks, Trojan, Hysteria 2, TUIC, NaiveProxy, SOCKS, HTTP, NOWHERE, ANYTLS
            bool showPassword = proto == "SHADOWSOCKS" || proto == "TROJAN" || proto == "HYSTERIA 2" || 
                               proto == "TUIC" || proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP" || proto == "NOWHERE" || proto == "ANYTLS";
            PasswordPanel.Visibility = showPassword ? Visibility.Visible : Visibility.Collapsed;

            // 2. UUID Panel: visible for VLESS, VMess, TUIC, NaiveProxy, SOCKS, HTTP
            bool showUuid = proto == "VLESS" || proto == "VMESS" || proto == "TUIC" || 
                            proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP";
            UuidPanel.Visibility = showUuid ? Visibility.Visible : Visibility.Collapsed;
            
            // Customize Uuid label
            if (ServerUuidInput != null)
            {
                if (proto == "NAIVEPROXY" || proto == "SOCKS" || proto == "HTTP")
                {
                    ServerUuidInput.Header = L.Servers_HeaderUsername;
                }
                else if (proto == "TUIC")
                {
                    ServerUuidInput.Header = "UUID (TUIC)";
                }
                else
                {
                    ServerUuidInput.Header = "UUID (VMess / VLESS)";
                }
            }

            // 3. Encryption Panel: visible for Shadowsocks, VMess
            bool showEncryption = proto == "SHADOWSOCKS" || proto == "VMESS";
            EncryptionPanel.Visibility = showEncryption ? Visibility.Visible : Visibility.Collapsed;

            // Update encryption choices based on protocol
            UpdateEncryptionDropdown(proto);

            // 4. Network Panel: not applicable to Hysteria 2, TUIC, NaiveProxy, NOWHERE
            bool showNetwork = proto != "HYSTERIA 2" && proto != "TUIC" && proto != "NAIVEPROXY" && proto != "NOWHERE";
            NetworkPanel.Visibility = showNetwork ? Visibility.Visible : Visibility.Collapsed;

            // 5. Security Panel: not applicable to Hysteria 2, TUIC, NaiveProxy, SOCKS, NOWHERE
            bool showSecurity = proto != "HYSTERIA 2" && proto != "TUIC" && proto != "NAIVEPROXY" && proto != "SOCKS" && proto != "NOWHERE";
            SecurityPanel.Visibility = showSecurity ? Visibility.Visible : Visibility.Collapsed;

            // Get selected Network and Security
            string network = "TCP";
            if (ServerNetworkInput != null && ServerNetworkInput.SelectedItem is ComboBoxItem netItem)
            {
                network = (netItem.Content?.ToString() ?? "").ToUpper();
            }

            string security = "NONE";
            if (ServerSecurityInput != null && ServerSecurityInput.SelectedItem is ComboBoxItem secItem)
            {
                security = (secItem.Content?.ToString() ?? "").ToUpper();
            }

            // 6. Path & WsHost Panel: visible when Network is WS, HTTPUPGRADE, GRPC, XHTTP, H2
            bool showPathHost = showNetwork && (network == "WS" || network == "HTTPUPGRADE" || network == "GRPC" || network == "XHTTP" || network == "H2");
            PathHostPanel.Visibility = showPathHost ? Visibility.Visible : Visibility.Collapsed;

            // 7. TLS Panel: visible when Security is TLS/REALITY, or protocol is Hysteria 2, TUIC, NaiveProxy, AnyTLS, NOWHERE
            bool isTls = (showSecurity && (security == "TLS" || security == "REALITY")) || 
                         proto == "HYSTERIA 2" || proto == "TUIC" || proto == "NAIVEPROXY" || proto == "ANYTLS" || proto == "NOWHERE";
            TlsPanel.Visibility = isTls ? Visibility.Visible : Visibility.Collapsed;

            // 8. Reality Panel: visible when Security is REALITY
            bool isReality = showSecurity && security == "REALITY";
            RealityPanel.Visibility = isReality ? Visibility.Visible : Visibility.Collapsed;

            // 9. Flow Panel: visible when VLESS and (Security is TLS or REALITY)
            bool isFlow = proto == "VLESS" && showSecurity && (security == "TLS" || security == "REALITY");
            FlowPanel.Visibility = isFlow ? Visibility.Visible : Visibility.Collapsed;

            // 10. AlterId Panel
            AlterIdPanel.Visibility = proto == "VMESS" ? Visibility.Visible : Visibility.Collapsed;

            // 11. Obfs Panel
            ObfsPanel.Visibility = (proto == "HYSTERIA 2" || proto == "HYSTERIA") ? Visibility.Visible : Visibility.Collapsed;

            // 12. Shadow-TLS Panel
            ShadowTlsPanel.Visibility = (proto == "SHADOWSOCKS" || proto == "SNELL") ? Visibility.Visible : Visibility.Collapsed;

            // 13. Header Type Panel
            bool showHeaderType = showNetwork && (network == "TCP" || network == "KCP" || network == "QUIC");
            HeaderTypePanel.Visibility = showHeaderType ? Visibility.Visible : Visibility.Collapsed;

            // 14. WireGuard Panel
            WireGuardPanel.Visibility = proto == "WIREGUARD" ? Visibility.Visible : Visibility.Collapsed;

            // 15. Snell Panel
            SnellPanel.Visibility = proto == "SNELL" ? Visibility.Visible : Visibility.Collapsed;

            // 16. Nowhere Panel
            if (NowherePanel != null)
                NowherePanel.Visibility = proto == "NOWHERE" ? Visibility.Visible : Visibility.Collapsed;

            // Hide standard panels that don't apply to Snell
            if (proto == "SNELL")
            {
                PasswordPanel.Visibility = Visibility.Collapsed;
                NetworkPanel.Visibility = Visibility.Collapsed;
                SecurityPanel.Visibility = Visibility.Collapsed;
                TlsPanel.Visibility = Visibility.Collapsed;
                UuidPanel.Visibility = Visibility.Collapsed;
            }

            // 16. Trigger sub-panel selection handlers
            ServerObfsTypeInput_SelectionChanged(null!, null!);
            ServerIsShadowTlsInput_Changed(null!, null!);
        }

        private void UpdateEncryptionDropdown(string proto)
        {
            if (ServerEncryptionInput == null) return;

            string currentSelected = "";
            if (ServerEncryptionInput.Tag != null && !string.IsNullOrEmpty(ServerEncryptionInput.Tag.ToString()))
            {
                currentSelected = ServerEncryptionInput.Tag.ToString() ?? "";
                ServerEncryptionInput.Tag = null; // Consume
            }
            else if (ServerEncryptionInput.SelectedItem is ComboBoxItem currentItem)
            {
                currentSelected = currentItem.Content?.ToString() ?? "";
            }

            ServerEncryptionInput.Items.Clear();

            if (proto == "SHADOWSOCKS")
            {
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-256-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "chacha20-ietf-poly1305" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-aes-256-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "2022-blake3-chacha20-poly1305" });
            }
            else if (proto == "VMESS")
            {
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "auto" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "none" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "aes-128-gcm" });
                ServerEncryptionInput.Items.Add(new ComboBoxItem { Content = "chacha20-poly1305" });
            }

            // Restore selection or select first
            if (ServerEncryptionInput.Items.Count > 0)
            {
                var match = ServerEncryptionInput.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content.ToString() == currentSelected);
                if (match != null)
                {
                    ServerEncryptionInput.SelectedItem = match;
                }
                else
                {
                    ServerEncryptionInput.SelectedIndex = 0;
                }
            }
        }
    }
}
