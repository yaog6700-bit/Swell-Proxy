using AnywhereWinUI.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using AnywhereWinUI.Plugins;

namespace AnywhereWinUI.Views
{
    public sealed partial class PluginsPage : Page
    {
        public PluginsPage()
        {
            InitializeComponent();
            Loaded += PluginsPage_Loaded;
        }

        private void PluginsPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        // ── Refresh ──────────────────────────────────────────────────

        private void RefreshList()
        {
            var manifests = PluginManager.Instance.Manifests;

            for (int i = PluginListPanel.Children.Count - 1; i >= 0; i--)
            {
                if (PluginListPanel.Children[i] is not StackPanel { Name: "EmptyState" })
                    PluginListPanel.Children.RemoveAt(i);
            }

            EmptyState.Visibility = manifests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var manifest in manifests)
                PluginListPanel.Children.Insert(0, BuildPluginCard(manifest));
        }

        private UIElement BuildPluginCard(PluginManifest manifest)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ── Left: info stack ──────────────────────────────────────
            var infoStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

            // Row 1: Name + version
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Version,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            // Error badge
            if (!string.IsNullOrEmpty(manifest.LastError))
            {
                var errBadge = new InfoBadge
                {
                    Style = (Style)Application.Current.Resources["CriticalIconInfoBadgeStyle"],
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameRow.Children.Add(errBadge);
            }
            infoStack.Children.Add(nameRow);

            // Row 2: Author + type (caption, secondary color)
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (!string.IsNullOrEmpty(manifest.Author))
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = manifest.Author,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                metaRow.Children.Add(new TextBlock
                {
                    Text = "·",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            // Type indicator: icon + label
            metaRow.Children.Add(new FontIcon
            {
                Glyph = manifest.Type == "Http" ? "\uE774" : "\uE8B7", // Globe : Folder
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
            metaRow.Children.Add(new TextBlock
            {
                Text = manifest.Type == "Http" ? "网络插件" : "本地插件",
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            });
            infoStack.Children.Add(metaRow);

            // Row 3: Description
            if (!string.IsNullOrEmpty(manifest.Description))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = manifest.Description,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Row 4: Triggers as simple caption
            if (manifest.Triggers.Count > 0)
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = string.Join("  ·  ", manifest.Triggers),
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Row 5: Last error
            if (!string.IsNullOrEmpty(manifest.LastError))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = manifest.LastError,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            Grid.SetColumn(infoStack, 0);
            root.Children.Add(infoStack);

            // ── Right: controls ───────────────────────────────────────
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Manual run button with loading state
            if (manifest.Triggers.Contains("OnManual"))
            {
                var runIcon = new FontIcon { Glyph = "\uE768", FontSize = 14 };
                var runRing = new ProgressRing
                {
                    IsActive = false,
                    Width = 16,
                    Height = 16,
                    Visibility = Visibility.Collapsed
                };
                var runContent = new Grid();
                runContent.Children.Add(runIcon);
                runContent.Children.Add(runRing);

                var runBtn = new Button { Content = runContent };
                ToolTipService.SetToolTip(runBtn, "手动运行");
                runBtn.Click += async (_, _) =>
                {
                    runBtn.IsEnabled = false;
                    runIcon.Visibility = Visibility.Collapsed;
                    runRing.Visibility = Visibility.Visible;
                    runRing.IsActive = true;

                    await RunManualAsync(manifest.Id);

                    runRing.IsActive = false;
                    runRing.Visibility = Visibility.Collapsed;
                    runIcon.Visibility = Visibility.Visible;
                    runBtn.IsEnabled = true;
                };
                controls.Children.Add(runBtn);
            }

            // Enable toggle
            var toggle = new ToggleSwitch
            {
                IsOn = !manifest.Disabled,
                OnContent = "",
                OffContent = "",
                MinWidth = 0
            };
            toggle.Toggled += async (_, _) => await TogglePluginAsync(manifest.Id, toggle.IsOn);
            controls.Children.Add(toggle);

            // Delete button
            var delBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 }
            };
            ToolTipService.SetToolTip(delBtn, "删除");
            delBtn.Click += async (_, _) => await DeletePluginAsync(manifest.Id, manifest.Name);
            controls.Children.Add(delBtn);

            Grid.SetColumn(controls, 1);
            root.Children.Add(controls);

            card.Child = root;
            return card;
        }

        // ── Actions ──────────────────────────────────────────────────

        private async void BtnAddPlugin_Click(object sender, RoutedEventArgs e)
        {
            // Two-option dialog: local file or URL
            var urlBox = new TextBox
            {
                PlaceholderText = "https://raw.githubusercontent.com/.../plugin.js",
                Margin = new Thickness(0, 8, 0, 0)
            };

            var localRadio = new RadioButton { Content = "从本地文件导入 (.js)", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
            var urlRadio = new RadioButton { Content = "从 URL 安装", Margin = new Thickness(0, 0, 0, 8) };

            urlBox.IsEnabled = false;
            urlRadio.Checked += (_, _) => urlBox.IsEnabled = true;
            localRadio.Checked += (_, _) => urlBox.IsEnabled = false;

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(localRadio);
            panel.Children.Add(urlRadio);
            panel.Children.Add(urlBox);

            var dialog = new ContentDialog
            {
                Title = "添加插件",
                Content = panel,
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.WithAppTheme().ShowAsync() != ContentDialogResult.Primary) return;

            if (localRadio.IsChecked == true)
                await AddFromFileAsync();
            else
            {
                var url = urlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(url))
                    await AddFromUrlAsync(url);
            }
        }

        private async Task AddFromFileAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                FileTypeFilter = { ".js" }
            };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, (Application.Current as App)!.MainWindowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy", "plugins");
            Directory.CreateDirectory(pluginsDir);

            var destName = file.Name;
            var destPath = Path.Combine(pluginsDir, destName);
            File.Copy(file.Path, destPath, overwrite: true);

            // Auto-detect triggers from the JS source code
            var code = await File.ReadAllTextAsync(destPath);
            var triggers = PluginManager.DetectTriggersFromCode(code);

            var manifest = new PluginManifest
            {
                Id = Path.GetFileNameWithoutExtension(destName) + "_" + Guid.NewGuid().ToString("N")[..6],
                Name = Path.GetFileNameWithoutExtension(destName),
                Type = "File",
                Path = $"plugins/{destName}",
                Triggers = triggers,
                Disabled = false
            };

            await PluginManager.Instance.AddPluginAsync(manifest);
            ShowInfo("插件已添加", $"已成功加载插件 {manifest.Name}（触发器: {string.Join(", ", triggers)}）", InfoBarSeverity.Success);
            RefreshList();
        }

