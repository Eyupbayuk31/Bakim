using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.ServiceControl;
using Bakım.Models;
using Bakım.Core.Activity;
using Bakım.Services;
using Bakım.Services.Activity;

namespace Bakım.ViewModels
{
    public partial class ServiceManagerViewModel : ObservableObject
    {
        private readonly IServiceManagerService _serviceManager;
        private readonly IActivityService _activity;

        public ServiceManagerViewModel(IServiceManagerService serviceManager, IActivityService activity)
        {
            _serviceManager = serviceManager;
            _activity = activity;

            Services = new ObservableCollection<ServiceItem>();
            Drivers = new ObservableCollection<DriverItem>();

            _filteredServices = CollectionViewSource.GetDefaultView(Services);
            _filteredServices.Filter = FilterServiceItem;

            _filteredDrivers = CollectionViewSource.GetDefaultView(Drivers);
            _filteredDrivers.Filter = FilterDriverItem;

            _ = RefreshAllAsync();
        }

        private readonly ICollectionView _filteredServices;
        public ICollectionView FilteredServices => _filteredServices;

        private readonly ICollectionView _filteredDrivers;
        public ICollectionView FilteredDrivers => _filteredDrivers;

        public ObservableCollection<ServiceItem> Services { get; }

        /// <summary>Güvenli önerilen profiller (§5.7); önizleme mevcut başlangıç türlerinden hesaplanır.</summary>
        public ObservableCollection<ServiceProfileCard> Profiles { get; } = new();
        public ObservableCollection<DriverItem> Drivers { get; }

        [ObservableProperty]
        private string _activeTab = "Services"; // Services, Drivers

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedServiceFilter = "All"; // All, Running, Stopped, Optimizable

        [ObservableProperty]
        private string _selectedDriverFilter = "All"; // All, Signed, Unsigned

        [ObservableProperty]
        private ServiceDriverStats _stats = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Hizmetler ve sürücüler yükleniyor...";

        partial void OnSearchTextChanged(string value)
        {
            _filteredServices.Refresh();
            _filteredDrivers.Refresh();
        }

        partial void OnSelectedServiceFilterChanged(string value)
        {
            _filteredServices.Refresh();
        }

        partial void OnSelectedDriverFilterChanged(string value)
        {
            _filteredDrivers.Refresh();
        }

        private bool FilterServiceItem(object obj)
        {
            if (obj is not ServiceItem item) return false;

            // 1. Status / Category Filter
            bool matchesFilter = SelectedServiceFilter switch
            {
                "Running" => item.IsRunning,
                "Stopped" => !item.IsRunning,
                "Optimizable" => item.IsOptimizable,
                _ => true
            };

            if (!matchesFilter) return false;

            // 2. Search Text
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.DisplayName.ToLowerInvariant().Contains(query) ||
                   item.ServiceName.ToLowerInvariant().Contains(query) ||
                   item.SafetyClassification.ToLowerInvariant().Contains(query) ||
                   item.StartupType.ToLowerInvariant().Contains(query) ||
                   item.Description.ToLowerInvariant().Contains(query);
        }

        private bool FilterDriverItem(object obj)
        {
            if (obj is not DriverItem item) return false;

            // 1. Status Filter
            bool matchesFilter = SelectedDriverFilter switch
            {
                "Signed" => item.IsSigned,
                "Unsigned" => !item.IsSigned,
                _ => true
            };

            if (!matchesFilter) return false;

            // 2. Search Text
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.DeviceName.ToLowerInvariant().Contains(query) ||
                   item.DeviceClass.ToLowerInvariant().Contains(query) ||
                   item.Manufacturer.ToLowerInvariant().Contains(query) ||
                   item.Signer.ToLowerInvariant().Contains(query) ||
                   item.DriverVersion.ToLowerInvariant().Contains(query);
        }

        [RelayCommand]
        public void SwitchTab(string tab)
        {
            ActiveTab = tab;
        }

        [RelayCommand]
        public void SetServiceFilter(string filter)
        {
            SelectedServiceFilter = filter;
        }

        [RelayCommand]
        public void SetDriverFilter(string filter)
        {
            SelectedDriverFilter = filter;
        }

        [RelayCommand]
        public async Task RefreshAllAsync()
        {
            IsBusy = true;
            StatusMessage = "Sistem hizmetleri ve donanım sürücüleri taranıyor...";
            try
            {
                var servicesTask = _serviceManager.GetServicesAsync();
                var driversTask = _serviceManager.GetDriversAsync();

                await Task.WhenAll(servicesTask, driversTask);

                var services = await servicesTask;
                var drivers = await driversTask;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Services.Clear();
                    foreach (var s in services) Services.Add(s);

                    Drivers.Clear();
                    foreach (var d in drivers) Drivers.Add(d);

                    _filteredServices.Refresh();
                    _filteredDrivers.Refresh();
                    RebuildProfiles();
                });

