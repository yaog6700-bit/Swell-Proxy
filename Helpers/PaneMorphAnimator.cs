using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Unigram 风格的紧凑桌面透底亚克力悬浮卡片菜单动画（适配 LeftMinimal 模式的 NavigationView）：
    /// 1. 真正的桌面亚克力 / 云母透底：
    ///    WinUI 3 的 Window.SystemBackdrop (DesktopAcrylicBackdrop / MicaBackdrop) 位于 XAML 交换链下方的 DWM 层，
    ///    而应用内 AcrylicBrush 只能采样 XAML 层（空白处为 #00000000 黑底，会完全遮住下方的桌面壁纸亚克力）。
    ///    因此我们在菜单打开/形变期间用 Win2D CanvasGeometry 在底层 ContentBorder 上实时裁出与卡片完全同形的圆角镂空，
    ///    并用低不透明度毛玻璃色罩绘制 _cardSurface —— 既不透出底下页面文字，又能直接透出主窗口同款的桌面壁纸亚克力！
    /// 2. 抽屉只包裹可见菜单项高度，下方留空，外阴影仅投射到底层页面外部；
    /// 3. 双轴错峰欠阻尼物理弹簧 (DampingRatio = 0.68, X 快 Y 慢) + 过冲回弹缩放；
    /// 4. 内容防挤压错峰淡入，关闭时用 CubicBezier(0.4, 0, 0.2, 1) 干脆折回。
    /// </summary>
    public sealed class PaneMorphAnimator
    {
        // ── 物理与时间参数 ───────────────────────────────────────────────────
        private const float DampingRatio = 0.68f;     // 欠阻尼 ≈5.45% 物理过冲
        private const double DampedPeriodX = 0.28;    // 横向阻尼振荡周期（约 140ms 达峰值，更快领跑）
        private const double DampedPeriodY = 0.38;    // 纵向阻尼振荡周期（约 190ms 达峰值，稍慢拉伸）
        private const double CloseWidthSec = 0.24;    // 关闭宽度时长 240ms
        private const double CloseHeightSec = 0.19;   // 关闭高度时长 190ms（高度先收，宽度后收）
        private static readonly TimeSpan CloseHold = TimeSpan.FromMilliseconds(260);

        private const float CardLeft = 4f;
        private const float CardTop = 4f;
        private const float StartCornerRadius = 4f;
        private const float EndCornerRadius = 8f;
        private const float BounceFactor = 0.6f;

        private readonly NavigationView _nav;
        private readonly FrameworkElement? _miniButton;
        private readonly FrameworkElement? _themeButton;
        private readonly FrameworkElement? _logoPanel;

        private SplitView? _splitView;
        private Grid? _morphHost;
        private Border? _cardSurface;
        private Grid? _contentClipHost;
        private FrameworkElement? _paneRoot;
        private FrameworkElement? _paneContent;
        private FrameworkElement? _pageContentBorder;
        private Grid? _shadowReceiver;
        private Visual? _pageContentVisual;
        private Visual? _shadowReceiverVisual;
        private CompositionPathGeometry? _cutoutPathGeometry;
        private CompositionGeometricClip? _cutoutClip;
        private CompositionGeometricClip? _shadowCutoutClip;
        private ScrollViewer? _menuScrollViewer;
        private Grid? _shadowCaster;
        private Grid? _cardShadowCaster;
        private FrameworkElement? _toggleButton;

        private Compositor? _compositor;
        private CompositionPropertySet? _props;
        private bool _initialized;

        // ── 同步帧循环状态（仅在展开/收起约 0.3s 期间挂载，静止时零开销）────
        private readonly Stopwatch _animClock = new();
        private bool _renderingHooked;
        private bool _animOpening;
        private int _closeVersion;
        private float _curPx;
        private float _curPy;
        private float _velPx;
        private float _velPy;
        private float _startPx;
        private float _startPy;
        private float _startVelX;
        private float _startVelY;

        private Vector2 _btnOffset = new(CardLeft, CardTop);
        private Vector2 _btnSize = new(40f, 36f);
        private Vector3 _anchor = new(24f, 22f, 0f);
        private float _cardW = 196f;
        private float _cardH = 260f;
        private Vector2 _pageDelta = Vector2.Zero;

        public bool HandlesHeaderElements => _initialized;

        public PaneMorphAnimator(
            NavigationView nav,
            FrameworkElement? miniModeButton = null,
            FrameworkElement? themeToggleButton = null,
            FrameworkElement? logoStackPanel = null)
        {
            _nav = nav;
            _miniButton = miniModeButton;
            _themeButton = themeToggleButton;
            _logoPanel = logoStackPanel;
            _nav.Loaded += (_, _) => TryInitialize();
        }

        private void TryInitialize()
        {
            if (_initialized) return;
            try
            {
                _splitView = FindByName<SplitView>(_nav, "RootSplitView");
                if (_splitView == null) return;

                _paneRoot = FindByName<FrameworkElement>(_splitView, "PaneRoot");
                _toggleButton = FindByName<FrameworkElement>(_nav, "TogglePaneButton");
                _shadowCaster = FindByName<Grid>(_nav, "ShadowCaster");
                if (_paneRoot == null) return;

                if (VisualTreeHelper.GetChildrenCount(_paneRoot) > 0)
                    _paneContent = VisualTreeHelper.GetChild(_paneRoot, 0) as FrameworkElement;

                if (!WrapPaneRootInMorphHost()) return;

                ConfigureCompactCardLayout();
                ApplyCardThemeBrushes();
                PatchTransitions();
                SetupComposition();

                _nav.ActualThemeChanged += (_, _) => ApplyCardThemeBrushes();
                _splitView.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, OnIsPaneOpenChanged);
                _splitView.SizeChanged += (_, _) =>
                {
                    UpdateScrollMaxHeight();
                    UpdateTargetSize();
                };
                _paneRoot.SizeChanged += (_, _) => UpdateTargetSize();
                _paneRoot.LayoutUpdated += (_, _) =>
                {
                    if (_splitView.IsPaneOpen) UpdateTargetSize();
                };

                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PaneMorph] init failed, fallback to default animation: {ex}");
            }
        }

        private bool WrapPaneRootInMorphHost()
        {
            if (_splitView == null || _paneRoot == null) return false;
            if (VisualTreeHelper.GetChildrenCount(_splitView) == 0) return false;
            if (VisualTreeHelper.GetChild(_splitView, 0) is not Grid templateRootGrid) return false;

            int idx = templateRootGrid.Children.IndexOf(_paneRoot);
            if (idx < 0) return false;

            _morphHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumnSpan(_morphHost, 2);
            Canvas.SetZIndex(_morphHost, 1);

            _cardSurface = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Width = _nav.OpenPaneLength,
                CornerRadius = new CornerRadius(EndCornerRadius),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            _contentClipHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            templateRootGrid.Children.RemoveAt(idx);
            _contentClipHost.Children.Add(_paneRoot);
            _morphHost.Children.Add(_cardSurface);
            _morphHost.Children.Add(_contentClipHost);

            var contentRoot = FindByName<FrameworkElement>(templateRootGrid, "ContentRoot");
            if (contentRoot != null)
            {
                if (VisualTreeHelper.GetChildrenCount(contentRoot) > 0)
                {
                    _pageContentBorder = VisualTreeHelper.GetChild(contentRoot, 0) as FrameworkElement;
                }

                // 独立的阴影接收层：同步应用与底层页面相同的圆角镂空裁剪，
                // 这样卡片外围有柔和外阴影，而卡片内部不会残留黑色阴影核遮挡桌面亚克力。
                _shadowReceiver = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = false
                };
                Grid.SetColumnSpan(_shadowReceiver, 2);
                Canvas.SetZIndex(_shadowReceiver, 1);

                _cardShadowCaster = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(CardLeft, CardTop, 0, 0),
                    Width = _nav.OpenPaneLength,
                    CornerRadius = new CornerRadius(EndCornerRadius),
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Translation = new Vector3(0f, 0f, 32f)
                };
                Grid.SetColumnSpan(_cardShadowCaster, 2);
                Canvas.SetZIndex(_cardShadowCaster, 1);

                var themeShadow = new ThemeShadow();
                themeShadow.Receivers.Add(_shadowReceiver);
                _cardShadowCaster.Shadow = themeShadow;

                templateRootGrid.Children.Insert(idx, _shadowReceiver);
                templateRootGrid.Children.Insert(idx + 1, _cardShadowCaster);
                templateRootGrid.Children.Insert(idx + 2, _morphHost);
            }
            else
            {
                templateRootGrid.Children.Insert(idx, _morphHost);
            }

            if (_paneRoot is Grid paneGrid)
            {
                paneGrid.BackgroundTransition = null;
                paneGrid.Background = new SolidColorBrush(Colors.Transparent);
                paneGrid.BorderBrush = null;
                paneGrid.BorderThickness = new Thickness(0);
                paneGrid.CornerRadius = new CornerRadius(EndCornerRadius);
            }

            return true;
        }

        /// <summary>
        /// 半透明悬浮玻璃色罩：因为卡片正下方的页面内容与阴影核均已被同形圆角镂空，
        /// 卡片下方直接就是主窗口的 DesktopAcrylicBackdrop / MicaBackdrop（真实桌面壁纸模糊层）。
        /// 深色模式下主窗口本身已是深色桌面亚克力，叠加微亮毛玻璃光泽（Fluent Card Fill）即可完整保留壁纸通透感；
        /// 浅色模式下叠加清透雾白玻璃罩。
        /// </summary>
        private void ApplyCardThemeBrushes()
        {
            if (_cardSurface == null) return;

            bool isDark = _nav.ActualTheme == ElementTheme.Dark ||
                          (_nav.ActualTheme == ElementTheme.Default &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);

            _cardSurface.Background = new SolidColorBrush(
                isDark
                    ? Color.FromArgb(24, 255, 255, 255)
                    : Color.FromArgb(148, 255, 255, 255));

            _cardSurface.BorderBrush = new SolidColorBrush(
                isDark
                    ? Color.FromArgb(44, 255, 255, 255)
                    : Color.FromArgb(24, 0, 0, 0));
        }

        private void ConfigureCompactCardLayout()
        {
            if (_paneRoot == null || _splitView == null) return;

            _splitView.CornerRadius = new CornerRadius(EndCornerRadius);

            _paneRoot.VerticalAlignment = VerticalAlignment.Top;
            _paneRoot.Margin = new Thickness(CardLeft, CardTop, 0, 0);

            var paneContentGrid = FindByName<Grid>(_paneRoot, "PaneContentGrid");
            if (paneContentGrid != null)
            {
                paneContentGrid.Margin = new Thickness(0, 0, 0, 2);
                paneContentGrid.VerticalAlignment = VerticalAlignment.Top;
                if (paneContentGrid.RowDefinitions.Count > 6)
                {
                    paneContentGrid.RowDefinitions[6].Height = GridLength.Auto;
                }
            }

            var itemsContainerGrid = FindByName<Grid>(_paneRoot, "ItemsContainerGrid");
            if (itemsContainerGrid != null && itemsContainerGrid.RowDefinitions.Count > 0)
            {
                itemsContainerGrid.VerticalAlignment = VerticalAlignment.Top;
                itemsContainerGrid.RowDefinitions[0].Height = GridLength.Auto;
            }

            _menuScrollViewer = FindByName<ScrollViewer>(_paneRoot, "MenuItemsScrollViewer");
            UpdateScrollMaxHeight();

            var hcBorder = FindByName<FrameworkElement>(_paneRoot, "HCPaneBorder");
            if (hcBorder != null)
            {
                hcBorder.MaxWidth = 0;
                hcBorder.Opacity = 0;
            }

            if (_shadowCaster != null)
            {
                _shadowCaster.Shadow = null;
                _shadowCaster.Width = 0;
                _shadowCaster.Height = 0;
                _shadowCaster.Opacity = 0;
                _shadowCaster.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateScrollMaxHeight()
        {
            if (_menuScrollViewer == null || _splitView == null) return;
            double avail = _splitView.ActualHeight;
            if (avail > 80)
            {
                _menuScrollViewer.MaxHeight = Math.Max(80, avail - 64);
            }
        }

        private void PatchTransitions()
        {
            if (_splitView != null && VisualTreeHelper.GetChildrenCount(_splitView) > 0 &&
                VisualTreeHelper.GetChild(_splitView, 0) is FrameworkElement splitTemplateRoot)
            {
                foreach (var group in VisualStateManager.GetVisualStateGroups(splitTemplateRoot))
                {
                    var toRemove = new List<VisualTransition>();
                    foreach (var t in group.Transitions)
                    {
                        string from = t.From ?? string.Empty;
                        string to = t.To ?? string.Empty;

                        if (to.Contains("OverlayLeft", StringComparison.Ordinal))
                        {
                            toRemove.Add(t);
                        }
                        else if (from.Contains("OverlayLeft", StringComparison.Ordinal) && to == "Closed" && t.Storyboard != null)
                        {
                            for (int i = t.Storyboard.Children.Count - 1; i >= 0; i--)
                            {
                                string targetName = Storyboard.GetTargetName(t.Storyboard.Children[i]);
                                if (targetName == "PaneTransform" || targetName == "PaneClipRectangleTransform")
                                {
                                    t.Storyboard.Children.RemoveAt(i);
                                }
                                else if (targetName == "LightDismissLayer" &&
                                         t.Storyboard.Children[i] is DoubleAnimationUsingKeyFrames dak &&
                                         dak.KeyFrames.Count > 0)
                                {
                                    dak.KeyFrames[dak.KeyFrames.Count - 1].KeyTime = KeyTime.FromTimeSpan(CloseHold);
                                }
                            }
                        }
                    }
                    foreach (var r in toRemove) group.Transitions.Remove(r);
                }
            }

            if (VisualTreeHelper.GetChildrenCount(_nav) > 0 &&
                VisualTreeHelper.GetChild(_nav, 0) is FrameworkElement navTemplateRoot)
            {
                foreach (var group in VisualStateManager.GetVisualStateGroups(navTemplateRoot))
                {
                    if (group.Name == "PaneOverlayGroup")
                    {
                        group.Transitions.Clear();
                    }
                }
            }
        }

        private void SetupComposition()
        {
            var hostVisual = ElementCompositionPreview.GetElementVisual(_morphHost);
            var clipHostVisual = ElementCompositionPreview.GetElementVisual(_contentClipHost);
            _compositor = hostVisual.Compositor;

            _cutoutPathGeometry = _compositor.CreatePathGeometry();

            if (_pageContentBorder != null)
            {
                _pageContentVisual = ElementCompositionPreview.GetElementVisual(_pageContentBorder);
                _cutoutClip = _compositor.CreateGeometricClip(_cutoutPathGeometry);
            }

            if (_shadowReceiver != null)
            {
                _shadowReceiverVisual = ElementCompositionPreview.GetElementVisual(_shadowReceiver);
                _shadowCutoutClip = _compositor.CreateGeometricClip(_cutoutPathGeometry);
            }

            _cardW = (float)_nav.OpenPaneLength;
            _cardH = 260f;

            _props = _compositor.CreatePropertySet();
            _props.InsertScalar("px", 0f);
            _props.InsertScalar("py", 0f);
            _props.InsertVector2("startOffset", _btnOffset);
            _props.InsertVector2("startSize", _btnSize);
            _props.InsertVector2("cardPos", new Vector2(CardLeft, CardTop));
            _props.InsertVector2("cardSize", new Vector2(_cardW, _cardH));
            _props.InsertVector3("anchor", _anchor);
            _props.InsertVector3("contentAnchor", new Vector3(20f, 18f, 0f));

            string bounce = "(1 + Max((p.px + p.py) / 2 - 1, 0) * " +
                            BounceFactor.ToString(CultureInfo.InvariantCulture) + ")";
            string sr = StartCornerRadius.ToString(CultureInfo.InvariantCulture);
            string er = EndCornerRadius.ToString(CultureInfo.InvariantCulture);

            if (_cardSurface != null)
            {
                var surfaceVisual = ElementCompositionPreview.GetElementVisual(_cardSurface);
                surfaceVisual.CenterPoint = Vector3.Zero;
                StartExpr(surfaceVisual, "Offset",
                    "Vector3(Lerp(p.startOffset.X, p.cardPos.X, Clamp(p.px, 0, 1)), Lerp(p.startOffset.Y, p.cardPos.Y, Clamp(p.py, 0, 1)), 0)");
                StartExpr(surfaceVisual, "Scale",
                    "Vector3(Lerp(p.startSize.X / Max(p.cardSize.X, 1), 1, Clamp(p.px, 0, 1)), Lerp(p.startSize.Y / Max(p.cardSize.Y, 1), 1, Clamp(p.py, 0, 1)), 1)");
                StartExpr(surfaceVisual, "Opacity", "Clamp(p.py * 6, 0, 1)");
            }

            var geometry = _compositor.CreateRoundedRectangleGeometry();
            clipHostVisual.Clip = _compositor.CreateGeometricClip(geometry);

            StartExpr(geometry, "Offset",
                "Vector2(Lerp(p.startOffset.X, p.cardPos.X, Clamp(p.px, 0, 1)), Lerp(p.startOffset.Y, p.cardPos.Y, Clamp(p.py, 0, 1)))");
            StartExpr(geometry, "Size",
                "Vector2(Lerp(p.startSize.X, p.cardSize.X, Clamp(p.px, 0, 1)), Lerp(p.startSize.Y, p.cardSize.Y, Clamp(p.py, 0, 1)))");
            StartExpr(geometry, "CornerRadius",
                $"Vector2(Lerp({sr}, {er}, Clamp(p.py, 0, 1)), Lerp({sr}, {er}, Clamp(p.py, 0, 1)))");

            StartExpr(hostVisual, "CenterPoint", "p.anchor");
            StartExpr(hostVisual, "Scale", $"Vector3({bounce}, {bounce}, 1)");

            if (_cardShadowCaster != null)
            {
                var shadowVisual = ElementCompositionPreview.GetElementVisual(_cardShadowCaster);
                StartExpr(shadowVisual, "CenterPoint",
                    $"Vector3(p.anchor.X - {CardLeft.ToString(CultureInfo.InvariantCulture)}, p.anchor.Y - {CardTop.ToString(CultureInfo.InvariantCulture)}, 0)");
                StartExpr(shadowVisual, "Scale",
                    $"Vector3(Lerp(0.18, 1, Clamp(p.px, 0, 1)) * {bounce}, Lerp(0.14, 1, Clamp(p.py, 0, 1)) * {bounce}, 1)");
                StartExpr(shadowVisual, "Opacity", "Clamp((p.py - 0.25) * 1.4, 0, 1)");
            }

            if (_paneContent != null)
            {
                var contentVisual = ElementCompositionPreview.GetElementVisual(_paneContent);
                StartExpr(contentVisual, "CenterPoint", "p.contentAnchor");
                StartExpr(contentVisual, "Opacity", "Clamp((p.py - 0.35) * 2.5, 0, 1)");
                StartExpr(contentVisual, "Scale",
                    "Vector3(Lerp(0.6, 1, Clamp(p.py, 0, 1)), Lerp(0.6, 1, Clamp(p.py, 0, 1)), 1)");
            }

            if (_miniButton != null)
            {
                var v = ElementCompositionPreview.GetElementVisual(_miniButton);
                StartExpr(v, "Opacity", "Clamp((p.px - 0.35) * 2.5, 0, 1) * Clamp((p.py - 0.25) * 2.5, 0, 1)");
                StartExpr(v, "Scale", $"Vector3({bounce}, {bounce}, 1)");
            }

            if (_themeButton != null)
            {
                var v = ElementCompositionPreview.GetElementVisual(_themeButton);
                StartExpr(v, "Opacity", "Clamp((p.px - 0.72) * 3.8, 0, 1) * Clamp((p.py - 0.35) * 2.5, 0, 1)");
                StartExpr(v, "Scale", $"Vector3({bounce}, {bounce}, 1)");
            }

            if (_logoPanel != null)
            {
                var v = ElementCompositionPreview.GetElementVisual(_logoPanel);
                StartExpr(v, "Opacity", "Clamp(1 - p.px * 3.2, 0, 1)");
            }
        }

        private void StartExpr(CompositionObject target, string property, string expression)
        {
            var expr = _compositor!.CreateExpressionAnimation(expression);
            expr.SetReferenceParameter("p", _props!);
            target.StartAnimation(property, expr);
        }

        private void OnIsPaneOpenChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (!_initialized || _splitView == null) return;
            if (_splitView.IsPaneOpen) AnimateOpen();
            else AnimateClose();
        }

        private void AnimateOpen()
        {
            if (_compositor == null || _props == null) return;
            _closeVersion++;

            if (_cardSurface != null) _cardSurface.Visibility = Visibility.Visible;
            if (_cardShadowCaster != null) _cardShadowCaster.Visibility = Visibility.Visible;
            if (_miniButton != null) _miniButton.Visibility = Visibility.Visible;
            if (_themeButton != null) _themeButton.Visibility = Visibility.Visible;
            if (_logoPanel != null) _logoPanel.Visibility = Visibility.Visible;

            if (_paneRoot != null && _paneRoot.ActualHeight <= 0)
            {
                try { _paneRoot.UpdateLayout(); } catch { }
            }

            UpdateStartRect();
            UpdateTargetSize();

            if (!_renderingHooked && !_animOpening)
            {
                _curPx = 0f;
                _curPy = 0f;
                _velPx = 0f;
                _velPy = 0f;
            }

            _startPx = _curPx;
            _startPy = _curPy;
            _startVelX = _velPx;
            _startVelY = _velPy;
            _animOpening = true;

            if (_pageContentVisual != null && _cutoutClip != null)
            {
                _pageContentVisual.Clip = _cutoutClip;
            }
            if (_shadowReceiverVisual != null && _shadowCutoutClip != null)
            {
                _shadowReceiverVisual.Clip = _shadowCutoutClip;
            }

            _animClock.Restart();
            ApplyFrame(_curPx, _curPy);
            EnsureRenderingHooked();
        }

        private void AnimateClose()
        {
            if (_compositor == null || _props == null) return;
            int version = ++_closeVersion;

            UpdateStartRect();

            _startPx = _curPx;
            _startPy = _curPy;
            _startVelX = 0f;
            _startVelY = 0f;
            _animOpening = false;

            _animClock.Restart();
            EnsureRenderingHooked();
            _ = version;
        }

        private void EnsureRenderingHooked()
        {
            if (_renderingHooked) return;
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_renderingHooked) return;
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }

        private void OnRendering(object? sender, object e)
        {
            if (_props == null)
            {
                UnhookRendering();
                return;
            }

            double t = _animClock.Elapsed.TotalSeconds;

            if (_animOpening)
            {
                EvaluateSpring(t, DampedPeriodX, _startPx, _startVelX, out _curPx, out _velPx);
                EvaluateSpring(t, DampedPeriodY, _startPy, _startVelY, out _curPy, out _velPy);

                bool settled = t > 0.45 &&
                               Math.Abs(_curPx - 1f) < 0.001f &&
                               Math.Abs(_curPy - 1f) < 0.001f &&
                               Math.Abs(_velPx) < 0.01f &&
                               Math.Abs(_velPy) < 0.01f;
                if (settled)
                {
                    _curPx = 1f;
                    _curPy = 1f;
                    _velPx = 0f;
                    _velPy = 0f;
                    ApplyFrame(1f, 1f);
                    UnhookRendering();
                    return;
                }

                ApplyFrame(_curPx, _curPy);
            }
            else
            {
                float ex = EvaluateCloseBezier(t, CloseWidthSec);
                float ey = EvaluateCloseBezier(t, CloseHeightSec);
                _curPx = _startPx * (1f - ex);
                _curPy = _startPy * (1f - ey);
                _velPx = 0f;
                _velPy = 0f;

                if (t >= Math.Max(CloseWidthSec, CloseHeightSec))
                {
                    _curPx = 0f;
                    _curPy = 0f;
                    ApplyFrame(0f, 0f);
                    UnhookRendering();

                    if (_pageContentVisual != null) _pageContentVisual.Clip = null;
                    if (_shadowReceiverVisual != null) _shadowReceiverVisual.Clip = null;
                    if (_cardSurface != null) _cardSurface.Visibility = Visibility.Collapsed;
                    if (_cardShadowCaster != null) _cardShadowCaster.Visibility = Visibility.Collapsed;
                    if (_miniButton != null) _miniButton.Visibility = Visibility.Collapsed;
                    if (_themeButton != null) _themeButton.Visibility = Visibility.Collapsed;
                    return;
                }

                ApplyFrame(_curPx, _curPy);
            }
        }

        private void ApplyFrame(float px, float py)
        {
            if (_props == null) return;
            _props.InsertScalar("px", px);
            _props.InsertScalar("py", py);
            UpdatePageCutout(px, py);
        }

        /// <summary>
        /// 用 Win2D 在底层页面上裁出与当前形变帧完全重合的圆角矩形镂空，使卡片区域直接露出桌面壁纸亚克力层。
        /// </summary>
        private void UpdatePageCutout(float px, float py)
        {
            if (_cutoutPathGeometry == null || _pageContentVisual == null) return;
            try
            {
                float cx = Math.Clamp(px, 0f, 1f);
                float cy = Math.Clamp(py, 0f, 1f);
                if (cy <= 0.005f || cx <= 0.005f)
                {
                    return;
                }

                float curX = _btnOffset.X + (CardLeft - _btnOffset.X) * cx;
                float curY = _btnOffset.Y + (CardTop - _btnOffset.Y) * cy;
                float curW = _btnSize.X + (_cardW - _btnSize.X) * cx;
                float curH = _btnSize.Y + (_cardH - _btnSize.Y) * cy;
                float curR = StartCornerRadius + (EndCornerRadius - StartCornerRadius) * cy;

                float bounce = 1f + MathF.Max((px + py) * 0.5f - 1f, 0f) * BounceFactor;
                float finalX = _anchor.X + (curX - _anchor.X) * bounce + _pageDelta.X;
                float finalY = _anchor.Y + (curY - _anchor.Y) * bounce + _pageDelta.Y;
                float finalW = curW * bounce;
                float finalH = curH * bounce;
                float finalR = curR * bounce;

                using var outer = CanvasGeometry.CreateRectangle(null, -200f, -200f, 6000f, 6000f);
                using var hole = CanvasGeometry.CreateRoundedRectangle(null, finalX, finalY, finalW, finalH, finalR, finalR);
                using var combined = outer.CombineWith(hole, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
                _cutoutPathGeometry.Path = new CompositionPath(combined);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PaneMorph] cutout failed: {ex.Message}");
            }
        }

        private static void EvaluateSpring(
            double t,
            double dampedPeriod,
            float startP,
            float startVel,
            out float p,
            out float vel)
        {
            double zeta = DampingRatio;
            double wd = 2.0 * Math.PI / dampedPeriod;
            double wn = wd / Math.Sqrt(1.0 - zeta * zeta);
            double alpha = zeta * wn;

            double a = startP - 1.0;
            double b = (startVel + alpha * a) / wd;
            double exp = Math.Exp(-alpha * t);
            double cos = Math.Cos(wd * t);
            double sin = Math.Sin(wd * t);

            p = (float)(1.0 + exp * (a * cos + b * sin));
            vel = (float)(exp * ((b * wd - alpha * a) * cos - (alpha * b + a * wd) * sin));
        }

        /// <summary>CubicBezier(0.4, 0, 0.2, 1) 精确求解。</summary>
        private static float EvaluateCloseBezier(double t, double duration)
        {
            if (t <= 0) return 0f;
            if (t >= duration) return 1f;
            double x = t / duration;
            const double x1 = 0.4, y1 = 0.0, x2 = 0.2, y2 = 1.0;

            double u = x;
            for (int i = 0; i < 6; i++)
            {
                double inv = 1.0 - u;
                double bx = 3.0 * inv * inv * u * x1 + 3.0 * inv * u * u * x2 + u * u * u - x;
                double dbx = 3.0 * inv * inv * x1 + 6.0 * inv * u * (x2 - x1) + 3.0 * u * u * (1.0 - x2);
                if (Math.Abs(dbx) < 1e-6) break;
                u = Math.Clamp(u - bx / dbx, 0.0, 1.0);
            }

            double iu = 1.0 - u;
            return (float)(3.0 * iu * iu * u * y1 + 3.0 * iu * u * u * y2 + u * u * u);
        }

        private void UpdateStartRect()
        {
            if (_props == null || _morphHost == null) return;

            var offset = new Vector2(CardLeft, CardTop);
            var size = new Vector2(40f, 36f);
            if (_toggleButton != null && _toggleButton.ActualWidth > 0)
            {
                try
                {
                    var pt = _toggleButton.TransformToVisual(_morphHost).TransformPoint(new Windows.Foundation.Point(0, 0));
                    offset = new Vector2((float)pt.X, (float)pt.Y);
                    size = new Vector2((float)_toggleButton.ActualWidth, (float)_toggleButton.ActualHeight);
                }
                catch { }
            }

            if (_pageContentBorder != null)
            {
                try
                {
                    var d = _morphHost.TransformToVisual(_pageContentBorder).TransformPoint(new Windows.Foundation.Point(0, 0));
                    _pageDelta = new Vector2((float)d.X, (float)d.Y);
                }
                catch { _pageDelta = Vector2.Zero; }
            }

            _btnOffset = offset;
            _btnSize = size;
            _anchor = new Vector3(offset.X + size.X / 2f, offset.Y + size.Y / 2f, 0f);

            _props.InsertVector2("startOffset", offset);
            _props.InsertVector2("startSize", size);
            _props.InsertVector3("anchor", _anchor);
            _props.InsertVector3("contentAnchor", new Vector3(_anchor.X - CardLeft, _anchor.Y - CardTop, 0f));

            UpdateButtonPivot(_miniButton, _anchor);
            UpdateButtonPivot(_themeButton, _anchor);
        }

        private void UpdateButtonPivot(FrameworkElement? button, Vector3 anchorInMorphHost)
        {
            if (button == null || _morphHost == null) return;
            try
            {
                var pt = _morphHost.TransformToVisual(button).TransformPoint(
                    new Windows.Foundation.Point(anchorInMorphHost.X, anchorInMorphHost.Y));
                ElementCompositionPreview.GetElementVisual(button).CenterPoint =
                    new Vector3((float)pt.X, (float)pt.Y, 0f);
            }
            catch { }
        }

        private void UpdateTargetSize()
        {
            if (_props == null || _paneRoot == null) return;

            float w = _paneRoot.ActualWidth > 0 ? (float)_paneRoot.ActualWidth : (float)_nav.OpenPaneLength;
            float h = (float)_paneRoot.ActualHeight;
            if (h <= 0)
            {
                int visibleCount = 0;
                foreach (var item in _nav.MenuItems)
                {
                    if (item is UIElement ue && ue.Visibility == Visibility.Visible) visibleCount++;
                }
                h = 48f + Math.Max(1, visibleCount) * 40f + 8f;
            }

            if (Math.Abs(w - _cardW) < 0.5f && Math.Abs(h - _cardH) < 0.5f) return;
            _cardW = w;
            _cardH = h;

            if (_cardSurface != null)
            {
                _cardSurface.Width = w;
                _cardSurface.Height = h;
            }

            if (_cardShadowCaster != null)
            {
                _cardShadowCaster.Width = w;
                _cardShadowCaster.Height = h;
            }

            _props.InsertVector2("cardPos", new Vector2(CardLeft, CardTop));
            _props.InsertVector2("cardSize", new Vector2(w, h));

            if (_splitView != null && _splitView.IsPaneOpen && !_renderingHooked)
            {
                UpdatePageCutout(1f, 1f);
            }
        }

        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T fe && fe.Name == name) return fe;
                var found = FindByName<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
