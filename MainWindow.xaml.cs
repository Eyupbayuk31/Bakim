using System.Windows;
using Wpf.Ui.Controls;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    ///
    /// ViewModel artık XAML'den değil konteynerden gelir. Bu fark önemlidir:
    /// XAML örneklemesi parametresiz yapıcı metot zorunlu kılıyor, o da her
    /// ViewModel'i servislerini elle `new` etmeye mecbur bırakıyordu.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly ITrayIconService? _trayIcon;

        public MainWindow(MainViewModel viewModel, ITrayIconService trayIcon, IAppSettingsService settings, IThemeService theme)
        {
            InitializeComponent();

            _trayIcon = trayIcon;
            DataContext = viewModel;

            Loaded += (_, _) =>
            {
                _trayIcon.Attach(this);
                theme.ApplyBackdrop(settings.Current.IsMicaEnabled);
            };
        }
    }
}
