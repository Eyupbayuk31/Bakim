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
    public partial class ServiceManagerViewModel : ObservableObject
    {
        private readonly IServiceManagerService _serviceManager;

        public ServiceManagerViewModel(IServiceManagerService serviceManager)
        {
            _serviceManager = serviceManager;

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

        [RelayCommand]
        public async Task SetStartupTypeAsync((ServiceItem Item, string Type) param)
        {
            if (param.Item == null || string.IsNullOrWhiteSpace(param.Type)) return;

            if (param.Item.IsCritical && param.Type.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Kritik sistem hizmetleri devre dışı bırakılamaz!", "Sistem Koruması", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var ok = await _serviceManager.SetStartupTypeAsync(param.Item.ServiceName, param.Type);
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
}
