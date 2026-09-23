using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class StoreViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IStoreService _storeService;
        private readonly ILogService _log;
        private readonly List<StoreAppItem> _masterCatalog = new();
        private CancellationTokenSource? _currentCts;

        public ObservableCollection<StoreAppItem> DisplayApps { get; } = new();

        #region KPI Metrics

        [ObservableProperty]
        private int _totalAppsCount;

        [ObservableProperty]
        private int _installedAppsCount;

        [ObservableProperty]
        private int _availableAppsCount;

        [ObservableProperty]
        private int _selectedAppsCount;

        [ObservableProperty]
        private string _selectedAppsText = "0 Paket";

        #endregion

        #region Filters & Category Selection

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private StoreCategory _selectedCategory = StoreCategory.All;

        [ObservableProperty]
        private bool _onlyUninstalled;

        #endregion

        #region Queue & Drawer Status

        [ObservableProperty]
        private bool _isQueueDrawerOpen;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private string _currentAppName = string.Empty;

        [ObservableProperty]
        private string _currentStatusText = "Hazır";

        [ObservableProperty]
        private int _overallProgress;

        [ObservableProperty]
        private string _queueProgressSummary = "0 / 0";

        [ObservableProperty]
        private string _consoleLogs = "Mağaza ve Runtimes motoru hazır.\n";

        #endregion

        public StoreViewModel(IStoreService storeService, ILogService log)
        {
            _storeService = storeService;
            _log = log;

            _masterCatalog = _storeService.GetCatalog();
            foreach (var app in _masterCatalog)
            {
                app.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(StoreAppItem.IsSelected))
                    {
                        UpdateSelectionMetrics();
                    }
                };
            }

            ApplyFilters();
            UpdateKpiMetrics();
        }

        public async Task OnActivatedAsync()
        {
            await RefreshInstalledStatusAsync();
        }

        public Task OnDeactivatedAsync()
        {
            return Task.CompletedTask;
        }

        [RelayCommand]
        public async Task RefreshInstalledStatusAsync()
        {
            await _storeService.CheckInstalledStatusesAsync(_masterCatalog);
            UpdateKpiMetrics();
            ApplyFilters();
        }

        #region Filter Logic

        partial void OnSearchQueryChanged(string value) => ApplyFilters();
        partial void OnSelectedCategoryChanged(StoreCategory value) => ApplyFilters();
        partial void OnOnlyUninstalledChanged(bool value) => ApplyFilters();

        [RelayCommand]
        public void SetCategory(string categoryName)
        {
            if (Enum.TryParse<StoreCategory>(categoryName, true, out var cat))
            {
                SelectedCategory = cat;
            }
        }

        private void ApplyFilters()
        {
            var query = _masterCatalog.AsEnumerable();

            if (SelectedCategory != StoreCategory.All)
            {
                query = query.Where(a => a.Category == SelectedCategory);
            }

            if (OnlyUninstalled)
            {
                query = query.Where(a => !a.IsInstalled);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                string q = SearchQuery.Trim().ToLowerInvariant();
                query = query.Where(a =>
                    a.Name.ToLowerInvariant().Contains(q) ||
                    a.Description.ToLowerInvariant().Contains(q) ||
                    a.Publisher.ToLowerInvariant().Contains(q) ||
                    a.CategoryDisplayName.ToLowerInvariant().Contains(q));
            }

            DisplayApps.Clear();
            foreach (var item in query)
            {
                DisplayApps.Add(item);
            }
        }

        private void UpdateKpiMetrics()
        {
            TotalAppsCount = _masterCatalog.Count;
            InstalledAppsCount = _masterCatalog.Count(a => a.IsInstalled);
            AvailableAppsCount = TotalAppsCount - InstalledAppsCount;
            UpdateSelectionMetrics();
        }

        private void UpdateSelectionMetrics()
        {
            var selected = _masterCatalog.Where(a => a.IsSelected).ToList();
            SelectedAppsCount = selected.Count;
            SelectedAppsText = $"{SelectedAppsCount} Paket Seçildi";
        }

        #endregion

        #region 1-Click Presets

        [RelayCommand]
        public void ApplyPresetFormatEssentials()
        {
            // Format Kurtarıcı: VC++ AIO, DirectX, Chrome, WinRAR, 7-Zip, Spotify, VLC, Discord
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tpu_vcredist_aio", "directx_web_setup", "chrome", "winrar", "seven_zip", "spotify", "vlc_player", "discord_gaming"
            };

            SelectSpecificIds(ids);
            AppendLog("🚀 [Preset Seçildi] Format Kurtarıcı Paketi (Runtimes + Tarayıcı + Arşiv + Medya)");
        }

        [RelayCommand]
        public void ApplyPresetGamer()
        {
            // Oyuncu Paketi: VC++ AIO, DirectX, Steam, Epic Games, Discord, Spotify, WinRAR
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "tpu_vcredist_aio", "directx_web_setup", "steam", "epic_games", "discord_gaming", "spotify", "winrar"
            };

            SelectSpecificIds(ids);
            AppendLog("🎮 [Preset Seçildi] Oyuncu Paketi (Oyun Platformları + VC++ AIO + DirectX)");
        }

        [RelayCommand]
        public void ApplyPresetOffice()
        {
            // Ofis Paketi: Chrome, WhatsApp, Telegram, 7-Zip, Notepad++, PowerToys, VLC
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "chrome", "whatsapp", "telegram", "seven_zip", "notepad_plus_plus", "powertoys", "vlc_player"
            };

            SelectSpecificIds(ids);
            AppendLog("💼 [Preset Seçildi] Ofis & Günlük Paket (Tarayıcı + İletişim + Araçlar)");
        }

        [RelayCommand]
        public void ApplyPresetDeveloper()
        {
            // Geliştirici Paketi: VS Code, Git, Python 3, Node.js, Windows Terminal, PowerToys, 7-Zip
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "vscode", "git_for_windows", "python_3", "nodejs_lts", "windows_terminal", "powertoys", "seven_zip"
            };

            SelectSpecificIds(ids);
            AppendLog("💻 [Preset Seçildi] Geliştirici Paketi (VS Code + Git + Python + Node + Terminal)");
        }

        private void SelectSpecificIds(HashSet<string> targetIds)
        {
            foreach (var app in _masterCatalog)
            {
                app.IsSelected = targetIds.Contains(app.Id);
            }
            UpdateSelectionMetrics();
        }

        [RelayCommand]
        public void SelectAllVisible()
        {
            foreach (var app in DisplayApps)
            {
                app.IsSelected = true;
            }
            UpdateSelectionMetrics();
        }

        [RelayCommand]
        public void ClearSelection()
        {
            foreach (var app in _masterCatalog)
            {
                app.IsSelected = false;
            }
            UpdateSelectionMetrics();
        }

        #endregion

        #region Installation Execution

        [RelayCommand]
        public async Task InstallAllRuntimesAsync()
        {
            if (IsInstalling) return;

            var runtimes = _masterCatalog.Where(a => a.Category == StoreCategory.Runtimes).ToList();
            if (runtimes.Count == 0) return;

            foreach (var app in _masterCatalog) app.IsSelected = false;
            foreach (var r in runtimes) r.IsSelected = true;
            UpdateSelectionMetrics();

            await RunBatchInstallationAsync(runtimes, "All-in-One Runtimes Paketi");
        }

        [RelayCommand]
        public async Task InstallSelectedBatchAsync()
        {
            if (IsInstalling) return;

            var selected = _masterCatalog.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen kurulmasını istediğiniz en az bir uygulama seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunBatchInstallationAsync(selected, $"{selected.Count} Seçili Uygulama");
        }

        [RelayCommand]
        public async Task InstallSingleAppAsync(StoreAppItem? app)
        {
            if (app == null || IsInstalling) return;

            await RunBatchInstallationAsync(new List<StoreAppItem> { app }, app.Name);
        }

        private async Task RunBatchInstallationAsync(List<StoreAppItem> queue, string batchTitle)
        {
            IsInstalling = true;
            IsQueueDrawerOpen = true;
            _currentCts = new CancellationTokenSource();
            var token = _currentCts.Token;

            AppendLog($"=======================================================");
            AppendLog($"[BAŞLATILDI] {batchTitle} Kurulum Kuyruğu ({queue.Count} Öğe)");
            AppendLog($"=======================================================");

            int completed = 0;
            int total = queue.Count;

            try
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var app = queue[i];
                    CurrentAppName = app.Name;
                    QueueProgressSummary = $"{i + 1} / {total}";
                    OverallProgress = (int)(((double)i / total) * 100);

                    AppendLog($"\n>>> [{i + 1}/{total}] {app.Name} kuruluyor...");

                    bool success = await _storeService.InstallAppAsync(app, (pct, status) =>
                    {
                        CurrentStatusText = status;
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            AppendLog($"  - {status}");
                        }
                    }, token);

                    if (success)
                    {
                        completed++;
                        AppendLog($"[BAŞARILI] {app.Name} başarıyla tamamlandı.");
                    }
                    else
                    {
                        AppendLog($"[HATA] {app.Name} kurulamadı!");
                    }
                }

                OverallProgress = 100;
                CurrentStatusText = token.IsCancellationRequested ? "Kurulum iptal edildi." : "Tüm işlemler tamamlandı!";
                AppendLog($"\n=======================================================");
                AppendLog($"[BİTTİ] {completed}/{total} uygulama başarıyla kuruldu.");
                AppendLog($"=======================================================");

                await RefreshInstalledStatusAsync();
            }
            catch (OperationCanceledException)
            {
                CurrentStatusText = "Kullanıcı tarafından iptal edildi.";
                AppendLog("\n[İPTAL] İşlem kullanıcı tarafından durduruldu.");
            }
            catch (Exception ex)
            {
                CurrentStatusText = $"Beklenmeyen hata: {ex.Message}";
                AppendLog($"\n[KRİTİK HATA] {ex.Message}");
            }
            finally
            {
                IsInstalling = false;
                _currentCts?.Dispose();
                _currentCts = null;
            }
        }

        [RelayCommand]
        public void CancelCurrentOperation()
        {
            if (_currentCts != null && !_currentCts.IsCancellationRequested)
            {
                _currentCts.Cancel();
                CurrentStatusText = "İptal ediliyor...";
                AppendLog("[UYARI] İptal isteği gönderildi, mevcut süreç sonlandırılıyor...");
            }
        }

        [RelayCommand]
        public void ToggleQueueDrawer()
        {
            IsQueueDrawerOpen = !IsQueueDrawerOpen;
        }

        private void AppendLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var sb = new StringBuilder(ConsoleLogs);
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                ConsoleLogs = sb.ToString();
            });
        }

        #endregion
    }
}
