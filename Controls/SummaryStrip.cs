using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Özet şeridi (Windows 11 Ayarlar > Depolama üst satırı gibi): tek kart içinde yan yana
    /// 2-4 değer, aralarında ince ayırıcı. 4'lü büyük sayı kartı satırlarının yerini alır;
    /// sayfanın asıl işine (liste, eylem) yer açar.
    ///
    /// Kullanım: öğeler StatCard'dır; şerit onları kendiliğinden "şerit içi" kipe alır.
    ///   &lt;c:SummaryStrip&gt;
    ///     &lt;c:StatCard Label="Toplam" Value="{Binding Total}"/&gt;
    ///     &lt;c:StatCard Label="Bugün" Value="{Binding Today}"/&gt;
    ///   &lt;/c:SummaryStrip&gt;
    /// </summary>
    public class SummaryStrip : ItemsControl
    {
        static SummaryStrip()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SummaryStrip), new FrameworkPropertyMetadata(typeof(SummaryStrip)));
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            if (element is StatCard card)
            {
                card.IsEmbedded = true;
                card.ShowDivider = Items.IndexOf(item) > 0;
            }
        }
    }
}
