using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    /// <summary>Kurulum geçmişindeki bir oturum.</summary>
    public sealed class SentinelReportRow
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public SentinelReportRow(SetupDeltaReport report)
        {
            Report = report;
            var local = report.InstallTime.ToLocalTime();
            DateText = local.ToString("d MMMM yyyy HH:mm", Tr);
            DayGroup = Core.Activity.ActivityQuery.DayLabel(local, DateTime.Now).ToUpper(Tr);
        }

        public SetupDeltaReport Report { get; }
        public string AppName => string.IsNullOrWhiteSpace(Report.AppName) ? "Adı bilinmeyen kurulum" : Report.AppName;
        public string DateText { get; }
        public string DayGroup { get; }
        public string KindText => Report.Kind switch
        {
            SessionKind.Uninstall => "Kaldırma",
            SessionKind.Update => "Güncelleme",
            SessionKind.Install => "Kurulum",
            _ => "Bilinmiyor"
        };
        public string Summary =>
            $"+{Report.CreatedFiles.Count:N0} dosya · {Report.FormattedSize} · {Report.AddedExecutables.Count} yürütülebilir" +
            (Report.AddedStartupEntries.Count > 0 ? $" · {Report.AddedStartupEntries.Count} başlangıç" : "") +
            (Report.AddedServices.Count > 0 ? $" · {Report.AddedServices.Count} hizmet" : "");
        public string RiskText => string.IsNullOrWhiteSpace(Report.QuickRiskSummary) ? "" : Report.QuickRiskSummary;
        public bool HasVerdict => Report.RiskEvaluated;
        public RiskLevel VerdictLevel => SetupRiskPresentation.ToLevel(Report.RiskVerdict);
        public bool IsRisky => Report.RiskEvaluated && Report.RiskVerdict >= Core.Sentinel.RiskVerdict.Caution;
        public string SourceText => Report.Installer?.SourceSite is { } site ? $"Kaynak: {site}" : string.Empty;
        public bool HasRisk => RiskText.Length > 0;
        public bool HasPersistence => Report.AddedStartupEntries.Count > 0 || Report.AddedServices.Count > 0;
        public bool IsIncomplete => Report.IsPossiblyIncomplete;
        public Intent Intent => HasPersistence ? Intent.Caution : Intent.Success;
        public string PersistenceText => HasPersistence ? "Açılışa/hizmete eklendi" : "Kalıcılık yok";
    }

    /// <summary>
    /// Kurulum Nöbetçisi sayfası (MASTER_PLAN §2.2, §5.15): nöbetçinin durumu ve kurulum geçmişi.
    /// Ayrıntı, analiz ve geri alma mevcut inceleme penceresiyle yapılır.
    /// </summary>
    public partial class SentinelViewModel : ObservableObject, IModuleViewModel
    {
        private readonly ISetupSentinelService _sentinel;
        private readonly IAppSettingsService _settings;
        private List<SetupDeltaReport> _allReports = new();
        private bool _isActive;

        public SentinelViewModel(ISetupSentinelService sentinel, IAppSettingsService settings)
        {
            _sentinel = sentinel;
            _settings = settings;
            _isEnabled = sentinel.IsEnabled;
            _notifyLevel = settings.Current.SentinelNotifyLevel;

            _sentinel.SetupFinished += report => OnUi(() => { if (_isActive) Refresh(); });
            _sentinel.SetupDetected += session => OnUi(UpdateStatus);
        }

        public ObservableCollection<SentinelReportRow> Rows { get; } = new();

        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusTitle = string.Empty;
        [ObservableProperty] private string _statusDetail = string.Empty;
        [ObservableProperty] private bool _isMonitoring;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _persistenceCount;
        [ObservableProperty] private int _riskyCount;
        [ObservableProperty] private int _last30Count;
        [ObservableProperty] private bool _isEmpty = true;
        [ObservableProperty] private string _searchText = string.Empty;

        /// <summary>0 hepsi, 1 yalnızca dikkat gerektirenler, 2 hiçbiri.</summary>
        [ObservableProperty] private int _notifyLevel;

        public IReadOnlyList<string> NotifyLevelOptions { get; } = new[]
        {
            "Her kurulumda bildir", "Yalnızca dikkat gerektirenlerde bildir", "Bildirim gösterme"
        };

        partial void OnNotifyLevelChanged(int value)
        {
            if (value < 0 || value > 2 || _settings.Current.SentinelNotifyLevel == value) return;
            _settings.Update(d => d.SentinelNotifyLevel = value);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        private SentinelReportRow? _selected;

        public bool HasSelection => Selected != null;

        public Core.Sentinel.SentinelProtectionStatus? ProtectionStatus => _sentinel.ProtectionStatus;
        public string ProtectionBadgeText => _sentinel.ProtectionStatus?.BadgeText ?? "Temel Mod";
        public string ProtectionDescription => _sentinel.ProtectionStatus?.Description ?? "Standart mod devrede.";
        public bool IsTamKoruma => _sentinel.ProtectionStatus?.Mode == Core.Sentinel.SentinelProtectionMode.TamKoruma;
        public Intent ProtectionIntent => IsTamKoruma ? Intent.Accent : Intent.Neutral;
        public string ActiveSensorsSummary => _sentinel.ProtectionStatus?.ActiveSensorsSummary ?? string.Empty;

        partial void OnIsEnabledChanged(bool value)
        {
            if (_sentinel.IsEnabled == value) return;
            _sentinel.IsEnabled = value;
            _settings.Update(d => d.IsSentinelSetupGuardEnabled = value);
            UpdateStatus();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        public async Task OnActivatedAsync()
        {
            _isActive = true;
            IsEnabled = _sentinel.IsEnabled;
            UpdateStatus();
            await RefreshAsync();
        }

        public Task OnDeactivatedAsync()
        {
            _isActive = false;
            return Task.CompletedTask;
        }

        private void UpdateStatus()
        {
            IsMonitoring = _sentinel.IsMonitoringActiveSession;
            if (!_sentinel.IsEnabled)
            {
                StatusTitle = "Nöbetçi kapalı";
                StatusDetail = "Yeni kurulumlar izlenmiyor. Açtığınızda .exe ve .msi kurulumlarının eklediği dosyalar, başlangıç girdileri ve hizmetler kaydedilir.";
            }
            else if (IsMonitoring)
            {
                StatusTitle = $"İzleniyor: {_sentinel.ActiveSession?.AppName}";
                StatusDetail = "Kurulum bitince değişiklik raporu burada ve bildirimde görünür.";
            }
            else
            {
                StatusTitle = "Nöbetçi etkin";
                StatusDetail = "Başlatılan kurulumlar otomatik algılanır; her kurulumun eklediği dosya, başlangıç girdisi ve hizmet kaydedilir.";
            }

            OnPropertyChanged(nameof(ProtectionStatus));
            OnPropertyChanged(nameof(ProtectionBadgeText));
            OnPropertyChanged(nameof(ProtectionDescription));
            OnPropertyChanged(nameof(IsTamKoruma));
            OnPropertyChanged(nameof(ProtectionIntent));
            OnPropertyChanged(nameof(ActiveSensorsSummary));
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            UpdateStatus();
            IsLoading = true;
            try
            {
                var reports = await _sentinel.LoadSavedReportsAsync().ConfigureAwait(false);
                _allReports = reports ?? new List<SetupDeltaReport>();
            }
            catch (Exception ex)
            {
                AppLog.Warning("Kurulum geçmişi okunamadı.", ex, nameof(SentinelViewModel));
                _allReports = new List<SetupDeltaReport>();
            }
            finally
            {
                IsLoading = false;
            }

            await OnUiAsync(ApplyFilter);
        }

        public void Refresh() => _ = RefreshAsync();

        private void ApplyFilter()
        {
            var merged = _allReports.Concat(_sentinel.RecentReports)
                .GroupBy(r => r.SessionId)
                .Select(g => g.First())
                .OrderByDescending(r => r.InstallTime)
                .ToList();

            string q = SearchText.Trim();
            var visible = q.Length == 0
                ? merged
                : merged.Where(r => (r.AppName ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                    (r.InstallerPath ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();

            string? selectedId = Selected?.Report.SessionId;
            Rows.Clear();
            foreach (var r in visible) Rows.Add(new SentinelReportRow(r));
            Selected = selectedId == null ? null : Rows.FirstOrDefault(r => r.Report.SessionId == selectedId);

            TotalCount = merged.Count;
            PersistenceCount = merged.Count(r => r.AddedStartupEntries.Count > 0 || r.AddedServices.Count > 0);
            RiskyCount = merged.Count(r => r.RiskEvaluated && r.RiskVerdict >= Core.Sentinel.RiskVerdict.Caution);
            Last30Count = merged.Count(r => r.InstallTime >= DateTime.UtcNow.AddDays(-30));
            IsEmpty = Rows.Count == 0;
        }

        [RelayCommand]
        private void Inspect(SentinelReportRow? row)
        {
            row ??= Selected;
            if (row == null) return;
            try
            {
                var dialog = new Views.Dialogs.InstallationDeltaInspectionDialog(row.Report)
                {
                    Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null
                };
                dialog.Show();
            }
            catch (Exception ex)
            {
                AppLog.Error("Kurulum raporu açılamadı.", ex, nameof(SentinelViewModel));
                MessageBox.Show($"Rapor açılamadı: {ex.Message}", "Kurulum Nöbetçisi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        private static async Task OnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                await dispatcher.InvokeAsync(action);
            }
        }
    }
}
