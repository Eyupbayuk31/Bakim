using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Durum işareti (düzen v2): 16 px renkli simge + metin; dolgu yok. Renkli dolgu hapların
    /// ("Dikkat", "İyi") yerini alır — anlam simge biçimi + renk + metinle taşınır.
    /// </summary>
    public class StatusGlyph : Control
    {
        static StatusGlyph()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusGlyph), new FrameworkPropertyMetadata(typeof(StatusGlyph)));
            FocusableProperty.OverrideMetadata(typeof(StatusGlyph), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty IntentProperty = DependencyProperty.Register(
            nameof(Intent), typeof(Models.Intent), typeof(StatusGlyph), new PropertyMetadata(Models.Intent.Neutral));
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(StatusGlyph), new PropertyMetadata(string.Empty));

        public Models.Intent Intent { get => (Models.Intent)GetValue(IntentProperty); set => SetValue(IntentProperty, value); }
        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    }
}
