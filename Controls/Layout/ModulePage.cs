using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Sayfa kabuğu (düzen v2). Her modül sayfası aynı iskeleti kullanır:
    ///   Başlık (28) + alt başlık · sağda eylemler (en fazla bir birincil + ⋯)
    ///   Sekmeler (SelectorBar) · Komut satırı · İçerik · (isteğe bağlı) alt çubuk
    /// Kenar boşluğu 36/28 (dar pencerede 24/20), içerik <see cref="MaxContentWidth"/> ile sınırlı ve sola yaslı.
    /// <see cref="IsScrollable"/> false ise içerik kalan yüksekliği doldurur (kendi listesi kayan sayfalar).
    /// </summary>
    public class ModulePage : ContentControl
    {
        static ModulePage()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ModulePage), new FrameworkPropertyMetadata(typeof(ModulePage)));
            FocusableProperty.OverrideMetadata(typeof(ModulePage), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ModulePage), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
            nameof(Subtitle), typeof(string), typeof(ModulePage), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
            nameof(Actions), typeof(object), typeof(ModulePage), new PropertyMetadata(null));
        public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(
            nameof(Tabs), typeof(object), typeof(ModulePage), new PropertyMetadata(null));
        public static readonly DependencyProperty ToolbarProperty = DependencyProperty.Register(
            nameof(Toolbar), typeof(object), typeof(ModulePage), new PropertyMetadata(null));
        public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
            nameof(Footer), typeof(object), typeof(ModulePage), new PropertyMetadata(null));
        public static readonly DependencyProperty MaxContentWidthProperty = DependencyProperty.Register(
            nameof(MaxContentWidth), typeof(double), typeof(ModulePage), new PropertyMetadata(1360.0));
        public static readonly DependencyProperty IsScrollableProperty = DependencyProperty.Register(
            nameof(IsScrollable), typeof(bool), typeof(ModulePage), new PropertyMetadata(true));
        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
            nameof(IsCompact), typeof(bool), typeof(ModulePage), new PropertyMetadata(false));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
        public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
        public object? Tabs { get => GetValue(TabsProperty); set => SetValue(TabsProperty, value); }
        public object? Toolbar { get => GetValue(ToolbarProperty); set => SetValue(ToolbarProperty, value); }
        public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
        public double MaxContentWidth { get => (double)GetValue(MaxContentWidthProperty); set => SetValue(MaxContentWidthProperty, value); }
        public bool IsScrollable { get => (bool)GetValue(IsScrollableProperty); set => SetValue(IsScrollableProperty, value); }
        /// <summary>Genişlik 1000 px altındayken true: kenar boşlukları daralır.</summary>
        public bool IsCompact { get => (bool)GetValue(IsCompactProperty); private set => SetValue(IsCompactProperty, value); }

        public const double CompactBreakpoint = 1000;

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            IsCompact = sizeInfo.NewSize.Width < CompactBreakpoint;
        }
    }
}
