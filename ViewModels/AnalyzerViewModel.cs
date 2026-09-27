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
    public partial class AnalyzerViewModel : ObservableObject, IModuleViewModel
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
        public bool IsDriversCategory => SelectedCategory == "Drivers";
        public bool IsFilesCategory => SelectedCategory == "Files";
        public bool IsHooksCategory => SelectedCategory == "Hooks";
        public bool IsBrowserCategory => SelectedCategory == "Browser";
        public bool IsOfficeCategory => SelectedCategory == "Office";

        /// <summary>Son başarılı taramanın zamanı; sayfa açılışında eskiyse yeniden taranır (ANL Z8).</summary>
        private DateTime _lastScanUtc = DateTime.MinValue;
        private static readonly TimeSpan RescanAfter = TimeSpan.FromMinutes(10);

        #endregion

        private readonly Bakım.Services.Activity.IActivityService _activity;

        public AnalyzerViewModel(
            IAutorunsScannerEngine scannerEngine,
            IVirusTotalCheckService virusTotalService,
            IFileThreatAnalyzerService threatAnalyzerService,
            Bakım.Services.History.IAnalysisHistoryService history,
            AnalyzerHistoryViewModel historyViewModel,
            Bakım.Services.Activity.IActivityService activity)
        {
            _activity = activity;
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

            _liveWatchEnabled = App.TryGetService<IAppSettingsService>()?.Current.AnalyzerLiveWatch ?? true;
            if (App.TryGetService<Bakım.Services.Analyzer.IPersistenceWatchService>() is { } watch)
                watch.PersistenceAdded += OnPersistenceAdded;
        }

        /// <summary>Canlı kalıcılık izleme açık mı (ANL Faz 4)? Ayar kalıcıdır.</summary>
        [ObservableProperty]
        private bool _liveWatchEnabled;

        partial void OnLiveWatchEnabledChanged(bool value)
        {
            App.TryGetService<IAppSettingsService>()?.Update(s => s.AnalyzerLiveWatch = value);
            var watch = App.TryGetService<Bakım.Services.Analyzer.IPersistenceWatchService>();
            if (value) watch?.Start();
            else watch?.Stop();
        }

        /// <summary>Canlı izleme yeni girdi buldu: liste yüklüyse en üste eklenir.</summary>
        private void OnPersistenceAdded(PersistenceItem item)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_lastScanUtc == DateTime.MinValue) return; // liste henüz taranmadı; ilk tarama zaten gösterecek
                Items.Insert(0, item);
                ApplyRiskSorting();
                UpdateStats();
                ScanStatusText = $"Yeni kalıcılık girdisi eklendi: {item.Name} ({item.RiskLabel}).";
            });
        }

        /// <summary>
        /// Nöbetçi raporlarından "bu girdiyi hangi kurulum ekledi?" bilgisini satırlara yazar (ANL Faz 4).
        /// </summary>
        private async Task ApplyProvenanceAsync()
        {
            var sentinel = App.TryGetService<ISetupSentinelService>();
            if (sentinel == null) return;
            try
            {
                var reports = (await sentinel.LoadSavedReportsAsync()).Concat(sentinel.RecentReports)
                    .GroupBy(r => r.SessionId).Select(g => g.First());
                var index = Bakım.Core.Analyzer.ProvenanceIndex.Build(reports.Select(Bakım.Services.Analyzer.ProvenanceFromReports.ToTrail));
                if (index.Count == 0) return;
                foreach (var item in Items)
                {
                    if (index.Find(Bakım.Core.Analyzer.ProvenanceIndex.CandidateKeys(item.RegistryKeyPath, item.RegistryValueName, item.ServiceName, item.TaskPath)) is { } hit)
                        item.Provenance = Bakım.Services.Analyzer.ProvenanceFromReports.Describe(hit);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Warning("Nöbetçi raporları Analizör'e bağlanamadı.", ex, nameof(AnalyzerViewModel));
            }
        }

        /// <summary>
        /// Sayfa açıldı: ilk açılışta ya da liste 10 dakikadan eskiyse tarar. Eskiden tarama
        /// kurucuda (UI iş parçacığında) başlıyor ve bir daha tazelenmiyordu (AN-12, AN-21).
        /// </summary>
        public Task OnActivatedAsync()
        {
            if (!IsScanning && DateTime.UtcNow - _lastScanUtc > RescanAfter) _ = ScanAsync();
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync() => Task.CompletedTask;

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
            OnPropertyChanged(nameof(IsDriversCategory));
            OnPropertyChanged(nameof(IsFilesCategory));
            OnPropertyChanged(nameof(IsHooksCategory));
            OnPropertyChanged(nameof(IsBrowserCategory));
            OnPropertyChanged(nameof(IsOfficeCategory));
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
                    "Drivers" => item.Category == PersistenceCategory.Driver,
                    "Files" => item.Category == PersistenceCategory.InspectedFile,
                    "Hooks" => item.Category == PersistenceCategory.SystemHook,
                    "Browser" => item.Category == PersistenceCategory.BrowserExtension,
                    "Office" => item.Category == PersistenceCategory.OfficeAddin,
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
            HighRiskCount = visible.Count(i => i.RiskScore >= Bakım.Core.Risk.RiskScale.Suspicious);

            HasNoResults = visible.Count == 0 && !IsScanning;
        }

        /// <summary>
        /// Analizör Geçmişi'ndeki "Güvenilir" kararlarını satırlara uygular (ANL Faz 2): güvenilen hash'in
        /// puanı tabana iner; VirusTotal uzlaşısı bu kararı geçersiz kılar.
        /// </summary>
        private void ApplyTrustDecisions()
        {
            foreach (var item in Items)
            {
                bool trusted = !string.IsNullOrWhiteSpace(item.Sha256Hash) && _history.IsTrustedHash(item.Sha256Hash);
                if (trusted == item.IsUserTrusted) continue;
                item.IsUserTrusted = trusted;
                item.NotifyRiskChanged();
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
            ScanStatusText = "Kalıcılık noktaları taranıyor…";
            // Nöbetçi'den gelen incelenen dosyalar yeni taramada korunur.
            var inspected = Items.Where(i => i.Category == PersistenceCategory.InspectedFile).ToList();
            Items.Clear();

            var progress = new Progress<string>(status => ScanStatusText = status);

            try
            {
                // Tüm tarama arka planda; sonuçlar 50'lik gruplar halinde eklenir (AN-12). Eskiden her
                // girdi için ayrı Dispatcher.Invoke yapılıyor, toplayıcıların bir kısmı UI iş parçacığında koşuyordu.
                var batch = new System.Collections.Generic.List<PersistenceItem>(50);
                await Task.Run(async () =>
                {
                    await foreach (var item in _scannerEngine.ScanAllAsync(progress))
                    {
                        batch.Add(item);
                        if (batch.Count < 50) continue;
                        var chunk = batch.ToArray();
                        batch.Clear();
                        await Application.Current.Dispatcher.InvokeAsync(() => { foreach (var i in chunk) Items.Add(i); });
                    }
                });
                foreach (var item in batch.Concat(inspected)) Items.Add(item);

                ApplyTrustDecisions();
                await ApplyProvenanceAsync();
                ApplyRiskSorting();
                _lastScanUtc = DateTime.UtcNow;

                int risky = Items.Count(i => i.RiskScore >= Bakım.Core.Risk.RiskScale.Suspicious);
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
            if (!item.CanToggle)
            {
                // Arayüz anahtarı göstermez; yine de çağrılırsa (komut paleti, eski bağlama) durum geri alınır.
                item.IsEnabled = !targetState;
                ScanStatusText = item.ToggleLockReason;
                return;
            }
            item.IsBusy = true;

            try
            {
                bool success;
                using (var capture = Bakım.Helpers.RegistryCapture.Begin())
                {
                    success = await _scannerEngine.ToggleItemAsync(item, targetState);
                    Bakım.Services.Activity.ActivityRecording.RecordRegistryChange(_activity,
                        Bakım.Core.Activity.ActivityKind.StartupChange, "Analizör",
                        $"'{item.Name}' {(targetState ? "etkinleştirildi" : "devre dışı bırakıldı")}",
                        item.LocationSource,
                        success ? Bakım.Core.Activity.ActivityOutcome.Succeeded : Bakım.Core.Activity.ActivityOutcome.Failed,
                        capture.Items, deepLink: "Analyzer", handler: Bakım.Core.Activity.UndoHandlers.StartupApproved);
                }
                if (!success)
                {
                    // Revert state on failure
                    item.IsEnabled = !targetState;
                    string reason = "Yönetici izni verilmemiş olabilir ya da girdi artık yok (listeyi yenileyin).";
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
            if (!item.CanDelete)
            {
                ScanStatusText = item.DeleteLockReason;
                return;
            }

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
                bool success;
                using (var capture = Bakım.Helpers.RegistryCapture.Begin())
                {
                    success = await _scannerEngine.DeleteItemAsync(item);
                    var outcome = success ? Bakım.Core.Activity.ActivityOutcome.Succeeded : Bakım.Core.Activity.ActivityOutcome.Failed;
                    string title = $"'{item.Name}' kalıcılık girdisi silindi";
                    if (!string.IsNullOrEmpty(item.SourceFilePath))
                        Bakım.Services.Activity.ActivityRecording.RecordRecycled(_activity, Bakım.Core.Activity.ActivityKind.StartupChange, "Analizör",
                            title, "Kısayol Geri Dönüşüm Kutusu'na taşındı", outcome,
                            new[] { new Bakım.Core.Activity.ActivityItem(item.SourceFilePath, "Geri Dönüşüm Kutusu", success ? "Tamam" : "Başarısız") },
                            deepLink: "Analyzer");
                    else
                        Bakım.Services.Activity.ActivityRecording.RecordRegistryChange(_activity, Bakım.Core.Activity.ActivityKind.StartupChange, "Analizör",
                            title, item.LocationSource, outcome, capture.Items, deepLink: "Analyzer",
                            handler: Bakım.Core.Activity.UndoHandlers.StartupApproved);
                }
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

            // TotalScanners: 0 = VT'de kayıt yok (yükleme sayfası), > 0 = kayıt var, -1 = sorgulanmadı.
            _virusTotalService.SmartOpenInBrowser(item.FilePath, item.Sha256Hash, item.TotalScanners);
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
                await CheckWithVirusTotalAsync(item);
            }
            finally
            {
                item.IsBusy = false;
            }
            ApplyRiskSorting();
            UpdateStats();
        }

        /// <summary>VT sonucunu girdiye yazar ve risk puanını tazeler (TotalScanners artık doldurulur).</summary>
        private async Task CheckWithVirusTotalAsync(PersistenceItem item)
        {
            try
            {
                var (malicious, total, msg) = await _virusTotalService.CheckHashAsync(item.Sha256Hash);
                item.VirusTotalScore = msg;
                item.VirusTotalPositives = malicious;
                item.TotalScanners = total;
                item.VirusTotalSuspicious = _virusTotalService.LastSuspicious(item.Sha256Hash);
                item.NotifyRiskChanged();
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                item.VirusTotalScore = $"Hata: {ex.Message}";
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
                // Hız sınırı serviste (ücretsiz anahtar dakikada 4 istek); burada yalnızca kalan süre gösterilir.
                foreach (var item in itemsToScan)
                {
                    scanned++;
                    int pending = itemsToScan.Skip(scanned - 1).Count(i => !_virusTotalService.IsCached(i.Sha256Hash));
                    var eta = _virusTotalService.EstimateWait(pending);
                    VirusTotalScanProgress = $"VirusTotal ({scanned}/{itemsToScan.Count}): {item.Name}" +
                                             (eta.TotalSeconds >= 5 ? $" · yaklaşık {Math.Ceiling(eta.TotalMinutes)} dk kaldı" : "");
                    item.IsBusy = true;
                    try
                    {
                        await CheckWithVirusTotalAsync(item);
                    }
                    finally
                    {
                        item.IsBusy = false;
                    }
                }

                ApplyRiskSorting();
                UpdateStats();
                VirusTotalScanProgress = $"VirusTotal sorgusu tamamlandı ({scanned} dosya).";
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
                    sb.AppendLine("Ad,Kategori,Konum,Ham komut,Dosya Yolu,Argümanlar,Sarmalayıcı,Yayıncı,Dijital İmza,SHA256,Aktif,Risk Puanı,Risk,VirusTotal");

                    // Formül enjeksiyonu kaçışı (AN-16): "=", "+", "-", "@" ile başlayan alanlar metne çevrilir.
                    static string F(string? v) => Bakım.Core.Analyzer.CsvText.Field(v);
                    foreach (var item in Items)
                    {
                        sb.AppendLine(string.Join(",", F(item.Name), F(item.CategoryDisplayName), F(item.LocationSource), F(item.RawCommand),
                            F(item.FilePath), F(item.Arguments), F(item.HostName), F(item.Publisher), F(item.Signature.ToString()),
                            F(item.Sha256Hash), F(item.IsEnabled ? "Evet" : "Hayır"), F(item.RiskScore.ToString()), F(item.RiskLabel), F(item.VirusTotalScore)));
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

        [RelayCommand]
        public async Task AnalyzeThreatAsync(PersistenceItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            ScanStatusText = $"{item.Name} için sezgisel dosya analizi yürütülüyor…";
            IsScanning = true;

            try
            {
                ThreatAnalysisResult analysisResult;
                using (AnalysisContext.Begin(AnalysisSource.Analyzer, item.LocationSource))
                {
                    analysisResult = await _threatAnalyzerService.AnalyzeFileAsync(item.FilePath, item.Arguments, item);
                }
                ScanStatusText = "Windows Defender ile ikinci görüş alınıyor…";
                await _threatAnalyzerService.ApplyDefenderOpinionAsync(analysisResult);
                ApplyDeepResult(item, analysisResult);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, _threatAnalyzerService, _scannerEngine);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                    ApplyTrustDecisions();
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
                        ApplyDeepResult(item, report);
                        if (item.RiskScore >= Bakım.Core.Risk.RiskScale.Suspicious) elevated++;
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

        /// <summary>
        /// Derin analizin sonucunu satıra yazar: satır puanı artık derin puandır (AN-09). Eskiden liste
        /// yalnızca imzayı güncelliyor, pencere "yüksek risk" derken satır "düşük" kalabiliyordu.
        /// </summary>
        private static void ApplyDeepResult(PersistenceItem item, ThreatAnalysisResult report)
        {
            item.Signature = report.IsSigned
                ? SignatureStatus.Verified
                : report.DigitalSignatureText.Contains("Bozulmuş", StringComparison.OrdinalIgnoreCase)
                    ? SignatureStatus.InvalidOrTampered
                    : SignatureStatus.Unsigned;
            if (!string.IsNullOrWhiteSpace(report.SignerName)) item.SignatureSignerName = report.SignerName;
            if (!string.IsNullOrWhiteSpace(report.Sha256)) item.Sha256Hash = report.Sha256;
            if (report.VirusTotalTotal > 0)
            {
                item.VirusTotalPositives = report.VirusTotalMalicious;
                item.TotalScanners = report.VirusTotalTotal;
                item.VirusTotalScore = report.VirusTotalSummary;
            }
            item.DeepRiskScore = report.RiskScore;
            item.NotifyRiskChanged();
        }

        [RelayCommand]
        public async Task PickAndAnalyzeFileAsync()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sezgisel dosya analizi için dosya seçin",
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
                ScanStatusText = "Windows Defender ile ikinci görüş alınıyor…";
                await _threatAnalyzerService.ApplyDefenderOpinionAsync(analysisResult);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new Bakım.Views.Dialogs.ThreatAnalysisDialog(analysisResult, _threatAnalyzerService, _scannerEngine);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        dialog.Owner = Application.Current.MainWindow;
                    }
                    dialog.ShowDialog();
                    ApplyTrustDecisions();
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

            // Tek dosya ise doğrudan dosya analizi penceresini aç
            if (validFiles.Count == 1)
            {
                string singleFile = validFiles[0];
                ScanStatusText = $"{Path.GetFileName(singleFile)} için tehdit analizi yürütülüyor...";
                try
                {
                    var analysisResult = await _threatAnalyzerService.AnalyzeFileAsync(singleFile);
                    await _threatAnalyzerService.ApplyDefenderOpinionAsync(analysisResult);
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
                        LocationSource = AnalysisContext.Detail ?? "Kurulum Nöbetçisi",
                        // Başlangıç kaydı değil (AN-14): eskiden StartupFolder sayılıyor, "Başlangıç" süzgecini
                        // kirletiyor ve sil/kapat düğmeleri yanlış yolla çalışıyordu.
                        Category = PersistenceCategory.InspectedFile,
                        CategoryDisplayName = "İncelenen dosya",
                        IsEnabled = true,
                        FileCreatedUtc = File.GetCreationTimeUtc(file)
                    };

                    try
                    {
                        var report = await _threatAnalyzerService.AnalyzeFileAsync(file, string.Empty, item);
                        ApplyDeepResult(item, report);
                        item.Publisher = string.IsNullOrWhiteSpace(report.SignerName) ? "İmzasız" : report.SignerName;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        AppLog.Warning($"Dosya incelenemedi: {file}", ex, nameof(AnalyzerViewModel));
                    }

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
