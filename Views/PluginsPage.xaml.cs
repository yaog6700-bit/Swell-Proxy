using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            // Remove all plugin cards (keep EmptyState sentinel)
            for (int i = PluginListPanel.Children.Count - 1; i >= 0; i--)
            {
                if (PluginListPanel.Children[i] is not StackPanel { Name: "EmptyState" })
                    PluginListPanel.Children.RemoveAt(i);
            }

            EmptyState.Visibility = manifests.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            foreach (var manifest in manifests)
                PluginListPanel.Children.Insert(0, BuildPluginCard(manifest));
        }

        private UIElement BuildPluginCard(PluginManifest manifest)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: info
            var infoPanel = new StackPanel { Spacing = 2 };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Version,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            if (!string.IsNullOrEmpty(manifest.LastError))
            {
                nameRow.Children.Add(new FontIcon
                {
                    Glyph = "\uEA39",
                    FontSize = 14,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed)
                });
            }
            infoPanel.Children.Add(nameRow);

            if (!string.IsNullOrEmpty(manifest.Description))
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = manifest.Description,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var triggerText = manifest.Triggers.Count > 0
                ? string.Join(", ", manifest.Triggers)
                : "无触发器";
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"触发器: {triggerText}",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Margin = new Thickness(0, 4, 0, 0)
            });

            if (!string.IsNullOrEmpty(manifest.LastError))
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"错误: {manifest.LastError}",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
                });
            }

            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // Right: controls
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Run manually button
            if (manifest.Triggers.Contains("OnManual"))
            {
                var runBtn = new Button
                {
                    Content = new FontIcon { Glyph = "\uE768", FontSize = 14 }
                };
                ToolTipService.SetToolTip(runBtn, "手动触发");
                runBtn.Click += async (_, _) => await RunManualAsync(manifest.Id);
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
            ToolTipService.SetToolTip(delBtn, "删除插件");
            delBtn.Click += async (_, _) => await DeletePluginAsync(manifest.Id, manifest.Name);
            controls.Children.Add(delBtn);

            Grid.SetColumn(controls, 1);
            grid.Children.Add(controls);

            card.Child = grid;
            return card;
        }

        // ── Actions ──────────────────────────────────────────────────

        private async void BtnAddPlugin_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                FileTypeFilter = { ".js" }
            };

            // Associate the picker with the current window
            var hwnd = (Application.Current as App)!.MainWindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            // Copy to plugins dir
            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy", "plugins");
            Directory.CreateDirectory(pluginsDir);

            var destName = file.Name;
            var destPath = Path.Combine(pluginsDir, destName);
            File.Copy(file.Path, destPath, overwrite: true);

            var pluginId = Path.GetFileNameWithoutExtension(destName) + "_" + Guid.NewGuid().ToString("N")[..6];
            var manifest = new PluginManifest
            {
                Id = pluginId,
                Name = Path.GetFileNameWithoutExtension(destName),
                Type = "File",
                Path = $"plugins/{destName}",
                Triggers = ["OnManual"],
                Disabled = false
            };

            await PluginManager.Instance.AddPluginAsync(manifest);
            ShowInfo("插件已添加", $"已成功加载插件 {manifest.Name}", InfoBarSeverity.Success);
            RefreshList();
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
                Content = $"确定要删除插件「{name}」吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };
            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            await PluginManager.Instance.RemovePluginAsync(id);
            ShowInfo("已删除", $"插件「{name}」已移除", InfoBarSeverity.Informational);
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
