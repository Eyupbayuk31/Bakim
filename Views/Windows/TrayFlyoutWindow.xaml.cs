using System;
using System.Collections.Generic;
using System.Linq;
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
            // Win11 Hızlı Ayarlar: açık olan kutucuk vurgu dolgulu, kapalı olan standart.
            BtnGameMode.Appearance = isActive ? ControlAppearance.Primary : ControlAppearance.Secondary;
            BtnGameMode.ToolTip = isActive ? "Oyun Modu açık. Kapatmak için tıklayın." : "Oyun Modunu başlatır.";
            StatusText.Text = isActive ? "Oyun Modu açık" : "Arka plan bakımı etkin";

            RefreshSentinelAndActivity();
            _ = RefreshTelemetryAsync();
        }

        /// <summary>Nöbetçi durumu ve son 3 etkinlik (§5.19).</summary>
        private void RefreshSentinelAndActivity()
        {
            var sentinel = App.TryGetService<ISetupSentinelService>();
            SentinelText.Text = sentinel == null ? "Kurulum Nöbetçisi kullanılamıyor"
                : !sentinel.IsEnabled ? "Kurulum Nöbetçisi kapalı"
                : sentinel.IsMonitoringActiveSession ? $"İzleniyor: {sentinel.ActiveSession?.AppName}"
                : sentinel.RecentReports.FirstOrDefault() is { } last ? $"Son kurulum: {last.AppName}"
                : "Kurulum Nöbetçisi etkin";

            var activity = App.TryGetService<Bakım.Services.Activity.IActivityService>();
            var recent = activity?.Entries
                .Where(e => e.Kind != Bakım.Core.Activity.ActivityKind.Restore)
                .OrderByDescending(e => e.AtUtc)
                .Take(3)
                .Select(e => $"{e.AtUtc.ToLocalTime():HH:mm} · {e.Title}")
                .ToList() ?? new List<string>();
            RecentActivityList.ItemsSource = recent.Count > 0 ? recent : new List<string> { "Henüz etkinlik yok." };
        }

        private void BtnActivity_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _trayIconService.RestoreWindow();
            _navigationService.Navigate("Activity");
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
                BtnQuickBoost.Content = "Temizleniyor…";
                var quick = App.TryGetService<IQuickMaintenanceService>();
                if (quick == null) return;
                var result = await quick.RunAsync(null, System.Threading.CancellationToken.None);
                _trayIconService.ShowBalloon("Hızlı Bakım", result.Summary);
                RefreshSentinelAndActivity();
            }
            catch { }
            finally
            {
                BtnQuickBoost.IsEnabled = true;
                BtnQuickBoost.Content = "Hızlı bakım";
            }
        }

        private async void BtnGameMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long freed = await _gameModeService.ToggleGameModeAsync();
                UpdateState();
                bool isActive = _gameModeService.IsGameModeActive;
                _trayIconService.ShowBalloon(isActive ? "Oyun Modu açık" : "Oyun Modu kapatıldı", _gameModeService.LastActionSummary);
            }
            catch (Exception ex)
            {
                AppLog.Error("Oyun Modu tepsi menüsünden değiştirilemedi.", ex, nameof(TrayFlyoutWindow));
            }
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
