using System;
using System.IO;
using AnywhereWinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AnywhereWinUI.Views
{
    public sealed partial class AddCustomRuleDialog : ContentDialog
    {
        /// <summary>编辑模式时传入要编辑的规则，否则为 null（添加模式）。</summary>
        public CustomRule? EditingRule { get; set; }

        /// <summary>对话框关闭后，通过此属性取回结果规则。</summary>
        public CustomRule? ResultRule { get; private set; }

        public AddCustomRuleDialog()
        {
            this.InitializeComponent();

            TypeComboBox.SelectionChanged += TypeComboBox_SelectionChanged;
            BrowseButton.Click += BrowseButton_Click;
            this.PrimaryButtonClick += AddCustomRuleDialog_PrimaryButtonClick;
        }

        /// <summary>加载编辑模式数据（外部调用）。</summary>
        public void LoadRule(CustomRule rule)
        {
            EditingRule = rule;
            Title = "编辑规则";
            (Content as FrameworkElement)?.FindName("PrimaryButtonText"); // no-op

            // Set type
            int typeIdx = rule.Type switch
            {
                "ip" => 1,
                "process" => 2,
                _ => 0
            };
            TypeComboBox.SelectedIndex = typeIdx;
            MatchTextBox.Text = rule.Match;

            // Set outbound
            int outIdx = rule.OutboundTag switch
            {
                "direct" => 1,
                "block" => 2,
                _ => 0
            };
            OutboundComboBox.SelectedIndex = outIdx;

            PrimaryButtonText = "保存";
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TypeComboBox.SelectedItem is not ComboBoxItem item) return;
            string type = item.Tag?.ToString() ?? "domain";

            switch (type)
            {
                case "domain":
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = "youtube.com 或 geosite:cn";
                    HintTextBlock.Text = "支持精确匹配、regexp:、geosite: 前缀";
                    break;

                case "ip":
                    BrowsePanel.Visibility = Visibility.Collapsed;
                    MatchTextBox.PlaceholderText = "192.168.0.0/16 或 geoip:cn";
                    HintTextBlock.Text = "支持 CIDR 格式，多个用逗号分隔";
                    break;

                case "process":
                    BrowsePanel.Visibility = Visibility.Visible;
                    MatchTextBox.PlaceholderText = "chrome.exe 或完整路径";
                    HintTextBlock.Text = "填写进程名或完整路径，多个用逗号分隔";
                    break;
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder
                };
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");

                // Initialize with the current window handle (WinUI 3 requirement)
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                string filePath = file.Path;
                bool useFullPath = GetBrowseFormat() == "path";

                string value = useFullPath
                    ? filePath
                    : Path.GetFileName(filePath);

                // Append to existing text
                string existing = MatchTextBox.Text.Trim();
                MatchTextBox.Text = string.IsNullOrEmpty(existing)
                    ? value
                    : $"{existing},{value}";
            }
            catch { /* ignore picker cancel */ }
        }

        private string GetBrowseFormat()
        {
            if (BrowseFormatComboBox.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "name";
            return "name";
        }

        private void AddCustomRuleDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            string match = MatchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(match))
            {
                ErrorText.Text = "请填写匹配内容";
                ErrorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            string type = (TypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "domain";
            string outbound = (OutboundComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "proxy";

            ResultRule = new CustomRule
            {
                Type = type,
                Match = match,
                OutboundTag = outbound,
                IsEnabled = EditingRule?.IsEnabled ?? true
            };
        }
    }
}
