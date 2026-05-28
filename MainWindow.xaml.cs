using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;
using AnywhereWinUI.Services;

using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public MainViewModel ViewModel { get; }
        
        private readonly WindowManager _windowManager;
        private bool _isHiddenToTray;
        
        public bool MinimizeOnClose { get; set; } = true;

        public MainWindow()
        {
            ViewModel = ((App)Application.Current).Services.GetService(typeof(MainViewModel)) as MainViewModel;
            Instance = this;
            // Force eager instantiation of TrafficViewModel so it starts listening to traffic events immediately.
            // If we don't do this, it won't subscribe until the user clicks the "Traffic" tab, losing all prior background traffic.
            ((App)Application.Current).Services.GetService(typeof(ViewModels.TrafficViewModel));

            this.InitializeComponent();

            // Set up Window size and location using WinUIEx WindowManager
            _windowManager = WindowManager.Get(this);
            _windowManager.Width = 960;
            _windowManager.Height = 740;
            _windowManager.MinWidth = 0;
            _windowManager.MinHeight = 0;
            this.CenterOnScreen();

            // Set up titlebar behaviors
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Configure system tray
            ConfigureTray();

            // Load and apply saved personalization settings
            var savedTheme = NodesManager.Instance.ThemeSetting switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            SetTheme(savedTheme);
            SetBackdrop(NodesManager.Instance.BackdropSetting);

            // Update theme toggle icon state on startup
            UpdateThemeToggleIcon(savedTheme);

            // Listen to core engine run state changes to dynamically update system tray icon
            CoreManager.Instance.RunningChanged += (s, isRunning) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateAppIcon();
                    UpdateMiniModeUI();
                });
            };

            // Handle pane open/close to toggle header details visibility
            MainNav.PaneOpening += (s, e) => UpdateHeaderVisibility(true);
            MainNav.PaneClosing += (s, e) => UpdateHeaderVisibility(false);
            UpdateHeaderVisibility(false, animate: false); // Default to closed state on startup without animating

            // Handle navigation selection
            MainNav.SelectionChanged += MainNav_SelectionChanged;

            // Load initial view - navigate after layout is ready to avoid double-instantiation
            if (AnywhereWinUI.Services.AppSession.Instance.EnableClassicDashboard)
            {
                MainNav.SelectedItem = DashboardNavItem;
            }
            else
            {
                MainNav.SelectedItem = ServersNavItem;
            }
            
            try
            {
                bool completed = AnywhereWinUI.Helpers.LocalSettingsHelper.TryGetValue<bool>("onboardingCompleted", out var val) && val;
                if (!completed)
                {
                    EnsureOnboardingVisible();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalSettings access failed: {ex.Message}");
            }

            // Wire up Privacy Mode
            ViewModel.PrivacyLocked += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    HideToTray();
                });
            };

            if (AppPrivacyOverlay != null)
            {
                AppPrivacyOverlay.UnlockRequested += (_, password) =>
                {
                    if (ViewModel.Unlock(password))
                    {
                        AppPrivacyOverlay.Clear();
                    }
                    else
                    {
                        AppPrivacyOverlay.ShowError();
                    }
                };
            }

            // Lazy-load OnboardingControl only when needed - skip XAML construction for users
            // who have already completed onboarding (saves ~10MB at startup).
            // AppOnboarding is now a ContentControl placeholder; actual content is inserted here.
            UpdateRoutingNavVisibility();
            UpdateDashboardNavVisibility();
        }

        public void UpdateDashboardNavVisibility()
        {
            if (DashboardNavItem != null)
            {
                DashboardNavItem.Visibility = AnywhereWinUI.Services.AppSession.Instance.EnableClassicDashboard 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;

                if (!AnywhereWinUI.Services.AppSession.Instance.EnableClassicDashboard && MainNav.SelectedItem == DashboardNavItem)
                {
                    MainNav.SelectedItem = ServersNavItem;
                }
            }
        }

        public void UpdateRoutingNavVisibility()
        {
            if (RoutingNavItem != null)
            {
                RoutingNavItem.Visibility = AnywhereWinUI.Services.AppSession.Instance.EnableAdvancedRouting 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;

                // 如果当前正好在 Routing 页面，且设置被关闭了，我们可以跳回默认页面
                if (!AnywhereWinUI.Services.AppSession.Instance.EnableAdvancedRouting && MainNav.SelectedItem == RoutingNavItem)
                {
                    MainNav.SelectedItem = AnywhereWinUI.Services.AppSession.Instance.EnableClassicDashboard ? DashboardNavItem : ServersNavItem;
                }
            }
        }

        private void EnsureOnboardingVisible()
        {
            // Lazy-instantiate the onboarding control only on first need
            if (AppOnboardingHost.Content == null)
            {
                AppOnboardingHost.Content = new Views.OnboardingControl();
            }
            AppOnboardingHost.Visibility = Visibility.Visible;
        }

        public void ShowOnboarding()
        {
            EnsureOnboardingVisible();
            if (AppOnboardingHost.Content is Views.OnboardingControl ctrl)
                ctrl.ResetState();
        }

        public void HideOnboarding()
        {
            AppOnboardingHost.Visibility = Visibility.Collapsed;
        }

        private void MainNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            Type? pageType = null;

            // BUG-9 fix: IsSettingsVisible="False" 已隐藏原生设置按鈕， args.IsSettingsSelected 永远为 false
            // 设置页面入口只通过 tag="settings" 的自定义 NavItem 触发
            if (args.SelectedItemContainer is NavigationViewItem item)
            {
                string tag = item.Tag?.ToString() ?? string.Empty;

                pageType = tag switch
                {
                    "dashboard"   => typeof(AnywhereWinUI.Views.DashboardPage),
                    "traffic"     => typeof(AnywhereWinUI.Views.TrafficPage),
                    "connections" => typeof(AnywhereWinUI.Views.ConnectionsPage),
                    "servers"     => typeof(AnywhereWinUI.Views.ServersPage),
                    "routing"     => typeof(AnywhereWinUI.Views.RoutingPage),
                    "logs"        => typeof(AnywhereWinUI.Views.LogsPage),
                    "settings"    => typeof(AnywhereWinUI.Views.SettingsPage),
                    _ => null
                };
            }

            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
                // Clear back-stack so Frame doesn't keep old pages in memory.
                // Each navigation creates a fresh page instance; no stale references accumulate.
                ContentFrame.BackStack.Clear();
            }
        }

        public ElementTheme GetActiveTheme() => ViewModel.CurrentTheme;

        public void SetTheme(ElementTheme theme)
        {
            ViewModel.CurrentTheme = theme;
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
            UpdateThemeToggleIcon(theme);
        }

        private ElementTheme GetActualTheme()
        {
            if (ViewModel.CurrentTheme != ElementTheme.Default)
            {
                return ViewModel.CurrentTheme;
            }
            return Application.Current.RequestedTheme == ApplicationTheme.Dark 
                ? ElementTheme.Dark 
                : ElementTheme.Light;
        }

        private bool _isThemeTransitioning = false;

        private async void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isThemeTransitioning) return;
            _isThemeTransitioning = true;

            var actualTheme = GetActualTheme();
            var newTheme = actualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;

            // Fire icon spin+bounce animation in parallel — doesn't block the circular reveal
            _ = AnimateThemeIconAsync();

            if (this.Content is FrameworkElement rootElement && ThemeTransitionOverlay != null && MainNav != null)
            {
                // Capture the current visual state (old theme) before changing the theme
                var renderTargetBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                try
                {
                    await renderTargetBitmap.RenderAsync(rootElement);
                    ThemeTransitionImage.Source = renderTargetBitmap;
                    
                    if (actualTheme == ElementTheme.Dark)
                        ThemeTransitionBackground.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32)); // #202020
                    else
                        ThemeTransitionBackground.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243)); // #F3F3F3
                        
                    ThemeTransitionOverlay.Visibility = Visibility.Visible;
                }
                catch
                {
                    // Fallback if rendering fails
                    SetTheme(newTheme);
                    NodesManager.Instance.ThemeSetting = newTheme.ToString();
                    NodesManager.Instance.Save();
                    _isThemeTransitioning = false;
                    return;
                }

                // Get coordinates of the theme toggle button center relative to the rootElement
                Windows.Foundation.Point buttonCenter = new Windows.Foundation.Point(rootElement.ActualWidth - 40, 40); // default fallback
                try
                {
                    var buttonTransform = ThemeToggleButton.TransformToVisual(rootElement);
                    buttonCenter = buttonTransform.TransformPoint(new Windows.Foundation.Point(ThemeToggleButton.ActualWidth / 2, ThemeToggleButton.ActualHeight / 2));
                }
                catch { }

                // Set up the clip on the ContentWrapper visual (covers nav pane)
                // AND on ContentFrame visual (covers right-side content area)
                // Both share the same EllipseGeometry so one animation drives both
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ContentWrapper);
                var frameVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ContentFrame);
                var compositor = visual.Compositor;
                
                var ellipseGeometry = compositor.CreateEllipseGeometry();
                ellipseGeometry.Center = new System.Numerics.Vector2((float)buttonCenter.X, (float)buttonCenter.Y);
                ellipseGeometry.Radius = new System.Numerics.Vector2(0, 0);

                // Apply same geometry to both visuals (shared geometry = synchronized animation)
                visual.Clip = compositor.CreateGeometricClip(ellipseGeometry);

                // Temporarily give ContentWrapper a solid background matching the NEW theme
                // so the circular wipe reveals a solid background instead of transparent Mica
                if (newTheme == ElementTheme.Dark)
                    ContentWrapper.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
                else
                    ContentWrapper.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243));

                // Change the theme underneath (this updates both nav and content to new theme)
                SetTheme(newTheme);
                NodesManager.Instance.ThemeSetting = newTheme.ToString();
                NodesManager.Instance.Save();

                // Small delay to let the theme apply to the visual tree
                await Task.Delay(30);

                // Calculate maximum radius to cover the entire window
                // Button is top-left → farthest corner is bottom-right → animation ends there
                float width = (float)rootElement.ActualWidth;
                float height = (float)rootElement.ActualHeight;

                float maxDistanceX = Math.Max((float)buttonCenter.X, width - (float)buttonCenter.X);
                float maxDistanceY = Math.Max((float)buttonCenter.Y, height - (float)buttonCenter.Y);
                float maxRadius = (float)Math.Sqrt(maxDistanceX * maxDistanceX + maxDistanceY * maxDistanceY);

                // Create easing function (smooth cinematic ease out, like iPhone's torch transition)
                var cubicBezierEasing = compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.25f, 0.85f), 
                    new System.Numerics.Vector2(0.15f, 1.0f)
                );

                // Create animations for Radius.X and Radius.Y on the geometry to grow from 0 to maxRadius
                var radiusAnimationX = compositor.CreateScalarKeyFrameAnimation();
                radiusAnimationX.InsertKeyFrame(1f, maxRadius, cubicBezierEasing);
                radiusAnimationX.Duration = TimeSpan.FromMilliseconds(1300);

                var radiusAnimationY = compositor.CreateScalarKeyFrameAnimation();
                radiusAnimationY.InsertKeyFrame(1f, maxRadius, cubicBezierEasing);
                radiusAnimationY.Duration = TimeSpan.FromMilliseconds(1300);

                var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
                ellipseGeometry.StartAnimation("Radius.X", radiusAnimationX);
                ellipseGeometry.StartAnimation("Radius.Y", radiusAnimationY);
                
                batch.Completed += (s, ev) =>
                {
                    // Cleanup both clips when animation completes
                    visual.Clip = null;
                    ContentWrapper.Background = null; // Restore transparency for actual Mica
                    ThemeTransitionOverlay.Visibility = Visibility.Collapsed;
                    ThemeTransitionImage.Source = null;
                    _isThemeTransitioning = false;
                };
                batch.End();
            }
            else
            {
                SetTheme(newTheme);
                NodesManager.Instance.ThemeSetting = newTheme.ToString();
                NodesManager.Instance.Save();
                _isThemeTransitioning = false;
            }
        }

        /// <summary>
        /// Animates the theme toggle icon: spins out (shrink+rotate 180°), then spins in the new
        /// icon with a spring-bounce scale (0 → 1.25 → 0.92 → 1.0) + continues rotation to 360°.
        /// Runs concurrently with the circular-reveal screen transition.
        /// </summary>
        private async Task AnimateThemeIconAsync()
        {
            if (ThemeToggleIcon == null) return;

            var iconVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ThemeToggleIcon);
            var compositor = iconVisual.Compositor;

            // Anchor rotation/scale at the icon center
            float cx = ThemeToggleIcon.ActualWidth  > 0 ? (float)(ThemeToggleIcon.ActualWidth  / 2) : 8f;
            float cy = ThemeToggleIcon.ActualHeight > 0 ? (float)(ThemeToggleIcon.ActualHeight / 2) : 8f;
            iconVisual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeIn = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.4f, 0f),
                new System.Numerics.Vector2(1f,   1f));

            // ── Phase 1 EXIT: scale 1→0 + rotate 0°→180° over 180 ms ──────────────
            var exitBatch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);

            var exitSX = compositor.CreateScalarKeyFrameAnimation();
            exitSX.InsertKeyFrame(0f, 1f);
            exitSX.InsertKeyFrame(1f, 0f, easeIn);
            exitSX.Duration = TimeSpan.FromMilliseconds(180);

            var exitSY = compositor.CreateScalarKeyFrameAnimation();
            exitSY.InsertKeyFrame(0f, 1f);
            exitSY.InsertKeyFrame(1f, 0f, easeIn);
            exitSY.Duration = TimeSpan.FromMilliseconds(180);

            var exitRot = compositor.CreateScalarKeyFrameAnimation();
            exitRot.InsertKeyFrame(0f, 0f);
            exitRot.InsertKeyFrame(1f, 180f, easeIn);
            exitRot.Duration = TimeSpan.FromMilliseconds(180);

            iconVisual.StartAnimation("Scale.X", exitSX);
            iconVisual.StartAnimation("Scale.Y", exitSY);
            iconVisual.StartAnimation("RotationAngleInDegrees", exitRot);

            var exitTcs = new TaskCompletionSource<bool>();
            exitBatch.Completed += (s, ev) => exitTcs.TrySetResult(true);
            exitBatch.End();
            await exitTcs.Task;

            // Snap visual state so Phase 2 starts cleanly
            iconVisual.RotationAngleInDegrees = 180f;
            iconVisual.Scale = new System.Numerics.Vector3(0f, 0f, 1f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0f,   0f),
                new System.Numerics.Vector2(0.2f, 1f));

            // ── Phase 2 ENTER: spring-bounce scale + rotate 180°→360° over 400 ms ─
            var enterBatch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);

            // Spring-bounce: 0 → overshoot → undershoot → settle
            var enterSX = compositor.CreateScalarKeyFrameAnimation();
            enterSX.InsertKeyFrame(0.00f, 0f);
            enterSX.InsertKeyFrame(0.55f, 1.25f);
            enterSX.InsertKeyFrame(0.75f, 0.92f);
            enterSX.InsertKeyFrame(1.00f, 1f);
            enterSX.Duration = TimeSpan.FromMilliseconds(400);

            var enterSY = compositor.CreateScalarKeyFrameAnimation();
            enterSY.InsertKeyFrame(0.00f, 0f);
            enterSY.InsertKeyFrame(0.55f, 1.25f);
            enterSY.InsertKeyFrame(0.75f, 0.92f);
            enterSY.InsertKeyFrame(1.00f, 1f);
            enterSY.Duration = TimeSpan.FromMilliseconds(400);

            var enterRot = compositor.CreateScalarKeyFrameAnimation();
            enterRot.InsertKeyFrame(0f, 180f);
            enterRot.InsertKeyFrame(1f, 360f, easeOut);
            enterRot.Duration = TimeSpan.FromMilliseconds(400);

            iconVisual.StartAnimation("Scale.X", enterSX);
            iconVisual.StartAnimation("Scale.Y", enterSY);
            iconVisual.StartAnimation("RotationAngleInDegrees", enterRot);

            var enterTcs = new TaskCompletionSource<bool>();
            enterBatch.Completed += (s, ev) => enterTcs.TrySetResult(true);
            enterBatch.End();
            await enterTcs.Task;

            // Cleanup — reset to identity so nothing is left dirty
            iconVisual.RotationAngleInDegrees = 0f;
            iconVisual.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
        }

        private void UpdateThemeToggleIcon(ElementTheme theme)
        {
            if (ThemeToggleIcon != null)
            {
                var actualTheme = theme == ElementTheme.Default 
                    ? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
                    : theme;
                ThemeToggleIcon.Glyph = actualTheme == ElementTheme.Dark ? "\uE706" : "\uE708";
                if (ThemeToggleButton != null)
                {
                    ToolTipService.SetToolTip(ThemeToggleButton, actualTheme == ElementTheme.Dark ? "切换至浅色模式" : "切换至深色模式");
                }
            }
        }

        private void FadeVisual(UIElement element, float targetOpacity, double durationMs)
        {
            if (element == null) return;

            if (targetOpacity > 0f)
            {
                element.Visibility = Visibility.Visible;
            }

            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1f, targetOpacity);
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);

            var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", animation);
            batch.Completed += (s, e) =>
            {
                if (targetOpacity == 0f)
                {
                    element.Visibility = Visibility.Collapsed;
                }
            };
            batch.End();
        }

        private void UpdateHeaderVisibility(bool isOpen, bool animate = true)
        {
            if (ThemeToggleButton != null)
            {
                if (animate)
                {
                    FadeVisual(ThemeToggleButton, isOpen ? 1f : 0f, 250);
                }
                else
                {
                    ThemeToggleButton.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ThemeToggleButton);
                    visual.Opacity = isOpen ? 1f : 0f;
                }
            }

            if (MiniModeToggleButton != null)
            {
                if (animate)
                {
                    FadeVisual(MiniModeToggleButton, isOpen ? 1f : 0f, 250);
                }
                else
                {
                    MiniModeToggleButton.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(MiniModeToggleButton);
                    visual.Opacity = isOpen ? 1f : 0f;
                }
            }

            if (LogoStackPanel != null)
            {
                if (animate)
                {
                    FadeVisual(LogoStackPanel, isOpen ? 0f : 1f, 250);
                }
                else
                {
                    LogoStackPanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(LogoStackPanel);
                    visual.Opacity = isOpen ? 0f : 1f;
                }
            }



        }

        private string _activeBackdrop = "Mica";
        public string ActiveBackdrop => _activeBackdrop;

        public void SetBackdrop(string backdrop)
        {
            _activeBackdrop = backdrop;
            this.SystemBackdrop = backdrop switch
            {
                "Acrylic" => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                _         => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
            };
        }

        private void ConfigureTray()
        {
            UpdateAppIcon();
            this.Title = "Swell Proxy";

            // Configure WinUIEx tray settings
            _windowManager.IsVisibleInTray = true;
            _windowManager.TrayIconSelected += (_, _) => RestoreFromTray();
            _windowManager.TrayIconContextMenu += (_, e) =>
            {
                var flyout = new MenuFlyout();

                var openItem = new MenuFlyoutItem { Text = "显示主界面" };
                openItem.Click += (_, _) => RestoreFromTray();
                flyout.Items.Add(openItem);
                
                var privacyItem = new MenuFlyoutItem { Text = "锁定并隐藏" };
                privacyItem.Click += async (_, _) => await ViewModel.TogglePrivacyModeAsync();
                flyout.Items.Add(privacyItem);

                flyout.Items.Add(new MenuFlyoutSeparator());

                var exitItem = new MenuFlyoutItem { Text = "退出程序" };
                exitItem.Click += (_, _) => ExitApplication();
                flyout.Items.Add(exitItem);

                e.Flyout = flyout;
            };

            // Wire closing behavior: intercept close and hide to system tray
            AppWindow.Closing += (_, args) =>
            {
                if (MinimizeOnClose)
                {
                    args.Cancel = true;
                    HideToTray();
                }
                else
                {
                    ExitApplication();
                }
            };
        }

        private void UpdateAppIcon()
        {
            try
            {
                // Dynamically load active or inactive icon state
                string iconName = CoreManager.Instance.IsRunning ? "output.ico" : "output_grey.ico";
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", iconName);
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
                
                // Update title bar logo
                if (TitleBarLogo != null)
                {
                    TitleBarLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{iconName}"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set AppWindow icon: {ex.Message}");
            }
        }

        private void HideToTray()
        {
            if (_isHiddenToTray) return;

            _isHiddenToTray = true;
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
            // BUG-12 fix: 不再 collapse 根视觉树，避免触发全局 Unloaded 和动画关闭异常
            // OS 在 Hide() 后会自动释放 GPU 资源，无需手动干预。
            ReleaseUiResources();
        }

        public void RestoreFromTray()
        {
            if (!_isHiddenToTray)
            {
                if (!AppWindow.IsVisible)
                {
                    Activate();
                    AppWindow.Show();
                }
                return;
            }

            _isHiddenToTray = false;
            // BUG-12 fix: 对应删除了 HideToTray 中的 Collapsed，此处不再需要 restore Visibility
            AppWindow.IsShownInSwitchers = true;
            Activate();
            AppWindow.Show();
            AppWindow.MoveInZOrderAtTop();
        }

        private static void ReleaseUiResources()
        {
            Task.Run(() =>
            {
                try
                {
                    // Compact LOH in this single GC pass — subscription/JSON paths
                    // routinely allocate >85KB buffers that pin into LOH and never
                    // compact under default settings.
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();

                    using var process = Process.GetCurrentProcess();
                    NativeMethods.SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Tray] Failed to release UI resources: {ex.Message}");
                }
            });
        }

        public async Task<string?> ShowSetPasswordDialogAsync()
        {
            var pwdBox = new PasswordBox
            {
                Header = "请输入隐私保护密码",
                PlaceholderText = "设置一个用于解锁的密码",
                Width = 300
            };

            var dialog = new ContentDialog
            {
                XamlRoot = this.Content.XamlRoot,
                Title = "设置隐私密码",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children = { pwdBox }
                }
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? pwdBox.Password : null;
        }

        // ── Dashboard Hover Animation (Elastic Bounce Squish & Stretch) ──────────────────
        private void DashboardNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DashboardNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(DashboardNavIcon);
            var compositor = visual.Compositor;

            // DESIGN-12 fix: 在启动新动画前先停止正在运行的，防止快速掠过时动画叠加闪烁
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");

            float cx = DashboardNavIcon.ActualWidth > 0 ? (float)(DashboardNavIcon.ActualWidth / 2) : 8f;
            float cy = DashboardNavIcon.ActualHeight > 0 ? (float)(DashboardNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var scaleAnimX = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimX.InsertKeyFrame(0f, 1f);
            scaleAnimX.InsertKeyFrame(0.4f, 1.25f);
            scaleAnimX.InsertKeyFrame(0.7f, 0.9f);
            scaleAnimX.InsertKeyFrame(1.0f, 1.15f);
            scaleAnimX.Duration = TimeSpan.FromMilliseconds(450);

            var scaleAnimY = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimY.InsertKeyFrame(0f, 1f);
            scaleAnimY.InsertKeyFrame(0.4f, 0.82f);
            scaleAnimY.InsertKeyFrame(0.7f, 1.18f);
            scaleAnimY.InsertKeyFrame(1.0f, 1.15f);
            scaleAnimY.Duration = TimeSpan.FromMilliseconds(450);

            visual.StartAnimation("Scale.X", scaleAnimX);
            visual.StartAnimation("Scale.Y", scaleAnimY);
        }

        private void DashboardNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DashboardNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(DashboardNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        // ── Traffic Hover Animation (Scale Pulse) ────────────────────────────────────────
        private void TrafficNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (TrafficNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TrafficNavIcon);
            var compositor = visual.Compositor;

            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");

            float cx = TrafficNavIcon.ActualWidth > 0 ? (float)(TrafficNavIcon.ActualWidth / 2) : 8f;
            float cy = TrafficNavIcon.ActualHeight > 0 ? (float)(TrafficNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(300);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void TrafficNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (TrafficNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(TrafficNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        // ── Servers Hover Animation (Globe 360 Spin & Scale Pulse) ────────────────────────
        private void ServersNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ServersNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ServersNavIcon);
            var compositor = visual.Compositor;

            visual.StopAnimation("RotationAngleInDegrees");
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");

            float cx = ServersNavIcon.ActualWidth > 0 ? (float)(ServersNavIcon.ActualWidth / 2) : 8f;
            float cy = ServersNavIcon.ActualHeight > 0 ? (float)(ServersNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeInOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.42f, 0f), new System.Numerics.Vector2(0.58f, 1f));

            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
            rotAnim.InsertKeyFrame(1f, 360f, easeInOut);
            rotAnim.Duration = TimeSpan.FromMilliseconds(700);

            var scaleAnimX = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimX.InsertKeyFrame(0.5f, 1.2f);
            scaleAnimX.InsertKeyFrame(1f, 1.15f);
            scaleAnimX.Duration = TimeSpan.FromMilliseconds(700);

            var scaleAnimY = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimY.InsertKeyFrame(0.5f, 1.2f);
            scaleAnimY.InsertKeyFrame(1f, 1.15f);
            scaleAnimY.Duration = TimeSpan.FromMilliseconds(700);

            visual.StartAnimation("RotationAngleInDegrees", rotAnim);
            visual.StartAnimation("Scale.X", scaleAnimX);
            visual.StartAnimation("Scale.Y", scaleAnimY);
        }

        private void ServersNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ServersNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ServersNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var rotAnim = compositor.CreateScalarKeyFrameAnimation();
            rotAnim.InsertKeyFrame(1f, 0f, easeOut);
            rotAnim.Duration = TimeSpan.FromMilliseconds(300);

            var scaleAnimX = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimX.InsertKeyFrame(1f, 1f, easeOut);
            scaleAnimX.Duration = TimeSpan.FromMilliseconds(300);

            var scaleAnimY = compositor.CreateScalarKeyFrameAnimation();
            scaleAnimY.InsertKeyFrame(1f, 1f, easeOut);
            scaleAnimY.Duration = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("RotationAngleInDegrees", rotAnim);
            visual.StartAnimation("Scale.X", scaleAnimX);
            visual.StartAnimation("Scale.Y", scaleAnimY);
        }

        // ── Routing Hover Animation (Funnel Sway / Pendulum Swing) ───────────────────────
        private void RoutingNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (RoutingNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RoutingNavIcon);
            var compositor = visual.Compositor;

            visual.StopAnimation("RotationAngleInDegrees");
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");

            float cx = RoutingNavIcon.ActualWidth > 0 ? (float)(RoutingNavIcon.ActualWidth / 2) : 8f;
            float cy = RoutingNavIcon.ActualHeight > 0 ? (float)(RoutingNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var sway = compositor.CreateScalarKeyFrameAnimation();
            sway.InsertKeyFrame(0f, 0f);
            sway.InsertKeyFrame(0.25f, -18f);
            sway.InsertKeyFrame(0.5f, 15f);
            sway.InsertKeyFrame(0.75f, -8f);
            sway.InsertKeyFrame(1.0f, 0f);
            sway.Duration = TimeSpan.FromMilliseconds(650);

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(0.5f, 1.2f);
            scaleX.InsertKeyFrame(1.0f, 1.12f);
            scaleX.Duration = TimeSpan.FromMilliseconds(650);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(0.5f, 1.2f);
            scaleY.InsertKeyFrame(1.0f, 1.12f);
            scaleY.Duration = TimeSpan.FromMilliseconds(650);

            visual.StartAnimation("RotationAngleInDegrees", sway);
            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void RoutingNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (RoutingNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RoutingNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            var rot = compositor.CreateScalarKeyFrameAnimation();
            rot.InsertKeyFrame(1f, 0f, easeOut);
            rot.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
            visual.StartAnimation("RotationAngleInDegrees", rot);
        }



        // ── Logs Hover Animation (Document Scale Pulse) ──────────────────────────────────
        private void LogsNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (LogsNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(LogsNavIcon);
            var compositor = visual.Compositor;

            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");

            float cx = LogsNavIcon.ActualWidth > 0 ? (float)(LogsNavIcon.ActualWidth / 2) : 8f;
            float cy = LogsNavIcon.ActualHeight > 0 ? (float)(LogsNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(300);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void LogsNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (LogsNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(LogsNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        // ── Settings Hover Animation (Gear 180 Spin) ─────────────────────────────────────
        private void SettingsNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            AnimateSettingsGear(180f);
        }

        private void SettingsNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            AnimateSettingsGear(0f);
        }

        private void AnimateSettingsGear(float targetAngle)
        {
            if (SettingsNavIcon == null) return;

            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SettingsNavIcon);
            var compositor = visual.Compositor;

            float cx = SettingsNavIcon.ActualWidth > 0 ? (float)(SettingsNavIcon.ActualWidth / 2) : 8f;
            float cy = SettingsNavIcon.ActualHeight > 0 ? (float)(SettingsNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.1f, 0.9f),
                new System.Numerics.Vector2(0.2f, 1f));

            var rotAnimation = compositor.CreateScalarKeyFrameAnimation();
            rotAnimation.InsertKeyFrame(1f, targetAngle, easeOut);
            rotAnimation.Duration = TimeSpan.FromMilliseconds(400);

            visual.StartAnimation("RotationAngleInDegrees", rotAnimation);
        }

        private async void ExitApplication()
        {
            // Stop backend engine core completely
            await CoreManager.Instance.StopAsync();

            // Quit application process
            _windowManager.Dispose();
            Application.Current.Exit();
        }

        // ── Mini Mode Logic ──────────────────────────────────────────────────────────────
        private bool _isMiniMode;

        private void MiniModeToggleButton_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            SetMiniMode(!_isMiniMode);
        }

        private void MinicloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideToTray();
        }

        private void MiniExpandButton_Click(object sender, RoutedEventArgs e)
        {
            SetMiniMode(false);
        }

        private async void MiniStartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (CoreManager.Instance.IsRunning)
            {
                await CoreManager.Instance.StopAsync();
            }
            else
            {
                if (AppSession.Instance.ProxyModeIndex == 1 && !AnywhereWinUI.Helpers.AdminHelper.IsAdministrator())
                {
                    MiniStartStopButton.IsChecked = false;
                    SetMiniMode(false); // Expand to full mode
                    // Find DashboardViewModel and trigger TUN toggle
                    var dashboardVm = ((App)Application.Current).Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
                    if (dashboardVm != null)
                    {
                        // Set the selected mode in navigation to Dashboard to show the dialog
                        MainNav.SelectedItem = DashboardNavItem;
                        // The ToggleCoreAsync logic in DashboardViewModel will handle the prompt
                        if (dashboardVm.ToggleCoreCommand.CanExecute(null))
                        {
                            dashboardVm.ToggleCoreCommand.Execute(null);
                        }
                    }
                    return;
                }

                try
                {
                    var activeNode = AnywhereWinUI.Services.NodesManager.Instance.Nodes.Find(n => n.Id == AnywhereWinUI.Services.NodesManager.Instance.SelectedNodeId);
                    if (activeNode != null)
                    {
                        var config = AnywhereWinUI.Services.ConfigBuilder.Build(activeNode);
                        bool success = await CoreManager.Instance.StartAsync(config);
                        if (!success)
                        {
                            MiniStartStopButton.IsChecked = false;
                            // Optionally show error to user in mini mode
                        }
                    }
                }
                catch
                {
                    MiniStartStopButton.IsChecked = false;
                }
            }
        }



        private void SetMiniMode(bool isMini)
        {
            _isMiniMode = isMini;
            ApplyWindowMode(isMini);
        }

        private void ApplyWindowMode(bool isMini)
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scale = NativeMethods.GetDpiForWindow(hWnd) / 96.0;

            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;

            var width = isMini ? 330 : 960;
            var height = isMini ? 136 : 740;

            // Remove window manager constraints to allow free resizing
            _windowManager.MinWidth = 0;
            _windowManager.MinHeight = 0;

            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: !isMini);
            presenter.IsResizable = !isMini;
            presenter.IsMaximizable = !isMini;

            AppTitleBar.Visibility = isMini ? Visibility.Collapsed : Visibility.Visible;
            FullModeGrid.Visibility = isMini ? Visibility.Collapsed : Visibility.Visible;
            MiniModeGrid.Visibility = isMini ? Visibility.Visible : Visibility.Collapsed;

            // Use native WinUI title bar logic for dragging
            if (isMini)
            {
                SetTitleBar(MiniDragRegion);
            }
            else
            {
                SetTitleBar(AppTitleBar);
            }

            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)Math.Round(width * scale),
                (int)Math.Round(height * scale)));
                
            UpdateMiniModeUI();
        }

        private void UpdateMiniModeUI()
        {
            if (!_isMiniMode) return;
            
            var activeNode = NodesManager.Instance.Nodes.Find(n => n.Id == NodesManager.Instance.SelectedNodeId);
            var nodeName = activeNode?.Name ?? "未选择节点";
            MiniServerNameText.Text = nodeName;

            string routingMode = AppSession.Instance.RoutingMode;
            MiniProxyModeText.Text = routingMode switch
            {
                "global" => "全局代理",
                "direct" => "全部直连",
                _ => "智能分流"
            };
            
            bool isRunning = CoreManager.Instance.IsRunning;
            MiniStartStopButton.IsChecked = isRunning;
            
            string pingText = isRunning ? "已连接" : "未连接";
            Microsoft.UI.Xaml.Media.Brush statusBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Gray);

            if (isRunning)
            {
                var serversVm = ((App)Application.Current).Services.GetService(typeof(ServersViewModel)) as ServersViewModel;
                var serverItem = serversVm?.AllServers != null ? System.Linq.Enumerable.FirstOrDefault(serversVm.AllServers, s => s.Id == NodesManager.Instance.SelectedNodeId) : null;
                
                if (serverItem != null && serverItem.PingText != "未测试" && serverItem.PingText != "测试中...")
                {
                    pingText = serverItem.PingText;
                    statusBrush = serverItem.PingColor ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Green);
                }
                else
                {
                    if (Application.Current.Resources.TryGetValue("StateSuccessBrush", out var resBrush) && resBrush is Microsoft.UI.Xaml.Media.Brush brush)
                    {
                        statusBrush = brush;
                    }
                    else
                    {
                        statusBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Green);
                    }
                }
            }
            
            MiniStatusDot.Fill = statusBrush;
            MiniStatusText.Text = pingText;
        }
        // ── Connections Hover Animation (Scale Pulse similar to Traffic) ──────────────────
        private void ConnectionsNavItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ConnectionsNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ConnectionsNavIcon);
            var compositor = visual.Compositor;

            float cx = ConnectionsNavIcon.ActualWidth > 0 ? (float)(ConnectionsNavIcon.ActualWidth / 2) : 8f;
            float cy = ConnectionsNavIcon.ActualHeight > 0 ? (float)(ConnectionsNavIcon.ActualHeight / 2) : 8f;
            visual.CenterPoint = new System.Numerics.Vector3(cx, cy, 0f);

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.1f, 0.9f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(300);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1.22f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(300);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }

        private void ConnectionsNavItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ConnectionsNavIcon == null) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ConnectionsNavIcon);
            var compositor = visual.Compositor;

            var easeOut = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0f, 0f), new System.Numerics.Vector2(0.2f, 1f));

            var scaleX = compositor.CreateScalarKeyFrameAnimation();
            scaleX.InsertKeyFrame(1f, 1f, easeOut);
            scaleX.Duration = TimeSpan.FromMilliseconds(250);

            var scaleY = compositor.CreateScalarKeyFrameAnimation();
            scaleY.InsertKeyFrame(1f, 1f, easeOut);
            scaleY.Duration = TimeSpan.FromMilliseconds(250);

            visual.StartAnimation("Scale.X", scaleX);
            visual.StartAnimation("Scale.Y", scaleY);
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
