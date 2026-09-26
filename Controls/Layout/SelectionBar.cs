using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    /// <summary>
    /// Alt seçim çubuğu (düzen v2): "14 öğe seçili · 3,2 GB   [Birincil eylem] [İptal]".
    /// Toplu eylemleri sayfa başından seçimin olduğu yere taşır. Content = eylem düğmeleri.
    /// </summary>
    public class SelectionBar : ContentControl
    {
        static SelectionBar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectionBar), new FrameworkPropertyMetadata(typeof(SelectionBar)));
            FocusableProperty.OverrideMetadata(typeof(SelectionBar), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
            nameof(Summary), typeof(string), typeof(SelectionBar), new PropertyMetadata(string.Empty));

        public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    }
}
