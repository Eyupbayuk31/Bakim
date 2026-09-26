using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Kart başlık satırı (düzen v2): 16 px simge + 14 SemiBold başlık + sağda rozet/bağlantı/⋯.
    /// Tek satır, 32 px; altta 8 px. Kart başlıkları başka boyutta yazılmaz.
    /// </summary>
    public class CardHeader : Control
    {
        static CardHeader()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CardHeader), new FrameworkPropertyMetadata(typeof(CardHeader)));
            FocusableProperty.OverrideMetadata(typeof(CardHeader), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(CardHeader), new PropertyMetadata(string.Empty));

        /// <summary>SymbolRegular adı (ör. "History24"); boşsa simge gösterilmez.</summary>
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(CardHeader), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
            nameof(Trailing), typeof(object), typeof(CardHeader), new PropertyMetadata(null));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
    }
}