                Stats = new ServiceDriverStats
                {
                    TotalServices = services.Count,
                    RunningServices = services.Count(s => s.IsRunning),
                    StoppedServices = services.Count(s => !s.IsRunning),
                    OptimizableServices = services.Count(s => s.IsOptimizable),
                    TotalDrivers = drivers.Count,
                    SignedDrivers = drivers.Count(d => d.IsSigned),
                    ProblematicDrivers = drivers.Count(d => d.IsProblematic)
                };

                StatusMessage = $"{Stats.TotalServices} Hizmet ({Stats.RunningServices} Çalışıyor), {Stats.TotalDrivers} Sürücü Yüklü";
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

        [RelayCommand]
        public async Task StartServiceAsync(ServiceItem? item)
        {
            if (item == null) return;
            item.IsBusy = true;
            try
            {
                var ok = await _serviceManager.StartServiceAsync(item.ServiceName);
                _activity.RecordServiceChange($"'{item.DisplayName}' başlatıldı",
                    ok ? item.ServiceName : $"Başlatılamadı: {_serviceManager.LastError}",
                    ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed,
                    ActivityRecording.ReadServiceState(item.ServiceName, item.DisplayName, restoreRunning: false, includeStartType: false));
                if (ok)
                {
                    item.Status = "Running";
                    _filteredServices.Refresh();
                }
                else
                {
                    MessageBox.Show($"{item.DisplayName} başlatılamadı: {_serviceManager.LastError}", "Hizmet Başlatma", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task StopServiceAsync(ServiceItem? item)
        {
            if (item == null) return;

            if (item.IsCritical)
            {
                MessageBox.Show($"{item.DisplayName} bir kritik sistem hizmetidir! Durdurulması sistemin çökmesine neden olabilir.", "Kritik Sistem Hizmeti", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"{item.DisplayName} ({item.ServiceName}) hizmetini durdurmak istediğinize emin misiniz?",
                "Hizmeti Durdur",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            item.IsBusy = true;
            try
            {
                var ok = await _serviceManager.StopServiceAsync(item.ServiceName);
                _activity.RecordServiceChange($"'{item.DisplayName}' durduruldu",
                    ok ? item.ServiceName : $"Durdurulamadı: {_serviceManager.LastError}",
                    ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed,
                    ActivityRecording.ReadServiceState(item.ServiceName, item.DisplayName, restoreRunning: true, includeStartType: false));
                if (ok)
                {
                    item.Status = "Stopped";
                    _filteredServices.Refresh();
                }
                else
                {
                    MessageBox.Show($"{item.DisplayName} durdurulamadı: {_serviceManager.LastError}", "Hizmet Durdurma", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RestartServiceAsync(ServiceItem? item)
        {
            if (item == null) return;
            item.IsBusy = true;
            try
            {
                var ok = await _serviceManager.RestartServiceAsync(item.ServiceName);
                if (ok)
                {
                    item.Status = "Running";
                    _filteredServices.Refresh();
                }
                else
                {
                    MessageBox.Show($"{item.DisplayName} yeniden başlatılamadı: {_serviceManager.LastError}", "Hizmeti Yeniden Başlat", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        private void RebuildProfiles()
        {
            var modes = Services.GroupBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().StartupType, StringComparer.OrdinalIgnoreCase);
            Profiles.Clear();
            foreach (var profile in ServiceProfiles.All)
                Profiles.Add(new ServiceProfileCard(profile, ServiceProfiles.Preview(profile, modes)));
        }

        /// <summary>Profili önizler, tek onayla uygular ve tek Etkinlik kaydıyla geri alınabilir kılar.</summary>
        [RelayCommand]
        public async Task ApplyProfileAsync(ServiceProfileCard? card)
        {
            if (card == null || !card.CanApply) return;
            var confirm = MessageBox.Show(
                $"{card.Title}\n\nDeğişecek hizmetler:\n{card.PreviewText}\n\nTek yönetici onayıyla uygulanır; Etkinlik Merkezi'nden tek tıkla geri alınabilir.",
                "Hizmet profili", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                var originals = card.Steps
                    .Select(s => ActivityRecording.ReadServiceState(s.ServiceName, DisplayNameOf(s.ServiceName), restoreRunning: null, includeStartType: true))
                    .Where(p => p != null).Select(p => p!).ToList();
                var result = await _serviceManager.SetStartupTypesAsync(card.Steps.Select(s => (s.ServiceName, s.TargetMode)).ToList());
                var outcome = result.Cancelled ? ActivityOutcome.Cancelled : ActivityQuery.OutcomeFromCounts(result.Succeeded, result.Failed);
                _activity.RecordServiceBatch($"Hizmet profili: {card.Title}",
                    $"{result.Succeeded} hizmet değişti" + (result.Failed > 0 ? $" · {result.Failed} başarısız" : ""),
                    outcome, result.Succeeded > 0 ? originals : Array.Empty<ServiceUndoPayload>(),
                    card.Steps.Select(s => new ActivityItem(s.ServiceName, "Başlangıç türü",
                        $"{ServiceProfiles.ModeLabel(s.CurrentMode)} → {ServiceProfiles.ModeLabel(s.TargetMode)}")));
                StatusMessage = result.Cancelled
                    ? "Yönetici izni verilmedi; profil uygulanmadı."
                    : $"{card.Title}: {result.Succeeded} hizmet değişti" + (result.Failed > 0 ? $", {result.Failed} başarısız ({_serviceManager.LastError})." : ".");
            }
            finally
            {
                IsBusy = false;
            }
            await RefreshAllAsync();
        }

        private string DisplayNameOf(string serviceName) =>
            Services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? serviceName;

        [RelayCommand]
        public async Task SetStartupTypeAsync((ServiceItem Item, string Type) param)
        {
            if (param.Item == null || string.IsNullOrWhiteSpace(param.Type)) return;

            // Kritik hizmetin başlangıç türü hiç değiştirilmez (tek kaynak: CriticalServicePolicy).
            if (param.Item.IsCritical)
            {
                MessageBox.Show("Kritik sistem hizmetlerinin başlangıç türü değiştirilemez.", "Sistem Koruması", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (param.Type.Equals("disabled", StringComparison.OrdinalIgnoreCase) &&
                CriticalServicePolicy.ReducesSecurityWhenDisabled(param.Item.ServiceName) &&
                MessageBox.Show($"{param.Item.DisplayName} güvenlik ya da güncellemeyle ilgili bir hizmettir. Devre dışı bırakmak bilgisayarı daha az korunaklı yapar.\n\nYine de devam edilsin mi?",
                    "Güvenliği azaltır", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            // Özgün başlangıç türü değişiklikten ÖNCE okunur (geri alma için).
            var original = ActivityRecording.ReadServiceState(param.Item.ServiceName, param.Item.DisplayName, restoreRunning: null, includeStartType: true);
            var ok = await _serviceManager.SetStartupTypeAsync(param.Item.ServiceName, param.Type);
            string typeLabel = param.Type.ToLowerInvariant() switch { "auto" => "Otomatik", "disabled" => "Devre dışı", _ => "El ile" };
            _activity.RecordServiceChange($"'{param.Item.DisplayName}' başlangıç türü: {typeLabel}",
                ok ? param.Item.ServiceName : $"Değiştirilemedi: {_serviceManager.LastError}",
                ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed, original);
            if (ok)
            {
                param.Item.StartupType = param.Type switch
                {
                    "auto" => "Auto",
                    "disabled" => "Disabled",
                    _ => "Manual"
                };
                _filteredServices.Refresh();
            }
            else
            {
                MessageBox.Show($"{param.Item.DisplayName} başlangıç türü değiştirilemedi: {_serviceManager.LastError}", "Başlangıç Türü", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void OpenServiceLocation(ServiceItem? item)
        {
            if (item == null) return;
            _serviceManager.OpenFileLocation(item.ExecutablePath);
        }

        [RelayCommand]
        public void CopyDeviceId(DriverItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.DeviceID)) return;
            Clipboard.SetText(item.DeviceID);
        }
    }

    /// <summary>Önerilen hizmet profili kartı.</summary>
    public sealed class ServiceProfileCard
    {
        public ServiceProfileCard(ServiceProfile profile, IReadOnlyList<ServiceProfileStep> steps)
        {
            Profile = profile;
            Steps = steps;
            Icon = Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(profile.Icon, out var icon) ? icon : Wpf.Ui.Controls.SymbolRegular.Settings24;
        }

        public ServiceProfile Profile { get; }
        public IReadOnlyList<ServiceProfileStep> Steps { get; }
        public string Title => Profile.Title;
        public string Description => Profile.Description;
        public Wpf.Ui.Controls.SymbolRegular Icon { get; }
        public bool CanApply => Steps.Count > 0;
        public string StepText => Steps.Count > 0
            ? $"{Steps.Count} hizmet değişecek"
            : "Uygulanmış ya da bu bilgisayarda ilgili hizmet yok";
        public string PreviewText => string.Join("\n", Steps.Select(s =>
            $"• {s.ServiceName}: {ServiceProfiles.ModeLabel(s.CurrentMode)} → {ServiceProfiles.ModeLabel(s.TargetMode)}"));
    }
}
