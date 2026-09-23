using System.Windows.Controls;
using System.Windows.Input;
using Bakım.ViewModels;

namespace Bakım.Views.Modules
{
    /// <summary>
    /// Interaction logic for StoreModuleView.xaml
    /// Sıfır code-behind kuralı: Tüm iş mantığı StoreViewModel içindedir.
    /// Buradaki olaylar salt UI fare tekerleği ve konsol oto-kaydırma yönlendirmesidir.
    /// </summary>
    public partial class StoreModuleView : UserControl
    {
        public StoreModuleView()
        {
            InitializeComponent();
        }

        private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scv)
            {
                scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 2.0));
                e.Handled = true;
            }
        }

        private void OnConsoleTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is StoreViewModel vm && vm.IsAutoScrollEnabled)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
