using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bakım.Controls
{
    /// <summary>
    /// Tema seçim kutucuğundaki küçük pencere çizimi (Windows 11 Kişiselleştirme gibi):
    /// pencere zemini, kenar çubuğu, kart, iki metin çizgisi ve vurgu düğmesi.
    /// </summary>
    public class ThemePreview : Control
    {
        static ThemePreview()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ThemePreview), new FrameworkPropertyMetadata(typeof(ThemePreview)));
            FocusableProperty.OverrideMetadata(typeof(ThemePreview), new FrameworkPropertyMetadata(false));
            IsHitTestVisibleProperty.OverrideMetadata(typeof(ThemePreview), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty WindowBrushProperty = DependencyProperty.Register(nameof(WindowBrush), typeof(Brush), typeof(ThemePreview));
        public static readonly DependencyProperty CardBrushProperty = DependencyProperty.Register(nameof(CardBrush), typeof(Brush), typeof(ThemePreview));
        public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(ThemePreview));
        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(ThemePreview));

        public Brush? WindowBrush { get => (Brush?)GetValue(WindowBrushProperty); set => SetValue(WindowBrushProperty, value); }
        public Brush? CardBrush { get => (Brush?)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }
        public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
        public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    }
}