        private async Task AddFromUrlAsync(string url)
        {
            var uri = new Uri(url);
            var filename = System.IO.Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrEmpty(filename) || !filename.EndsWith(".js"))
                filename = "plugin_" + Guid.NewGuid().ToString("N")[..6] + ".js";

            var manifest = new PluginManifest
            {
                Id = System.IO.Path.GetFileNameWithoutExtension(filename) + "_" + Guid.NewGuid().ToString("N")[..6],
                Name = System.IO.Path.GetFileNameWithoutExtension(filename),
                Type = "Http",
                Path = $"plugins/{filename}",
                Url = url,
                Triggers = ["OnManual"],  // Temporary; will be updated after download
                Disabled = false
            };

            ShowInfo("正在下载...", filename, InfoBarSeverity.Informational);
            try
            {
                await PluginManager.Instance.AddPluginAsync(manifest);
                await PluginManager.Instance.UpdatePluginAsync(manifest.Id);

                // Re-detect triggers from the downloaded code
                var pluginPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwellProxy", manifest.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(pluginPath))
                {
                    var code = await File.ReadAllTextAsync(pluginPath);
                    manifest.Triggers = PluginManager.DetectTriggersFromCode(code);
                    await PluginManager.Instance.SaveAsync();
                }

                ShowInfo("安装成功", $"{manifest.Name}（触发器: {string.Join(", ", manifest.Triggers)}）", InfoBarSeverity.Success);
                RefreshList();
            }
            catch (Exception ex)
            {
                ShowInfo("安装失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy", "plugins");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        private async Task TogglePluginAsync(string id, bool enabled)
        {
            await PluginManager.Instance.SetEnabledAsync(id, enabled);
            RefreshList();
        }

        private async Task RunManualAsync(string id)
        {
            try
            {
                await PluginManager.Instance.ManualTriggerAsync(id);
                ShowInfo("执行完成", "插件已手动触发", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo("执行失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task DeletePluginAsync(string id, string name)
        {
            var dlg = new ContentDialog
            {
                Title = "删除插件",
                Content = $"确定要删除「{name}」吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            if (await dlg.WithAppTheme().ShowAsync() != ContentDialogResult.Primary) return;

            await PluginManager.Instance.RemovePluginAsync(id);
            ShowInfo("已删除", name, InfoBarSeverity.Informational);
            RefreshList();
        }

        private void ShowInfo(string title, string message, InfoBarSeverity severity)
        {
            InfoBarMain.Title = title;
            InfoBarMain.Message = message;
            InfoBarMain.Severity = severity;
            InfoBarMain.IsOpen = true;
        }
    }
}
