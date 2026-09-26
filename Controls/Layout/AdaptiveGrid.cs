using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Duyarlı kart ızgarası (düzen v2). Sütun sayısı genişlikten hesaplanır
    /// (<see cref="MinColumnWidth"/>, en fazla <see cref="MaxColumns"/>); aynı satırdaki
    /// öğeler <b>eşit yükseklikte</b> yerleşir — kısa kartın ortada kalıp komşusundan
    /// kaymasını (ui:Card sorunu) yapısal olarak imkânsız kılar.
    ///
    ///   &lt;c:AdaptiveGrid MinColumnWidth="260" MaxColumns="4"&gt;
    ///     &lt;c:MetricTile .../&gt;
    ///     &lt;Border c:AdaptiveGrid.Span="2" .../&gt;
    ///   &lt;/c:AdaptiveGrid&gt;
    /// </summary>
    public class AdaptiveGrid : Panel
    {
        public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
            nameof(MaxColumns), typeof(int), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
            nameof(ColumnSpacing), typeof(double), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
            nameof(RowSpacing), typeof(double), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Öğenin kapladığı sütun sayısı (sütun sayısından büyükse satırın tamamı).</summary>
        public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
            "Span", typeof(int), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

        /// <summary>true ise öğe her zaman satırın tamamını kaplar (ör. tam genişlik hero).</summary>
        public static readonly DependencyProperty FullRowProperty = DependencyProperty.RegisterAttached(
            "FullRow", typeof(bool), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

        public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
        public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
        public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
        public double RowSpacing { get => (double)GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }

        public static int GetSpan(DependencyObject o) => (int)o.GetValue(SpanProperty);
        public static void SetSpan(DependencyObject o, int v) => o.SetValue(SpanProperty, v);
        public static bool GetFullRow(DependencyObject o) => (bool)o.GetValue(FullRowProperty);
        public static void SetFullRow(DependencyObject o, bool v) => o.SetValue(FullRowProperty, v);

        /// <summary>Son ölçümde hesaplanan sütun sayısı (testler ve tetikleyiciler için).</summary>
        public int Columns { get; private set; } = 1;

        private readonly List<(UIElement Child, int Row, int Col, int Span)> _slots = new();
        private readonly List<double> _rowHeights = new();
        private double _columnWidth;

        /// <summary>Genişliğe sığan sütun sayısı (saf hesap; test edilebilir).</summary>
        public static int ComputeColumns(double width, double minColumnWidth, int maxColumns, double spacing)
        {
            if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0) return Math.Max(1, maxColumns);
            int cols = (int)Math.Floor((width + spacing) / (Math.Max(1, minColumnWidth) + spacing));
            return Math.Clamp(cols, 1, Math.Max(1, maxColumns));
        }

        protected override Size MeasureOverride(Size available)
        {
            Columns = ComputeColumns(available.Width, MinColumnWidth, MaxColumns, ColumnSpacing);
            double width = double.IsInfinity(available.Width)
                ? Columns * MinColumnWidth + (Columns - 1) * ColumnSpacing
                : available.Width;
            _columnWidth = Math.Max(0, (width - (Columns - 1) * ColumnSpacing) / Columns);

            _slots.Clear();
            _rowHeights.Clear();
            int row = 0, col = 0;
            double rowHeight = 0;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed) { child.Measure(new Size(0, 0)); continue; }

                int span = GetFullRow(child) ? Columns : Math.Clamp(GetSpan(child), 1, Columns);
                if (col + span > Columns)
                {
                    _rowHeights.Add(rowHeight);
                    row++; col = 0; rowHeight = 0;
                }

                double w = span * _columnWidth + (span - 1) * ColumnSpacing;
                child.Measure(new Size(w, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                _slots.Add((child, row, col, span));
                col += span;
            }
            if (_slots.Count > 0) _rowHeights.Add(rowHeight);

            double total = 0;
            for (int i = 0; i < _rowHeights.Count; i++) total += _rowHeights[i];
            total += Math.Max(0, _rowHeights.Count - 1) * RowSpacing;
            return new Size(width, total);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Yerleşim genişliği ölçümden farklıysa sütun genişliğini yeniden hesapla.
            double colWidth = Math.Max(0, (finalSize.Width - (Columns - 1) * ColumnSpacing) / Columns);

            var rowTops = new double[_rowHeights.Count];
            double y = 0;
            for (int i = 0; i < _rowHeights.Count; i++) { rowTops[i] = y; y += _rowHeights[i] + RowSpacing; }

            foreach (var (child, row, col, span) in _slots)
            {
                double x = col * (colWidth + ColumnSpacing);
                double w = span * colWidth + (span - 1) * ColumnSpacing;
                child.Arrange(new Rect(x, rowTops[row], w, _rowHeights[row]));
            }
            return finalSize;
        }
    }
}
