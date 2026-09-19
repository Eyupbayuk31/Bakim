using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bakım.Views.Modules
{
    /// <summary>
    /// Interaction logic for WindowsTweakerModuleView.xaml
    /// Sıfır business logic: tüm iş mantığı WindowsTweakerViewModel üzerinde yürütülür.
    /// Sadece klavye kısayolu (Ctrl+F / Ctrl+K) arama kutusu odaklaması yer alır.
    /// </summary>
    public partial class WindowsTweakerModuleView : UserControl
    {
        public WindowsTweakerModuleView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewKeyDown += OnWindowPreviewKeyDown;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewKeyDown -= OnWindowPreviewKeyDown;
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (e.Key == Key.F || e.Key == Key.K))
            {
                TweakerSearchBox?.Focus();
                e.Handled = true;
            }
        }
    }
}
