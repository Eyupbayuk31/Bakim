using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.Activity;
using Bakım.Core.GameMode;
using Bakım.Core.Text;
using Bakım.Services;
using Bakım.Services.Activity;
using Bakım.Views.Dialogs;

namespace Bakım.ViewModels
{
    /// <summary>Güç planı seçeneği: açılır kutuda başlık + açıklama.</summary>
    public sealed record PowerPlanOption(string Key, string Title, string Description);

    /// <summary>"Son oturumlar" satırı.</summary>
    public sealed record GameSessionRow(string WhenText, string Title, string Summary, string OutcomeText, Models.Intent OutcomeIntent, bool ShowOutcome);

    /// <summary>
    /// Oyun Modu sayfası (MASTER_PLAN §2.2, §5.5; docs/OYUN_MODU_TASARIM_PLANI.md).
    /// Durum kartı (kapalı/açık, süre, ölçümler), Fluent ayar kartları, profilden canlı üretilen
    /// "Açınca ne olacak" listesi ve son oturumlar.
    /// </summary>
    public partial class GameModeViewModel : ObservableObject, IModuleViewModel
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
        public const string Shortcut = "Ctrl+Shift+G";

        private readonly IGameModeService _gameMode;
        private readonly IActivityService _activity;
        private readonly IAppSettingsService _settings;
        private readonly IGameLibraryService _library;
        private readonly INavigationService _navigation;
        private readonly DispatcherTimer _clock;
        private bool _loadingProfile;
        private bool _isPageVisible;

        public GameModeViewModel(IGameModeService gameMode, IActivityService activity, IAppSettingsService settings,
            IGameLibraryService library, INavigationService navigation)
        {
            _gameMode = gameMode;
            _activity = activity;
            _settings = settings;
            _library = library;
            _navigation = navigation;

            _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => OnPropertyChanged(nameof(ElapsedText));

            SuspendAppItems.CollectionChanged += (_, _) => OnProfileChanged();
            AutoStartGameItems.CollectionChanged += (_, _) => OnProfileChanged();

            LoadProfile();
            _isActive = gameMode.IsGameModeActive;
            RebuildSteps();

            _gameMode.GameModeChanged += active => OnUi(() =>
            {
                IsActive = active;
                LastSummary = _gameMode.LastActionSummary;
                LoadSessions();
            });
        }

        #region Durum kartı

