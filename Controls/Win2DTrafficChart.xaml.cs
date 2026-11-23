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
                if (e.NewValue is ObservableCollection<double> newColl && chart._isLoadedInTree && MainWindow.IsVisibleToUser)
                {
                    newColl.CollectionChanged += chart.OnCollectionChanged;
                }
                chart.RefreshDataSnapshot();
            }
        }

        private readonly object _dataLock = new object();
        private double[] _upData   = new double[60];
        private double[] _downData = new double[60];
        private double _smoothMax = 1.0;
        private volatile bool _isDirty = true;
        private volatile bool _isSettlingScale;
        private bool _isLoadedInTree;

        private void RefreshDataSnapshot()
        {
            lock (_dataLock)
            {
                _upData = UploadTraffic?.ToArray() ?? Array.Empty<double>();
                _downData = DownloadTraffic?.ToArray() ?? Array.Empty<double>();
            }
            _isDirty = true;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshDataSnapshot();
        }

        private DispatcherTimer? _timer;
        private bool _isSubscribedToWindowVisibility;

        public Win2DTrafficChart()
        {
            this.InitializeComponent();
            this.Loaded += UserControl_Loaded;
            // Do NOT start DispatcherTimer until actually Loaded & visible!
        }

        private void Timer_Tick(object? sender, object e)
        {
            if (ChartCanvas == null || ChartCanvas.Visibility != Visibility.Visible)
                return;

            // Skip GPU invalidation completely when neither traffic data nor scale decay changed!
            if (!_isDirty && !_isSettlingScale)
                return;

            _isDirty = false;
            ChartCanvas.Invalidate();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoadedInTree = true;
            SubscribeToWindowVisibility();
            if (MainWindow.IsVisibleToUser)
            {
                if (ChartCanvas != null) ChartCanvas.Visibility = Visibility.Visible;
                ResubscribeToTraffic();
                RefreshDataSnapshot();
                StartTimer();
                ChartCanvas?.Invalidate();
            }
            else
            {
                if (ChartCanvas != null) ChartCanvas.Visibility = Visibility.Collapsed;
                StopTimer();
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoadedInTree = false;
            StopTimer();
            UnsubscribeFromTraffic(UploadTraffic);
            UnsubscribeFromTraffic(DownloadTraffic);
            UnsubscribeFromWindowVisibility();
        }

        private void StartTimer()
        {
            if (_timer != null) return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer == null) return;

            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        private void SubscribeToWindowVisibility()
        {
            if (_isSubscribedToWindowVisibility) return;

            MainWindow.VisibilityToUserChanged += OnMainWindowVisibilityChanged;
            _isSubscribedToWindowVisibility = true;
        }

        private void UnsubscribeFromWindowVisibility()
        {
            if (!_isSubscribedToWindowVisibility) return;

            MainWindow.VisibilityToUserChanged -= OnMainWindowVisibilityChanged;
            _isSubscribedToWindowVisibility = false;
        }

        private void OnMainWindowVisibilityChanged(object? sender, bool isVisible)
        {
            if (isVisible && _isLoadedInTree)
            {
                if (ChartCanvas != null) ChartCanvas.Visibility = Visibility.Visible;
                ResubscribeToTraffic();
                RefreshDataSnapshot();
                StartTimer();
                ChartCanvas?.Invalidate();
            }
            else
            {
                StopTimer();
                UnsubscribeFromTraffic(UploadTraffic);
                UnsubscribeFromTraffic(DownloadTraffic);
                if (ChartCanvas != null) ChartCanvas.Visibility = Visibility.Collapsed;
                try { CanvasDevice.GetSharedDevice()?.Trim(); } catch { }
            }
        }

        private void UnsubscribeFromTraffic(ObservableCollection<double>? collection)
        {
            if (collection != null)
            {
                collection.CollectionChanged -= OnCollectionChanged;
            }
        }

        private void ResubscribeToTraffic()
        {
            if (UploadTraffic != null)
            {
                UploadTraffic.CollectionChanged -= OnCollectionChanged;
                UploadTraffic.CollectionChanged += OnCollectionChanged;
            }

            if (DownloadTraffic != null)
            {
                DownloadTraffic.CollectionChanged -= OnCollectionChanged;
                DownloadTraffic.CollectionChanged += OnCollectionChanged;
            }
        }

        private void ChartCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
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

            float stepX = (float)(width / (pointCount - 1));
            float offsetX = 0f;

            double rawMax = Math.Max(
                upDataCopy.Length   > 0 ? upDataCopy.Max()   : 0,
                downDataCopy.Length > 0 ? downDataCopy.Max() : 0);

            if (rawMax < 1) rawMax = 1;

            double targetMax = Math.Max(rawMax * 1.12, 1.0);
            if (targetMax >= _smoothMax)
            {
                _smoothMax = targetMax;
                _isSettlingScale = false;
            }
            else
            {
                double nextSmooth = _smoothMax * 0.85 + targetMax * 0.15;
                if (Math.Abs(nextSmooth - targetMax) < 0.5)
                {
                    _smoothMax = targetMax;
                    _isSettlingScale = false;
                }
                else
                {
                    _smoothMax = nextSmooth;
                    _isSettlingScale = true;
                }
            }
            double maxValue = Math.Max(_smoothMax, 1.0);

            var gridColor = Color.FromArgb(22, 128, 128, 128);
            for (int i = 1; i <= 3; i++)
            {
                float lineY = (float)(height * i / 4.0);
                ds.DrawLine(0, lineY, (float)width, lineY, gridColor, 1f);
            }

            var upColor   = ColorHelper.FromArgb(255, 52,  211, 153);
            var downColor = ColorHelper.FromArgb(255, 96,  165, 250);

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
            float topPadding = 6f;
            float bottomPadding = 3f;
            float minY = topPadding;
            float maxY = Math.Max(topPadding + 1f, (float)height - bottomPadding);
            float drawHeight = maxY - minY;

            float X(int i) => (i * stepX) + offsetX;
            float Y(int i)
            {
                double ratio = Math.Clamp(data[Math.Clamp(i, 0, data.Length - 1)] / maxValue, 0.0, 1.0);
                return minY + drawHeight - (float)(ratio * drawHeight);
            }

            using var fillBuilder = new CanvasPathBuilder(resourceCreator);
            using var strokeBuilder = new CanvasPathBuilder(resourceCreator);

            float startX = X(-1);
            float startY = Y(0);
            fillBuilder.BeginFigure(startX, (float)height);
            fillBuilder.AddLine(startX, startY);
            strokeBuilder.BeginFigure(startX, startY);

            for (int i = 0; i < data.Length - 1; i++)
            {
                Vector2 p0 = new Vector2(X(i - 1), Y(i - 1));
                Vector2 p1 = new Vector2(X(i),     Y(i));
                Vector2 p2 = new Vector2(X(i + 1), Y(i + 1));
                Vector2 p3 = new Vector2(X(Math.Min(i + 2, data.Length - 1)),
                                         Y(Math.Min(i + 2, data.Length - 1)));

                const float tension = 0.2f;
                Vector2 cp1 = p1 + (p2 - p0) * tension;
                Vector2 cp2 = p2 - (p3 - p1) * tension;
                cp1.Y = Math.Clamp(cp1.Y, minY, maxY);
                cp2.Y = Math.Clamp(cp2.Y, minY, maxY);

                fillBuilder.AddCubicBezier(cp1, cp2, p2);
                strokeBuilder.AddCubicBezier(cp1, cp2, p2);
            }

            fillBuilder.AddLine(X(data.Length - 1), (float)height);
            fillBuilder.EndFigure(CanvasFigureLoop.Closed);
            strokeBuilder.EndFigure(CanvasFigureLoop.Open);

            using var fillGeometry = CanvasGeometry.CreatePath(fillBuilder);
            using var strokeGeometry = CanvasGeometry.CreatePath(strokeBuilder);

            var gradientStops = new CanvasGradientStop[]
            {
                new CanvasGradientStop { Position = 0.0f, Color = Color.FromArgb(64, color.R, color.G, color.B) },
                new CanvasGradientStop { Position = 1.0f, Color = Color.FromArgb(0, color.R, color.G, color.B) }
            };
            using var brush = new CanvasLinearGradientBrush(resourceCreator, gradientStops)
            {
                StartPoint = new Vector2(0, 0),
                EndPoint   = new Vector2(0, (float)height)
            };
            ds.FillGeometry(fillGeometry, brush);
            ds.DrawGeometry(strokeGeometry, color, 2f);
        }
    }
}
