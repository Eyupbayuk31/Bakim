using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bakım.Helpers
{
    /// <summary>
    /// Yatay kayan çipli şeritler için: fare tekerleği sağa/sola kaydırır (çubuk gizli olsa da),
    /// kesilen kenar yumuşakça solar. Kaydırılacak içerik yoksa tekerlek olayı üst öğeye geçer.
    ///
    /// Kullanım: &lt;ScrollViewer HorizontalScrollBarVisibility="Hidden" helpers:HorizontalWheelScroll.IsEnabled="True"/&gt;
    /// </summary>
    public static class HorizontalWheelScroll
    {
        private const double FadeLength = 28;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(HorizontalWheelScroll),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer viewer) return;
            viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            viewer.ScrollChanged -= OnScrollChanged;
            if ((bool)e.NewValue)
            {
                viewer.PreviewMouseWheel += OnPreviewMouseWheel;
                viewer.ScrollChanged += OnScrollChanged;
            }
            else
            {
                viewer.OpacityMask = null;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (viewer.ScrollableWidth <= 0) return;

            double target = Math.Clamp(viewer.HorizontalOffset - e.Delta, 0, viewer.ScrollableWidth);
            viewer.ScrollToHorizontalOffset(target);
            e.Handled = true;
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            double width = viewer.ActualWidth;
            bool fadeLeft = viewer.HorizontalOffset > 0.5;
            bool fadeRight = viewer.HorizontalOffset < viewer.ScrollableWidth - 0.5;

            if ((!fadeLeft && !fadeRight) || width <= FadeLength * 2)
            {
                viewer.OpacityMask = null;
                return;
            }

            var mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(width, 0)
            };
            mask.GradientStops.Add(new GradientStop(fadeLeft ? Colors.Transparent : Colors.Black, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, FadeLength / width));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - FadeLength / width));
            mask.GradientStops.Add(new GradientStop(fadeRight ? Colors.Transparent : Colors.Black, 1));
            mask.Freeze();
            viewer.OpacityMask = mask;
        }
    }
}