        public string ShortcutText => Shortcut;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusCaption), nameof(StatusTitle), nameof(StatusDetail), nameof(ElapsedText),
            nameof(StepsHeader), nameof(Session), nameof(PowerPlanMetric), nameof(FreedMemoryMetric), nameof(SuspendedMetric))]
        private bool _isActive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyText))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _errorMessage = string.Empty;

        [ObservableProperty] private string _lastSummary = string.Empty;

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public GameModeSessionInfo? Session => IsActive ? _gameMode.CurrentSession : null;

        public string StatusCaption => IsActive ? "AÇIK" : "KAPALI";

        public string StatusTitle => IsActive ? "Oyun Modu çalışıyor" : "Oyun Modu hazır";

        public string StatusDetail
        {
            get
            {
                if (IsActive)
                    return Session?.TriggerGame is { } game ? $"{game} algılandı · otomatik açıldı" : "Elle açıldı";
                int count = GameModePlan.ActiveStepCount(Steps);
                return $"{count} adım uygulanacak · kapatınca hepsi geri alınır";
            }
        }

        /// <summary>Açık oturumun süresi ("04:07"); kapalıyken boş.</summary>
        public string ElapsedText => Session is { } s ? DurationText.Clock(DateTime.UtcNow - s.StartedAtUtc) : string.Empty;

        public string BusyText => IsActive ? "Kapatılıyor…" : "Açılıyor…";

        public string PowerPlanMetric => Session switch
        {
            null => string.Empty,
            { PowerPlanFailed: true } => "Uygulanamadı",
            { AppliedPowerPlan: null } => "Değiştirilmedi",
            { AppliedPowerPlan: var plan } => plan!
        };

        public string FreedMemoryMetric => Session is { FreedBytes: > 0 } s ? ByteFormatter.Format(s.FreedBytes, Tr) : "—";

        public string SuspendedMetric => Session is { } s
            ? (s.SuspendedApps.Count == 0 ? "Yok" : s.SuspendedApps.Count == 1 ? "1 uygulama" : $"{s.SuspendedApps.Count} uygulama")
            : string.Empty;

        #endregion

        #region Profil (§5.5)

        public IReadOnlyList<PowerPlanOption> PowerPlanOptions { get; } = new List<PowerPlanOption>
        {
            new(GameModePlan.HighPerformance, "Yüksek Performans", "Çoğu oyun için önerilir."),
            new(GameModePlan.Ultimate, "Nihai Performans", "Yoksa Yüksek Performans kullanılır."),
            new(GameModePlan.Keep, "Değiştirme", "Geçerli güç planı olduğu gibi kalır.")
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PowerPlanDescription))]
        private string _powerPlan = GameModePlan.HighPerformance;

        /// <summary>Seçili planın açıklaması (ayar kartının alt satırı).</summary>
        public string PowerPlanDescription =>
            (PowerPlanOptions.FirstOrDefault(o => o.Key == PowerPlan)?.Description ?? string.Empty) +
            (PowerPlan == GameModePlan.Keep ? string.Empty : " Kapatınca önceki plan geri yüklenir.");
        [ObservableProperty] private bool _trimMemory = true;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutoStartHint))]
        private bool _autoStart;

        public string AutoStartHint => AutoStart
            ? "Bu oyunlardan biri çalışınca Oyun Modu başlar."
            : "Anahtarı açınca bu oyunlar izlenir.";

        /// <summary>Oyun süresince askıya alınacak uygulamalar (çip listesi).</summary>
        public ObservableCollection<string> SuspendAppItems { get; } = new();

        /// <summary>Otomatik tetikleyen oyunların süreç adları (çip listesi).</summary>
        public ObservableCollection<string> AutoStartGameItems { get; } = new();

        /// <summary>Çalışan bilinen arka plan uygulamaları (öneri çipleri).</summary>
        [ObservableProperty] private IReadOnlyList<string> _suspendSuggestions = Array.Empty<string>();

        /// <summary>Kütüphanede bulunan oyunlar (öneri çipleri).</summary>
        [ObservableProperty] private IReadOnlyList<string> _gameSuggestions = Array.Empty<string>();

        [ObservableProperty] private bool _isScanningLibrary;

        private void LoadProfile()
        {
            _loadingProfile = true;
            try
            {
                var d = _settings.Current;
                PowerPlan = d.GameModePowerPlan;
                TrimMemory = d.GameModeTrimMemory;
                AutoStart = d.GameModeAutoStart;
                Replace(SuspendAppItems, ProcessNameList.Parse(d.GameModeSuspendApps));
                Replace(AutoStartGameItems, ProcessNameList.Parse(d.GameModeAutoStartExes));
            }
            finally
            {
                _loadingProfile = false;
            }
        }

        private static void Replace(ObservableCollection<string> target, IEnumerable<string> items)
        {
            target.Clear();
            foreach (var item in items) target.Add(item);
        }

        private void OnProfileChanged()
        {
            RebuildSteps();
            if (_loadingProfile) return;
            _settings.Update(d =>
            {
                d.GameModePowerPlan = PowerPlan;
                d.GameModeTrimMemory = TrimMemory;
                d.GameModeSuspendApps = ProcessNameList.Format(SuspendAppItems);
                d.GameModeAutoStart = AutoStart;
                d.GameModeAutoStartExes = ProcessNameList.Format(AutoStartGameItems);
            });
        }

        partial void OnPowerPlanChanged(string value) => OnProfileChanged();
        partial void OnTrimMemoryChanged(bool value) => OnProfileChanged();
        partial void OnAutoStartChanged(bool value) => OnProfileChanged();

        private GameModeProfile CurrentProfile() =>
            new(PowerPlan, TrimMemory, SuspendAppItems.ToList(), AutoStart, AutoStartGameItems.ToList());

        #endregion

        #region Açınca ne olacak

        /// <summary>Profilden (açıkken oturumdan) üretilen adımlar; statik metin değildir.</summary>
        [ObservableProperty] private IReadOnlyList<GameModeStep> _steps = Array.Empty<GameModeStep>();

        public string StepsHeader => IsActive ? "Bu oturumda yapılanlar" : "Açınca ne olacak";

        private void RebuildSteps()
        {
            Steps = GameModePlan.Build(CurrentProfile(), Session);
            OnPropertyChanged(nameof(StatusDetail));
        }

        partial void OnIsActiveChanged(bool value)
        {
            RebuildSteps();
            UpdateClock();
        }

        #endregion

        #region Son oturumlar

        public ObservableCollection<GameSessionRow> Sessions { get; } = new();

        [ObservableProperty] private bool _hasSessions;

        private void LoadSessions()
        {
            var now = DateTime.Now;
            Sessions.Clear();
            foreach (var e in _activity.Entries.Where(e => e.Kind == ActivityKind.GameModeSession).Take(5))
            {
                var row = new ActivityRow(e, now);
                var local = e.AtUtc.ToLocalTime();
                Sessions.Add(new GameSessionRow(
                    $"{ActivityQuery.DayLabel(local, now)} {local.ToString("HH:mm", Tr)}",
                    e.Title, e.Summary, row.OutcomeText, row.OutcomeIntent, row.ShowOutcome));
            }
            HasSessions = Sessions.Count > 0;
        }

        [RelayCommand]
        private void OpenSessionHistory() => _navigation.Navigate("Activity?kind=GameModeSession");

        #endregion

        #region Yaşam döngüsü

        public Task OnActivatedAsync()
        {
            _isPageVisible = true;
            IsActive = _gameMode.IsGameModeActive;
            LastSummary = _gameMode.LastActionSummary;
            RebuildSteps();
            OnPropertyChanged(nameof(Session));
            LoadSessions();
            UpdateClock();
            _ = LoadSuggestionsAsync();
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync()
        {
            _isPageVisible = false;
            UpdateClock();
            return Task.CompletedTask;
        }

        /// <summary>Sayaç yalnızca sayfa görünürken ve oturum açıkken çalışır.</summary>
        private void UpdateClock()
        {
            if (_isPageVisible && IsActive) _clock.Start();
            else _clock.Stop();
        }

        private async Task LoadSuggestionsAsync()
        {
            try
            {
                var running = await Task.Run(() => _library.GetRunningApps().Select(a => a.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase));
                var known = _library.KnownBackgroundApps;
                var runningKnown = known.Where(k => running.Contains(k.Process)).Select(k => k.Process).ToList();
                SuspendSuggestions = runningKnown.Count > 0 ? runningKnown : known.Take(5).Select(k => k.Process).ToList();

                IsScanningLibrary = true;
                var games = await _library.DetectGamesAsync();
                GameSuggestions = games.Select(g => g.ProcessName).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Warning("Oyun Modu önerileri yüklenemedi.", ex, nameof(GameModeViewModel));
            }
            finally
            {
                IsScanningLibrary = false;
            }
        }

        #endregion

        #region Komutlar

        [RelayCommand]
        private async Task ToggleAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                await _gameMode.ToggleGameModeAsync();
                IsActive = _gameMode.IsGameModeActive;
                LastSummary = _gameMode.LastActionSummary;
                OnPropertyChanged(nameof(Session));
                RebuildSteps();
                LoadSessions();
            }
            catch (Exception ex)
            {
                AppLog.Error("Oyun Modu değiştirilemedi.", ex, nameof(GameModeViewModel));
                ErrorMessage = $"Oyun Modu değiştirilemedi: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Çalışan uygulamalardan askıya alınacakları seçtirir.</summary>
        [RelayCommand]
        private async Task PickRunningAppsAsync()
        {
            var apps = await Task.Run(_library.GetRunningApps);
            var items = apps
                .Where(a => !SuspendAppItems.Contains(a.ProcessName, StringComparer.OrdinalIgnoreCase))
                .Select(a => new PickItem(a.ProcessName, a.Description, a.ProcessName, ByteFormatter.Format(a.WorkingSetBytes, Tr)))
                .ToList();
            var picked = PickListDialog.Show(Application.Current?.MainWindow, "Çalışan uygulamalar",
                "Askıya alınacak uygulamaları seçin",
                "Oyun süresince duraklatılırlar, Oyun Modu kapanınca devam ederler. Windows'un kritik süreçleri listelenmez.",
                items, "Seçilebilecek çalışan uygulama yok.");
            foreach (var name in picked) ProcessNameList.AddTo(SuspendAppItems, name);
        }

        /// <summary>Steam / Epic kütüphanesinden otomatik tetikleyen oyunları seçtirir.</summary>
        [RelayCommand]
        private async Task PickFromLibraryAsync()
        {
            IsScanningLibrary = true;
            IReadOnlyList<DetectedGame> games;
            try
            {
                games = await _library.DetectGamesAsync(refresh: true);
                GameSuggestions = games.Select(g => g.ProcessName).ToList();
            }
            finally
            {
                IsScanningLibrary = false;
            }

            var items = games
                .Where(g => !AutoStartGameItems.Contains(g.ProcessName, StringComparer.OrdinalIgnoreCase))
                .Select(g => new PickItem(g.ProcessName, g.DisplayName, $"{g.ProcessName}.exe", g.Source))
                .ToList();
            var picked = PickListDialog.Show(Application.Current?.MainWindow, "Oyun kütüphanesi",
                "Otomatik tetikleyecek oyunları seçin",
                "Steam ve Epic Games kütüphanelerinde bulunan oyunlar. Listede olmayan bir oyunu adını yazarak ekleyebilirsiniz.",
                items, "Steam ya da Epic Games kütüphanesinde oyun bulunamadı.");
            foreach (var name in picked) ProcessNameList.AddTo(AutoStartGameItems, name);
        }

        #endregion

        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }
    }
}
