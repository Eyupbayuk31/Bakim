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
    public partial class CrashAnalyzerViewModel : ObservableObject
    {
        private readonly ICrashAnalyzerService _crashService;

        public CrashAnalyzerViewModel(ICrashAnalyzerService crashService)
        {
            _crashService = crashService;

            Crashes = new ObservableCollection<BsodCrashItem>();
            Events = new ObservableCollection<SystemEventItem>();

            _filteredEvents = CollectionViewSource.GetDefaultView(Events);
            _filteredEvents.Filter = FilterEventItem;

            _ = RefreshAllAsync();
        }

        private readonly ICollectionView _filteredEvents;
        public ICollectionView FilteredEvents => _filteredEvents;

        public ObservableCollection<BsodCrashItem> Crashes { get; }
        public ObservableCollection<SystemEventItem> Events { get; }

        [ObservableProperty]
        private string _activeTab = "Bsod"; // Bsod, Events, Repair

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedEventLevel = "All"; // All, Kritik, Hata, Kernel, App

        [ObservableProperty]
        private SystemHealthStats _stats = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Sistem olayları ve çökme kayıtları taranıyor...";

        [ObservableProperty]
        private bool _isRepairRunning;

        [ObservableProperty]
        private string _consoleOutput = "Sistem onarım konsolu hazır. Başlatmak için yukarıdaki butonları kullanın.\n";

        [ObservableProperty]
        private string _repairSummary = string.Empty;

        partial void OnSearchTextChanged(string value)
        {
            _filteredEvents.Refresh();
        }

        partial void OnSelectedEventLevelChanged(string value)
        {
            _filteredEvents.Refresh();
        }

        private bool FilterEventItem(object obj)
        {
            if (obj is not SystemEventItem item) return false;

            // 1. Level Filter
            if (SelectedEventLevel == "Kritik" && item.Level != "Kritik")
                return false;
            if (SelectedEventLevel == "Hata" && item.Level != "Hata")
                return false;
            if (SelectedEventLevel == "Kernel" && !item.Source.Contains("Kernel", StringComparison.OrdinalIgnoreCase))
                return false;
            if (SelectedEventLevel == "App" && !item.Source.Contains("Application", StringComparison.OrdinalIgnoreCase) && item.EventId != 1000)
                return false;

            // 2. Search Text
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.Source.ToLowerInvariant().Contains(query) ||
                   item.Message.ToLowerInvariant().Contains(query) ||
                   item.EventId.ToString().Contains(query) ||
                   item.Level.ToLowerInvariant().Contains(query);
        }

        [RelayCommand]
        public void SwitchTab(string tab)
        {
            ActiveTab = tab;
        }

        [RelayCommand]
        public void SetEventLevelFilter(string level)
        {
            SelectedEventLevel = level;
        }

        [RelayCommand]
        public async Task RefreshAllAsync()
        {
            if (IsBusy || IsRepairRunning) return;

            IsBusy = true;
            StatusMessage = "Minidump dökümleri ve Windows Olay Günlükleri taranıyor...";

            try
            {
                var crashesTask = _crashService.GetMinidumpCrashesAsync();
                var eventsTask = _crashService.GetCriticalEventsAsync(7);

                await Task.WhenAll(crashesTask, eventsTask);

                var crashesList = await crashesTask;
                var eventsList = await eventsTask;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Crashes.Clear();
                    foreach (var c in crashesList) Crashes.Add(c);

                    Events.Clear();
                    foreach (var e in eventsList) Events.Add(e);

                    _filteredEvents.Refresh();
                });

                Stats = await _crashService.CalculateHealthStatsAsync(crashesList, eventsList);
                StatusMessage = $"Sistem Sağlık Skoru: %{Stats.HealthScore} ({Stats.HealthStatusText}) - {Crashes.Count} Çökme, {Events.Count} Olay";
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
        public void OpenDumpLocation(BsodCrashItem? crash)
        {
            if (crash == null) return;
            _crashService.OpenDumpLocation(crash.DumpFilePath);
        }

        [RelayCommand]
        public async Task RunSfcAsync()
        {
            if (IsRepairRunning) return;

            IsRepairRunning = true;
            ConsoleOutput = $"--- [SFC SCAN BAŞLATILDI: {DateTime.Now:HH:mm:ss}] ---\n";
            StatusMessage = "Sistem Dosyası Denetleyicisi (sfc /scannow) çalışıyor...";

            try
            {
                await _crashService.RunSfcScannowAsync(
                    onOutputReceived: line =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConsoleOutput += line + "\n";
                        });
                    },
                    onCompleted: (success, summary) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            RepairSummary = summary;
                            StatusMessage = summary;
                        });
                    });
            }
            finally
            {
                IsRepairRunning = false;
            }
        }

        [RelayCommand]
        public async Task RunDismAsync()
        {
            if (IsRepairRunning) return;

            IsRepairRunning = true;
            ConsoleOutput = $"--- [DISM RESTOREHEALTH BAŞLATILDI: {DateTime.Now:HH:mm:ss}] ---\n";
            StatusMessage = "Windows İmaj Onarımı (DISM /RestoreHealth) çalışıyor...";

            try
            {
                await _crashService.RunDismRepairAsync(
                    onOutputReceived: line =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConsoleOutput += line + "\n";
                        });
                    },
                    onCompleted: (success, summary) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            RepairSummary = summary;
                            StatusMessage = summary;
                        });
                    });
            }
            finally
            {
                IsRepairRunning = false;
            }
        }

        [RelayCommand]
        public async Task ClearEventLogsAsync()
        {
            var confirm = MessageBox.Show(
                "System ve Application olay günlüklerini tamamen temizlemek istediğinize emin misiniz?",
                "Günlükleri Temizle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Olay günlükleri temizleniyor...";

            try
            {
                bool ok = await _crashService.ClearEventLogsAsync();
                if (ok)
                {
                    Events.Clear();
                    _filteredEvents.Refresh();
                    MessageBox.Show("Windows Olay Günlükleri başarıyla temizlendi.", "Temizleme Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshAllAsync();
                }
                else
                {
                    MessageBox.Show("Olay günlükleri temizlenemedi. Yönetici yetkisi gerekebilir.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ClearConsole()
        {
            ConsoleOutput = "Konsol çıktısı temizlendi.\n";
            RepairSummary = string.Empty;
        }
    }
}
