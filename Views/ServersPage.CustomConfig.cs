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
        // ================= Custom Native sing-box Config Editor =================

        private void AddCustomConfig_Click(object sender, RoutedEventArgs e)
        {
            _ = OpenCustomConfigEditorAsync(null);
        }

        private async Task OpenCustomConfigEditorAsync(PersistedNode? existingNode, string? initialName = null)
        {
            _editingNodeId = existingNode?.Id ?? string.Empty;

            if (CustomConfigPanelTitle != null)
            {
                CustomConfigPanelTitle.Text = existingNode == null ? L.CustomCfg_AddTitle : L.CustomCfg_EditTitle;
            }

            if (CustomConfigNameInput != null)
            {
                CustomConfigNameInput.Text = existingNode?.Name ?? initialName ?? string.Empty;
            }

            if (CustomBridgeGuiToggle != null)
            {
                CustomBridgeGuiToggle.IsOn = existingNode?.CustomBridgeGui ?? true;
            }

            if (CustomConfigInfoBar != null)
            {
                CustomConfigInfoBar.IsOpen = false;
            }

            bool hasSavedFile = existingNode != null;
            if (OpenExternalJsonButton != null) OpenExternalJsonButton.IsEnabled = hasSavedFile;
            if (ReloadDiskJsonButton != null) ReloadDiskJsonButton.IsEnabled = hasSavedFile;

            string jsonContent = string.Empty;
            if (existingNode != null)
            {
                jsonContent = await CustomConfigService.ReadConfigTextAsync(existingNode);
            }

            ShowPanel("custom_edit");

            if (CustomJsonEditorBox != null)
            {
                CustomJsonEditorBox.Text = jsonContent;
            }
        }

        private void ShowCustomInfoBar(InfoBarSeverity severity, string title, string? message = null)
        {
            if (CustomConfigInfoBar == null) return;
            CustomConfigInfoBar.Severity = severity;
            CustomConfigInfoBar.Title = title;
            CustomConfigInfoBar.Message = message ?? string.Empty;
            CustomConfigInfoBar.IsOpen = true;
        }

        private async void ImportCustomJsonFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var filters = new (string Name, string Spec)[]
                {
                    ("JSON (*.json)", "*.json"),
                    (L.FilePicker_AllFiles, "*.*")
                };

                string? picked = await Win32FilePickerHelper.PickSingleFileAsync(
                    App.Current.MainWindowHandle,
                    filters,
                    L.CustomCfg_PickFileTitle);

                if (string.IsNullOrWhiteSpace(picked) || !File.Exists(picked))
                    return;

                string rawText = await File.ReadAllTextAsync(picked, Encoding.UTF8);
                CustomJsonEditorBox.Text = rawText;

                if (string.IsNullOrWhiteSpace(CustomConfigNameInput.Text))
                {
                    CustomConfigNameInput.Text = Path.GetFileNameWithoutExtension(picked);
                }

                var summary = CustomConfigService.InspectConfig(rawText);
                if (summary.IsValidJson)
                {
                    ShowCustomInfoBar(
                        InfoBarSeverity.Success,
                        L.CustomCfg_ImportedTitle,
                        Loc.Format("CustomCfg_SummaryBannerFmt", summary.InboundCount, summary.OutboundCount, summary.RuleCount, summary.ShortSummary));
                }
                else
                {
                    ShowCustomInfoBar(InfoBarSeverity.Warning, L.CustomCfg_ImportedWithWarnTitle, summary.ParseError);
                }
            }
            catch (Exception ex)
            {
                ShowCustomInfoBar(InfoBarSeverity.Error, L.Servers_ImportErrorTitle, ex.Message);
            }
        }

        private async void InsertCustomTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(CustomJsonEditorBox.Text))
            {
                var dialog = new ContentDialog
                {
                    Title = L.CustomCfg_OverwriteTemplateTitle,
                    Content = L.CustomCfg_OverwriteTemplateMsg,
                    PrimaryButtonText = L.Dialog_OK,
                    CloseButtonText = L.Dialog_Cancel,
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                var res = await dialog.WithAppTheme().ShowAsync();
                if (res != ContentDialogResult.Primary)
                    return;
            }

            CustomJsonEditorBox.Text = CustomConfigService.GetMinimalTemplate(AppSession.Instance.MixedPort);
            if (string.IsNullOrWhiteSpace(CustomConfigNameInput.Text))
            {
                CustomConfigNameInput.Text = L.CustomCfg_DefaultName;
            }
            ShowCustomInfoBar(InfoBarSeverity.Informational, L.CustomCfg_TemplateInsertedTitle, string.Empty);
        }

        private void FormatCustomJson_Click(object sender, RoutedEventArgs e)
        {
            if (CustomConfigService.TryFormatJson(CustomJsonEditorBox.Text, out var formatted, out var error))
            {
                CustomJsonEditorBox.Text = formatted;
                ShowCustomInfoBar(InfoBarSeverity.Success, L.CustomCfg_FormattedOkTitle, string.Empty);
            }
            else
            {
                ShowCustomInfoBar(InfoBarSeverity.Error, L.CustomCfg_FormatFailTitle, error);
            }
        }

        private async void ValidateCustomJson_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateCustomJsonButton != null) ValidateCustomJsonButton.IsEnabled = false;
            if (ValidateCustomIcon != null) ValidateCustomIcon.Visibility = Visibility.Collapsed;
            if (ValidateCustomSpinner != null)
            {
                ValidateCustomSpinner.Visibility = Visibility.Visible;
                ValidateCustomSpinner.IsActive = true;
            }

            try
            {
                var (ok, msg) = await CustomConfigService.ValidateWithCoreAsync(CustomJsonEditorBox.Text);
                ShowCustomInfoBar(
                    ok ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                    ok ? L.CustomCfg_ValidateOkTitle : L.CustomCfg_ValidateFailTitle,
                    msg);
            }
            finally
            {
                if (ValidateCustomSpinner != null)
                {
                    ValidateCustomSpinner.IsActive = false;
                    ValidateCustomSpinner.Visibility = Visibility.Collapsed;
                }
                if (ValidateCustomIcon != null) ValidateCustomIcon.Visibility = Visibility.Visible;
                if (ValidateCustomJsonButton != null) ValidateCustomJsonButton.IsEnabled = true;
            }
        }

        private void OpenExternalCustomJson_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_editingNodeId)) return;
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
            if (node == null) return;

            try
            {
                string path = CustomConfigService.ResolveConfigFilePath(node);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(CustomConfigService.ConfigsDirectory);
                    File.WriteAllText(path, CustomJsonEditorBox.Text ?? string.Empty, Encoding.UTF8);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowCustomInfoBar(InfoBarSeverity.Error, L.CustomCfg_OpenExtFailTitle, ex.Message);
            }
        }

        private async void ReloadCustomJsonFromDisk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_editingNodeId)) return;
            var node = NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
            if (node == null) return;

            string reloaded = await CustomConfigService.ReadConfigTextAsync(node);
            CustomJsonEditorBox.Text = reloaded;
            ShowCustomInfoBar(InfoBarSeverity.Informational, L.CustomCfg_ReloadedTitle, string.Empty);
        }

        private void CustomJsonEditorBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Tab && sender is TextBox tb)
            {
                int start = tb.SelectionStart;
                int len = tb.SelectionLength;
                string current = tb.Text ?? string.Empty;
                if (start >= 0 && start <= current.Length)
                {
                    tb.Text = current.Substring(0, start) + "  " + current.Substring(Math.Min(current.Length, start + len));
                    tb.SelectionStart = start + 2;
                    tb.SelectionLength = 0;
                    e.Handled = true;
                }
            }
        }

        private async void SaveCustomConfig_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomConfigNameInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = L.CustomCfg_DefaultName;
            }

            string jsonText = CustomJsonEditorBox.Text ?? string.Empty;
            var checkSummary = CustomConfigService.InspectConfig(jsonText);
            if (!checkSummary.IsValidJson)
            {
                ShowCustomInfoBar(InfoBarSeverity.Error, L.CustomCfg_SaveBlockedInvalidTitle, checkSummary.ParseError);
                return;
            }

            SaveCustomConfigButton.IsEnabled = false;
            try
            {
                PersistedNode targetNode;
                if (string.IsNullOrEmpty(_editingNodeId))
                {
                    targetNode = new PersistedNode
                    {
                        Name = name,
                        Protocol = CustomConfigService.ProtocolName,
                        CustomBridgeGui = CustomBridgeGuiToggle.IsOn,
                        SubscriptionId = string.Empty
                    };
                    await CustomConfigService.SaveConfigTextAsync(targetNode, jsonText);
                    NodesManager.Instance.AddManualNode(targetNode);
                }
                else
                {
                    var found = NodesManager.Instance.Nodes.Find(n => n.Id == _editingNodeId);
                    if (found == null)
                    {
                        targetNode = new PersistedNode
                        {
                            Id = _editingNodeId,
                            Name = name,
                            Protocol = CustomConfigService.ProtocolName,
                            CustomBridgeGui = CustomBridgeGuiToggle.IsOn,
                            SubscriptionId = string.Empty
                        };
                        await CustomConfigService.SaveConfigTextAsync(targetNode, jsonText);
                        NodesManager.Instance.AddManualNode(targetNode);
                    }
                    else
                    {
                        targetNode = found;
                        targetNode.Name = name;
                        targetNode.Protocol = CustomConfigService.ProtocolName;
                        targetNode.CustomBridgeGui = CustomBridgeGuiToggle.IsOn;
                        await CustomConfigService.SaveConfigTextAsync(targetNode, jsonText);
                        NodesManager.Instance.Save();
                    }
                }

                _lastSelectedNodeId = targetNode.Id;
                LoadServersList();

                // Hot-reload core if currently connected to this exact custom profile
                if (CoreManager.Instance.IsRunning && NodesManager.Instance.SelectedNodeId == targetNode.Id)
                {
                    string runtimeCfg = await ConfigBuilder.BuildAsync(targetNode);
                    await CoreManager.Instance.StartAsync(runtimeCfg);
                }
            }
            catch (Exception ex)
            {
                ShowCustomInfoBar(InfoBarSeverity.Error, L.Servers_ImportErrorTitle, ex.Message);
            }
            finally
            {
                SaveCustomConfigButton.IsEnabled = true;
            }
        }

        private async Task CopyCustomConfigJsonWithDialogAsync(PersistedNode node)
        {
            try
            {
                string json = await CustomConfigService.ReadConfigTextAsync(node);
                if (!string.IsNullOrEmpty(json))
                {
                    var pkg = new DataPackage();
                    pkg.SetText(json);
                    Clipboard.SetContent(pkg);
                }

                var dialog = new ContentDialog
                {
                    Title = L.CustomCfg_CopiedJsonTitle,
                    Content = L.CustomCfg_CopiedJsonMsg,
                    CloseButtonText = L.Dialog_OK,
                    XamlRoot = this.XamlRoot
                };
                await dialog.WithAppTheme().ShowAsync();
            }
            catch { }
        }
    }
}
