using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Metrik kutucuğu (düzen v2): etiket, büyük değer + birim, tek satır ikincil bilgi ve
    /// isteğe bağlı küçük grafik (Content, alt kenara yaslı). Sabit 136 px yükseklik; köşe rozeti yok.
    /// Durum rengi yalnızca <see cref="Intent"/> Caution/Critical iken değerde kullanılır.
    /// </summary>
    public class MetricTile : ContentControl
    {
        static MetricTile()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MetricTile), new FrameworkPropertyMetadata(typeof(MetricTile)));
            FocusableProperty.OverrideMetadata(typeof(MetricTile), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty IntentProperty = DependencyProperty.Register(
            nameof(Intent), typeof(Models.Intent), typeof(MetricTile), new PropertyMetadata(Models.Intent.Neutral));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public Models.Intent Intent { get => (Models.Intent)GetValue(IntentProperty); set => SetValue(IntentProperty, value); }
    }
}
