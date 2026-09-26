using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Sayfa bölümü (düzen v2): başlık kartın <b>dışında</b> (20 SemiBold), isteğe bağlı açıklama
    /// ve sağda bağlantı/eylem ("Tümünü gör ›"). Bölümler arası 28 px; ilk bölümde üst boşluk yok
    /// (<see cref="IsFirst"/>).
    /// </summary>
    public class Section : ContentControl
    {
        static Section()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Section), new FrameworkPropertyMetadata(typeof(Section)));
            FocusableProperty.OverrideMetadata(typeof(Section), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(Section), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
            nameof(Description), typeof(string), typeof(Section), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
            nameof(Trailing), typeof(object), typeof(Section), new PropertyMetadata(null));

        public static readonly DependencyProperty IsFirstProperty = DependencyProperty.Register(
            nameof(IsFirst), typeof(bool), typeof(Section), new PropertyMetadata(false));

        public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
        public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
        public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
        public bool IsFirst { get => (bool)GetValue(IsFirstProperty); set => SetValue(IsFirstProperty, value); }
    }
}
