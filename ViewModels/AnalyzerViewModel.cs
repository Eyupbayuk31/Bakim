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
using Bakım.Core.History;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class AnalyzerViewModel : ObservableObject
    {
        private readonly IAutorunsScannerEngine _scannerEngine;
        private readonly IVirusTotalCheckService _virusTotalService;
        private readonly IFileThreatAnalyzerService _threatAnalyzerService;
        private readonly Bakım.Services.History.IAnalysisHistoryService _history;
        private readonly ICollectionView _filteredView;

        /// <summary>Geçmiş ve Değişiklikler sekmeleri (Analizör Geçmişi, §6).</summary>
        public AnalyzerHistoryViewModel History { get; }

        /// <summary>Etkin sekme: Scan, History, Changes.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsScanTab), nameof(IsHistoryTab), nameof(IsChangesTab))]
        private string _activeTab = "Scan";

        public bool IsScanTab => ActiveTab == "Scan";
        public bool IsHistoryTab => ActiveTab == "History";
        public bool IsChangesTab => ActiveTab == "Changes";

        [RelayCommand]
        public void SwitchTab(string tab)
        {
            ActiveTab = tab;
            if (tab != "Scan") History.Refresh();
        }

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

        /// <summary>Son 7 günde eklenmiş kalıcılık girdisi sayısı (zaman çizelgesi sinyali).</summary>
        [ObservableProperty]
        private int _recentlyAddedCount;

        /// <summary>Risk skoru 60 ve üzeri girdi sayısı.</summary>
        [ObservableProperty]
        private int _highRiskCount;

        /// <summary>Filtre sonucu boş ve tarama sürmüyor: boş durum yüzeyi gösterilir.</summary>
        [ObservableProperty]
        private bool _hasNoResults;

        /// <summary>Toplu derin analiz ilerlemesi.</summary>
        [ObservableProperty]
        private bool _isDeepAnalyzing;

        [ObservableProperty]
        private string _deepAnalysisProgress = string.Empty;

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

        public AnalyzerViewModel(
            IAutorunsScannerEngine scannerEngine,
            IVirusTotalCheckService virusTotalService,
            IFileThreatAnalyzerService threatAnalyzerService,
            Bakım.Services.History.IAnalysisHistoryService history,
            AnalyzerHistoryViewModel historyViewModel)
        {
            _scannerEngine = scannerEngine;
            _virusTotalService = virusTotalService;
            _threatAnalyzerService = threatAnalyzerService;
            _history = history;
            History = historyViewModel;
            _ = History.InitializeAsync();

            ApiKeyInput = _virusTotalService.ApiKey;
            ApiKeyStatusText = _virusTotalService.HasApiKey ? "Kayıtlı ve Kullanıma Hazır" : "API Anahtarı Tanımlanmadı";

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

            // Zaman çizelgesi sinyali: son 7 günde eklenen kalıcılık girdileri.
            // "Dün ne değişti?" sorusunun cevabı bulaşma tespitinde en hızlı yoldur.
            RecentlyAddedCount = visible.Count(i => i.IsRecentlyAdded);
            HighRiskCount = visible.Count(i => i.RiskScore >= 60);

            HasNoResults = visible.Count == 0 && !IsScanning;
        }

        /// <summary>
        /// Girdilerin dosya zaman damgalarını doldurur. Tarama motoruna değil
        /// buraya konuldu: motor 36 KB'lık kritik bir dosya ve bu bilgi
        /// yalnızca arayüzün zaman çizelgesi için gerekli.
        /// </summary>
        private static void PopulateTimestamps(IEnumerable<PersistenceItem> items)
        {
            foreach (var item in items)
            {
                if (item.FileCreatedUtc.HasValue) continue;
                if (string.IsNullOrWhiteSpace(item.FilePath)) continue;

                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        item.FileCreatedUtc = File.GetCreationTimeUtc(item.FilePath);
                    }
                }
                catch
                {
                    // Erişim reddi veya bozuk yol: zaman çizelgesi bu girdi için boş kalır
                }
            }
        }

        /// <summary>Listeyi risk skoruna göre sıralar: en tehlikeli en üstte.</summary>
        private void ApplyRiskSorting()
        {
            var ordered = Items
                .OrderByDescending(i => i.RiskScore)
                .ThenByDescending(i => i.IsRecentlyAdded)
                .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Items.Clear();
            foreach (var item in ordered) Items.Add(item);
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

                PopulateTimestamps(Items);
                ApplyRiskSorting();

                int risky = Items.Count(i => i.RiskScore >= 60);
                ScanStatusText = risky > 0
                    ? $"Tarama tamamlandı. {Items.Count} kalıcılık noktası — {risky} tanesi yüksek riskli, listenin en üstünde."
                    : $"Tarama tamamlandı. {Items.Count} kalıcılık noktası listelendi, yüksek riskli girdi yok.";

                await SaveScanSnapshotAsync();
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

        /// <summary>
        /// Taramanın anlık görüntüsünü geçmişe yazar ve bir önceki taramayla farkı hesaplar
        /// (Değişiklikler sekmesi). Yeni girdi varsa durum satırında belirtilir.
        /// </summary>
        private async Task SaveScanSnapshotAsync()
        {
            try
            {
                await _history.SaveSnapshotAsync(Items.ToList(), "Manual");
                History.RefreshSnapshots();
                if (History.AddedCount > 0 || History.ChangedCount > 0)
                {
                    ScanStatusText += $" Önceki taramaya göre {History.AddedCount} yeni, {History.ChangedCount} değişen girdi var (Değişiklikler sekmesi).";
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Kalıcılık anlık görüntüsü kaydedilemedi.", ex, nameof(AnalyzerViewModel));
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
                    string reason = item.Category == PersistenceCategory.RegistryRun
                                    && item.RegistryKeyPath != null
                                    && !item.RegistryKeyPath.EndsWith(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase)
                        ? "Bu tür girdiler (RunOnce / politika anahtarı) Windows'ta devre dışı bırakılamaz; gerekiyorsa silebilirsiniz."
                        : "Yönetici izni gerekebilir ya da girdi artık yok (listeyi yenileyin).";
                    MessageBox.Show(
                        $"{item.Name} durumu değiştirilemedi.\n\n{reason}",
                        "Değiştirilemedi",
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
                $"Başlangıç girdisi kaldırılsın mı?\n\n" +
                $"Girdi: {item.Name}\n" +
                $"Kaynak: {item.LocationSource}\n\n" +
                "Yalnızca başlangıç kaydı kaldırılır; programın kendisine dokunulmaz. Kayıt defteri girdisi önce " +
                "yedeklenir, başlangıç klasörü kısayolu Geri Dönüşüm Kutusu'na gider. Devam edilsin mi?",
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
                    MessageBox.Show("Girdi kaldırılamadı. Yönetici izni gerekebilir ya da girdi artık yok (listeyi yenileyin).", "Kaldırılamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            int recordStatus = item.TotalScanners; // 0 means 404 not found, > 0 means record found, -1 means unscanned
            _virusTotalService.SmartOpenInBrowser(item.FilePath, item.Sha256Hash, recordStatus);
        }

        [RelayCommand]
        public void OpenApiKeyDialog()
        {
            ApiKeyInput = _virusTotalService.ApiKey;
            ApiKeyStatusText = _virusTotalService.HasApiKey ? "Kayıtlı ve Kullanıma Hazır" : "Henüz bir anahtar kaydedilmedi.";
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
                ApiKeyStatusText = "Doğrulandı ve Kaydedildi!";
                await Task.Delay(800);
                IsApiKeyDialogOpen = false;
            }
            else
            {
                ApiKeyStatusText = "Geçersiz API Anahtarı! Lütfen kontrol edin.";
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
                    FileName = $"Bakim_Analizor_Raporu_{DateTime.Now:yyyyMMdd_HHmm}.csv",
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

        [RelayCommand]
        public async Task AnalyzeThreatAsync(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            ScanStatusText = $"{item.Name} için sezgisel AI tehdit analizi yürütülüyor...";
            IsScanning = true;

            try
            {
                ThreatAnalysisResult analysisResult;
                using (AnalysisContext.Begin(AnalysisSource.Analyzer, item.LocationSource))
                {
                    analysisResult = await _threatAnalyzerService.AnalyzeFileAsync(item.FilePath, item.Arguments, item);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, _threatAnalyzerService, _scannerEngine);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analiz sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
                ScanStatusText = "Analiz tamamlandı.";
            }
        }

        /// <summary>
        /// Görünen tüm girdileri PE + imza motorundan geçirir ve risk skorlarını
        /// tazeler. Tek tek "Dosya İncele" yapmak yerine listenin tamamını
        /// bir seferde derinlemesine değerlendirir.
        ///
        /// İşlem arka plan thread'inde yürür; arayüz donmaz ve kullanıcı
        /// istediği an başka sekmeye geçebilir.
        /// </summary>
        [RelayCommand]
        public async Task DeepAnalyzeAllAsync()
        {
            if (IsDeepAnalyzing || IsScanning) return;

            var targets = Items.Where(FilterItem)
                               .Where(i => i.HasValidFile)
                               .ToList();

            if (targets.Count == 0)
            {
                ScanStatusText = "Derin analiz için geçerli dosya yolu olan girdi bulunamadı.";
                return;
            }

            IsDeepAnalyzing = true;
            int analyzed = 0, elevated = 0;
            using var analysisContext = AnalysisContext.Begin(AnalysisSource.Analyzer, "Toplu derin analiz");

            try
            {
                foreach (var item in targets)
                {
                    DeepAnalysisProgress = $"Derin analiz: {analyzed + 1} / {targets.Count} — {item.Name}";

                    try
                    {
                        var report = await _threatAnalyzerService.AnalyzeFileAsync(
                            item.FilePath, item.Arguments, item);

                        // İmza sonucunu girdiye geri yaz: liste artık katalog
                        // imzalarını da doğru gösterir.
                        item.Signature = report.IsSigned
                            ? SignatureStatus.Verified
                            : (report.DigitalSignatureText.Contains("GEÇERSİZ", StringComparison.OrdinalIgnoreCase)
                                ? SignatureStatus.InvalidOrTampered
                                : SignatureStatus.Unsigned);

                        if (!string.IsNullOrWhiteSpace(report.SignerName))
                            item.SignatureSignerName = report.SignerName;

                        if (!string.IsNullOrWhiteSpace(report.Sha256))
                            item.Sha256Hash = report.Sha256;

                        if (report.RiskScore >= 60) elevated++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning($"Derin analiz başarısız: {item.FilePath}", ex, nameof(AnalyzerViewModel));
                    }

                    analyzed++;
                }

                ApplyRiskSorting();
                UpdateStats();

                ScanStatusText = elevated > 0
                    ? $"Derin analiz tamamlandı: {analyzed} girdi incelendi, {elevated} tanesi yüksek riskli çıktı."
                    : $"Derin analiz tamamlandı: {analyzed} girdi incelendi, yüksek riskli girdi saptanmadı.";
            }
            finally
            {
                IsDeepAnalyzing = false;
                DeepAnalysisProgress = string.Empty;
            }
        }

        [RelayCommand]
        public async Task PickAndAnalyzeFileAsync()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sezgisel AI Tehdit Analizi için Dosya Seçin",
                Filter = "Yürütülebilir ve Sistem Dosyaları (*.exe;*.dll;*.sys;*.bat;*.cmd;*.ps1;*.vbs;*.scr)|*.exe;*.dll;*.sys;*.bat;*.cmd;*.ps1;*.vbs;*.scr|Tüm Dosyalar (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true) return;

            string selectedFile = ofd.FileName;
            ScanStatusText = $"{Path.GetFileName(selectedFile)} analiz ediliyor...";
            IsScanning = true;

            try
            {
                ThreatAnalysisResult analysisResult;
                using (AnalysisContext.Begin(AnalysisSource.Analyzer, "Dosya İncele"))
                {
                    analysisResult = await _threatAnalyzerService.AnalyzeFileAsync(selectedFile);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, _threatAnalyzerService, _scannerEngine);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Analiz sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
                ScanStatusText = "Analiz tamamlandı.";
            }
        }

        [RelayCommand]
        public async Task AnalyzeSpecificFilesAsync(System.Collections.Generic.IEnumerable<string> filePaths)
        {
            if (filePaths == null) return;
            var validFiles = filePaths.Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Distinct().ToList();
            if (validFiles.Count == 0) return;

            // Çağıran (ör. Kurulum Nöbetçisi) kaynak belirttiyse o korunur.
            using var analysisContext = AnalysisContext.Source == AnalysisSource.Unknown
                ? AnalysisContext.Begin(AnalysisSource.Analyzer)
                : null;

            // Tek dosya ise doğrudan derin AI tehdit analizi penceresini aç
            if (validFiles.Count == 1)
            {
                string singleFile = validFiles[0];
                ScanStatusText = $"{Path.GetFileName(singleFile)} için tehdit analizi yürütülüyor...";
                try
                {
                    var analysisResult = await _threatAnalyzerService.AnalyzeFileAsync(singleFile);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, _threatAnalyzerService, _scannerEngine, _virusTotalService);
                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                        {
                            dialog.Owner = Application.Current.MainWindow;
                        }
                        dialog.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Analiz sırasında hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            // Çoklu dosya: Analizör listesine ekle ve toplu derin analiz yürüt
            ScanStatusText = $"{validFiles.Count} adet kurulum dosyası Analizör'e aktarılıyor...";
            IsScanning = true;

            try
            {
                var newItems = new System.Collections.Generic.List<PersistenceItem>();

                foreach (var file in validFiles)
                {
                    var item = new PersistenceItem
                    {
                        Name = Path.GetFileName(file),
                        FilePath = file,
                        LocationSource = "Kurulum Nöbetçisi",
                        Category = PersistenceCategory.StartupFolder,
                        CategoryDisplayName = "Kurulum Dosyası",
                        IsEnabled = true,
                        FileCreatedUtc = File.GetCreationTimeUtc(file)
                    };

                    try
                    {
                        var report = await _threatAnalyzerService.AnalyzeFileAsync(file, string.Empty, item);
                        item.Signature = report.IsSigned
                            ? SignatureStatus.Verified
                            : (report.DigitalSignatureText.Contains("GEÇERSİZ", StringComparison.OrdinalIgnoreCase)
                                ? SignatureStatus.InvalidOrTampered
                                : SignatureStatus.Unsigned);

                        if (!string.IsNullOrWhiteSpace(report.SignerName))
                            item.SignatureSignerName = report.SignerName;

                        if (!string.IsNullOrWhiteSpace(report.Sha256))
                            item.Sha256Hash = report.Sha256;
                    }
                    catch { }

                    newItems.Add(item);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in newItems)
                    {
                        Items.Insert(0, item);
                    }
                    ApplyRiskSorting();
                    UpdateStats();
                });

                ScanStatusText = $"{validFiles.Count} adet kurulum dosyası başarıyla incelendi ve listeye eklendi.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Kurulum dosyaları incelenirken hata: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                UpdateStats();
            }
        }

        #endregion
    }
}
