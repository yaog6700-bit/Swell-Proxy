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
        // ================= Top Sheet Animation =================
        
        private void OpenTopSheet()
        {
            TopSheetOverlay.Visibility = Visibility.Visible;
            
            var visual = ElementCompositionPreview.GetElementVisual(TopSheetOverlay);
            var compositor = visual.Compositor;

            // 初始化位置（隐藏在上方）
            ElementCompositionPreview.SetIsTranslationEnabled(TopSheetOverlay, true);
            var properties = visual.Properties;
            
            if (properties.TryGetVector3("Translation", out var currentTranslation) == Microsoft.UI.Composition.CompositionGetValueStatus.NotFound || currentTranslation.Y == 0)
            {
                properties.InsertVector3("Translation", new System.Numerics.Vector3(0, -1000f, 0));
            }

            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.Target = "Translation";
            springAnim.FinalValue = new System.Numerics.Vector3(0, 0, 0);
            springAnim.DampingRatio = 0.75f;
            springAnim.Period = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Translation", springAnim);

            TopSheetMockChart.UploadTraffic = _realUpTraffic;
            TopSheetMockChart.DownloadTraffic = _realDownTraffic;

            _isTopSheetOpen = true;

            // 打开时更新节点名称
            var nodeName = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId)?.Name ?? "---";
            TopSheetNodeNameText.Text = nodeName;

            // 打开时立刻探测一次延迟
            if (!_isLatencyProbing)
                _ = RefreshTopSheetLatencyAsync();
        }

        private async Task RefreshTopSheetLatencyAsync()
        {
            if (_isLatencyProbing || !MainWindow.IsVisibleToUser) return;
            _isLatencyProbing = true;
            DispatcherQueue.TryEnqueue(() => TopSheetLatencyText.Text = "--- ms");
            try
            {
                var selectedId = NodesManager.Instance.SelectedNodeId;
                var node = NodesManager.Instance.GetNodesSnapshot().Find(n => n.Id == selectedId);
                if (node == null && !AppSession.Instance.IsAutoNodeSelect) { return; }

                if (!AppSession.Instance.IsAutoNodeSelect && CustomConfigService.IsCustomNode(node))
                {
                    DispatcherQueue.TryEnqueue(() => TopSheetLatencyText.Text = L.CustomCfg_ListBadge);
                    return;
                }

                // Feature N2: Prefer true end-to-end HTTP 204 URL-Test on the active "proxy" selector
                int? trueDelay = null;
                if (CoreManager.Instance.IsRunning &&
                    string.Equals(AppSession.Instance.ActiveCoreEngine, ShoesConfigBuilder.EngineSingBox, StringComparison.OrdinalIgnoreCase) &&
                    !AppSession.Instance.IsActiveNodeCustom)
                {
                    trueDelay = await SingboxApiClient.Instance.ProbeOutboundDelayAsync("proxy", 3500);
                }

                LatencyProbeResult result;
                if (trueDelay.HasValue)
                {
                    result = trueDelay.Value > 0
                        ? new LatencyProbeResult { Status = LatencyProbeStatus.Success, Milliseconds = trueDelay.Value }
                        : new LatencyProbeResult { Status = LatencyProbeStatus.Timeout };
                }
                else
                {
                    if (node == null) return;
                    if (!NodeLinkParser.TrySplitHostPort(node.Host, out string host, out int port) || string.IsNullOrWhiteSpace(host))
                    {
                        host = node.Host;
                        port = 443;
                    }
                    if (port <= 0) port = 443;

                    result = await _latencyProbeService.ProbeAsync(
                        node.Protocol, host, port,
                        TimeSpan.FromSeconds(5));
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (result.Status == LatencyProbeStatus.Success && result.Milliseconds.HasValue)
                    {
                        int ms = result.Milliseconds.Value;
                        TopSheetLatencyText.Text = $"{ms} ms";
                        TopSheetLatencyText.Foreground = ms < 150
                            ? ResolveStateBrush("SystemFillColorSuccessBrush", Colors.Green)
                            : ms < 350
                                ? ResolveStateBrush("SystemFillColorCautionBrush", Color.FromArgb(255, 251, 146, 60))
                                : ResolveStateBrush("SystemFillColorCriticalBrush", Color.FromArgb(255, 248, 113, 113));
                    }
                    else
                    {
                        TopSheetLatencyText.Text = L.Servers_PingTimeout;
                        TopSheetLatencyText.Foreground = ResolveStateBrush("SystemFillColorCriticalBrush", Colors.Gray);
                    }
                });
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => TopSheetLatencyText.Text = "--- ms");
            }
            finally
            {
                _isLatencyProbing = false;
            }
        }

        private void CloseTopSheet()
        {
            _isTopSheetOpen = false;

            var visual = ElementCompositionPreview.GetElementVisual(TopSheetOverlay);
            var compositor = visual.Compositor;

            ElementCompositionPreview.SetIsTranslationEnabled(TopSheetOverlay, true);
            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.Target = "Translation";
            springAnim.FinalValue = new System.Numerics.Vector3(0, -1000f, 0);
            springAnim.DampingRatio = 0.95f;
            springAnim.Period = TimeSpan.FromMilliseconds(300);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Translation", springAnim);
            batch.Completed += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (!_isTopSheetOpen)
                    {
                        TopSheetOverlay.Visibility = Visibility.Collapsed;
                    }
                });
            };
            batch.End();
        }

        private void CloseTopSheet_Click(object sender, RoutedEventArgs e)
        {
            CloseTopSheet();
            ReopenTopSheetButton.Visibility = Visibility.Visible;
        }

        private void ReopenTopSheetButton_Click(object sender, RoutedEventArgs e)
        {
            ReopenTopSheetButton.Visibility = Visibility.Collapsed;
            OpenTopSheet();
        }



        // ================= QR Share Card =================

        private async void QrShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isQrCardVisible)
            {
                HideQrCard();
                return;
            }

            // Resolve share URL from currently selected node
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
            if (CustomConfigService.IsCustomNode(node) && node != null)
            {
                await CopyCustomConfigJsonWithDialogAsync(node);
                return;
            }
            _currentShareUrl = node != null ? NodesManager.ToShareUrl(node) : string.Empty;

            // Reset copy button state
            CopyLinkButtonText.Text = L.Servers_CopyShareLink;
            var grad = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint   = new Windows.Foundation.Point(1, 0.5)
            };
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 99, 102, 241), Offset = 0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 139, 92, 246), Offset = 1 });
            CopyLinkButton.Background = grad;

            // Position card above the QR button before making visible
            PositionQrCard();

            // Show overlay
            QrOverlayCanvas.Visibility = Visibility.Visible;
            _isQrCardVisible = true;

            // Start spring animation after a layout pass
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                AnimateQrCardIn);

            // Generate QR code
            if (!string.IsNullOrEmpty(_currentShareUrl))
                await GenerateQrCodeAsync(_currentShareUrl);
            else
                QrCodeImage.Source = null;
        }

        private void PositionQrCard()
        {
            try
            {
                var transform = QrShareButton.TransformToVisual(RootGrid);
                var btnPos    = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                const double cardWidth           = 230;
                const double estimatedCardHeight = 320;

                // Center card horizontally over button, appear above it
                double left = btnPos.X + (QrShareButton.ActualWidth  / 2) - (cardWidth / 2);
                double top  = btnPos.Y - estimatedCardHeight - 12;

                // Clamp to avoid clipping at edges
                left = Math.Max(8, left);
                top  = Math.Max(8, top);

                Canvas.SetLeft(QrCardBorder, left);
                Canvas.SetTop(QrCardBorder,  top);
            }
            catch { }
        }

        private void AnimateQrCardIn()
        {
            try
            {
                var visual     = ElementCompositionPreview.GetElementVisual(QrCardBorder);
                var compositor = visual.Compositor;

                // Origin at bottom-center of card (card grows upward from button)
                float w = (float)QrCardBorder.ActualWidth;
                float h = (float)QrCardBorder.ActualHeight;
                visual.CenterPoint = new System.Numerics.Vector3(w / 2f, h, 0f);

                // Spring scale — DampingRatio < 1 = underdamped = natural bounce
                var spring = compositor.CreateSpringVector3Animation();
                spring.InitialValue = new System.Numerics.Vector3(0.82f, 0.82f, 1f);
                spring.FinalValue   = new System.Numerics.Vector3(1f, 1f, 1f);
                spring.DampingRatio = 0.55f;
                spring.Period       = TimeSpan.FromMilliseconds(280);

                visual.Scale = new System.Numerics.Vector3(0.82f, 0.82f, 1f);
                visual.StartAnimation("Scale", spring);

                // Fade in
                visual.Opacity = 0f;
                var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                fadeIn.InsertKeyFrame(0f, 0f);
                fadeIn.InsertKeyFrame(1f, 1f);
                fadeIn.Duration = TimeSpan.FromMilliseconds(180);
                visual.StartAnimation("Opacity", fadeIn);
            }
            catch { }
        }

        private void HideQrCard()
        {
            if (!_isQrCardVisible) return;
            try
            {
                var visual     = ElementCompositionPreview.GetElementVisual(QrCardBorder);
                var compositor = visual.Compositor;

                var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                fadeOut.InsertKeyFrame(0f, 1f);
                fadeOut.InsertKeyFrame(1f, 0f);
                fadeOut.Duration = TimeSpan.FromMilliseconds(140);

                var scaleOut = compositor.CreateVector3KeyFrameAnimation();
                scaleOut.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
                scaleOut.InsertKeyFrame(
                    1f, new System.Numerics.Vector3(0.9f, 0.9f, 1f),
                    compositor.CreateCubicBezierEasingFunction(
                        new System.Numerics.Vector2(0.4f, 0f),
                        new System.Numerics.Vector2(1f, 1f)));
                scaleOut.Duration = TimeSpan.FromMilliseconds(140);

                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                visual.StartAnimation("Opacity", fadeOut);
                visual.StartAnimation("Scale",   scaleOut);
                batch.End();

                batch.Completed += (_, _) =>
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        QrOverlayCanvas.Visibility = Visibility.Collapsed;
                        _isQrCardVisible = false;
                    });
            }
            catch
            {
                QrOverlayCanvas.Visibility = Visibility.Collapsed;
                _isQrCardVisible = false;
            }
        }

        private void QrDismissRect_PointerPressed(object sender, PointerRoutedEventArgs e)
            => HideQrCard();

        private async Task GenerateQrCodeAsync(string url)
        {
            try
            {
                byte[] pngBytes = await Task.Run(() =>
                {
                    using var gen    = new QRCodeGenerator();
                    var       data   = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
                    using var qrCode = new PngByteQRCode(data);
                    return qrCode.GetGraphic(10); // 10 px per module, black on white
                });

                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(pngBytes);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                QrCodeImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QR] Generation failed: {ex.Message}");
            }
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentShareUrl)) return;

            try
            {
                ClipboardMonitorService.Instance.IgnoreText(_currentShareUrl);
                var pkg = new DataPackage();
                pkg.SetText(_currentShareUrl);
                Clipboard.SetContent(pkg);
            }
            catch { }

            // Visual feedback: green + "已复制！"
            CopyLinkButtonText.Text   = L.Servers_Copied;
            CopyLinkButton.Background = new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));

            // Auto-revert after 1.5 s
            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _copyResetTimer.Tick += (_, _) =>
            {
                _copyResetTimer?.Stop();
                _copyResetTimer = null;

                CopyLinkButtonText.Text = L.Servers_CopyShareLink;
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0.5),
                    EndPoint   = new Windows.Foundation.Point(1, 0.5)
                };
                grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 99, 102, 241), Offset = 0 });
                grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 139, 92, 246), Offset = 1 });
                CopyLinkButton.Background = grad;
            };
            _copyResetTimer.Start();
        }

        private void ServerItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            try
            {
                e.Handled = true;
                if (sender is FrameworkElement element && element.DataContext is ServerEntryItem server)
                {
                    ServersListView.SelectedItem = server;
                    _ = ViewModel.ConnectNodeCommand.ExecuteAsync(server);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerItem_DoubleTapped error: {ex}");
            }
        }

        private void ServerItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ServerEntryItem server)
            {
                args.Handled = true;
                return;
            }

            // Always select the item that was right-clicked
            ServersListView.SelectedItem = server;

            var flyout = new MenuFlyout();

            var editItem = new MenuFlyoutItem
            {
                Text = L.Servers_MenuEdit,
                Icon = new FontIcon { Glyph = "\uE70F" }
            };
            editItem.Click += EditManualServerDetail_Click;

            var deleteItem = new MenuFlyoutItem
            {
                Text = L.Servers_MenuDelete,
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += DeleteServerDetail_Click;

            var shareItem = new MenuFlyoutItem
            {
                Text = L.Servers_MenuShare,
                Icon = new FontIcon { Glyph = "\uE72D" }
            };
            shareItem.Click += ShareServer_Click;

            var favoriteText = server.IsFavorite ? L.Servers_MenuRemoveFav : L.Servers_MenuAddFav;
            var favoriteIcon = server.IsFavorite ? "\uE735" : "\uE734";
            var favoriteItem = new MenuFlyoutItem
            {
                Text = favoriteText,
                Icon = new FontIcon { Glyph = favoriteIcon }
            };
            favoriteItem.Click += (_, _) =>
            {
                server.IsFavorite = !server.IsFavorite;
                server.FavoriteVisibility = server.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
                if (ServersListView.SelectedItem == server && DetailFavoriteButton != null && DetailFavoriteIcon != null)
                {
                    DetailFavoriteButton.IsChecked = server.IsFavorite;
                    DetailFavoriteIcon.Glyph = server.IsFavorite ? "\uE735" : "\uE734";
                }

                var node = NodesManager.Instance.Nodes.Find(n => n.Id == server.Id);
                if (node != null)
                {
                    node.IsFavorite = server.IsFavorite;
                    NodesManager.Instance.Save();
                }
            };

            flyout.Items.Add(editItem);
            flyout.Items.Add(deleteItem);
            flyout.Items.Add(shareItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(favoriteItem);

            if (args.TryGetPosition(element, out Windows.Foundation.Point point))
            {
                flyout.ShowAt(element, new FlyoutShowOptions { Position = point });
            }
            else
            {
                flyout.ShowAt(element);
            }

            args.Handled = true;
        }

        // ================= Bottom Action Buttons =================

        private void AddManualServer_Click(object sender, RoutedEventArgs e)
        {
            _editingNodeId = string.Empty;
            ServerNameInput.Text = string.Empty;
            ServerHostInput.Text = string.Empty;
            ServerPortInput.Text = "443";
            ServerProtocolInput.SelectedIndex = 0;

            ServerPasswordInput.Password = string.Empty;
            ServerUuidInput.Text = string.Empty;
            ServerPathInput.Text = string.Empty;
            ServerWsHostInput.Text = string.Empty;
            ServerSniInput.Text = string.Empty;
            ServerFingerprintInput.SelectedIndex = 0;
            ServerAllowInsecureInput.IsChecked = false;
            ServerPublicKeyInput.Text = string.Empty;
            ServerShortIdInput.Text = string.Empty;
            if (ServerFlowInput != null) ServerFlowInput.SelectedIndex = 0;

            ServerNetworkInput.SelectedIndex = 0;
            ServerSecurityInput.SelectedIndex = 0;

            // Reset new fields
            ServerAlterIdInput.Text = "0";
            ServerObfsTypeInput.SelectedIndex = 0;
            ServerObfsPasswordInput.Text = string.Empty;
            ServerIsShadowTlsInput.IsChecked = false;
            ServerShadowTlsVersionInput.SelectedIndex = 0;
            ServerShadowTlsPasswordInput.Text = string.Empty;
            ServerHeaderTypeInput.SelectedIndex = 0;
            ServerAlpnInput.Text = string.Empty;
            ServerSpiderXInput.Text = string.Empty;

            PopulateProxyChainOptions(string.Empty);

            UpdateFormFieldsVisibility();

            EditFormTitle.Text = L.Servers_AddManualTitle;
            ShowPanel("edit");
        }

        private async void ImportFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try to read initial text from clipboard to pre-populate
                string initialText = string.Empty;
                try
                {
                    var packageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (packageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    {
                        initialText = await packageView.GetTextAsync() ?? string.Empty;
                    }
                }
                catch { }

                // Create input dialog elements
                var textBox = new TextBox
                {
                    PlaceholderText = L.Servers_ImportPlaceholder,
                    AcceptsReturn = true,
                    Height = 160,
                    Text = initialText,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var scrollViewer = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = L.Servers_ImportDialogHint,
                                FontSize = 12,
                                Opacity = 0.8
                            },
                            textBox
                        }
                    },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var dialog = new ContentDialog
                {
                    Title = L.Servers_ImportDialogTitle,
                    PrimaryButtonText = L.Dialog_OK,
                    CloseButtonText = L.Dialog_Cancel,
                    DefaultButton = ContentDialogButton.Primary,
                    Content = scrollViewer,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.WithAppTheme().ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                string text = textBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text)) return;

                var (addedNodesCount, addedSubsCount) = await ViewModel.ImportNodesFromTextAsync(text);

                if (addedNodesCount > 0 || addedSubsCount > 0)
                {

                    // Show success feedback
                    string msg = string.Empty;
                    if (addedSubsCount > 0 && addedNodesCount > 0)
                        msg = Loc.Format("Servers_ImportBothFmt", addedSubsCount, addedNodesCount);
                    else if (addedSubsCount > 0)
                        msg = Loc.Format("Servers_ImportSubsFmt", addedSubsCount);
                    else
                        msg = Loc.Format("Servers_ImportNodesFmt", addedNodesCount);

                    var successDialog = new ContentDialog
                    {
                        Title = L.Servers_ImportSuccessTitle,
                        Content = msg,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.WithAppTheme().ShowAsync();
                }
                else
                {
                    var failDialog = new ContentDialog
                    {
                        Title = L.Servers_ImportFailTitle,
                        Content = L.Servers_ImportFailMsg,
                        CloseButtonText = L.Dialog_OK,
                        XamlRoot = this.XamlRoot
                    };
                    await failDialog.WithAppTheme().ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = L.Servers_ImportErrorTitle,
                    Content = ex.Message,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                try { await errDialog.WithAppTheme().ShowAsync(); } catch { }
            }
        }

        private async void SetProxyMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is string mode)
            {
                int index = mode == "tun" ? 1 : mode == "manual" ? 2 : mode == "tun-system" ? 3 : 0;

                // If switching to TUN mode without admin, show UAC dialog first
                if ((index == 1 || index == 3) && !Helpers.AdminHelper.IsAdministrator())
                {
                    int previousModeIndex = AppSession.Instance.ProxyModeIndex;
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                    {
                        try
                        {
                            await Task.Delay(100);
                            var dialog = new ContentDialog
                            {
                                Title = L.Tun_AdminRequiredTitle,
                                Content = L.Tun_AdminRequiredMsg,
                                PrimaryButtonText = L.Tun_AdminConfirmBtn,
                                CloseButtonText = L.Dialog_Cancel,
                                XamlRoot = this.XamlRoot
                            };
                            var result = await dialog.WithAppTheme().ShowAsync();
                            if (result != ContentDialogResult.Primary)
                            {
                                tcs.SetResult(false);
                                return;
                            }
                            bool wasRunning = CoreManager.Instance.IsRunning;
                            if (wasRunning) await CoreManager.Instance.StopAsync();
                            AppSession.Instance.ProxyModeIndex = index;
                            Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", index);
                            Helpers.LocalSettingsHelper.SetValue("enableTunMode", true);

                            string arg = index == 3
                                ? (wasRunning ? "--tun-system-start" : "--tun-system")
                                : (wasRunning ? "--tun-start" : "--tun");
                            bool restarted = Helpers.AdminHelper.RestartAsAdmin(arg);
                            if (!restarted)
                            {
                                AppSession.Instance.ProxyModeIndex = previousModeIndex;
                                Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", previousModeIndex);
                                Helpers.LocalSettingsHelper.SetValue("enableTunMode", previousModeIndex == 1 || previousModeIndex == 3);
                                UpdateProxyModeUI();
                                UpdateRoutingModeUI();
                            }
                            tcs.SetResult(restarted);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[TUN] SetProxyMode_Click dialog failed: {ex.Message}");
                            tcs.SetResult(false);
                        }
                    });
                    await tcs.Task;
                    return;
                }

                bool wasRunning = CoreManager.Instance.IsRunning;
                if (wasRunning)
                {
                    CoreManager.Instance.AppendLog("[系统] 检测到代理模式更新，正在自动重启代理引擎以重新加载配置...");
                    await CoreManager.Instance.StopAsync();
                }

                AppSession.Instance.ProxyModeIndex = index;
                Helpers.LocalSettingsHelper.SetValue("proxyModeIndex", index);
                Helpers.LocalSettingsHelper.SetValue("enableTunMode", index == 1 || index == 3);

                UpdateProxyModeUI();
                UpdateRoutingModeUI();

                if (wasRunning)
                {
                    var node = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
                    if (node != null)
                    {
                        string realConfig = await ConfigBuilder.BuildAsync(node);
                        await CoreManager.Instance.StartAsync(realConfig);
                    }
                }
            }
        }

        private async void ManageSubscriptions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageSubscriptionsDialog(() =>
            {
                LoadServersList();
            })
            {
                XamlRoot = this.XamlRoot
            };

            // Intercept the "添加" primary button: run add+sync, cancel close on failure
            dialog.PrimaryButtonClick += async (d, args) =>
            {
                var deferral = args.GetDeferral();
                bool ok = await dialog.TryAddSubscriptionAsync();
                if (!ok) args.Cancel = true;   // keep dialog open if URL was empty
                deferral.Complete();
                if (ok) LoadServersList();
            };

            try
            {
                await dialog.WithAppTheme().ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Show subscription dialog failed: {ex.Message}");
            }
        }

        private async void PingAllServers_Click(object sender, RoutedEventArgs e)
        {
            PingAllButton.IsEnabled = false;
            PingAllButton.Content = new ProgressRing { IsActive = true, Width = 16, Height = 16 };

            try
            {
                await ViewModel.PingAllServersCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ping all error: {ex.Message}");
            }
            finally
            {
                PingAllButton.IsEnabled = true;
                PingAllButton.Content = new FontIcon { Glyph = "\uEC4A", FontSize = 16 };
            }
        }

        // ================= Selection & Navigation Switcher =================

        private void ServersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ServersListView.SelectedItem is ServerEntryItem selected)
                {
                    _lastSelectedNodeId = selected.Id;
                    if (DetailTitle == null || DetailSubtitle == null || DetailHostText == null ||
                        DetailPortText == null || DetailNetworkText == null || DetailFavoriteButton == null ||
                        DetailFavoriteIcon == null || DetailLatencyText == null)
                        return;

                    // Populate Detailed View
                    DetailTitle.Text = selected.Name;
                    DetailFavoriteButton.IsChecked = selected.IsFavorite;
                    DetailFavoriteIcon.Glyph = selected.IsFavorite ? "\uE735" : "\uE734"; // Solid / Outline star

                    if (selected.IsCustom)
                    {
                        var customNode = NodesManager.Instance.Nodes.Find(n => n.Id == selected.Id);
                        DetailSubtitle.Text = L.CustomCfg_DetailSubtitle;
                        if (DetailEditCustomJsonButton != null) DetailEditCustomJsonButton.Visibility = Visibility.Visible;
                        if (TestSingleLatencyButton != null) TestSingleLatencyButton.Visibility = Visibility.Collapsed;

                        if (DetailAddrLabel != null) DetailAddrLabel.Text = L.CustomCfg_DetailFileLabel;
                        if (DetailPortLabel != null) DetailPortLabel.Text = L.CustomCfg_DetailSummaryLabel;
                        if (DetailTransportLabel != null) DetailTransportLabel.Text = L.CustomCfg_DetailBridgeLabel;
                        if (DetailLatencyLabel != null) DetailLatencyLabel.Text = L.CustomCfg_DetailModeLabel;

                        DetailHostText.Text = customNode != null ? CustomConfigService.EnsureFileName(customNode) : "config.json";
                        DetailPortText.Text = selected.HostPortDisplay;
                        DetailNetworkText.Text = (customNode?.CustomBridgeGui ?? true) ? L.CustomCfg_BridgeEnabled : L.CustomCfg_BridgeRawOnly;
                        DetailLatencyText.Text = L.CustomCfg_RouteSelfManaged;
                    }
                    else
                    {
                        DetailSubtitle.Text = selected.Protocol;
                        if (DetailEditCustomJsonButton != null) DetailEditCustomJsonButton.Visibility = Visibility.Collapsed;
                        if (TestSingleLatencyButton != null) TestSingleLatencyButton.Visibility = Visibility.Visible;

                        if (DetailAddrLabel != null) DetailAddrLabel.Text = L.Servers_DetailAddrLabelText;
                        if (DetailPortLabel != null) DetailPortLabel.Text = L.Servers_DetailPortLabelText;
                        if (DetailTransportLabel != null) DetailTransportLabel.Text = L.Servers_DetailTransportLabelText;
                        if (DetailLatencyLabel != null) DetailLatencyLabel.Text = L.Servers_DetailLatencyLabelText;

                        DetailHostText.Text = selected.Host;
                        DetailPortText.Text = selected.Port.ToString();
                        DetailNetworkText.Text = string.IsNullOrEmpty(selected.Network) ? "TCP" : selected.Network.ToUpper();
                        DetailLatencyText.Text = L.Servers_PingUntested;
                    }

                    try
                    {
                        DetailLatencyText.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                    }
                    catch
                    {
                        // Fallback
                    }

                    ResetAiLights();
                    ResetIpInfoText();

                    ShowPanel("detail");
                    UpdateRoutingModeUI();

                    if (!selected.IsCustom)
                    {
                        // Start Latency checks in background
                        _ = ViewModel.TestSingleLatencyCommand.ExecuteAsync(selected);
                    }
                    
                    // Show cached AI/IP info if applicable
                    UpdateAiAndIpDisplay();
                }
                else
                {
                    // SelectedItem is null — this can happen when ApplyFilters() rebuilds
                    // FilteredServers (e.g. after a latency test completes in Latency sort mode)
                    // and WinUI temporarily clears the selection before restoring it.
                    // If _lastSelectedNodeId is still present in the (now rebuilt) list, restore
                    // the selection immediately so the detail panel never flashes to "empty".
                    if (!string.IsNullOrEmpty(_lastSelectedNodeId))
                    {
                        var restore = ViewModel.FilteredServers.FirstOrDefault(s => s.Id == _lastSelectedNodeId);
                        if (restore != null)
                        {
                            // Re-select without recursing: the next SelectionChanged will be the
                            // real one with a non-null SelectedItem, which fills the detail panel.
                            ServersListView.SelectedItem = restore;
                            return;
                        }
                    }
                    ShowPanel("empty");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SelectionChanged: {ex}");
            }
        }

        private void ShowPanel(string name)
        {
            EmptyViewPanel.Visibility = (name == "empty") ? Visibility.Visible : Visibility.Collapsed;
            ServerDetailsPanel.Visibility = (name == "detail") ? Visibility.Visible : Visibility.Collapsed;
            EditFormPanel.Visibility = (name == "edit") ? Visibility.Visible : Visibility.Collapsed;
            SubscriptionPanel.Visibility = (name == "subscription") ? Visibility.Visible : Visibility.Collapsed;
            if (CustomConfigPanel != null)
            {
                CustomConfigPanel.Visibility = (name == "custom_edit") ? Visibility.Visible : Visibility.Collapsed;
            }

            // Release raw JSON buffer from XAML TextBox as soon as leaving custom editor to minimize RAM
            if (name != "custom_edit")
            {
                if (CustomJsonEditorBox != null && CustomJsonEditorBox.Text.Length > 0)
                {
                    CustomJsonEditorBox.Text = string.Empty;
                }
                if (CustomConfigInfoBar != null)
                {
                    CustomConfigInfoBar.IsOpen = false;
                }
            }

            // Hide bottom connection/control bar when adding/editing nodes or managing subscriptions
            if (BottomControlBar != null)
            {
                BottomControlBar.Visibility = (name == "edit" || name == "subscription" || name == "custom_edit")
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        // ================= Latency Speed Test =================

        private async void TestSingleLatency_Click(object sender, RoutedEventArgs e)
        {
            if (ServersListView.SelectedItem is ServerEntryItem selected)
            {
                await ViewModel.TestSingleLatencyCommand.ExecuteAsync(selected);

                // Update detail text manually for now until XAML bindings are fully migrated
                if (DetailLatencyText != null)
                {
                    DetailLatencyText.Text = selected.PingText;
                    DetailLatencyText.Foreground = selected.PingColor;
                }
            }
        }


        private AiUnlockStatus? _openAiStatus;
        private AiUnlockStatus? _claudeStatus;
        private AiUnlockStatus? _geminiStatus;
        private bool _isIpCheckLoading;
        private string _ipOrgCache = "-";
        private string _ipAsnCache = "-";
        private string _ipLocationCache = "-";

        private ServerEntryItem? GetActiveServerItem()
        {
            return AllServers.FirstOrDefault(s => s.Id == NodesManager.Instance.SelectedNodeId);
        }

        private Microsoft.UI.Xaml.Media.Brush ResolveStateBrush(string resourceKey, Windows.UI.Color fallback)
        {
            if (this.Resources.TryGetValue(resourceKey, out var pageRes) && pageRes is Microsoft.UI.Xaml.Media.Brush pageBrush)
                return pageBrush;
            if (Application.Current.Resources.TryGetValue(resourceKey, out var appRes) && appRes is Microsoft.UI.Xaml.Media.Brush appBrush)
                return appBrush;
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(fallback);
        }

        private void ResetAiLights()
        {
            try
            {
                var neutral = ResolveStateBrush("StateNeutralBrush", Microsoft.UI.Colors.Gray);
                if (OpenAiLight != null) OpenAiLight.Fill = neutral;
                if (ClaudeLight != null) ClaudeLight.Fill = neutral;
                if (GeminiLight != null) GeminiLight.Fill = neutral;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetAiLights failed: {ex.Message}");
            }
        }

        private void ResetIpInfoText()
        {
            try
            {
                if (IpAddressText != null) IpAddressText.Text = "-";
                if (IpOrgText != null) IpOrgText.Text = "-";
                if (IpAsnText != null) IpAsnText.Text = "-";
                if (IpLocationText != null) IpLocationText.Text = "-";
                if (IpLoadingRing != null) IpLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResetIpInfoText failed: {ex.Message}");
            }
        }

        private void UpdateAiAndIpDisplay()
        {
            try
            {
                var activeItem = GetActiveServerItem();
                if (!CoreManager.Instance.IsRunning || ServersListView.SelectedItem as ServerEntryItem == null || ServersListView.SelectedItem as ServerEntryItem != activeItem)
                {
                    ResetAiLights();
                    ResetIpInfoText();
                    return;
                }

                var neutralBrush = ResolveStateBrush("StateNeutralBrush", Microsoft.UI.Colors.Gray);
                var successBrush = ResolveStateBrush("StateSuccessBrush", Microsoft.UI.Colors.Green);
                var errorBrush   = ResolveStateBrush("StateErrorBrush", Microsoft.UI.Colors.Red);

                Microsoft.UI.Xaml.Media.Brush MapAiBrush(AiUnlockStatus? status) => status switch
                {
                    null => neutralBrush,
                    AiUnlockStatus.Unlocked => successBrush,
                    _ => errorBrush
                };

                if (OpenAiLight != null) OpenAiLight.Fill = MapAiBrush(_openAiStatus);
                if (ClaudeLight != null) ClaudeLight.Fill = MapAiBrush(_claudeStatus);
                if (GeminiLight != null) GeminiLight.Fill = MapAiBrush(_geminiStatus);

                UpdateIpAddressDisplay();
                if (IpOrgText != null) IpOrgText.Text = _ipOrgCache;
                if (IpAsnText != null) IpAsnText.Text = _ipAsnCache;
                if (IpLocationText != null) IpLocationText.Text = _ipLocationCache;
                if (IpLoadingRing != null) IpLoadingRing.IsActive = _isIpCheckLoading;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateAiAndIpDisplay failed: {ex}");
            }
        }

        private void RefreshAiIpChecks_Click(object sender, RoutedEventArgs e)
        {
            if (!CoreManager.Instance.IsRunning) return;
            _ = TriggerAiAndIpChecksAsync();
        }

        private static bool IsDatacenter(string org)
        {
            if (string.IsNullOrEmpty(org) || org == "-") return false;
            
            string[] keywords = { 
                "Amazon", "Google", "Microsoft", "Azure", "DigitalOcean", 
                "Linode", "Vultr", "Oracle", "Cloudflare", "OVH", 
                "Hetzner", "Data Center", "Hosting", "Cloud", "Server",
                "Akamai", "Zenlayer", "Choopa", "Tencent", "Alibaba",
                "Baidu", "Huawei", "Equinix", "Leaseweb", "QuadraNet"
            };

            foreach (var kw in keywords)
            {
                if (org.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task TriggerAiAndIpChecksAsync()
        {
            _aiCheckCts?.Cancel();
            _aiCheckCts = new CancellationTokenSource();
            var token = _aiCheckCts.Token;

            _openAiStatus = null;
            _claudeStatus = null;
            _geminiStatus = null;
            _isIpCheckLoading = true;
            _activeIpAddress = "-";
            _ipOrgCache = "-";
            _ipAsnCache = "-";
            _ipLocationCache = "-";

            UpdateAiAndIpDisplay();

            try
            {
                var ipInfoService = new IpInfoService();
                var aiService = new AiUnlockCheckService();
                int? proxyPort = AppSession.Instance.EffectiveHttpProxyPort;

                var openAiTask = aiService.CheckOpenAiAsync(proxyPort, token);
                var claudeTask = aiService.CheckClaudeAsync(proxyPort, token);
                var geminiTask = aiService.CheckGeminiAsync(proxyPort, token);
                var ipInfoTask = ipInfoService.GetIpInfoAsync(proxyPort, token);

                async Task RunOne(Task<AiUnlockStatus> check, Action<AiUnlockStatus> assign)
                {
                    var status = await check;
                    if (token.IsCancellationRequested) return;
                    assign(status);
                    DispatcherQueue.TryEnqueue(() => UpdateAiAndIpDisplay());
                }

                async Task RunIpInfo()
                {
                    var ipInfo = await ipInfoTask;
                    if (token.IsCancellationRequested) return;

                    _isIpCheckLoading = false;
                    if (ipInfo != null)
                    {
                        _activeIpAddress = ipInfo.ip ?? "-";
                        var org = ipInfo.asOrganization ?? "-";
                        if (IsDatacenter(org))
                        {
                            _ipOrgCache = $"{org} [{L.Servers_IpDatacenterTag}]";
                        }
                        else
                        {
                            _ipOrgCache = org;
                        }
                        _ipAsnCache = ipInfo.asn?.ToString() ?? "-";

                        var locParts = new[] { ipInfo.city, ipInfo.region, ipInfo.country }
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .ToArray();
                        _ipLocationCache = locParts.Length > 0 ? string.Join(", ", locParts) : "-";
                    }
                    else
                    {
                        _activeIpAddress = "-";
                        _ipOrgCache = L.Servers_IpFetchFailed;
                        _ipAsnCache = "-";
                        _ipLocationCache = "-";
                    }
                    DispatcherQueue.TryEnqueue(() => UpdateAiAndIpDisplay());
                }

                await Task.WhenAll(
                    RunOne(openAiTask, s => _openAiStatus = s),
                    RunOne(claudeTask, s => _claudeStatus = s),
                    RunOne(geminiTask, s => _geminiStatus = s),
                    RunIpInfo()
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI check error: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _isIpCheckLoading = false;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            UpdateAiAndIpDisplay();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error updating UI in TriggerAiAndIpChecksAsync finally: {ex}");
                        }
                    });
                }
            }
        }

        private void UpdateIpAddressDisplay()
        {
            IpMaskIcon.Glyph = _isIpMasked ? "\uED1A" : "\uE890";
            if (ToggleIpMaskButton != null)
            {
                ToolTipService.SetToolTip(ToggleIpMaskButton, _isIpMasked ? L.Servers_ShowFullIp : L.Servers_HideExitIp);
            }

            if (_activeIpAddress == "-" || string.IsNullOrEmpty(_activeIpAddress))
            {
                IpAddressText.Text = "-";
                return;
            }

            if (!_isIpMasked)
            {
                IpAddressText.Text = _activeIpAddress;
            }
            else
            {
                var parts = _activeIpAddress.Split('.');
                if (parts.Length == 4)
                {
                    IpAddressText.Text = $"{parts[0]}.{parts[1]}.*.*";
                }
                else if (_activeIpAddress.Contains(':'))
                {
                    var v6Parts = _activeIpAddress.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    IpAddressText.Text = v6Parts.Length >= 2
                        ? $"{v6Parts[0]}:{v6Parts[1]}:*:*"
                        : "*:*";
                }
                else
                {
                    IpAddressText.Text = _activeIpAddress;
                }
            }
        }

        private void ToggleIpMask_Click(object sender, RoutedEventArgs e)
        {
            _isIpMasked = !_isIpMasked;
            UpdateIpAddressDisplay();
        }

        private void CopyIpStatus_Click(object sender, RoutedEventArgs e)
        {
            if (_activeIpAddress == "-" || string.IsNullOrEmpty(_activeIpAddress)) return;

            var summary = Loc.Format("Servers_CopyIpSummaryFmt", _activeIpAddress, IpOrgText.Text, IpAsnText.Text, IpLocationText.Text);

            var package = new DataPackage();
            package.SetText(summary);
            Clipboard.SetContent(package);
        }
    }
}
