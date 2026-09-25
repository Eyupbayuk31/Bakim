using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.Activity;
using Bakım.Services;
using Bakım.Services.Activity;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Oyun Modu sayfası (MASTER_PLAN §2.2, §5.5). Eskiden Kontrol Paneli'nde gömülü bir karttı.
    /// Büyük durum kartı, ne yapıldığının açık listesi ve son oturumlar (Etkinlik Merkezi'nden).
    /// </summary>
    public partial class GameModeViewModel : ObservableObject, IModuleViewModel
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        private readonly IGameModeService _gameMode;
        private readonly IActivityService _activity;
        private readonly IAppSettingsService _settings;
        private bool _loadingProfile;

        public GameModeViewModel(IGameModeService gameMode, IActivityService activity, IAppSettingsService settings)
        {
            _gameMode = gameMode;
            _activity = activity;
            _settings = settings;
            LoadProfile();
            _isActive = gameMode.IsGameModeActive;
            _gameMode.GameModeChanged += active => OnUi(() =>
            {
                IsActive = active;
                LastSummary = _gameMode.LastActionSummary;
                LoadSessions();
            });
        }

        public ObservableCollection<ActivityRow> Sessions { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusTitle), nameof(ButtonText), nameof(StatusDetail))]
        private bool _isActive;

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _lastSummary = string.Empty;
        [ObservableProperty] private bool _hasSessions;

        public bool HasLastSummary => !string.IsNullOrWhiteSpace(LastSummary);
        partial void OnLastSummaryChanged(string value) => OnPropertyChanged(nameof(HasLastSummary));

        public string StatusTitle => IsActive ? "Oyun Modu açık" : "Oyun Modu kapalı";
        public string StatusDetail => IsActive
            ? "Kapatınca önceki güç planı birebir geri yüklenir ve Bakım'ın arka plan işleri devam eder."
            : "Açtığınızda aşağıdaki adımlar uygulanır; kapatınca her şey önceki haline döner.";
        public string ButtonText => IsActive ? "Oyun Modunu kapat" : "Oyun Modunu aç";

        #region Profil (§5.5)

        /// <summary>Güç planı seçenekleri: anahtar → görünen ad.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> PowerPlanOptions { get; } = new List<KeyValuePair<string, string>>
        {
            new("HighPerformance", "Yüksek Performans"),
            new("Ultimate", "Nihai Performans (yoksa Yüksek Performans)"),
            new("Keep", "Değiştirme")
        };

        [ObservableProperty] private string _powerPlan = "HighPerformance";
        [ObservableProperty] private bool _trimMemory = true;
        [ObservableProperty] private string _suspendApps = string.Empty;
        [ObservableProperty] private bool _autoStart;
        [ObservableProperty] private string _autoStartExes = string.Empty;

        public string SuspendAppsPreview => Preview(SuspendApps, "Hiçbir uygulama askıya alınmaz.");
        public string AutoStartPreview => Preview(AutoStartExes, "Liste boş: otomatik açılmaz.");

        private static string Preview(string text, string empty)
        {
            var names = GameModeService.ParseProcessList(text);
            return names.Count == 0 ? empty : string.Join(" · ", names);
        }

        private void LoadProfile()
        {
            _loadingProfile = true;
            try
            {
                var d = _settings.Current;
                PowerPlan = d.GameModePowerPlan;
                TrimMemory = d.GameModeTrimMemory;
                SuspendApps = d.GameModeSuspendApps;
                AutoStart = d.GameModeAutoStart;
                AutoStartExes = d.GameModeAutoStartExes;
            }
            finally
            {
                _loadingProfile = false;
            }
        }

        private void SaveProfile()
        {
            if (_loadingProfile) return;
            _settings.Update(d =>
            {
                d.GameModePowerPlan = PowerPlan;
                d.GameModeTrimMemory = TrimMemory;
                d.GameModeSuspendApps = SuspendApps ?? string.Empty;
                d.GameModeAutoStart = AutoStart;
                d.GameModeAutoStartExes = AutoStartExes ?? string.Empty;
            });
        }

        partial void OnPowerPlanChanged(string value) => SaveProfile();
        partial void OnTrimMemoryChanged(bool value) => SaveProfile();
        partial void OnSuspendAppsChanged(string value) { OnPropertyChanged(nameof(SuspendAppsPreview)); SaveProfile(); }
        partial void OnAutoStartChanged(bool value) => SaveProfile();
        partial void OnAutoStartExesChanged(string value) { OnPropertyChanged(nameof(AutoStartPreview)); SaveProfile(); }

        #endregion

        public Task OnActivatedAsync()
        {
            IsActive = _gameMode.IsGameModeActive;
            LastSummary = _gameMode.LastActionSummary;
            LoadSessions();
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync() => Task.CompletedTask;

        [RelayCommand]
        private async Task ToggleAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await _gameMode.ToggleGameModeAsync();
                IsActive = _gameMode.IsGameModeActive;
                LastSummary = _gameMode.LastActionSummary;
            }
            catch (Exception ex)
            {
                AppLog.Error("Oyun Modu değiştirilemedi.", ex, nameof(GameModeViewModel));
                LastSummary = $"Oyun Modu değiştirilemedi: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void LoadSessions()
        {
            var now = DateTime.Now;
            Sessions.Clear();
            foreach (var e in _activity.Entries.Where(e => e.Kind == ActivityKind.GameModeSession).Take(20))
                Sessions.Add(new ActivityRow(e, now));
            HasSessions = Sessions.Count > 0;
        }

        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }
    }
}
