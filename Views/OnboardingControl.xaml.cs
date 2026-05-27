using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AnywhereWinUI.Views
{
    public sealed partial class OnboardingControl : UserControl
    {
        private int _currentPage = 0;
        private bool _isUserInChina = false;
        private readonly HttpClient _httpClient = new();
        private Brush? _activeBrush;
        private Brush? _inactiveBrush;
        
        public OnboardingControl()
        {
            this.InitializeComponent();
            this.Loaded += OnboardingControl_Loaded;
        }
        
        private void OnboardingControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 启动背景环境球体动画
            AmbientLightStoryboard.Begin();
            // 2. 自动检测用户是否在中国大陆（基于时区或CultureInfo）
            DetectUserRegion();
        }
        
        private void DetectUserRegion()
        {
            try
            {
                var timeZoneId = TimeZoneInfo.Local.Id;
                var cultureName = CultureInfo.CurrentCulture.Name;
                // 判断时区或语言环境是否与中国大陆相关
                _isUserInChina = timeZoneId.Contains("China", StringComparison.OrdinalIgnoreCase) ||
                                 timeZoneId.Contains("Shanghai", StringComparison.OrdinalIgnoreCase) ||
                                 cultureName.EndsWith("CN", StringComparison.OrdinalIgnoreCase);
                if (_isUserInChina)
                {
                    RegionBannerText.Text = "自动检测：识别到您位于 中国大陆，已自动为您预设直连绕过规则。";
                    BypassChinaToggle.IsOn = true;
                }
                else
                {
                    RegionBannerText.Text = "自动检测：识别到您位于 海外地区，可按需配置直连规则。";
                    BypassChinaToggle.IsOn = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Region detection failed: {ex.Message}");
                RegionBannerText.Text = "自动检测已就绪，可按需配置规则。";
            }
        }
        public void ResetState()
        {
            _currentPage = 0;
            UpdatePageVisibility();
        }
        
        private void UpdatePageVisibility()
        {
            // 切换页面面板
            Page0.Visibility = _currentPage == 0 ? Visibility.Visible : Visibility.Collapsed;
            Page1.Visibility = _currentPage == 1 ? Visibility.Visible : Visibility.Collapsed;
            Page2.Visibility = _currentPage == 2 ? Visibility.Visible : Visibility.Collapsed;
            
            // 按钮显示状态
            PrevButton.Visibility = _currentPage > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Content = _currentPage == 2 ? "立即开启" : "下一步";
            
            // 进度圆点状态切换动画效果
            if (_activeBrush == null)
            {
                _activeBrush = Dot0.Fill;
                _inactiveBrush = Dot1.Fill;
            }
            
            var activeBrush = _activeBrush;
            var inactiveBrush = _inactiveBrush;
            
            Dot0.Width = _currentPage == 0 ? 24 : 8;
            Dot0.Fill = _currentPage == 0 ? activeBrush : inactiveBrush;
            
            Dot1.Width = _currentPage == 1 ? 24 : 8;
            Dot1.Fill = _currentPage == 1 ? activeBrush : inactiveBrush;
            
            Dot2.Width = _currentPage == 2 ? 24 : 8;
            Dot2.Fill = _currentPage == 2 ? activeBrush : inactiveBrush;
        }
        
        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                UpdatePageVisibility();
            }
        }
        
        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < 2)
            {
                _currentPage++;
                UpdatePageVisibility();
            }
            else
            {
                // 在最后一页，保存设置并导入订阅
                await CompleteOnboardingAsync();
            }
        }
        
        private async Task CompleteOnboardingAsync()
        {
            string inputText = SubscriptionInput.Text?.Trim() ?? string.Empty;
            
            // 保存分流规则配置到 LocalSettings
            AnywhereWinUI.Helpers.LocalSettingsHelper.SetValue("bypassChina", BypassChinaToggle.IsOn);
            AnywhereWinUI.Helpers.LocalSettingsHelper.SetValue("blockAds", BlockAdsToggle.IsOn);
            
            if (!string.IsNullOrEmpty(inputText))
            {
                ImportProgressRing.IsActive = true;
                ImportStatusText.Foreground = (Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"];
                ImportStatusText.Text = "正在下载并导入订阅...";
                NextButton.IsEnabled = false;
                PrevButton.IsEnabled = false;
                
                try
                {
                    if (inputText.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                        inputText.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        // 异步抓取订阅链接
                        var response = await _httpClient.GetStringAsync(inputText);
                        int importedCount = ParseAndSaveNodes(response);
                        
                        ImportStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                        ImportStatusText.Text = $"成功导入 {importedCount} 个节点！";
                    }
                    else
                    {
                        // 解析单个或多个分享链接
                        int importedCount = ParseShareLinks(inputText);
                        ImportStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                        ImportStatusText.Text = $"成功导入 {importedCount} 个节点！";
                    }
                    await Task.Delay(1200); // 预留物理过渡动画时间
                }
                catch (Exception ex)
                {
                    ImportStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    ImportStatusText.Text = $"导入失败: {ex.Message}";
                    ImportProgressRing.IsActive = false;
                    NextButton.IsEnabled = true;
                    PrevButton.IsEnabled = true;
                    return;
                }
            }
            
            // 完成并关闭向导界面：通过 MainWindow 公开方法隐藏宿主容器
            AnywhereWinUI.Helpers.LocalSettingsHelper.SetValue("onboardingCompleted", true);
            MainWindow.Instance?.HideOnboarding();
        }
        
        private int ParseAndSaveNodes(string subscriptionContent)
        {
            // 在这里实现您的 Base64 订阅内容解码和解析逻辑
            Debug.WriteLine($"Fetched subscription text size: {subscriptionContent.Length}");
            return 12; // 模拟返回导入了 12 个节点
        }
        
        private int ParseShareLinks(string shareLinksText)
        {
            // 在这里实现单条或多条 vmess:// vless:// ss:// 链接的解析
            var lines = shareLinksText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length;
        }
    }
}
