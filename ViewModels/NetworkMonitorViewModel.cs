using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class NetworkMonitorViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IAppSettingsService _settingsService;
        private readonly INetworkMonitorService _networkService;
        private readonly DispatcherTimer _timer;
        private List<NetworkConnectionItem> _allConnections = new();

        public NetworkMonitorViewModel(INetworkMonitorService networkService,
            IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _networkService = networkService;
            Connections = new ObservableCollection<NetworkConnectionItem>();

            _filteredView = CollectionViewSource.GetDefaultView(Connections);
            _filteredView.Filter = FilterConnectionItem;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _timer.Tick += async (s, e) =>
            {
                if (IsAutoRefreshEnabled && !IsBusy)
                {
                    await LoadConnectionsInternalAsync(false);
                }
            };

            // Zamanlayıcı OnActivatedAsync() içinde başlar — bkz. IModuleViewModel
        }

        private readonly ICollectionView _filteredView;
        public ICollectionView FilteredConnections => _filteredView;

        public ObservableCollection<NetworkConnectionItem> Connections { get; }

        [ObservableProperty]
        private NetworkOverviewStats _stats = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All"; // All, TCP, UDP, External, Listening

        [ObservableProperty]
        private bool _isAutoRefreshEnabled = true;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ağ bağlantıları taranıyor...";

        partial void OnSearchTextChanged(string value)
        {
            _filteredView.Refresh();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            _filteredView.Refresh();
        }

        private bool FilterConnectionItem(object obj)
        {
            if (obj is not NetworkConnectionItem item) return false;

            // 1. Protocol / State filter
            bool matchesFilter = SelectedFilter switch
            {
                "TCP" => item.Protocol == "TCP",
                "UDP" => item.Protocol == "UDP",
                "External" => item.IsExternal && item.State == "ESTABLISHED",
                "Listening" => item.State == "LISTENING",
                _ => true
            };

            if (!matchesFilter) return false;

            // 2. Search text filter
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.ProcessName.ToLowerInvariant().Contains(query) ||
                   item.ProcessId.ToString().Contains(query) ||
                   item.LocalEndpoint.ToLowerInvariant().Contains(query) ||
                   item.RemoteEndpoint.ToLowerInvariant().Contains(query) ||
                   item.RemoteHostName.ToLowerInvariant().Contains(query) ||
                   item.State.ToLowerInvariant().Contains(query);
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            await LoadConnectionsInternalAsync(true);
        }

        [RelayCommand]
        public void SetFilter(string filter)
        {
            SelectedFilter = filter;
        }

        private async Task LoadConnectionsInternalAsync(bool showBusy)
        {
            if (showBusy) IsBusy = true;
            try
            {
                var (connections, stats) = await _networkService.GetActiveConnectionsAsync();
                _allConnections = connections;
                Stats = stats;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Connections.Clear();
                    foreach (var conn in connections)
                    {
                        Connections.Add(conn);
                    }
                    _filteredView.Refresh();
                });

                StatusMessage = $"{stats.TotalConnections} Bağlantı Aktif (ESTABLISHED: {stats.EstablishedCount}, Dinleme: {stats.ListeningCount})";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Hata: {ex.Message}";
            }
            finally
            {
                if (showBusy) IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ToggleBlockAsync(NetworkConnectionItem? item)
        {
            if (item == null) return;

            if (string.IsNullOrWhiteSpace(item.ProcessPath))
            {
                MessageBox.Show("Bu sürecin dosya yolu tespit edilemediği için güvenlik duvarı kuralı oluşturulamadı.", "Güvenlik Duvarı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (item.IsBlocked)
            {
                var ok = await _networkService.UnblockProcessInFirewallAsync(item);
                if (ok)
                {
                    MessageBox.Show($"{item.ProcessName} güvenlik duvarı engeli kaldırıldı.", "Güvenlik Duvarı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var confirm = MessageBox.Show(
                    $"{item.ProcessName} uygulamasının dış ağ bağlantısını Windows Güvenlik Duvarı ile engellemek istiyor musunuz?",
                    "Ağ Erişimini Engelle",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    var ok = await _networkService.BlockProcessInFirewallAsync(item);
                    if (ok)
                    {
                        MessageBox.Show($"{item.ProcessName} için giden bağlantı engelleme kuralı eklendi.", "Güvenlik Duvarı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        [RelayCommand]
        public void KillProcess(NetworkConnectionItem? item)
        {
            if (item == null) return;

            if (item.ProcessId <= 4)
            {
                MessageBox.Show("Kritik Windows sistem süreçleri sonlandırılamaz!", "Sistem Koruması", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"{item.ProcessName} (PID: {item.ProcessId}) sürecini ve ilişkili tüm soket bağlantılarını sonlandırmak istiyor musunuz?",
                "Süreç ve Bağlantıyı Sonlandır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                bool killed = _networkService.KillProcess(item.ProcessId);
                if (killed)
                {
                    _ = RefreshAsync();
                }
                else
                {
                    MessageBox.Show("Süreç sonlandırılamadı. Yönetici yetkisi gerekiyor olabilir.", "Yetki Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void OpenProcessLocation(NetworkConnectionItem? item)
        {
            if (item == null) return;
            _networkService.OpenProcessLocation(item.ProcessPath);
        }

        [RelayCommand]
        public void CopyIp(string? ip)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                Clipboard.SetText(ip);
            }
        }
    
        #region Modül Yaşam Döngüsü

        private bool _isActive;

        /// <summary>
        /// Modül görünür oldu. Zamanlayıcı BURADA başlar — yapıcı metotta değil.
        /// Böylece açılışta yalnızca ilk modül kaynak tüketir.
        /// </summary>
        public async Task OnActivatedAsync()
        {
            if (_isActive) return;
            _isActive = true;

            ApplyRefreshInterval();
            _timer.Start();

            try
            {
                await LoadConnectionsInternalAsync(true);
            }
            catch (Exception ex)
            {
                Services.AppLog.Error("Modül etkinleştirilirken hata.", ex, nameof(NetworkMonitorViewModel));
            }
        }

        /// <summary>Modülden çıkıldı: arka planda WMI sorgusu atmaya devam etme.</summary>
        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _timer.Stop();
            return Task.CompletedTask;
        }

        /// <summary>Kullanıcının ayarlardaki yenileme aralığı tercihini uygular.</summary>
        private void ApplyRefreshInterval()
        {
            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            if (seconds < 1) seconds = 1;
            _timer.Interval = TimeSpan.FromSeconds(seconds);
        }

        #endregion

}
}
