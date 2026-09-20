using System;
using System.Windows;
using Bakım.Services;
using Wpf.Ui.Controls;

namespace Bakım.Views.Windows
{
    public partial class TrayFlyoutWindow : Window
    {
        private readonly ITrayIconService _trayIconService;
        private readonly IGameModeService _gameModeService;
        private readonly ISystemCleanService _cleanService;
        private readonly INavigationService _navigationService;

        public TrayFlyoutWindow(ITrayIconService trayIconService, IGameModeService gameModeService, ISystemCleanService cleanService, INavigationService navigationService)
        {
            InitializeComponent();
            _trayIconService = trayIconService;
            _gameModeService = gameModeService;
            _cleanService = cleanService;
            _navigationService = navigationService;
        }

        public void UpdateState()
        {
            bool isActive = _gameModeService.IsGameModeActive;
            BtnGameMode.Content = isActive ? "Oyun Modunu Kapat" : "Oyun Modunu Aç";
            if (isActive)
            {
                BtnGameMode.Appearance = Wpf.Ui.Controls.ControlAppearance.Success;
                StatusText.Text = "🎮 Oyun Modu Aktif (Sistem Donduruldu)";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                BtnGameMode.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
                StatusText.Text = "🛡️ Sistem Nöbette (Arka Plan Aktif)";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void BtnShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _trayIconService.RestoreWindow();
        }

        private async void BtnQuickBoost_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            try
            {
                long freed = await _cleanService.AutoTrimWorkingSetsAsync();
                _trayIconService.ShowBalloon("RAM Temizlendi", $"{Bakım.Models.CleanCategory.FormatBytes(freed)} bellek geri kazanıldı.");
            }
            catch { }
        }

        private async void BtnGameMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long freed = await _gameModeService.ToggleGameModeAsync();
                UpdateState();
                bool isActive = _gameModeService.IsGameModeActive;
                if (isActive)
                    _trayIconService.ShowBalloon("🎮 Ultra Oyun Modu Aktif!", $"Arka plan servisleri donduruldu. {Bakım.Models.CleanCategory.FormatBytes(freed)} serbest bırakıldı.");
                else
                    _trayIconService.ShowBalloon("Oyun Modu Kapatıldı", "Arka plan servisleri normale döndü.");
            }
            catch { }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _trayIconService.RestoreWindow();
            _navigationService.Navigate("Settings");
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            MainWindow.IsExplicitExit = true;
            _trayIconService.Detach();
            Application.Current.Shutdown();
        }
    }
}
