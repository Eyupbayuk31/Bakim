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

            ApplyFilter(string.Empty);
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
                        var result = await threatService.AnalyzeFileAsync(path);
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

                var mainVm = App.TryGetService<MainViewModel>();
                mainVm?.Navigate("Analyzer");

                var analyzerVm = App.TryGetService<AnalyzerViewModel>();
                if (analyzerVm != null && _report.AddedExecutables.Count > 0)
                {
                    await analyzerVm.AnalyzeSpecificFilesAsync(_report.AddedExecutables);
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
