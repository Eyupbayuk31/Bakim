using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class AutorunsViewModel : ObservableObject
    {
        private readonly IAutorunsScannerEngine _scannerEngine;
        private readonly IVirusTotalCheckService _virusTotalService;
        private readonly ICollectionView _filteredView;

        public ObservableCollection<PersistenceItem> Items { get; } = new();

        public ICollectionView FilteredItems => _filteredView;

        #region Observables & Filter State

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _scanStatusText = "Taramaya hazır.";

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedCategory = "All";

        [ObservableProperty]
        private bool _hideMicrosoftEntries = true;

        [ObservableProperty]
        private bool _showOnlyUnsigned;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _unsignedCount;

        [ObservableProperty]
        private int _verifiedCount;

        [ObservableProperty]
        private int _disabledCount;

        [ObservableProperty]
        private bool _isApiKeyDialogOpen;

        [ObservableProperty]
        private string _apiKeyInput = string.Empty;

        [ObservableProperty]
        private string _apiKeyStatusText = string.Empty;

        [ObservableProperty]
        private bool _isVirusTotalScanning;

        [ObservableProperty]
        private string _virusTotalScanProgress = string.Empty;

        public bool HasVirusTotalApiKey => _virusTotalService.HasApiKey;

        public bool IsAllCategory => SelectedCategory == "All";
        public bool IsRegistryCategory => SelectedCategory == "Registry";
        public bool IsStartupCategory => SelectedCategory == "Startup";
        public bool IsTasksCategory => SelectedCategory == "Tasks";
        public bool IsServicesCategory => SelectedCategory == "Services";
        public bool IsWmiCategory => SelectedCategory == "Wmi";
        public bool IsShellCategory => SelectedCategory == "Shell";

        #endregion

        public AutorunsViewModel(IAutorunsScannerEngine scannerEngine, IVirusTotalCheckService virusTotalService)
        {
            _scannerEngine = scannerEngine;
            _virusTotalService = virusTotalService;

            ApiKeyInput = _virusTotalService.ApiKey;
            ApiKeyStatusText = _virusTotalService.HasApiKey ? "Kayıtlı ve Kullanıma Hazır ✓" : "API Anahtarı Tanımlanmadı";

            _filteredView = CollectionViewSource.GetDefaultView(Items);
            _filteredView.Filter = FilterItem;

            // Start initial scan
            _ = ScanAsync();
        }

        #region Filter Logic & Reactivity

        partial void OnSearchTextChanged(string value) => _filteredView.Refresh();

        partial void OnHideMicrosoftEntriesChanged(bool value)
        {
            _filteredView.Refresh();
            UpdateStats();
        }

        partial void OnShowOnlyUnsignedChanged(bool value)
        {
            _filteredView.Refresh();
            UpdateStats();
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            OnPropertyChanged(nameof(IsAllCategory));
            OnPropertyChanged(nameof(IsRegistryCategory));
            OnPropertyChanged(nameof(IsStartupCategory));
            OnPropertyChanged(nameof(IsTasksCategory));
            OnPropertyChanged(nameof(IsServicesCategory));
            OnPropertyChanged(nameof(IsWmiCategory));
            OnPropertyChanged(nameof(IsShellCategory));
            _filteredView.Refresh();
            UpdateStats();
        }

        [RelayCommand]
        public void SetCategoryFilter(string category)
        {
            SelectedCategory = category;
        }

        private bool FilterItem(object obj)
        {
            if (obj is not PersistenceItem item) return false;

            // 1. Hide Microsoft Entries Filter
            if (HideMicrosoftEntries && item.IsMicrosoft)
            {
                return false;
            }

            // 2. Show Only Unsigned Filter
            if (ShowOnlyUnsigned && item.Signature == SignatureStatus.Verified)
            {
                return false;
            }

            // 3. Category Filter
            if (SelectedCategory != "All")
            {
                bool categoryMatch = SelectedCategory switch
                {
                    "Registry" => item.Category == PersistenceCategory.RegistryRun || item.Category == PersistenceCategory.WinlogonIfeo,
                    "Startup" => item.Category == PersistenceCategory.StartupFolder,
                    "Tasks" => item.Category == PersistenceCategory.ScheduledTask,
                    "Services" => item.Category == PersistenceCategory.WindowsService,
                    "Wmi" => item.Category == PersistenceCategory.WmiEventConsumer,
                    "Shell" => item.Category == PersistenceCategory.ShellExtension,
                    _ => true
                };
                if (!categoryMatch) return false;
            }

            // 4. Text Search
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string q = SearchText.Trim();
                bool nameMatch = item.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
                bool pathMatch = item.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase);
                bool pubMatch = item.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase);
                bool hashMatch = item.Sha256Hash.Contains(q, StringComparison.OrdinalIgnoreCase);

                return nameMatch || pathMatch || pubMatch || hashMatch;
            }

            return true;
        }

        private void UpdateStats()
        {
            var visible = Items.Where(FilterItem).ToList();
            TotalCount = visible.Count;
            UnsignedCount = visible.Count(i => i.Signature != SignatureStatus.Verified);
            VerifiedCount = visible.Count(i => i.Signature == SignatureStatus.Verified);
            DisabledCount = visible.Count(i => !i.IsEnabled);
        }

        #endregion

        #region Scanning Command

        [RelayCommand]
        public async Task ScanAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            ScanStatusText = "Kalıcılık noktaları taranıyor...";
            Items.Clear();

            var progress = new Progress<string>(status =>
            {
                ScanStatusText = status;
            });

            try
            {
                await foreach (var item in _scannerEngine.ScanAllAsync(progress))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Items.Add(item);
                    });
                }

                ScanStatusText = $"Tarama tamamlandı. {Items.Count} kalıcılık noktası listelendi.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Tarama hatası: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                UpdateStats();
            }
        }

        #endregion

        #region Item Actions & Safe Toggle

        [RelayCommand]
        public async Task ToggleItemAsync(PersistenceItem? item)
        {
            if (item == null) return;

            bool targetState = item.IsEnabled;
            item.IsBusy = true;

            try
            {
                bool success = await _scannerEngine.ToggleItemAsync(item, targetState);
                if (!success)
                {
                    // Revert state on failure
                    item.IsEnabled = !targetState;
                    MessageBox.Show(
                        $"{item.Name} durumu değiştirilemedi. Lütfen uygulamayı Yönetici Olarak çalıştırdığınızdan emin olun.",
                        "Yetki Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    UpdateStats();
                }
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task DeleteItemAsync(PersistenceItem? item)
        {
            if (item == null) return;

            var confirm = MessageBox.Show(
                $"Kalıcı Olarak Silinsin mi?\n\n" +
                $"Girdi: {item.Name}\n" +
                $"Kaynak: {item.LocationSource}\n" +
                $"Dosya: {item.FilePath}\n\n" +
                "Bu işlem ilgili başlangıç kaydını ve dosyasını sistemden kalıcı olarak silecektir. Devam etmek istiyor musunuz?",
                "Kalıcılık Girişini Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            item.IsBusy = true;
            try
            {
                bool success = await _scannerEngine.DeleteItemAsync(item);
                if (success)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Items.Remove(item);
                    });
                    UpdateStats();
                    MessageBox.Show($"{item.Name} başarıyla silindi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Silme işlemi gerçekleştirilemedi. Dosya kullanımda olabilir veya Yönetici yetkisi gereklidir.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        [RelayCommand]
        public void OpenVirusTotalWeb(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Sha256Hash))
            {
                MessageBox.Show("Dosyanın SHA-256 hash'i bulunamadı veya dosya mevcut değil.", "VirusTotal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _virusTotalService.OpenInBrowser(item.Sha256Hash);
        }

        [RelayCommand]
        public void OpenApiKeyDialog()
        {
            ApiKeyInput = _virusTotalService.ApiKey;
            ApiKeyStatusText = _virusTotalService.HasApiKey ? "Kayıtlı ve Kullanıma Hazır ✓" : "Henüz bir anahtar kaydedilmedi.";
            IsApiKeyDialogOpen = true;
        }

        [RelayCommand]
        public void CloseApiKeyDialog()
        {
            IsApiKeyDialogOpen = false;
        }

        [RelayCommand]
        public async Task SaveApiKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                ApiKeyStatusText = "Lütfen bir API anahtarı girin.";
                return;
            }

            ApiKeyStatusText = "Doğrulanıyor...";
            bool isValid = await _virusTotalService.ValidateApiKeyAsync(ApiKeyInput);
            if (isValid)
            {
                _virusTotalService.SaveApiKey(ApiKeyInput);
                OnPropertyChanged(nameof(HasVirusTotalApiKey));
                ApiKeyStatusText = "Doğrulandı ve Kaydedildi! ✓";
                await Task.Delay(800);
                IsApiKeyDialogOpen = false;
            }
            else
            {
                ApiKeyStatusText = "Geçersiz API Anahtarı! Lütfen kontrol edin. ✗";
            }
        }

        [RelayCommand]
        public async Task ScanItemWithVirusTotalAsync(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Sha256Hash))
            {
                MessageBox.Show("Dosyanın SHA-256 hash'i bulunamadı veya dosya mevcut değil.", "VirusTotal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!_virusTotalService.HasApiKey)
            {
                OpenApiKeyDialog();
                return;
            }

            item.IsBusy = true;
            try
            {
                var (malicious, total, msg) = await _virusTotalService.CheckHashAsync(item.Sha256Hash);
                item.VirusTotalScore = msg;
                item.VirusTotalPositives = malicious;
            }
            catch (Exception ex)
            {
                item.VirusTotalScore = $"Hata: {ex.Message}";
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ScanAllVisibleWithVirusTotalAsync()
        {
            if (!_virusTotalService.HasApiKey)
            {
                OpenApiKeyDialog();
                return;
            }

            if (IsVirusTotalScanning) return;

            var itemsToScan = _filteredView.Cast<PersistenceItem>()
                .Where(x => !string.IsNullOrWhiteSpace(x.Sha256Hash) && (string.IsNullOrWhiteSpace(x.VirusTotalScore) || x.VirusTotalScore == "Taranmadı"))
                .ToList();

            if (itemsToScan.Count == 0)
            {
                MessageBox.Show("Taranacak uygun veya yeni bir girdi bulunamadı.", "VirusTotal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsVirusTotalScanning = true;
            int scanned = 0;

            try
            {
                foreach (var item in itemsToScan)
                {
                    scanned++;
                    VirusTotalScanProgress = $"VT Taranıyor ({scanned}/{itemsToScan.Count}): {item.Name}";
                    item.IsBusy = true;

                    try
                    {
                        var (malicious, total, msg) = await _virusTotalService.CheckHashAsync(item.Sha256Hash);
                        item.VirusTotalScore = msg;
                        item.VirusTotalPositives = malicious;
                    }
                    catch (Exception ex)
                    {
                        item.VirusTotalScore = $"Hata: {ex.Message}";
                    }
                    finally
                    {
                        item.IsBusy = false;
                    }

                    await Task.Delay(400);
                }

                VirusTotalScanProgress = $"Tarama tamamlandı! ({scanned} dosya kontrol edildi)";
                await Task.Delay(3000);
                VirusTotalScanProgress = string.Empty;
            }
            finally
            {
                IsVirusTotalScanning = false;
            }
        }

        [RelayCommand]
        public void OpenFileLocation(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            try
            {
                if (File.Exists(item.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
                else
                {
                    string dir = Path.GetDirectoryName(item.FilePath) ?? string.Empty;
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                    else
                    {
                        MessageBox.Show("Dosya veya dizin mevcut değil.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya konumu açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CopySha256(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Sha256Hash)) return;

            try
            {
                Clipboard.SetText(item.Sha256Hash);
                MessageBox.Show($"SHA-256 Panoya Kopyalandı:\n{item.Sha256Hash}", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        [RelayCommand]
        public void CopyFilePath(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            try
            {
                Clipboard.SetText(item.FilePath);
            }
            catch { }
        }

        [RelayCommand]
        public void ExportReportCsv()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Bakim_Autoruns_Report_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                    Filter = "CSV Dosyası (*.csv)|*.csv|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = ".csv"
                };

                if (sfd.ShowDialog() == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Ad,Kategori,Konum,Dosya Yolu,Argümanlar,Yayıncı,Dijital İmza,İmzalayan,SHA256,Aktif");

                    foreach (var item in Items)
                    {
                        sb.AppendLine($"\"{EscapeCsv(item.Name)}\",\"{EscapeCsv(item.CategoryDisplayName)}\",\"{EscapeCsv(item.LocationSource)}\",\"{EscapeCsv(item.FilePath)}\",\"{EscapeCsv(item.Arguments)}\",\"{EscapeCsv(item.Publisher)}\",\"{item.Signature}\",\"{EscapeCsv(item.SignatureSignerName)}\",\"{item.Sha256Hash}\",\"{item.IsEnabled}\"");
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show($"Kalıcılık raporu başarıyla dışa aktarıldı:\n{sfd.FileName}", "Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Rapor kaydedilemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeCsv(string val) => val.Replace("\"", "\"\"");

        #endregion
    }
}
