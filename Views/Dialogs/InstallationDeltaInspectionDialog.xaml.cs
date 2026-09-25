using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım.Views.Dialogs
{
    public partial class InstallationDeltaInspectionDialog : Window
    {
        private readonly SetupDeltaReport _report;
        private readonly List<FileItemDisplay> _allFiles = new();
        private readonly List<RegistryItemDisplay> _allRegistry = new();
        private readonly List<ServiceItemDisplay> _allServices = new();

        public record FileItemDisplay(string FileName, string FullPath, string Extension);
        public record RegistryItemDisplay(string Hive, string KeyPath, string Details);
        public record ServiceItemDisplay(string Name, string PathOrKey);
        /// <summary>Bulgu satırı; tek tık müdahalenin sonucu satırda gösterilir.</summary>
        public sealed class FindingRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
        {
            public FindingRow(Core.Sentinel.RiskFinding finding)
            {
                Finding = finding;
                Severity = Core.Sentinel.SetupRiskEngine.SeverityLabel(finding.Severity);
                Technique = string.IsNullOrEmpty(finding.Technique) ? string.Empty : $"  ({finding.Technique})";
                ActionLabel = Services.Sentinel.Actions.FindingActions.Label(finding.Action) ?? string.Empty;
            }

            public Core.Sentinel.RiskFinding Finding { get; }
            public string Severity { get; }
            public string Title => Finding.Title;
            public string Detail => Finding.Detail;
            public string Technique { get; }
            public string ActionLabel { get; }
            public bool HasAction => ActionLabel.Length > 0;

            private bool _canAct = true;
            public bool CanAct { get => _canAct; set => SetProperty(ref _canAct, value); }

            private string _resultText = string.Empty;
            public string ResultText
            {
                get => _resultText;
                set
                {
                    if (SetProperty(ref _resultText, value)) OnPropertyChanged(nameof(HasResult));
                }
            }
            public bool HasResult => ResultText.Length > 0;
        }
        public record SystemChangeDisplay(string Area, string Kind, string Key, string Value);

        private readonly List<SystemChangeDisplay> _allSystem = new();

        public InstallationDeltaInspectionDialog(SetupDeltaReport report)
        {
            _report = report;
            InitializeComponent();
            PopulateData();
        }

        private void PopulateData()
        {
            DialogTitleText.Text = $"{_report.AppName} — Kurulum Raporu";
            DialogSubtitleText.Text = $"{_report.InstallTime.ToLocalTime():yyyy-MM-dd HH:mm} • Toplam Boyut: {_report.FormattedSize}";

            KpiFilesText.Text = _report.AddedFiles.Count.ToString();
            KpiExecutablesText.Text = _report.AddedExecutables.Count.ToString();
            KpiRegistryText.Text = _report.AddedRegistryRecords.Count.ToString();
            KpiServicesText.Text = (_report.AddedServices.Count + _report.AddedStartupEntries.Count).ToString();

            // Populate files
            foreach (var f in _report.AddedFiles)
            {
                string name = Path.GetFileName(f);
                string ext = Path.GetExtension(f).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(ext)) ext = "DOSYA";
                _allFiles.Add(new FileItemDisplay(name, f, ext));
            }

            // Populate registry
            foreach (var r in _report.AddedRegistryRecords)
            {
                string details = string.IsNullOrWhiteSpace(r.ValueName)
                    ? "Anahtar oluşturuldu"
                    : $"Değer: {r.ValueName} = {r.ValueData}";
                _allRegistry.Add(new RegistryItemDisplay(r.Hive, r.KeyPath, details));
            }

            // Populate services & autorun
            foreach (var s in _report.AddedServices)
            {
                _allServices.Add(new ServiceItemDisplay("Yeni Windows Servisi", s));
            }
            foreach (var st in _report.AddedStartupEntries)
            {
                _allServices.Add(new ServiceItemDisplay("Başlangıç Kaydı (Run)", st));
            }

            PopulateSummary();
            ApplyFilter(string.Empty);
        }

        /// <summary>Nöbetçi v2 özeti: karar, bulgular, kurulum dosyası, sistem alanları.</summary>
        private void PopulateSummary()
        {
            VerdictBadge.Level = SetupRiskPresentation.ToLevel(_report.RiskVerdict);
            VerdictBadge.Visibility = _report.RiskEvaluated ? Visibility.Visible : Visibility.Collapsed;
            VerdictText.Text = SetupRiskPresentation.VerdictText(_report);
            VerdictDetailText.Text = SetupRiskPresentation.VerdictDetail(_report);

            FindingsList.ItemsSource = _report.RiskFindings.Select(f => new FindingRow(f)).ToList();

            var lines = new List<string>();
            var installer = _report.Installer;
            lines.Add($"Dosya: {(_report.InstallerPath.Length > 0 ? _report.InstallerPath : "bilinmiyor")}");
            if (installer != null)
            {
                lines.Add($"İmza: {(installer.SignatureText.Length > 0 ? installer.SignatureText : "denetlenemedi")}");
                lines.Add($"Kurulum çatısı: {installer.FrameworkText}");
                if (installer.SourceSite != null || installer.HostUrl != null)
                    lines.Add($"İndirildiği yer: {installer.SourceSite ?? installer.HostUrl}" +
                              (installer.ReferrerUrl != null ? $" (sayfa: {installer.ReferrerUrl})" : string.Empty));
                else if (!installer.FromInternet)
                    lines.Add("İndirme kaynağı: kayıt yok (yerel dosya ya da kaynak bilgisi silinmiş)");
                if (installer.Sha256 != null) lines.Add($"SHA-256: {installer.Sha256}");
            }
            if (_report.NewPrograms.Count > 0)
                lines.Add($"Yeni programlar: {string.Join(", ", _report.NewPrograms)}");
            if (_report.IsPossiblyIncomplete)
                lines.Add("Not: dosya izleyici taştı; dosya listesi eksik olabilir.");
            InstallerInfoText.Text = string.Join(Environment.NewLine, lines);

            foreach (var c in _report.SystemChanges)
            {
                string kind = c.Kind switch
                {
                    Core.Sentinel.ChangeKind.Added => "Eklendi",
                    Core.Sentinel.ChangeKind.Removed => "Kaldırıldı",
                    _ => "Değişti"
                };
                string value = c.Kind == Core.Sentinel.ChangeKind.Modified ? $"{c.Before} → {c.After}" : (c.After ?? c.Before ?? string.Empty);
                _allSystem.Add(new SystemChangeDisplay(Core.Sentinel.SystemStateSnapshot.AreaLabel(c.Area), kind, c.Key, value));
            }
        }

        /// <summary>Bulgudaki tek tık müdahale (NÖB 5.2): onay → uygula → sonucu satıra yaz.</summary>
        private async void OnFindingActionClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FindingRow row } || !row.CanAct) return;

            var confirm = MessageBox.Show(
                $"{row.ActionLabel}: {row.Title}\n\n{row.Detail}\n\n{Services.Sentinel.Actions.FindingActions.ConfirmText(row.Finding)}",
                "Kurulum Nöbetçisi", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            row.CanAct = false;
            row.ResultText = "Uygulanıyor…";
            try
            {
                var result = await Services.Sentinel.Actions.FindingActions.ExecuteAsync(row.Finding, _report.AppName);
                row.ResultText = result.Message;
                row.CanAct = !result.Success;
            }
            catch (Exception ex)
            {
                AppLog.Error("Nöbetçi müdahalesi uygulanamadı.", ex, nameof(InstallationDeltaInspectionDialog));
                row.ResultText = $"Hata: {ex.Message}";
                row.CanAct = true;
            }
        }

        private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(SearchBox.Text);
        }

        private void ApplyFilter(string query)
        {
            query = query?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                FilesListView.ItemsSource = _allFiles;
                RegistryListView.ItemsSource = _allRegistry;
                ServicesListView.ItemsSource = _allServices;
                SystemListView.ItemsSource = _allSystem;
                FilteredCountText.Text = $"Toplam {_allFiles.Count} dosya, {_allRegistry.Count} kayıt";
                return;
            }

            var filteredFiles = _allFiles.Where(f =>
                f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            var filteredReg = _allRegistry.Where(r =>
                r.KeyPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Details.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            var filteredServices = _allServices.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.PathOrKey.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            SystemListView.ItemsSource = _allSystem.Where(c =>
                c.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Area.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            FilesListView.ItemsSource = filteredFiles;
            RegistryListView.ItemsSource = filteredReg;
            ServicesListView.ItemsSource = filteredServices;

            FilteredCountText.Text = $"Filtre sonucu: {filteredFiles.Count} dosya, {filteredReg.Count} kayıt";
        }

        private void OnOpenFolderForFileClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(path) ?? string.Empty;
                        if (Directory.Exists(dir))
                        {
                            Process.Start("explorer.exe", dir);
                        }
                    }
                }
                catch { }
            }
        }

        private async void OnAnalyzeSingleFileClicked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string path && !string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var threatService = App.TryGetService<IFileThreatAnalyzerService>();
                    var autorunsEngine = App.TryGetService<IAutorunsScannerEngine>();
                    var virusTotalService = App.TryGetService<IVirusTotalCheckService>();

                    if (threatService != null && File.Exists(path))
                    {
                        ThreatAnalysisResult result;
                        using (Bakım.Core.History.AnalysisContext.Begin(Bakım.Core.History.AnalysisSource.SetupSentinel, $"Kurulum: {_report.AppName}"))
                        {
                            result = await threatService.AnalyzeFileAsync(path);
                        }
                        var dialog = new ThreatAnalysisDialog(result, threatService, autorunsEngine, virusTotalService);
                        dialog.Owner = this;
                        dialog.ShowDialog();
                    }
                    else
                    {
                        MessageBox.Show("Dosya diskte bulunamadı veya tehdit analiz servisi aktif değil.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Dosya analiz edilirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnAnalyzeAllExecutablesClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    mainWindow.Show();
                    mainWindow.Activate();
                }

                // MainViewModel geçici kayıtlı; gezinme olay servisi üzerinden yapılır.
                App.TryGetService<INavigationService>()?.Navigate("Analyzer");

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
            }
        }

        private void OnExportJsonClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    FileName = $"Kurulum_Delta_{_report.AppName}_{DateTime.Now:yyyyMMdd_HHmm}.json",
                    Filter = "JSON Dosyası (*.json)|*.json|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = ".json"
                };

                if (sfd.ShowDialog() == true)
                {
                    string json = JsonSerializer.Serialize(_report, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    File.WriteAllText(sfd.FileName, json);
                    MessageBox.Show($"Rapor kaydedildi:\n{sfd.FileName}", "Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dışa aktarma başarısız: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                string? firstValid = _report.AddedFiles.FirstOrDefault(File.Exists);
                if (firstValid != null)
                {
                    string dir = Path.GetDirectoryName(firstValid) ?? string.Empty;
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                        return;
                    }
                }

                string storageDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Bakım", "InstallationLogs");

                if (Directory.Exists(storageDir))
                {
                    Process.Start("explorer.exe", storageDir);
                }
            }
            catch { }
        }
    }
}
