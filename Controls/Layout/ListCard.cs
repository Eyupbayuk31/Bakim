using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Liste kartı (düzen v2): tek kart içinde, aralarında 1 px ayırıcı olan satırlar.
    /// "Kart içinde kart" listelerinin yerini alır. İsteğe bağlı başlık satırı (<see cref="Header"/>,
    /// sağda <see cref="HeaderTrailing"/>). Satır içeriği genellikle <see cref="ListRow"/>'dur.
    /// </summary>
    public class ListCard : ItemsControl
    {
        static ListCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ListCard), new FrameworkPropertyMetadata(typeof(ListCard)));
            FocusableProperty.OverrideMetadata(typeof(ListCard), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
            nameof(Header), typeof(string), typeof(ListCard), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty HeaderIconProperty = DependencyProperty.Register(
            nameof(HeaderIcon), typeof(string), typeof(ListCard), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty HeaderTrailingProperty = DependencyProperty.Register(
            nameof(HeaderTrailing), typeof(object), typeof(ListCard), new PropertyMetadata(null));
        /// <summary>Liste boşken gösterilecek içerik (ör. c:EmptyState).</summary>
        public static readonly DependencyProperty EmptyContentProperty = DependencyProperty.Register(
            nameof(EmptyContent), typeof(object), typeof(ListCard), new PropertyMetadata(null));
        public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
            nameof(IsEmpty), typeof(bool), typeof(ListCard), new PropertyMetadata(true));

        public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
        public string HeaderIcon { get => (string)GetValue(HeaderIconProperty); set => SetValue(HeaderIconProperty, value); }
        public object? HeaderTrailing { get => GetValue(HeaderTrailingProperty); set => SetValue(HeaderTrailingProperty, value); }
        public object? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }
        public bool IsEmpty { get => (bool)GetValue(IsEmptyProperty); private set => SetValue(IsEmptyProperty, value); }

        protected override DependencyObject GetContainerForItemOverride() => new ListCardItem();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is ListCardItem;

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is ListCardItem row)
                row.ShowDivider = ItemContainerGenerator.IndexFromContainer(row) > 0 || !string.IsNullOrEmpty(Header);
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);
            IsEmpty = Items.Count == 0;
            // İlk satır değişmiş olabilir: ayırıcıları yeniden hesapla.
            for (int i = 0; i < Items.Count; i++)
                if (ItemContainerGenerator.ContainerFromIndex(i) is ListCardItem row)
                    row.ShowDivider = i > 0 || !string.IsNullOrEmpty(Header);
        }

        protected override void OnItemsSourceChanged(System.Collections.IEnumerable oldValue, System.Collections.IEnumerable newValue)
        {
            base.OnItemsSourceChanged(oldValue, newValue);
            IsEmpty = Items.Count == 0;
        }
    }

    /// <summary>Liste kartı satır kabı: üstte ayırıcı, üzerine gelince hafif zemin.</summary>
    public class ListCardItem : ContentControl
    {
        static ListCardItem()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ListCardItem), new FrameworkPropertyMetadata(typeof(ListCardItem)));
            FocusableProperty.OverrideMetadata(typeof(ListCardItem), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
            nameof(ShowDivider), typeof(bool), typeof(ListCardItem), new PropertyMetadata(true));

        public bool ShowDivider { get => (bool)GetValue(ShowDividerProperty); set => SetValue(ShowDividerProperty, value); }
    }

    /// <summary>
    /// Liste satırı: 20 px simge (veya uygulama ikonu), başlık + alt satır, sağda değer ve eylem.
    /// En az 48 px (alt satırlı 60 px).
    /// </summary>
    public class ListRow : Control
    {
        static ListRow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ListRow), new FrameworkPropertyMetadata(typeof(ListRow)));
            FocusableProperty.OverrideMetadata(typeof(ListRow), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));
        /// <summary>Simge yerine gösterilecek içerik (ör. uygulama ikonu Image, StatusGlyph).</summary>
        public static readonly DependencyProperty LeadingProperty = DependencyProperty.Register(
            nameof(Leading), typeof(object), typeof(ListRow), new PropertyMetadata(null));
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
            nameof(Subtitle), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty MetaProperty = DependencyProperty.Register(
            nameof(Meta), typeof(string), typeof(ListRow), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
            nameof(Trailing), typeof(object), typeof(ListRow), new PropertyMetadata(null));

        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public object? Leading { get => GetValue(LeadingProperty); set => SetValue(LeadingProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
        public string Meta { get => (string)GetValue(MetaProperty); set => SetValue(MetaProperty, value); }
        public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
    }
}
