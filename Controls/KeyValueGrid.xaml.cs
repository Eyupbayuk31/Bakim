using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Bakım.Controls
{
    public partial class KeyValueGrid : UserControl
    {
        public KeyValueGrid()
        {
            InitializeComponent();
        }

        /// <summary><see cref="Models.KeyValueRow"/> dizisi.</summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(KeyValueGrid));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty IsMonospaceProperty =
            DependencyProperty.Register(nameof(IsMonospace), typeof(bool), typeof(KeyValueGrid));

        public bool IsMonospace
        {
            get => (bool)GetValue(IsMonospaceProperty);
            set => SetValue(IsMonospaceProperty, value);
        }

        public static readonly DependencyProperty KeyColumnWidthProperty =
            DependencyProperty.Register(nameof(KeyColumnWidth), typeof(GridLength), typeof(KeyValueGrid),
                new PropertyMetadata(new GridLength(160)));

        public GridLength KeyColumnWidth
        {
            get => (GridLength)GetValue(KeyColumnWidthProperty);
            set => SetValue(KeyColumnWidthProperty, value);
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string value } && value.Length > 0)
            {
                try
                {
                    Clipboard.SetText(value);
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    // Pano başka bir süreç tarafından kilitliyse kopyalama sessizce başarısız olmasın.
                    Services.AppLog.Warning("Panoya kopyalanamadı.", ex, nameof(KeyValueGrid));
                }
            }
        }
    }
}
