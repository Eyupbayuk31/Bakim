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
        private CancellationTokenSource? _speedTestCts;

        public NetworkMonitorViewModel(INetworkMonitorService networkService,
            IAppSettingsService settingsService)
        {
            _settingsService = settingsService;
            _networkService = networkService;

            Connections = new ObservableCollection<NetworkConnectionItem>();
            _filteredView = CollectionViewSource.GetDefaultView(Connections);
            _filteredView.Filter = FilterConnectionItem;

            ListeningPorts = new ObservableCollection<ListeningPortItem>();
            _filteredListeningView = CollectionViewSource.GetDefaultView(ListeningPorts);
            _filteredListeningView.Filter = FilterListeningItem;

            Adapters = new ObservableCollection<NetworkAdapterItem>();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _timer.Tick += async (s, e) =>
            {
                if (IsAutoRefreshEnabled && !IsBusy && SelectedTab == "Connections")
                {
                    await LoadConnectionsInternalAsync(false);
                }
            };
        }

        #region Sekme & Navigasyon

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsConnectionsTab))]
        [NotifyPropertyChangedFor(nameof(IsListeningTab))]
        [NotifyPropertyChangedFor(nameof(IsAdaptersTab))]
        [NotifyPropertyChangedFor(nameof(IsSpeedTestTab))]
        [NotifyPropertyChangedFor(nameof(IsDiagnosticsTab))]
        private string _selectedTab = "Connections"; // Connections, Listening, Adapters, SpeedTest, Diagnostics

        public bool IsConnectionsTab => SelectedTab == "Connections";
        public bool IsListeningTab => SelectedTab == "Listening";
        public bool IsAdaptersTab => SelectedTab == "Adapters";
        public bool IsSpeedTestTab => SelectedTab == "SpeedTest";
        public bool IsDiagnosticsTab => SelectedTab == "Diagnostics";

        [RelayCommand]
        public async Task SetTabAsync(string tab)
        {
            SelectedTab = tab;
            if (tab == "Listening" && ListeningPorts.Count == 0)
            {
                await LoadListeningPortsAsync();
            }
            else if (tab == "Adapters" && Adapters.Count == 0)
            {
                await LoadAdaptersAsync();
            }
        }

        #endregion

        #region 1. Canlı Bağlantılar (Active Sockets)

        private readonly ICollectionView _filteredView;
        public ICollectionView FilteredConnections => _filteredView;
        public ObservableCollection<NetworkConnectionItem> Connections { get; }

        [ObservableProperty]
        private NetworkOverviewStats _stats = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAllFilter))]
        [NotifyPropertyChangedFor(nameof(IsTcpFilter))]
        [NotifyPropertyChangedFor(nameof(IsUdpFilter))]
        [NotifyPropertyChangedFor(nameof(IsExternalFilter))]
        [NotifyPropertyChangedFor(nameof(IsListeningFilter))]
        [NotifyPropertyChangedFor(nameof(IsSuspiciousFilter))]
        private string _selectedFilter = "All"; // All, TCP, UDP, External, Listening, Suspicious

        public bool IsAllFilter => SelectedFilter == "All";
        public bool IsTcpFilter => SelectedFilter == "TCP";
        public bool IsUdpFilter => SelectedFilter == "UDP";
        public bool IsExternalFilter => SelectedFilter == "External";
        public bool IsListeningFilter => SelectedFilter == "Listening";
        public bool IsSuspiciousFilter => SelectedFilter == "Suspicious";

        [ObservableProperty]
        private bool _isAutoRefreshEnabled = true;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ağ bağlantıları taranıyor...";

        [ObservableProperty]
        private NetworkConnectionItem? _selectedConnection;

        [ObservableProperty]
        private bool _isDrawerOpen;

        partial void OnSelectedConnectionChanged(NetworkConnectionItem? value)
        {
            if (value != null)
            {
                IsDrawerOpen = true;
            }
        }

        [RelayCommand]
        public void CloseDrawer()
        {
            IsDrawerOpen = false;
            SelectedConnection = null;
        }

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
                "Suspicious" => item.IsSuspicious,
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
                   item.ServiceDescription.ToLowerInvariant().Contains(query) ||
                   item.State.ToLowerInvariant().Contains(query);
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (SelectedTab == "Connections")
            {
                await LoadConnectionsInternalAsync(true);
            }
            else if (SelectedTab == "Listening")
            {
                await LoadListeningPortsAsync();
            }
            else if (SelectedTab == "Adapters")
            {
                await LoadAdaptersAsync();
            }
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
                    CloseDrawer();
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

        #endregion

        #region 2. Dinlenen Portlar (Listening Ports)

        private readonly ICollectionView _filteredListeningView;
        public ICollectionView FilteredListeningPorts => _filteredListeningView;
        public ObservableCollection<ListeningPortItem> ListeningPorts { get; }

        [ObservableProperty]
        private string _listeningSearchText = string.Empty;

        partial void OnListeningSearchTextChanged(string value)
        {
            _filteredListeningView.Refresh();
        }

        private bool FilterListeningItem(object obj)
        {
            if (obj is not ListeningPortItem item) return false;
            if (string.IsNullOrWhiteSpace(ListeningSearchText)) return true;

            string query = ListeningSearchText.Trim().ToLowerInvariant();
            return item.ProcessName.ToLowerInvariant().Contains(query) ||
                   item.Port.ToString().Contains(query) ||
                   item.ServiceName.ToLowerInvariant().Contains(query) ||
                   item.LocalAddress.ToLowerInvariant().Contains(query);
        }

        public async Task LoadListeningPortsAsync()
        {
            IsBusy = true;
            try
            {
                var ports = await _networkService.GetListeningPortsAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ListeningPorts.Clear();
                    foreach (var p in ports)
                    {
                        ListeningPorts.Add(p);
                    }
                    _filteredListeningView.Refresh();
                });
            }
            catch { }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 3. Ağ Adaptörleri (Network Adapters)

        public ObservableCollection<NetworkAdapterItem> Adapters { get; }

        public async Task LoadAdaptersAsync()
        {
            IsBusy = true;
            try
            {
                var list = await _networkService.GetNetworkAdaptersAsync();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Adapters.Clear();
                    foreach (var a in list)
                    {
                        Adapters.Add(a);
                    }
                });
            }
            catch { }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region 4. 1000 Mbps Gigabit Hız Testi (Speed Test)

        [ObservableProperty]
        private string _speedTestState = "Idle"; // Idle, TestingPing, Downloading, Completed, Canceled, Error

        [ObservableProperty]
        private double _currentSpeedMbps;

        [ObservableProperty]
        private double _peakSpeedMbps;

        [ObservableProperty]
        private double _averageSpeedMbps;

        [ObservableProperty]
        private double _speedPingMs;

        [ObservableProperty]
        private double _speedJitterMs;

        [ObservableProperty]
        private double _downloadedMb;

        [ObservableProperty]
        private int _speedProgressPercent;

        [ObservableProperty]
        private string _speedTestStatus = "1000 Mbps Hız Testini Başlatmaya Hazır";

        [ObservableProperty]
        private bool _isSpeedTestRunning;

        [RelayCommand]
        public async Task StartSpeedTestAsync()
        {
            if (IsSpeedTestRunning) return;

            IsSpeedTestRunning = true;
            CurrentSpeedMbps = 0;
            PeakSpeedMbps = 0;
            AverageSpeedMbps = 0;
            DownloadedMb = 0;
            SpeedProgressPercent = 0;
            SpeedTestStatus = "Sunucuya bağlanılıyor ve ping ölçülüyor...";

            _speedTestCts = new CancellationTokenSource();

            var progress = new Progress<SpeedTestProgress>(p =>
            {
                CurrentSpeedMbps = p.CurrentMbps;
                PeakSpeedMbps = p.PeakMbps;
                AverageSpeedMbps = p.AverageMbps;
                SpeedPingMs = p.PingMs;
                SpeedJitterMs = p.JitterMs;
                DownloadedMb = p.DownloadedMb;
                SpeedProgressPercent = p.ProgressPercent;
                SpeedTestStatus = p.StatusMessage;
                SpeedTestState = p.State;
            });

            try
            {
                await _networkService.RunSpeedTestAsync(progress, _speedTestCts.Token);
            }
            catch (Exception ex)
            {
                SpeedTestStatus = $"Hata: {ex.Message}";
                SpeedTestState = "Error";
            }
            finally
            {
                IsSpeedTestRunning = false;
                _speedTestCts?.Dispose();
                _speedTestCts = null;
            }
        }

        [RelayCommand]
        public void CancelSpeedTest()
        {
            if (_speedTestCts != null && !_speedTestCts.IsCancellationRequested)
            {
                _speedTestCts.Cancel();
                SpeedTestStatus = "Test iptal ediliyor...";
            }
        }

        #endregion

        #region 5. Ağ Teşhis Araçları (Ping, DNS Flush, Port Check)

        [ObservableProperty]
        private string _pingTarget = "1.1.1.1";

        [ObservableProperty]
        private string _pingResult = "Ping testi başlatılmadı.";

        [ObservableProperty]
        private bool _isPingRunning;

        [RelayCommand]
        public async Task RunPingAsync()
        {
            if (string.IsNullOrWhiteSpace(PingTarget) || IsPingRunning) return;

            IsPingRunning = true;
            PingResult = $"{PingTarget} adresine ping paketi gönderiliyor...";
            try
            {
                var res = await _networkService.PingHostAsync(PingTarget);
                PingResult = res.FormattedResult;
            }
            catch (Exception ex)
            {
                PingResult = $"Hata: {ex.Message}";
            }
            finally
            {
                IsPingRunning = false;
            }
        }

        [ObservableProperty]
        private string _flushDnsStatus = "DNS Çözümleyici Önbelleği Beklemede.";

        [ObservableProperty]
        private bool _isFlushingDns;

        [RelayCommand]
        public async Task FlushDnsAsync()
        {
            if (IsFlushingDns) return;

            IsFlushingDns = true;
            FlushDnsStatus = "Windows DNS önbelleği temizleniyor...";
            try
            {
                bool ok = await _networkService.FlushDnsCacheAsync();
                FlushDnsStatus = ok
                    ? "Tebrikler! Windows DNS Çözümleyici Önbelleği başarıyla temizlendi (Flush DNS OK)."
                    : "DNS önbelleği temizlenirken bir hata oluştu.";
            }
            catch (Exception ex)
            {
                FlushDnsStatus = $"Hata: {ex.Message}";
            }
            finally
            {
                IsFlushingDns = false;
            }
        }

        [ObservableProperty]
        private string _portCheckHost = "google.com";

        [ObservableProperty]
        private int _portCheckPort = 443;

        [ObservableProperty]
        private string _portCheckResultText = "Port testi henüz yapılmadı.";

        [ObservableProperty]
        private bool _isPortChecking;

        [RelayCommand]
        public async Task CheckPortAsync()
        {
            if (string.IsNullOrWhiteSpace(PortCheckHost) || PortCheckPort <= 0 || IsPortChecking) return;

            IsPortChecking = true;
            PortCheckResultText = $"{PortCheckHost}:{PortCheckPort} bağlantısı test ediliyor...";
            try
            {
                var res = await _networkService.CheckPortAsync(PortCheckHost, PortCheckPort);
                PortCheckResultText = res.Message;
            }
            catch (Exception ex)
            {
                PortCheckResultText = $"Hata: {ex.Message}";
            }
            finally
            {
                IsPortChecking = false;
            }
        }

        #endregion

        #region Modül Yaşam Döngüsü

        private bool _isActive;

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

        public Task OnDeactivatedAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;

            _timer.Stop();
            CancelSpeedTest();
            return Task.CompletedTask;
        }

        private void ApplyRefreshInterval()
        {
            int seconds = _settingsService.Current.RefreshIntervalSeconds;
            if (seconds < 1) seconds = 1;
            _timer.Interval = TimeSpan.FromSeconds(seconds);
        }

        #endregion
    }
}
