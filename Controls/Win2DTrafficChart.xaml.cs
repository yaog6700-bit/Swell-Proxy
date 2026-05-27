using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using System.Diagnostics;

namespace AnywhereWinUI.Controls
{
    public sealed partial class Win2DTrafficChart : UserControl
    {
        public static readonly DependencyProperty UploadTrafficProperty =
            DependencyProperty.Register(nameof(UploadTraffic), typeof(ObservableCollection<double>), typeof(Win2DTrafficChart), new PropertyMetadata(null, OnTrafficChanged));

        public static readonly DependencyProperty DownloadTrafficProperty =
            DependencyProperty.Register(nameof(DownloadTraffic), typeof(ObservableCollection<double>), typeof(Win2DTrafficChart), new PropertyMetadata(null, OnTrafficChanged));

        public ObservableCollection<double> UploadTraffic
        {
            get => (ObservableCollection<double>)GetValue(UploadTrafficProperty);
            set => SetValue(UploadTrafficProperty, value);
        }

        public ObservableCollection<double> DownloadTraffic
        {
            get => (ObservableCollection<double>)GetValue(DownloadTrafficProperty);
            set => SetValue(DownloadTrafficProperty, value);
        }


        private static void OnTrafficChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Win2DTrafficChart chart)
            {
                if (e.OldValue is ObservableCollection<double> oldColl)
                {
                    oldColl.CollectionChanged -= chart.OnCollectionChanged;
                }
                if (e.NewValue is ObservableCollection<double> newColl)
                {
                    newColl.CollectionChanged += chart.OnCollectionChanged;
                }
            }
        }

        private readonly object _dataLock = new object();
        private double[] _upData   = new double[60];
        private double[] _downData = new double[60];

        // Exponentially smoothed maximum — prevents the chart scale from
        // snapping up/down on every traffic spike (85% old + 15% new each frame).
        private double _smoothMax = 1.0;
        
        // No slide animation needed — data updates every 1s which matches animation duration,
        // causing a constant back-and-forth jitter. Direct draw is cleaner for real-time data.

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            lock (_dataLock)
            {
                var upSrc = UploadTraffic?.ToArray() ?? Array.Empty<double>();
                var downSrc = DownloadTraffic?.ToArray() ?? Array.Empty<double>();
                _upData = upSrc;
                _downData = downSrc;
            }
        }

        private DispatcherTimer _timer;

        public Win2DTrafficChart()
        {
            this.InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, object e)
        {
            if (ChartCanvas == null) return;
            ChartCanvas.Invalidate();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
            if (ChartCanvas == null) return;
            ChartCanvas.RemoveFromVisualTree();
            ChartCanvas = null;
        }

        private void ChartCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
            // Resources can be created here if needed
        }

        private void ChartCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            if (ChartCanvas == null) return;
            var ds = args.DrawingSession;
            var width = sender.Size.Width;
            var height = sender.Size.Height;

            if (width <= 0 || height <= 0) return;

            double[] upDataCopy;
            double[] downDataCopy;

            lock (_dataLock)
            {
                upDataCopy = _upData;
                downDataCopy = _downData;
            }

            if (upDataCopy.Length == 0 && downDataCopy.Length == 0) return;

            int pointCount = Math.Max(upDataCopy.Length, downDataCopy.Length);
            if (pointCount < 2) return;

            // stepX: gap between two adjacent data points
            float stepX = (float)(width / (pointCount - 1));
            // No slide animation: always draw at offsetX = 0
            float offsetX = 0f;

            double rawMax = Math.Max(
                upDataCopy.Length   > 0 ? upDataCopy.Max()   : 0,
                downDataCopy.Length > 0 ? downDataCopy.Max() : 0);

            if (rawMax < 1) rawMax = 1;

            // Smooth the scale so sudden spikes don't cause harsh jumps
            _smoothMax = _smoothMax * 0.85 + rawMax * 0.15;
            double maxValue = Math.Max(_smoothMax, 1);

            // ── Grid reference lines at 25 / 50 / 75 % ────────────────────
            var gridColor = Color.FromArgb(22, 128, 128, 128);
            for (int i = 1; i <= 3; i++)
            {
                float lineY = (float)(height * i / 4.0);
                ds.DrawLine(0, lineY, (float)width, lineY, gridColor, 1f);
            }
            // ──────────────────────────────────────────────────────────────

            var upColor   = ColorHelper.FromArgb(255, 52,  211, 153);  // emerald green
            var downColor = ColorHelper.FromArgb(255, 96,  165, 250);  // sky blue

            if (upDataCopy.Length > 1)
                DrawSmoothCurve(sender, ds, upDataCopy,   maxValue, width, height, stepX, offsetX, upColor);
            if (downDataCopy.Length > 1)
                DrawSmoothCurve(sender, ds, downDataCopy, maxValue, width, height, stepX, offsetX, downColor);
        }

        private void DrawSmoothCurve(
            ICanvasResourceCreator resourceCreator,
            CanvasDrawingSession ds,
            double[] data,
            double maxValue,
            double width,
            double height,
            float stepX,
            float offsetX,
            Color color)
        {
            // Add padding so 2px strokes at the very top/bottom are not clipped by Canvas edge
            float padding = 2f;
            float drawHeight = (float)height - padding * 2;

            // Helper: convert data index to screen X (index -1 = one step left of origin)
            float X(int i) => (i * stepX) + offsetX;
            float Y(int i) => padding + drawHeight - (float)(data[Math.Clamp(i, 0, data.Length - 1)] / maxValue * drawHeight);

            // ---------- build a single path for both fill and stroke ----------
            using var builder = new CanvasPathBuilder(resourceCreator);

            // Start at bottom-left (off screen to the left) to close the fill shape
            builder.BeginFigure(X(-1), (float)height);
            builder.AddLine(X(-1), Y(0));  // rise to first data point

            // Catmull-Rom spline through all points, from index 0 to data.Length-1
            for (int i = 0; i < data.Length - 1; i++)
            {
                Vector2 p0 = new Vector2(X(i - 1), Y(i - 1));
                Vector2 p1 = new Vector2(X(i),     Y(i));
                Vector2 p2 = new Vector2(X(i + 1), Y(i + 1));
                Vector2 p3 = new Vector2(X(Math.Min(i + 2, data.Length - 1)),
                                         Y(Math.Min(i + 2, data.Length - 1)));

                float tension = 0.2f;
                Vector2 cp1 = p1 + (p2 - p0) * tension;
                Vector2 cp2 = p2 - (p3 - p1) * tension;

                builder.AddCubicBezier(cp1, cp2, p2);
            }

            // Close shape: go to bottom-right corner
            builder.AddLine(X(data.Length - 1), (float)height);
            builder.EndFigure(CanvasFigureLoop.Closed);

            using var geometry = CanvasGeometry.CreatePath(builder);

            // Gradient fill
            var gradientStops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb((byte)(255 * 0.25), color.R, color.G, color.B) },
                new CanvasGradientStop { Position = 1.0f, Color = Color.FromArgb(0, color.R, color.G, color.B) }
            };
            using var brush = new CanvasLinearGradientBrush(resourceCreator, gradientStops)
            {
                StartPoint = new Vector2(0, 0),
                EndPoint   = new Vector2(0, (float)height)
            };
            ds.FillGeometry(geometry, brush);

            // Stroke: build a matching open path for the top line only
            using var strokeBuilder = new CanvasPathBuilder(resourceCreator);
            strokeBuilder.BeginFigure(X(-1), Y(0));
            for (int i = 0; i < data.Length - 1; i++)
            {
                Vector2 p0 = new Vector2(X(i - 1), Y(i - 1));
                Vector2 p1 = new Vector2(X(i),     Y(i));
                Vector2 p2 = new Vector2(X(i + 1), Y(i + 1));
                Vector2 p3 = new Vector2(X(Math.Min(i + 2, data.Length - 1)),
                                         Y(Math.Min(i + 2, data.Length - 1)));

                float tension = 0.2f;
                Vector2 cp1 = p1 + (p2 - p0) * tension;
                Vector2 cp2 = p2 - (p3 - p1) * tension;

                strokeBuilder.AddCubicBezier(cp1, cp2, p2);
            }
            strokeBuilder.EndFigure(CanvasFigureLoop.Open);
            using var strokeGeometry = CanvasGeometry.CreatePath(strokeBuilder);
            ds.DrawGeometry(strokeGeometry, color, 2f);
        }
    }
}
