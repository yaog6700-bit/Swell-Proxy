using AnywhereWinUI.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace AnywhereWinUI.Views
{
    public sealed partial class DashboardPage : Page
    {
        public ViewModels.DashboardViewModel ViewModel { get; }

        // Guard so animations only run once per page lifetime
        private bool _animationsStarted = false;

        public DashboardPage()
        {
            ViewModel = ((App)Application.Current).Services
                .GetService(typeof(ViewModels.DashboardViewModel))
                as ViewModels.DashboardViewModel;

            this.InitializeComponent();
            this.Loaded   += DashboardPage_Loaded;
            this.Unloaded += DashboardPage_Unloaded;
        }

        // ─────────────────────────────────────────────────────────
        //  Page lifecycle
        // ─────────────────────────────────────────────────────────

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_animationsStarted) return;
            _animationsStarted = true;

            // Subscribe to ViewModel property changes
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Play staggered card-entry animations
            PlayStaggeredEntryAnimation();

            // Sync UI state if core is already running on load
            if (ViewModel.IsCoreRunning)
            {
                StartPulseAnimation();
                UpdateStatusDot(running: true);
            }
        }

        private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            StopPulseAnimation();
        }

        // ─────────────────────────────────────────────────────────
        //  ViewModel changes
        // ─────────────────────────────────────────────────────────

        private void ViewModel_PropertyChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewModel.IsCoreRunning)) return;

            if (ViewModel.IsCoreRunning)
            {
                StartPulseAnimation();
                UpdateStatusDot(running: true);
                ShowStatusInfoBar(
                    "已连接",
                    $"成功连接到 {ViewModel.CurrentNodeName}",
                    InfoBarSeverity.Success);
            }
            else
            {
                StopPulseAnimation();
                UpdateStatusDot(running: false);
                ShowStatusInfoBar("已断开", "代理引擎已停止", InfoBarSeverity.Informational);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Staggered entry animation (cards slide up + fade in)
        // ─────────────────────────────────────────────────────────

        private void PlayStaggeredEntryAnimation()
        {
            var cards = new FrameworkElement[]
            {
                HeroCard, TrafficCard, SubCard, ActionCard
            };

            // Pre-hide all cards
            foreach (var card in cards)
            {
                card.Opacity = 0;
                card.RenderTransform = new TranslateTransform { Y = 20 };
            }

            // Stagger: each card animates in 80 ms after the previous
            int delay = 0;
            foreach (var card in cards)
            {
                var capturedCard  = card;
                var capturedDelay = delay;

                _ = Task.Run(async () =>
                {
                    await Task.Delay(capturedDelay);
                    DispatcherQueue.TryEnqueue(() => AnimateCardIn(capturedCard));
                });

                delay += 80;
            }
        }

        private static void AnimateCardIn(FrameworkElement element)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var opacityAnim = new DoubleAnimation
            {
                From           = 0,
                To             = 1,
                Duration       = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = ease
            };
            Storyboard.SetTarget(opacityAnim, element);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");

            var translateAnim = new DoubleAnimation
            {
                From           = 20,
                To             = 0,
                Duration       = new Duration(TimeSpan.FromMilliseconds(400)),
                EasingFunction = ease
            };
            Storyboard.SetTarget(translateAnim, element.RenderTransform);
            Storyboard.SetTargetProperty(translateAnim, "Y");

            var sb = new Storyboard();
            sb.Children.Add(opacityAnim);
            sb.Children.Add(translateAnim);
            sb.Begin();
        }

        // ─────────────────────────────────────────────────────────
        //  Pulse "breathing" animation on the core toggle button
        // ─────────────────────────────────────────────────────────

        private void StartPulseAnimation()
        {
            if (CoreToggleBtn == null) return;

            var visual     = ElementCompositionPreview.GetElementVisual(CoreToggleBtn);
            var compositor = visual.Compositor;

            // Centre-anchor the scale transform
            visual.CenterPoint = new Vector3(32f, 32f, 0f);

            var pulse = compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0.0f, 1.00f);
            pulse.InsertKeyFrame(0.5f, 1.05f, compositor.CreateLinearEasingFunction());
            pulse.InsertKeyFrame(1.0f, 1.00f);
            pulse.Duration          = TimeSpan.FromSeconds(2);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Scale.X", pulse);
            visual.StartAnimation("Scale.Y", pulse);
        }

        private void StopPulseAnimation()
        {
            if (CoreToggleBtn == null) return;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(CoreToggleBtn);
                visual.StopAnimation("Scale.X");
                visual.StopAnimation("Scale.Y");
                visual.Scale = Vector3.One;
            }
            catch { /* Silently ignore if element is already disposed */ }
        }

        // ─────────────────────────────────────────────────────────
        //  Status dot color (green when running, grey when stopped)
        // ─────────────────────────────────────────────────────────

        private void UpdateStatusDot(bool running)
        {
            if (HeroStatusDot == null) return;

            HeroStatusDot.Fill = running
                ? new SolidColorBrush(Microsoft.UI.Colors.MediumSeaGreen)
                : new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        // ─────────────────────────────────────────────────────────
        //  InfoBar — shows a transient connection event message
        // ─────────────────────────────────────────────────────────

        private void ShowStatusInfoBar(string title, string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Title    = title;
            StatusInfoBar.Message  = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen   = true;

            // Auto-dismiss after 4 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                StatusInfoBar.IsOpen = false;
            };
            timer.Start();
        }
    }
}
