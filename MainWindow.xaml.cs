using System.Windows;
using Wpf.Ui.Controls;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    ///
    /// Close-to-Tray: Çarpı (X) butonuna tıklandığında uygulama tamamen kapanmaz;
    /// sistem tepsisine gizlenerek arka planda nöbet tutmaya devam eder.
    /// Tamamen kapatmak için sistem tepsisinden "Çıkış" seçilmelidir.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly ITrayIconService? _trayIcon;

        /// <summary>
        /// Yalnızca tepsi menüsünden veya sistem çıkışından tetiklendiğinde true olur.
        /// </summary>
        public static bool IsExplicitExit { get; set; } = false;

        public MainWindow(MainViewModel viewModel, ITrayIconService trayIcon, IAppSettingsService settings, IThemeService theme)
        {
            InitializeComponent();

            _trayIcon = trayIcon;
            DataContext = viewModel;

            // Tepsi simgesini hemen bağla; pencere gizli başlatılsa bile tepsi ikonu aktif olsun
            _trayIcon.Attach(this);

            Loaded += (_, _) =>
            {
                theme.ApplyBackdrop(settings.Current.IsMicaEnabled);
            };
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!IsExplicitExit)
            {
                e.Cancel = true;
                Hide();
                _trayIcon?.ShowBalloon("Bakım Arka Planda Çalışıyor",
                    "Uygulama arka planda nöbet tutmaya devam ediyor. Açmak için çift tıklayın.");
                return;
            }

            base.OnClosing(e);
        }
    }
}
