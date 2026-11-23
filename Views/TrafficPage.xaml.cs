using System;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using AnywhereWinUI.Converters;
using AnywhereWinUI.Helpers;
using AnywhereWinUI.ViewModels;

namespace AnywhereWinUI.Views
{
    public sealed partial class TrafficPage : Page
    {
        private const double CellPitch = 13.0; // 10px cell + 1.5px margin * 2
        private const int RowsPerColumn = 7;

        public TrafficViewModel ViewModel { get; }

        private bool _isSyncingMode;
        private int _hoveredCellIndex = -1;

        public TrafficPage()
        {
            NavigationCacheMode = NavigationCacheMode.Disabled;
            ViewModel = ((App)Application.Current).Services.GetService(typeof(TrafficViewModel)) as TrafficViewModel;
            this.InitializeComponent();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyHeatmapPalette();
            RenderMonthLabels();
        }

        private void OnHeatmapUpdated(object? sender, EventArgs e)
        {
            RenderVectorHeatmap();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            this.ActualThemeChanged += OnActualThemeChanged;

            if (ViewModel != null)
            {
                ViewModel.HeatmapUpdated -= OnHeatmapUpdated;
                ViewModel.HeatmapUpdated += OnHeatmapUpdated;

                if (HeatmapModeSegmented != null)
                {
                    _isSyncingMode = true;
                    HeatmapModeSegmented.SelectedIndex = (int)ViewModel.CurrentHeatmapMode;
                    _isSyncingMode = false;
                }

                ViewModel.OnPageActivated();
                RenderVectorHeatmap();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            this.ActualThemeChanged -= OnActualThemeChanged;
            if (ViewModel != null)
            {
                ViewModel.HeatmapUpdated -= OnHeatmapUpdated;
                ViewModel.OnPageDeactivated();
            }

            ClearVectorHeatmap();
            this.Bindings.StopTracking();
        }

        private void HeatmapModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingMode) return;
            if (sender is not CommunityToolkit.WinUI.Controls.Segmented seg) return;
            var mode = seg.SelectedIndex switch
            {
                1 => HeatmapViewMode.Weekly,
                2 => HeatmapViewMode.Cumulative,
                _ => HeatmapViewMode.Daily
            };
            ViewModel?.SetHeatmapMode(mode);
        }

        private void ApplyHeatmapPalette()
        {
            bool isDark = ContentDialogExtensions.IsDarkThemeActive();
            HeatmapPath0.Fill = HeatmapColorConverter.GetBrush(0, isDark);
            HeatmapPath1.Fill = HeatmapColorConverter.GetBrush(1, isDark);
            HeatmapPath2.Fill = HeatmapColorConverter.GetBrush(2, isDark);
            HeatmapPath3.Fill = HeatmapColorConverter.GetBrush(3, isDark);
            HeatmapPath4.Fill = HeatmapColorConverter.GetBrush(4, isDark);
        }

        private void RenderVectorHeatmap()
        {
            if (ViewModel == null || HeatmapCanvas == null) return;

            var cells = ViewModel.HeatmapCellList;
            if (cells == null || cells.Count == 0)
            {
                ClearVectorHeatmap();
                return;
            }

            int totalCols = (cells.Count + RowsPerColumn - 1) / RowsPerColumn;
            double canvasWidth = Math.Max(13, totalCols * CellPitch);
            HeatmapCanvas.Width = canvasWidth;
            MonthLabelsCanvas.Width = canvasWidth;

            ApplyHeatmapPalette();

            var builders = new StringBuilder[5];
            for (int lv = 0; lv < 5; lv++)
                builders[lv] = new StringBuilder(lv == 0 ? 8192 : 2048);

            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                if (cell.IsPlaceholder) continue;

                int col = i / RowsPerColumn;
                int row = i % RowsPerColumn;
                int level = Math.Clamp(cell.ColorLevel, 0, 4);

                double x = col * CellPitch + 1.5;
                double y = row * CellPitch + 1.5;

                // 10x10 rounded square with 2px corner radius
                builders[level]
                    .Append("M ").Append((x + 2.0).ToString("0.#", inv)).Append(',').Append(y.ToString("0.#", inv))
                    .Append(" h 6 a 2,2 0 0 1 2,2 v 6 a 2,2 0 0 1 -2,2 h -6 a 2,2 0 0 1 -2,-2 v -6 a 2,2 0 0 1 2,-2 Z ");
            }

