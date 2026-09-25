using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım.Views.Dialogs
{
    public partial class SetupDetectedFlyoutWindow : Window
    {
        private WatchedSetupSession? _session;
        private SetupDeltaReport? _report;

        public SetupDetectedFlyoutWindow()
        {
            InitializeComponent();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            PositionAtBottomRight();
        }

        public void PositionAtBottomRight()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - ActualWidth - 24;
                Top = workArea.Bottom - ActualHeight - 24;
            }
            catch { }
        }

        public void SetMonitoringSession(WatchedSetupSession session)
        {
            _session = session;
            _report = null;

            MonitoringPanel.Visibility = Visibility.Visible;
            FinishedPanel.Visibility = Visibility.Collapsed;

            MonitoringAppNameText.Text = string.IsNullOrWhiteSpace(session.AppName)
                ? session.ProcessName
                : session.AppName;

            MonitoringStatusDetailText.Text = $"PID {session.RootProcessId} izleniyor • Değişiklikler anlık yakalanıyor";
            PositionAtBottomRight();
        }

        public void SetFinishedReport(SetupDeltaReport report)
        {
            _report = report;
            _session = null;

            MonitoringPanel.Visibility = Visibility.Collapsed;
            FinishedPanel.Visibility = Visibility.Visible;

            FinishedAppNameText.Text = string.IsNullOrWhiteSpace(report.AppName)
                ? "Bilinmeyen Kurulum"
                : report.AppName;

            FilesBadgeText.Text = $"+{report.AddedFiles.Count} Dosya";
            ExecutablesBadgeText.Text = $"+{report.AddedExecutables.Count} Yürütülebilir";
            RegistryBadgeText.Text = $"+{report.AddedRegistryRecords.Count} Kayıt";
            SizeBadgeText.Text = report.FormattedSize;

            if (report.RiskEvaluated)
            {
                // En önemli üç bulgu (NÖB 6.1); temiz kurulumda kısa açıklama.
                var top = report.RiskFindings.Where(f => f.Severity > Core.Sentinel.RiskSeverity.Info).Take(3).Select(f => "• " + f.Title).ToList();
                QuickRiskSummaryText.Text = $"{SetupRiskPresentation.VerdictText(report)} (puan {report.RiskScore}/100)" +
                    (top.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, top) : " · kalıcılık ya da sistem değişikliği yok");
                RiskSummaryCard.Visibility = Visibility.Visible;
            }
            else if (!string.IsNullOrWhiteSpace(report.QuickRiskSummary))
            {
                QuickRiskSummaryText.Text = report.QuickRiskSummary;
                RiskSummaryCard.Visibility = Visibility.Visible;
            }
            else
            {
                RiskSummaryCard.Visibility = Visibility.Collapsed;
            }

            PositionAtBottomRight();
        }

        private void OnDismissClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnInspectDeltaClicked(object sender, RoutedEventArgs e)
        {
            if (_report == null) return;

            try
            {
                var dlg = new InstallationDeltaInspectionDialog(_report);
                dlg.Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
                dlg.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Değişiklik penceresi açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnAnalyzeThreatClicked(object sender, RoutedEventArgs e)
        {
            if (_report == null)
            {
                Close();
                return;
            }

            try
            {
                // 1. Bring MainWindow to front
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Focus();
                }

                // 2. Navigate to Analyzer module
                // MainViewModel geçici (transient) kayıtlı: App.TryGetService yeni ve görünmeyen bir
                // örnek döndürüyordu, sayfa hiç değişmiyordu. Gezinme olay servisi üzerinden yapılır.
                App.TryGetService<INavigationService>()?.Navigate("Analyzer");

                // 3. Ingest and trigger analysis on detected executables
                var analyzerVm = App.TryGetService<AnalyzerViewModel>();
                if (analyzerVm != null && _report.AddedExecutables.Count > 0)
                {
                    using (Bakım.Core.History.AnalysisContext.Begin(Bakım.Core.History.AnalysisSource.SetupSentinel, $"Kurulum: {_report.AppName}"))
                    {
                        await analyzerVm.AnalyzeSpecificFilesAsync(_report.AddedExecutables);
                    }
                }

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analizör başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void OnSaveReportClicked(object sender, RoutedEventArgs e)
        {
            if (_report == null) return;

            string storageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Bakım", "InstallationLogs");

            try
            {
                if (Directory.Exists(storageDir))
                {
                    Process.Start("explorer.exe", storageDir);
                }
                else
                {
                    MessageBox.Show($"Kurulum raporu kaydedildi:\n{storageDir}", "Rapor Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch { }
        }
    }
}
