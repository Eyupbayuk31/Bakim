using System;
using System.Threading.Tasks;
using System.Windows;
using Bakım.Models;
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
        private readonly ITelemetryService? _telemetryService;

        public TrayFlyoutWindow(
            ITrayIconService trayIconService,
            IGameModeService gameModeService,
            ISystemCleanService cleanService,
            INavigationService navigationService,
            ITelemetryService? telemetryService = null)
        {
            InitializeComponent();
            _trayIconService = trayIconService;
            _gameModeService = gameModeService;
            _cleanService = cleanService;
            _navigationService = navigationService;
            _telemetryService = telemetryService;
        }

        public void UpdateState()
        {
            bool isActive = _gameModeService.IsGameModeActive;
            BtnGameMode.Content = isActive ? "Oyun Modunu Kapat" : "Oyun Modunu Aç";
            if (isActive)
            {
                BtnGameMode.Appearance = ControlAppearance.Success;
                StatusText.Text = "Oyun Modu Aktif (Sistem Donduruldu)";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
            }
            else
            {
                BtnGameMode.Appearance = ControlAppearance.Secondary;
                StatusText.Text = "Sistem Nöbette (Arka Plan Aktif)";
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
            }

            _ = RefreshTelemetryAsync();
        }

        private async Task RefreshTelemetryAsync()
        {
            if (_telemetryService == null) return;

            try
            {
                var sample = await _telemetryService.SampleMetricsAsync();
                Dispatcher.Invoke(() =>
                {
                    CpuText.Text = $"%{sample.CpuUsagePercentage}";
                    CpuProgress.Value = Math.Clamp(sample.CpuUsagePercentage, 0, 100);

                    RamText.Text = $"%{sample.RamUsagePercentage}";
                    RamProgress.Value = Math.Clamp(sample.RamUsagePercentage, 0, 100);

                    RamDetailText.Text = $"{sample.UsedRamGb:F1} GB / {sample.TotalRamGb:F1} GB (Boş: {sample.FreeRamGb:F1} GB)";
                });
            }
            catch { }
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
            try
            {
                BtnQuickBoost.IsEnabled = false;
                BtnQuickBoost.Content = "Temizleniyor...";
                long freed = await _cleanService.AutoTrimWorkingSetsAsync();
                await RefreshTelemetryAsync();
                _trayIconService.ShowBalloon("RAM Temizlendi", $"{CleanCategory.FormatBytes(freed)} bellek geri kazanıldı.");
            }
            catch { }
            finally
            {
                BtnQuickBoost.IsEnabled = true;
                BtnQuickBoost.Content = "Hızlı RAM Boşalt";
            }
        }

        private async void BtnGameMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long freed = await _gameModeService.ToggleGameModeAsync();
                UpdateState();
                bool isActive = _gameModeService.IsGameModeActive;
                if (isActive)
                    _trayIconService.ShowBalloon("Ultra Oyun Modu Aktif!", $"Arka plan servisleri donduruldu. {CleanCategory.FormatBytes(freed)} serbest bırakıldı.");
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
