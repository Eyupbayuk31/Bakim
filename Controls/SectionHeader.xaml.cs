using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Modül ve kart başlıkları için standart blok.
    ///
    /// Kullanım:
    ///   &lt;c:SectionHeader IsPageTitle="True"
    ///                    Title="Genel Bakış"
    ///                    Description="Gerçek zamanlı sistem telemetrisi."&gt;
    ///     &lt;c:SectionHeader.Actions&gt;
    ///       &lt;ui:Button Content="Yenile"/&gt;
    ///     &lt;/c:SectionHeader.Actions&gt;
    ///   &lt;/c:SectionHeader&gt;
    /// </summary>
    public partial class SectionHeader : UserControl
    {
        public SectionHeader()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SectionHeader),
                new PropertyMetadata(string.Empty));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(SectionHeader),
                new PropertyMetadata(string.Empty));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        /// <summary>True ise sayfa başlığı ölçeğinde (24px Bold), aksi halde bölüm başlığı (16px SemiBold).</summary>
        public static readonly DependencyProperty IsPageTitleProperty =
            DependencyProperty.Register(nameof(IsPageTitle), typeof(bool), typeof(SectionHeader),
                new PropertyMetadata(false));

        public bool IsPageTitle
        {
            get => (bool)GetValue(IsPageTitleProperty);
            set => SetValue(IsPageTitleProperty, value);
        }

        /// <summary>Başlığın hemen sağındaki rozet / canlı durum göstergesi.</summary>
        public static readonly DependencyProperty TitleAdornmentProperty =
            DependencyProperty.Register(nameof(TitleAdornment), typeof(object), typeof(SectionHeader),
                new PropertyMetadata(null));

        public object? TitleAdornment
        {
            get => GetValue(TitleAdornmentProperty);
            set => SetValue(TitleAdornmentProperty, value);
        }

        /// <summary>Sağ üstteki eylem düğmeleri.</summary>
        public static readonly DependencyProperty ActionsProperty =
            DependencyProperty.Register(nameof(Actions), typeof(object), typeof(SectionHeader),
                new PropertyMetadata(null));

        public object? Actions
        {
            get => GetValue(ActionsProperty);
            set => SetValue(ActionsProperty, value);
        }

        public static readonly DependencyProperty HeaderMarginProperty =
            DependencyProperty.Register(nameof(HeaderMargin), typeof(Thickness), typeof(SectionHeader),
                new PropertyMetadata(new Thickness(0, 0, 0, 18)));

        public Thickness HeaderMargin
        {
            get => (Thickness)GetValue(HeaderMarginProperty);
            set => SetValue(HeaderMarginProperty, value);
        }
    }
}
