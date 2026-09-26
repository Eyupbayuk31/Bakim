using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bakım.Helpers
{
    /// <summary>
    /// Kaydırılabilir listenin kesilen kenarını yumuşakça soldurur; yarım görünen öğe
    /// "devamı var" diye okunur. Solma yalnızca o yönde gerçekten içerik varken uygulanır,
    /// listenin başında ya da sonunda ilk/son öğe tam opak kalır.
    ///
    /// Kullanım: &lt;ScrollViewer helpers:ScrollFade.IsEnabled="True"/&gt;
    /// </summary>
    public static class ScrollFade
    {
        /// <summary>Solma bandının yüksekliği (px).</summary>
        private const double FadeLength = 24;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ScrollFade),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer viewer) return;
            viewer.ScrollChanged -= OnScrollChanged;
            if ((bool)e.NewValue) viewer.ScrollChanged += OnScrollChanged;
            else viewer.OpacityMask = null;
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            double height = viewer.ActualHeight;
            bool fadeTop = viewer.VerticalOffset > 0.5;
            bool fadeBottom = viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;

            if ((!fadeTop && !fadeBottom) || height <= FadeLength * 2)
            {
                viewer.OpacityMask = null;
                return;
            }

            var mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, height)
            };
            mask.GradientStops.Add(new GradientStop(fadeTop ? Colors.Transparent : Colors.Black, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, FadeLength / height));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - FadeLength / height));
            mask.GradientStops.Add(new GradientStop(fadeBottom ? Colors.Transparent : Colors.Black, 1));
            mask.Freeze();
            viewer.OpacityMask = mask;
        }
    }
}
