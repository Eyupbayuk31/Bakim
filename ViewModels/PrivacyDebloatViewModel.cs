using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class PrivacyDebloatViewModel : ObservableObject
    {
        private readonly IPrivacyDebloatService _privacyService;

        public PrivacyDebloatViewModel(IPrivacyDebloatService privacyService)
        {
            _privacyService = privacyService;

            Tweaks = new ObservableCollection<PrivacyTweakItem>();
            BloatwareApps = new ObservableCollection<BloatwareAppItem>();

            _filteredTweaks = CollectionViewSource.GetDefaultView(Tweaks);
            _filteredTweaks.Filter = FilterTweakItem;

            _filteredBloatware = CollectionViewSource.GetDefaultView(BloatwareApps);
            _filteredBloatware.Filter = FilterBloatwareItem;

            _ = RefreshAllAsync();
        }

        private readonly ICollectionView _filteredTweaks;
        public ICollectionView FilteredTweaks => _filteredTweaks;

        private readonly ICollectionView _filteredBloatware;
        public ICollectionView FilteredBloatware => _filteredBloatware;

        public ObservableCollection<PrivacyTweakItem> Tweaks { get; }
        public ObservableCollection<BloatwareAppItem> BloatwareApps { get; }

        public event Action? TweaksStateChanged;

        [ObservableProperty]
        private string _activeSubTab = "Privacy"; // Privacy, Debloat

        public bool IsPrivacyTab => ActiveSubTab == "Privacy";
        public bool IsDebloatTab => ActiveSubTab == "Debloat";

        partial void OnActiveSubTabChanged(string value)
        {
            OnPropertyChanged(nameof(IsPrivacyTab));
            OnPropertyChanged(nameof(IsDebloatTab));
        }

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedCategory = "All";

        public bool IsFilterAll => SelectedCategory == "All";
        public bool IsFilterTelemetry => SelectedCategory == "Telemetri & Tanılama";
        public bool IsFilterAds => SelectedCategory == "Reklamlar & Öneriler";
        public bool IsFilterLocation => SelectedCategory == "Konum & İzinler";

        public int TelemetryCount => Tweaks.Count(t => t.Category == "Telemetri & Tanılama");
        public int AdsCount => Tweaks.Count(t => t.Category == "Reklamlar & Öneriler");
        public int LocationCount => Tweaks.Count(t => t.Category == "Konum & İzinler");

        [ObservableProperty]
        private PrivacyStats _stats = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Gizlilik ayarları yükleniyor...";

        partial void OnSearchTextChanged(string value)
        {
            _filteredTweaks.Refresh();
            _filteredBloatware.Refresh();
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            _filteredTweaks.Refresh();
            OnPropertyChanged(nameof(IsFilterAll));
            OnPropertyChanged(nameof(IsFilterTelemetry));
            OnPropertyChanged(nameof(IsFilterAds));
            OnPropertyChanged(nameof(IsFilterLocation));
        }

        private bool FilterTweakItem(object obj)
        {
            if (obj is not PrivacyTweakItem item) return false;

            // 1. Category Filter
            if (SelectedCategory != "All" && item.Category != SelectedCategory)
                return false;

            // 2. Search Text
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.Title.ToLowerInvariant().Contains(query) ||
                   item.Description.ToLowerInvariant().Contains(query) ||
                   item.Category.ToLowerInvariant().Contains(query) ||
                   item.RiskLevel.ToLowerInvariant().Contains(query);
        }

        private bool FilterBloatwareItem(object obj)
        {
            if (obj is not BloatwareAppItem item) return false;

            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.DisplayName.ToLowerInvariant().Contains(query) ||
                   item.PackageName.ToLowerInvariant().Contains(query) ||
                   item.Category.ToLowerInvariant().Contains(query);
        }

        [RelayCommand]
        public void SwitchSubTab(string tab)
        {
            ActiveSubTab = tab;
        }

        [RelayCommand]
        public void SetCategoryFilter(string category)
        {
            SelectedCategory = category;
        }

        [RelayCommand]
        public async Task RefreshAllAsync()
        {
            IsBusy = true;
            StatusMessage = "Sistem gizlilik ve telemetri durumu taranıyor...";
            try
            {
                var tweaksTask = _privacyService.GetPrivacyTweaksAsync();
                var appsTask = _privacyService.GetInstalledBloatwareAsync();

                await Task.WhenAll(tweaksTask, appsTask);

                var tweaks = await tweaksTask;
                var apps = await appsTask;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Tweaks.Clear();
                    foreach (var t in tweaks) Tweaks.Add(t);

                    BloatwareApps.Clear();
                    foreach (var a in apps) BloatwareApps.Add(a);

                    _filteredTweaks.Refresh();
                    _filteredBloatware.Refresh();
                });

                UpdateStats();
                StatusMessage = $"Gizlilik Skoru: %{Stats.ProtectionPercentage} - {Stats.ActiveTweaksCount} / {Stats.TotalTweaksCount} Kural Aktif";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Hata: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateStats()
        {
            Stats = new PrivacyStats
            {
                TotalTweaksCount = Tweaks.Count,
                ActiveTweaksCount = Tweaks.Count(t => t.IsEnabled),
                RecommendedActiveCount = Tweaks.Count(t => t.IsRecommended && t.IsEnabled),
                TotalBloatwareCount = BloatwareApps.Count,
                InstalledBloatwareCount = BloatwareApps.Count(a => a.IsInstalled && !a.IsEssential)
            };
            OnPropertyChanged(nameof(TelemetryCount));
            OnPropertyChanged(nameof(AdsCount));
            OnPropertyChanged(nameof(LocationCount));
            TweaksStateChanged?.Invoke();
        }

        [RelayCommand]
        public async Task ToggleTweakAsync(PrivacyTweakItem? tweak)
        {
            if (tweak == null) return;

            tweak.IsBusy = true;
            try
            {
                bool newState = !tweak.IsEnabled;
                bool ok = await _privacyService.ApplyTweakAsync(tweak, newState);
                if (ok)
                {
                    tweak.IsEnabled = newState;
                    UpdateStats();
                }
            }
            finally
            {
                tweak.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ApplyRecommendedAsync()
        {
            IsBusy = true;
            StatusMessage = "Önerilen tüm gizlilik kuralları uygulanıyor...";
            try
            {
                bool ok = await _privacyService.ApplyAllRecommendedAsync(Tweaks.ToList());
                UpdateStats();
                if (ok)
                {
                    MessageBox.Show("Önerilen tüm gizlilik ve telemetri kuralları başarıyla uygulandı!", "Gizlilik Koruyucu", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                IsBusy = false;
                StatusMessage = $"Gizlilik Skoru: %{Stats.ProtectionPercentage} Koruma Aktif";
            }
        }

        [RelayCommand]
        public async Task RestoreDefaultsAsync()
        {
            var confirm = MessageBox.Show(
                "Tüm telemetri ve gizlilik ayarlarını orijinal Windows fabrika varsayılanlarına döndürmek istiyor musunuz?",
                "Varsayılanlara Sıfırla",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Windows varsayılan ayarlarına dönülüyor...";
            try
            {
                await _privacyService.RestoreAllDefaultsAsync(Tweaks.ToList());
                UpdateStats();
                MessageBox.Show("Tüm gizlilik ayarları Windows varsayılanlarına sıfırlandı.", "Varsayılan Ayarlar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = $"Gizlilik Skoru: %{Stats.ProtectionPercentage}";
            }
        }

        /// <summary>Son geri yükleme noktası denemesinin sonucu (komut paleti bildirimi için).</summary>
        public string LastRestorePointResult { get; private set; } = string.Empty;

        [RelayCommand]
        public async Task CreateRestorePointAsync()
        {
            IsBusy = true;
            StatusMessage = "Windows Sistem Geri Yükleme Noktası oluşturuluyor...";
            try
            {
                bool ok = await _privacyService.CreateRestorePointAsync("Gizlilik ayarlari oncesi");
                LastRestorePointResult = _privacyService.LastRestorePointMessage;
                MessageBox.Show(_privacyService.LastRestorePointMessage, "Geri Yükleme Noktası",
                    MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = string.IsNullOrEmpty(LastRestorePointResult) ? "Geri yükleme noktası işlemi tamamlandı." : LastRestorePointResult;
            }
        }

        [RelayCommand]
        public async Task RemoveBloatwareAsync(BloatwareAppItem? app)
        {
            if (app == null) return;

            if (app.IsEssential)
            {
                MessageBox.Show($"{app.DisplayName} temel bir Windows sistem bileşenidir ve sistem kararlılığı için kaldırılamaz!", "Hayati Sistem Koruması", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            var confirm = MessageBox.Show(
                $"{app.DisplayName} ({app.PackageName}) uygulamasını sistemden tamamen kaldırmak istediğinize emin misiniz?",
                "Bloatware Kaldır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            app.IsBusy = true;
            try
            {
                bool ok = await _privacyService.RemoveBloatwareAsync(app);
                if (ok)
                {
                    app.IsInstalled = false;
                    UpdateStats();
                    MessageBox.Show($"{app.DisplayName} başarıyla kaldırıldı.", "Uygulama Kaldırıldı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Uygulama kaldırılamadı. Yönetici ayrıcalıkları gerekebilir.", "Kaldırma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                app.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RemoveAllSafeBloatwareAsync()
        {
            var safeApps = BloatwareApps.Where(a => !a.IsEssential && a.IsInstalled).ToList();
            if (safeApps.Count == 0)
            {
                MessageBox.Show("Sisteminizde kaldırılacak temel olmayan yüklü bloatware bulunamadı.", "Temiz Sistem", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"{safeApps.Count} adet temel olmayan bloatware uygulamasını sistemden kaldırmak istiyor musunuz?\n\n(Hesap Makinesi, Windows Mağazası gibi temel sistem bileşenleri güvenle korunacaktır.)",
                "Toplu Bloatware Temizliği",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            int removed = 0;
            try
            {
                foreach (var app in safeApps)
                {
                    StatusMessage = $"{app.DisplayName} kaldırılıyor...";
                    app.IsBusy = true;
                    bool ok = await _privacyService.RemoveBloatwareAsync(app);
                    app.IsBusy = false;
                    if (ok)
                    {
                        app.IsInstalled = false;
                        removed++;
                    }
                }
                UpdateStats();
                MessageBox.Show($"{removed} adet gereksiz bloatware uygulaması başarıyla temizlendi.", "Temizlik Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
                StatusMessage = $"Gizlilik Skoru: %{Stats.ProtectionPercentage}";
            }
        }
    }
}
