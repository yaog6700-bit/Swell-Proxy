using AnywhereWinUI.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using AnywhereWinUI.Plugins;

namespace AnywhereWinUI.Views
{
    public sealed partial class PluginsPage : Page
    {
        public PluginsPage()
        {
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            InitializeComponent();
            Loaded += PluginsPage_Loaded;
            ActualThemeChanged += PluginsPage_ActualThemeChanged;
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Loaded -= PluginsPage_Loaded;
            ActualThemeChanged -= PluginsPage_ActualThemeChanged;

            for (int i = PluginListPanel.Children.Count - 1; i >= 0; i--)
            {
                if (PluginListPanel.Children[i] is not StackPanel { Name: "EmptyState" })
                    PluginListPanel.Children.RemoveAt(i);
            }
        }

        private async void PluginsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await PluginManager.Instance.EnsureManifestsLoadedAsync();
            if (IsLoaded)
            {
                RefreshList();
            }
        }

        private void PluginsPage_ActualThemeChanged(FrameworkElement sender, object args)
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
            bool isDark = ContentDialogExtensions.IsElementDark(this);

            Brush cardStroke = LookupThemedBrush("CardStrokeColorDefaultBrush", isDark, Color.FromArgb(36, 128, 128, 128));
            Brush primaryFg = LookupThemedBrush("TextFillColorPrimaryBrush", isDark, isDark ? Colors.White : Colors.Black);
            Brush secondaryFg = LookupThemedBrush("TextFillColorSecondaryBrush", isDark, Colors.Gray);
            Brush tertiaryFg = LookupThemedBrush("TextFillColorTertiaryBrush", isDark, Colors.Gray);
            Brush accentFg = LookupThemedBrush("AccentTextFillColorPrimaryBrush", isDark, Color.FromArgb(255, 96, 165, 250));
            Brush criticalFg = LookupThemedBrush("SystemFillColorCriticalBrush", isDark, Color.FromArgb(255, 196, 43, 28));
            Style? subtleBtnStyle = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var sbObj) ? sbObj as Style : null;
            Style captionStyle = (Style)Application.Current.Resources["CaptionTextBlockStyle"];

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = cardStroke,
                BorderThickness = new Thickness(1),
            };

            var root = new Grid { ColumnSpacing = 12 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // ── Left: info stack ──────────────────────────────────────
            var infoStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };

            // Row 1: Name + version
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Name,
                Foreground = primaryFg,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            nameRow.Children.Add(new TextBlock
            {
                Text = manifest.Version,
                Foreground = tertiaryFg,
                Style = captionStyle,
                VerticalAlignment = VerticalAlignment.Center
            });

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

            // Row 2: Author + type
            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (!string.IsNullOrEmpty(manifest.Author))
            {
                metaRow.Children.Add(new TextBlock
                {
                    Text = manifest.Author,
                    Foreground = secondaryFg,
                    Style = captionStyle,
                    VerticalAlignment = VerticalAlignment.Center
                });
                metaRow.Children.Add(new TextBlock
                {
                    Text = "·",
                    Foreground = tertiaryFg,
                    Style = captionStyle,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            metaRow.Children.Add(new FontIcon
            {
                Glyph = manifest.Type == "Http" ? "\uE774" : "\uE8B7",
                FontSize = 11,
                Foreground = tertiaryFg,
                VerticalAlignment = VerticalAlignment.Center
            });
            metaRow.Children.Add(new TextBlock
            {
                Text = manifest.Type == "Http" ? L.Plugins_TypeNetwork : L.Plugins_TypeLocal,
                Foreground = tertiaryFg,
                Style = captionStyle,
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
                    Foreground = secondaryFg,
                    Style = captionStyle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Row 4: Triggers
            if (manifest.Triggers.Count > 0)
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = string.Join("  ·  ", manifest.Triggers),
                    Foreground = accentFg,
                    Style = captionStyle,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            // Row 5: Last error
            if (!string.IsNullOrEmpty(manifest.LastError))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = manifest.LastError,
                    Foreground = criticalFg,
                    Style = captionStyle,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            Grid.SetColumn(infoStack, 0);
            root.Children.Add(infoStack);

            // ── Right: Fluent 2 action controls ───────────────────────
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
                var runContent = new Grid { Width = 16, Height = 16 };
                runContent.Children.Add(runIcon);
                runContent.Children.Add(runRing);

                var runBtn = new Button
                {
                    Width = 34,
                    Height = 32,
                    Padding = new Thickness(0),
                    Content = runContent
                };
                if (subtleBtnStyle != null) runBtn.Style = subtleBtnStyle;
                ToolTipService.SetToolTip(runBtn, L.Plugins_RunManualTip);
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

            // Feature N7: Visual Parameter Configuration Gear Button
            var configBtn = new Button
            {
                Width = 34,
                Height = 32,
                Padding = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE713", FontSize = 14 }
            };
            if (subtleBtnStyle != null) configBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(configBtn, L.Plugins_ConfigTip);
            configBtn.Click += async (_, _) => await ShowPluginConfigDialogAsync(manifest);
            controls.Children.Add(configBtn);

            // Enable toggle
            var toggle = new ToggleSwitch
            {
                IsOn = !manifest.Disabled,
                OnContent = "",
                OffContent = "",
                MinWidth = 0,
                Margin = new Thickness(4, 0, -6, 0)
            };
            toggle.Toggled += async (_, _) => await TogglePluginAsync(manifest.Id, toggle.IsOn);
            controls.Children.Add(toggle);

            // Delete button
            var delBtn = new Button
            {
                Width = 34,
                Height = 32,
                Padding = new Thickness(0),
                Content = new FontIcon
                {
                    Glyph = "\uE74D",
                    FontSize = 14,
                    Foreground = criticalFg
                }
            };
            if (subtleBtnStyle != null) delBtn.Style = subtleBtnStyle;
            ToolTipService.SetToolTip(delBtn, L.Plugins_DelTip);
            delBtn.Click += async (_, _) => await DeletePluginAsync(manifest.Id, manifest.Name);
            controls.Children.Add(delBtn);

            Grid.SetColumn(controls, 1);
            root.Children.Add(controls);

            card.Child = root;
            return card;
        }

        // ── Feature N7: Visual Plugin Configuration ContentDialog (Fluent 2) ──

        private sealed class EditableConfigRow
        {
            public required Grid RowRoot { get; init; }
            public required TextBox KeyBox { get; init; }
            public required TextBox ValueBox { get; init; }
            public string Label { get; init; } = string.Empty;
            public bool IsPresetKey { get; init; }
        }

        private async Task ShowPluginConfigDialogAsync(PluginManifest manifest)
        {
            bool isDark = ContentDialogExtensions.IsElementDark(this);
            Brush secondaryFg = LookupThemedBrush("TextFillColorSecondaryBrush", isDark, Colors.Gray);
            Brush tertiaryFg = LookupThemedBrush("TextFillColorTertiaryBrush", isDark, Colors.Gray);
            Brush criticalFg = LookupThemedBrush("SystemFillColorCriticalBrush", isDark, Color.FromArgb(255, 196, 43, 28));
            Style? subtleBtnStyle = Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var sbObj) ? sbObj as Style : null;
            Style captionStyle = (Style)Application.Current.Resources["CaptionTextBlockStyle"];

            var detectedKeys = await PluginManager.Instance.GetDetectedConfigKeysAsync(manifest);
            var initialEntries = new List<(string Key, string Label, string Value, bool IsPreset)>();
            var addedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in manifest.Configuration)
            {
                if (string.IsNullOrWhiteSpace(item.Key)) continue;
                if (addedKeys.Add(item.Key))
                {
                    bool isPreset = detectedKeys.Contains(item.Key, StringComparer.Ordinal);
                    initialEntries.Add((item.Key, item.Label, item.Value ?? string.Empty, isPreset));
                }
            }

            foreach (var detectedKey in detectedKeys)
            {
                if (addedKeys.Add(detectedKey))
                {
                    initialEntries.Add((detectedKey, string.Empty, string.Empty, true));
                }
            }

            var rows = new List<EditableConfigRow>();
            var rowsPanel = new StackPanel { Spacing = 10 };
            var emptyHint = new TextBlock
            {
                Text = L.Plugins_ConfigEmptyHint,
                Style = captionStyle,
                Foreground = tertiaryFg,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8),
                Visibility = initialEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed
            };

            void AddRowUI(string key, string label, string val, bool isPreset)
            {
                emptyHint.Visibility = Visibility.Collapsed;

                var rowGrid = new Grid { ColumnSpacing = 8 };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var keyBox = new TextBox
                {
                    Text = key,
                    PlaceholderText = L.Plugins_ConfigKeyPlaceholder,
                    IsReadOnly = isPreset,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (!string.IsNullOrWhiteSpace(label) && !string.Equals(label, key, StringComparison.Ordinal))
                {
                    ToolTipService.SetToolTip(keyBox, label);
                }
                else if (isPreset)
                {
                    ToolTipService.SetToolTip(keyBox, L.Plugins_ConfigAutoDetected);
                }
                Grid.SetColumn(keyBox, 0);

                var valueBox = new TextBox
                {
                    Text = val,
                    PlaceholderText = !string.IsNullOrWhiteSpace(label) && !string.Equals(label, key, StringComparison.Ordinal)
                        ? label
                        : L.Plugins_ConfigValuePlaceholder,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(valueBox, 1);

                rowGrid.Children.Add(keyBox);
                rowGrid.Children.Add(valueBox);

                EditableConfigRow? rowRef = null;
                if (!isPreset)
                {
                    var removeBtn = new Button
                    {
                        Width = 32,
                        Height = 32,
                        Padding = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Content = new FontIcon { Glyph = "\uE74D", FontSize = 13, Foreground = criticalFg }
                    };
                    if (subtleBtnStyle != null) removeBtn.Style = subtleBtnStyle;
                    removeBtn.Click += (_, _) =>
                    {
                        if (rowRef != null)
                        {
                            rows.Remove(rowRef);
                            rowsPanel.Children.Remove(rowGrid);
                            emptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        }
                    };
                    Grid.SetColumn(removeBtn, 2);
                    rowGrid.Children.Add(removeBtn);
                }

                rowRef = new EditableConfigRow
                {
                    RowRoot = rowGrid,
                    KeyBox = keyBox,
                    ValueBox = valueBox,
                    Label = label,
                    IsPresetKey = isPreset
                };

                rows.Add(rowRef);
                rowsPanel.Children.Add(rowGrid);
            }

            foreach (var entry in initialEntries)
            {
                AddRowUI(entry.Key, entry.Label, entry.Value, entry.IsPreset);
            }

            var addParamBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 12, 5),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE710", FontSize = 12 },
                        new TextBlock { Text = L.Plugins_ConfigAddParamBtn, FontSize = 12.5 }
                    }
                }
            };
            addParamBtn.Click += (_, _) => AddRowUI(string.Empty, string.Empty, string.Empty, isPreset: false);

            var bodyStack = new StackPanel { Spacing = 12, MinWidth = 420 };
            bodyStack.Children.Add(new TextBlock
            {
                Text = L.Plugins_ConfigDesc,
                Style = captionStyle,
                Foreground = secondaryFg,
                TextWrapping = TextWrapping.Wrap
            });
            bodyStack.Children.Add(emptyHint);
            bodyStack.Children.Add(new ScrollViewer
            {
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = rowsPanel
            });
            bodyStack.Children.Add(addParamBtn);

            var dialog = new ContentDialog
            {
                Title = string.Format(L.Plugins_ConfigDialogTitleFmt, manifest.Name),
                Content = bodyStack,
                PrimaryButtonText = L.CustomRules_SaveBtn,
                CloseButtonText = L.Dialog_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.WithAppTheme().ShowAsync() != ContentDialogResult.Primary)
                return;

            var updatedItems = new List<PluginConfigItem>();
            var dedup = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                string k = r.KeyBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(k) || !dedup.Add(k))
                    continue;

                updatedItems.Add(new PluginConfigItem
                {
                    Key = k,
                    Label = string.IsNullOrWhiteSpace(r.Label) ? k : r.Label,
                    Value = r.ValueBox.Text ?? string.Empty
                });
            }

            await PluginManager.Instance.UpdateConfigurationAsync(manifest.Id, updatedItems);
            ShowInfo(
                L.Plugins_ConfigSavedTitle,
                string.Format(L.Plugins_ConfigSavedFmt, manifest.Name),
                InfoBarSeverity.Success);
            RefreshList();
        }

        // ── Actions ──────────────────────────────────────────────────

        private async void BtnAddPlugin_Click(object sender, RoutedEventArgs e)
        {
            var urlBox = new TextBox
            {
                PlaceholderText = "https://raw.githubusercontent.com/.../plugin.js",
                Margin = new Thickness(0, 8, 0, 0)
            };

            var localRadio = new RadioButton { Content = L.Plugins_ImportLocalRadio, IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
            var urlRadio = new RadioButton { Content = L.Plugins_InstallUrlRadio, Margin = new Thickness(0, 0, 0, 8) };

            urlBox.IsEnabled = false;
            urlRadio.Checked += (_, _) => urlBox.IsEnabled = true;
            localRadio.Checked += (_, _) => urlBox.IsEnabled = false;

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(localRadio);
            panel.Children.Add(urlRadio);
            panel.Children.Add(urlBox);

            var dialog = new ContentDialog
            {
                Title = L.Plugins_AddDialogTitle,
                Content = panel,
                PrimaryButtonText = L.Plugins_ContinueBtn,
                CloseButtonText = L.Dialog_Cancel,
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
            if (MainWindow.Instance == null) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            var selectedFile = await Win32FilePickerHelper.PickSingleFileAsync(
                hwnd,
                filters: [("JavaScript (*.js)", "*.js"), (L.FilePicker_AllFiles, "*.*")]);
            if (string.IsNullOrWhiteSpace(selectedFile)) return;

            var pluginsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwellProxy", "plugins");
            Directory.CreateDirectory(pluginsDir);

            var destName = Path.GetFileName(selectedFile);
            var destPath = Path.Combine(pluginsDir, destName);
            File.Copy(selectedFile, destPath, overwrite: true);

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
            ShowInfo(L.Plugins_AddedTitle, string.Format(L.Plugins_AddedFmt, manifest.Name, string.Join(", ", triggers)), InfoBarSeverity.Success);
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
                Triggers = ["OnManual"],
                Disabled = false
            };

            ShowInfo(L.Plugins_DownloadingTitle, filename, InfoBarSeverity.Informational);
            try
            {
                await PluginManager.Instance.AddPluginAsync(manifest);
                await PluginManager.Instance.UpdatePluginAsync(manifest.Id);

                var pluginPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwellProxy", manifest.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(pluginPath))
                {
                    var code = await File.ReadAllTextAsync(pluginPath);
                    manifest.Triggers = PluginManager.DetectTriggersFromCode(code);
                    await PluginManager.Instance.SaveAsync();
                }

                ShowInfo(L.Plugins_InstallOkTitle, string.Format(L.Plugins_InstallOkFmt, manifest.Name, string.Join(", ", manifest.Triggers)), InfoBarSeverity.Success);
                RefreshList();
            }
            catch (Exception ex)
            {
                ShowInfo(L.Plugins_InstallFailTitle, ex.Message, InfoBarSeverity.Error);
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
                ShowInfo(L.Plugins_ExecOkTitle, L.Plugins_ExecOkMsg, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowInfo(L.Plugins_ExecFailTitle, ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task DeletePluginAsync(string id, string name)
        {
            var dlg = new ContentDialog
            {
                Title = L.Plugins_DelTitle,
                Content = string.Format(L.Plugins_DelConfirmFmt, name),
                PrimaryButtonText = L.Plugins_DelConfirmBtn,
                CloseButtonText = L.Dialog_Cancel,
                XamlRoot = XamlRoot
            };
            if (await dlg.WithAppTheme().ShowAsync() != ContentDialogResult.Primary) return;

            await PluginManager.Instance.RemovePluginAsync(id);
            ShowInfo(L.Plugins_DeletedTitle, name, InfoBarSeverity.Informational);
            RefreshList();
        }

        private void ShowInfo(string title, string message, InfoBarSeverity severity)
        {
            InfoBarMain.Title = title;
            InfoBarMain.Message = message;
            InfoBarMain.Severity = severity;
            InfoBarMain.IsOpen = true;
        }

        private static Brush LookupThemedBrush(string key, bool wantDark, Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, wantDark, fallback);
    }
}