            SetPathData(HeatmapPath0, builders[0]);
            SetPathData(HeatmapPath1, builders[1]);
            SetPathData(HeatmapPath2, builders[2]);
            SetPathData(HeatmapPath3, builders[3]);
            SetPathData(HeatmapPath4, builders[4]);

            RenderMonthLabels();
        }

        private static void SetPathData(Microsoft.UI.Xaml.Shapes.Path target, StringBuilder sb)
        {
            if (sb.Length == 0)
            {
                target.Data = null;
                return;
            }
            target.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), sb.ToString());
        }

        private void RenderMonthLabels()
        {
            if (ViewModel == null || MonthLabelsCanvas == null) return;

            MonthLabelsCanvas.Children.Clear();
            var labels = ViewModel.MonthLabelList;
            if (labels == null || labels.Count == 0) return;

            bool wantDark = this.ActualTheme == ElementTheme.Dark ||
                            (this.ActualTheme == ElementTheme.Default && ContentDialogExtensions.IsDarkThemeActive());
            Brush secondaryBrush = LookupThemedBrush("TextFillColorSecondaryBrush", wantDark, Microsoft.UI.Colors.Gray);

            for (int col = 0; col < labels.Count; col++)
            {
                string text = labels[col];
                if (string.IsNullOrEmpty(text)) continue;

                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = secondaryBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(tb, col * CellPitch + 1.5);
                Canvas.SetTop(tb, 0);
                MonthLabelsCanvas.Children.Add(tb);
            }
        }

        private void ClearVectorHeatmap()
        {
            _hoveredCellIndex = -1;
            if (HeatmapPath0 != null) HeatmapPath0.Data = null;
            if (HeatmapPath1 != null) HeatmapPath1.Data = null;
            if (HeatmapPath2 != null) HeatmapPath2.Data = null;
            if (HeatmapPath3 != null) HeatmapPath3.Data = null;
            if (HeatmapPath4 != null) HeatmapPath4.Data = null;
            if (HeatmapHoverRect != null) HeatmapHoverRect.Visibility = Visibility.Collapsed;
            if (HeatmapTooltipCard != null) HeatmapTooltipCard.Visibility = Visibility.Collapsed;
            MonthLabelsCanvas?.Children.Clear();
        }

        private void HeatmapCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (ViewModel == null) return;
            var cells = ViewModel.HeatmapCellList;
            if (cells == null || cells.Count == 0) return;

            var pt = e.GetCurrentPoint(HeatmapCanvas).Position;
            int col = (int)(pt.X / CellPitch);
            int row = (int)(pt.Y / CellPitch);

            if (col < 0 || row < 0 || row >= RowsPerColumn)
            {
                HideHoverTooltip();
                return;
            }

            int idx = col * RowsPerColumn + row;
            if (idx < 0 || idx >= cells.Count || cells[idx].IsPlaceholder)
            {
                HideHoverTooltip();
                return;
            }

            if (idx != _hoveredCellIndex)
            {
                _hoveredCellIndex = idx;
                Canvas.SetLeft(HeatmapHoverRect, col * CellPitch + 0.5);
                Canvas.SetTop(HeatmapHoverRect, row * CellPitch + 0.5);
                HeatmapHoverRect.Visibility = Visibility.Visible;

                HeatmapTooltipText.Text = cells[idx].Tooltip;
                HeatmapTooltipCard.Visibility = Visibility.Visible;
                HeatmapTooltipCard.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            double tipW = HeatmapTooltipCard.DesiredSize.Width;
            double tipH = HeatmapTooltipCard.DesiredSize.Height;
            double cellCenterX = col * CellPitch + 6.5;
            double tipX = Math.Clamp(cellCenterX - tipW / 2.0, 0, Math.Max(0, HeatmapCanvas.Width - tipW));
            double tipY = row * CellPitch - tipH - 6.0;
            if (tipY < -18) tipY = (row + 1) * CellPitch + 4.0;

            Canvas.SetLeft(HeatmapTooltipCard, tipX);
            Canvas.SetTop(HeatmapTooltipCard, tipY);
        }

        private void HeatmapCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            HideHoverTooltip();
        }

        private void HideHoverTooltip()
        {
            _hoveredCellIndex = -1;
            if (HeatmapHoverRect != null) HeatmapHoverRect.Visibility = Visibility.Collapsed;
            if (HeatmapTooltipCard != null) HeatmapTooltipCard.Visibility = Visibility.Collapsed;
        }

        private static Brush LookupThemedBrush(string key, bool wantDark, Windows.UI.Color fallback)
            => ContentDialogExtensions.LookupThemeBrush(key, wantDark, fallback);
    }
}
