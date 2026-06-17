using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace AnywhereWinUI.Helpers
{
    /// <summary>
    /// Composition-driven cross-fade for the main to mini window-mode switch.
    ///
    /// The outgoing panel just snaps away; the incoming panel scales + fades in as a
    /// short, non-blocking entrance flourish.
    ///
    /// The animation runs on the Composition (render) thread, so it stays smooth
    /// even while the UI thread is busy resizing.
    /// </summary>
    internal static class WindowModeTransition
    {
        private const float ShrunkScale = 0.90f;
        private static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(160);

        /// <summary>
        /// Stage a panel in its hidden (invisible + shrunk) state synchronously.
        /// </summary>
        public static void PrepareHidden(FrameworkElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.Opacity = 0f;
            visual.Scale = new Vector3(ShrunkScale, ShrunkScale, 1f);
        }

        /// <summary>
        /// Fade + scale the newly visible panel up to its resting state.
        /// </summary>
        public static void FadeIn(FrameworkElement element)
        {
            // The window just resized; force a layout pass so CenterPoint uses the new size
            element.UpdateLayout();

            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            // Pivot the scale around the panel's center
            visual.CenterPoint = new Vector3(
                (float)(element.ActualWidth / 2),
                (float)(element.ActualHeight / 2),
                0f);

            // Ease-out (Fluent "Decelerate"): arrive fast, settle softly.
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = InDuration;

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1f, Vector3.One, ease);
            scale.Duration = InDuration;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Scale", scale);
        }
    }
}
