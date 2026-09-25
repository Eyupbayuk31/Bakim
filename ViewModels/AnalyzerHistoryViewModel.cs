using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.History;
using Bakım.Core.Text;
using Bakım.Services;
using Bakım.Services.History;

namespace Bakım.ViewModels
{
    /// <summary>Geçmiş listesindeki bir satır (gösterim için hazırlanmış alanlar).</summary>
    public sealed class AnalysisRecordRow
    {
        public AnalysisRecordRow(AnalysisRecord record)
        {
            Record = record;
            var local = record.AnalyzedAtUtc.ToLocalTime();
            TimeText = local.ToString("HH:mm");
            DateText = local.ToString("d MMM yyyy HH:mm", new System.Globalization.CultureInfo("tr-TR"));
            DayGroup = DayGroupOf(local);
        }

        public AnalysisRecord Record { get; }
        public string Id => Record.Id;
        public string FileName => Record.FileName;
        public string FilePath => Record.FilePath;
        public string ShortPath => Shorten(HistoryExport.MaskUserName(Path.GetDirectoryName(Record.FilePath) ?? string.Empty));
        public int RiskScore => Record.RiskScore;
        public string VerdictText => AnalysisVerdicts.Display(Record.Verdict);
        public string RiskBadge => $"{VerdictText} {Record.RiskScore}";
        public string SourceText => AnalysisVerdicts.Display(Record.Source);
        public string DecisionText => AnalysisVerdicts.Display(Record.Decision);
        public bool HasDecision => Record.Decision != UserDecision.None;
        public string VtText => Record.VirusTotalMalicious.HasValue && Record.VirusTotalTotal.HasValue
            ? $"VT {Record.VirusTotalMalicious}/{Record.VirusTotalTotal}" : string.Empty;
        public bool HasVt => VtText.Length > 0;
        public bool HasVtHit => Record.HasVirusTotalHit;
        public string TimeText { get; }
        public string DateText { get; }
        public string DayGroup { get; }

        /// <summary>Renkten bağımsız risk düzeyi: Critical / Caution / Success (StatusBadge Intent'i).</summary>
        public Models.Intent Intent => Record.Verdict switch
        {
            AnalysisVerdict.Dangerous => Models.Intent.Critical,
            AnalysisVerdict.Suspicious or AnalysisVerdict.Caution => Models.Intent.Caution,
            AnalysisVerdict.Missing => Models.Intent.Neutral,
            _ => Models.Intent.Success
        };

        private static string DayGroupOf(DateTime local)
        {
            var today = DateTime.Now.Date;
            if (local.Date == today) return "BUGÜN";
            if (local.Date == today.AddDays(-1)) return "DÜN";
            return local.ToString("d MMMM yyyy, dddd", new System.Globalization.CultureInfo("tr-TR")).ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        private static string Shorten(string path) => path.Length <= 70 ? path : "…" + path[^69..];
    }

    /// <summary>Tarama seçicisinde gösterilen öğe.</summary>
    public sealed record SnapshotOption(SnapshotHeader Header)
    {
        public string Display => $"{AnalyzerHistoryViewModel.Describe(Header)} · {Header.EntryCount} girdi";
    }

    public sealed record DiffRow(SnapshotDiffItem Item)
    {
        public string KindText => Item.Kind switch
        {
            DiffKind.Added => "Eklendi",
            DiffKind.Removed => "Kaldırıldı",
            _ => "Değişti"
        };
        public string Symbol => Item.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => "~" };
        public Models.Intent Intent => Item.Kind switch { DiffKind.Added => Models.Intent.Caution, DiffKind.Removed => Models.Intent.Neutral, _ => Models.Intent.Accent };
        public string Category => Item.Current.Category;
        public string Name => Item.Current.Name;
        public string FilePath => HistoryExport.MaskUserName(Item.Current.FilePath);
        public string SignatureText => RecordComparer.SignatureDisplay(Item.Current.SignatureStatus) +
                                       (string.IsNullOrEmpty(Item.Current.Signer) ? "" : $": {Item.Current.Signer}");
        public string ChangeText => Item.Kind != DiffKind.Changed ? string.Empty : string.Join(" · ", Item.ChangedFields.Select(Describe));
        public bool HasChangeText => ChangeText.Length > 0;
        public bool CanInspect => Item.After != null && !string.IsNullOrWhiteSpace(Item.After.FilePath);

        private string Describe(string field)
        {
            var b = Item.Before!;
            var a = Item.After!;
            string label = SnapshotDiff.FieldDisplay(field);
            return field switch
            {
                nameof(PersistenceEntry.FilePath) => $"{label}: {HistoryExport.MaskUserName(b.FilePath)} → {HistoryExport.MaskUserName(a.FilePath)}",
                nameof(PersistenceEntry.IsEnabled) => $"{label}: {(b.IsEnabled ? "Açık" : "Kapalı")} → {(a.IsEnabled ? "Açık" : "Kapalı")}",
                nameof(PersistenceEntry.SignatureStatus) => $"{label}: {RecordComparer.SignatureDisplay(b.SignatureStatus)} → {RecordComparer.SignatureDisplay(a.SignatureStatus)}",
                nameof(PersistenceEntry.Signer) => $"{label}: {b.Signer ?? "—"} → {a.Signer ?? "—"}",
                _ => $"{label} değişti"
            };
        }
    }

    /// <summary>
    /// Analizör'ün "Geçmiş" ve "Değişiklikler" sekmeleri (§6.6).
    /// </summary>
    public partial class AnalyzerHistoryViewModel : ObservableObject
    {
        private readonly IAnalysisHistoryService _history;
        private readonly IFileThreatAnalyzerService _analyzer;
        private readonly DispatcherTimer _refreshDebounce;

        public ObservableCollection<AnalysisRecordRow> Rows { get; } = new();
        public ObservableCollection<AnalysisRecordRow> RelatedRows { get; } = new();
        public ObservableCollection<string> CompareLines { get; } = new();
        public ObservableCollection<AnalysisFactorSummary> SelectedFactors { get; } = new();
        public ObservableCollection<DiffRow> DiffRows { get; } = new();
        public ObservableCollection<SnapshotOption> Snapshots { get; } = new();

        public IReadOnlyList<KeyValuePair<AnalysisSource?, string>> SourceOptions { get; } =
            new[] { new KeyValuePair<AnalysisSource?, string>(null, "Tüm kaynaklar") }
                .Concat(Enum.GetValues<AnalysisSource>().Where(s => s != AnalysisSource.Unknown)
                    .Select(s => new KeyValuePair<AnalysisSource?, string>(s, AnalysisVerdicts.Display(s))))
                .ToList();

        #region Filtre durumu

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private string _rangeKey = "All";
        [ObservableProperty] private bool _showDangerous;
        [ObservableProperty] private bool _showSuspicious;
        [ObservableProperty] private bool _showCaution;
        [ObservableProperty] private bool _onlyVirusTotalHits;
        [ObservableProperty] private bool _onlyUndecided;
        [ObservableProperty] private AnalysisSource? _selectedSource;
        [ObservableProperty] private bool _maskUserNameOnExport = true;

        partial void OnSearchTextChanged(string value) => ScheduleRefresh();
        partial void OnRangeKeyChanged(string value) => ScheduleRefresh();
        partial void OnShowDangerousChanged(bool value) => ScheduleRefresh();
        partial void OnShowSuspiciousChanged(bool value) => ScheduleRefresh();
        partial void OnShowCautionChanged(bool value) => ScheduleRefresh();
        partial void OnOnlyVirusTotalHitsChanged(bool value) => ScheduleRefresh();
        partial void OnOnlyUndecidedChanged(bool value) => ScheduleRefresh();
        partial void OnSelectedSourceChanged(AnalysisSource? value) => ScheduleRefresh();

        #endregion

        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private string _listSummary = string.Empty;
        [ObservableProperty] private bool _isEmpty = true;
        [ObservableProperty] private string _statusText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedFileExists), nameof(SelectedVtText), nameof(SelectedSignatureText))]
        private AnalysisRecordRow? _selected;

        [ObservableProperty] private string _noteText = string.Empty;

        public bool HasSelection => Selected != null;
        public bool SelectedFileExists => Selected != null && File.Exists(Selected.FilePath);
        public string SelectedVtText => Selected?.Record is { } r && r.VirusTotalMalicious.HasValue && r.VirusTotalTotal.HasValue
            ? $"{r.VirusTotalMalicious}/{r.VirusTotalTotal}" + (r.VirusTotalCheckedAtUtc is { } at ? $" ({at.ToLocalTime():d MMM})" : string.Empty)
            : "Sorgulanmadı";
        public string SelectedSignatureText => Selected?.Record is { } r
            ? RecordComparer.SignatureDisplay(r.SignatureStatus) + (string.IsNullOrEmpty(r.Signer) ? "" : $" — {r.Signer}") + (r.IsCatalogSigned ? " (Windows kataloğu)" : "")
            : string.Empty;

        #region Değişiklikler sekmesi

        [ObservableProperty] private SnapshotOption? _olderSnapshot;
        [ObservableProperty] private SnapshotOption? _newerSnapshot;
        [ObservableProperty] private int _addedCount;
        [ObservableProperty] private int _changedCount;
        [ObservableProperty] private int _removedCount;
        [ObservableProperty] private string _diffSummary = "Karşılaştırma için en az iki tarama gerekir.";
        [ObservableProperty] private bool _hasDiff;

        /// <summary>Sekme başlığındaki rozet: son iki tarama arasında eklenen + değişen girdi.</summary>
        public int PendingChangesCount => AddedCount + ChangedCount;

        partial void OnAddedCountChanged(int value) => OnPropertyChanged(nameof(PendingChangesCount));
        partial void OnChangedCountChanged(int value) => OnPropertyChanged(nameof(PendingChangesCount));
        partial void OnOlderSnapshotChanged(SnapshotOption? value) => LoadDiff();
        partial void OnNewerSnapshotChanged(SnapshotOption? value) => LoadDiff();

        #endregion

        public AnalyzerHistoryViewModel(IAnalysisHistoryService history, IFileThreatAnalyzerService analyzer)
        {
            _history = history;
            _analyzer = analyzer;

            _refreshDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _refreshDebounce.Tick += (_, _) =>
            {
                _refreshDebounce.Stop();
                Refresh();
            };

            _history.RecordAdded += _ => DispatchRefresh();
            _history.HistoryChanged += () =>
            {
                _snapshotsDirty = true;
                DispatchRefresh();
            };
        }

        private bool _snapshotsDirty = true;

        /// <summary>Geçmişi arka planda yükler (açılışta arayüzü bekletmez), sonra listeyi doldurur.</summary>
        public async Task InitializeAsync()
        {
            try
            {
                await Task.Run(() => _history.Count);
                Refresh();
            }
            catch (Exception ex)
            {
                StatusText = $"Geçmiş yüklenemedi: {ex.Message}";
            }
        }

        private void DispatchRefresh()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(ScheduleRefresh);
        }

        private void ScheduleRefresh()
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        [RelayCommand]
        public void Refresh()
        {
            string? selectedId = Selected?.Id;
            var verdicts = new List<AnalysisVerdict>();
            if (ShowDangerous) verdicts.Add(AnalysisVerdict.Dangerous);
            if (ShowSuspicious) verdicts.Add(AnalysisVerdict.Suspicious);
            if (ShowCaution) verdicts.Add(AnalysisVerdict.Caution);

            var filter = new AnalysisHistoryFilter
            {
                Text = SearchText,
                Range = RangeKey switch
                {
                    "Today" => HistoryDateRange.Today,
                    "7" => HistoryDateRange.Last7Days,
                    "30" => HistoryDateRange.Last30Days,
                    _ => HistoryDateRange.All
                },
                Verdicts = verdicts,
                Source = SelectedSource,
                OnlyVirusTotalHits = OnlyVirusTotalHits,
                OnlyUndecided = OnlyUndecided,
                Limit = 2000
            };

            var records = _history.Query(filter);
            Rows.Clear();
            foreach (var r in records) Rows.Add(new AnalysisRecordRow(r));

            TotalCount = _history.Count;
            IsEmpty = Rows.Count == 0;
            ListSummary = Rows.Count == TotalCount
                ? $"{TotalCount} analiz"
                : $"{Rows.Count} / {TotalCount} analiz gösteriliyor";

            Selected = selectedId == null ? Rows.FirstOrDefault() : Rows.FirstOrDefault(r => r.Id == selectedId) ?? Rows.FirstOrDefault();

            // Anlık görüntü farkı yalnızca taramalar değiştiğinde yeniden hesaplanır (her yeni analizde değil).
            if (_snapshotsDirty) RefreshSnapshots();
        }

        partial void OnSelectedChanged(AnalysisRecordRow? value)
        {
            RelatedRows.Clear();
            CompareLines.Clear();
            SelectedFactors.Clear();
            NoteText = value?.Record.Note ?? string.Empty;
            if (value == null) return;

            foreach (var f in value.Record.Factors.OrderByDescending(f => f.ScoreImpact)) SelectedFactors.Add(f);

            var related = _history.GetRelated(value.Record);
            foreach (var r in related.Where(r => r.Id != value.Id)) RelatedRows.Add(new AnalysisRecordRow(r));

            // "Farkı göster": bu kayıttan önceki en yakın analizle karşılaştır.
            var previous = related.Where(r => r.AnalyzedAtUtc < value.Record.AnalyzedAtUtc).OrderByDescending(r => r.AnalyzedAtUtc).FirstOrDefault();
            if (previous != null)
            {
                var lines = RecordComparer.Compare(previous, value.Record);
                if (lines.Count == 0) CompareLines.Add("Önceki analizden (" + previous.AnalyzedAtUtc.ToLocalTime().ToString("d MMM HH:mm") + ") bu yana değişiklik yok.");
                foreach (var l in lines) CompareLines.Add(l);
            }
        }

        [RelayCommand]
        public void SetRange(string key) => RangeKey = key;

        [RelayCommand]
        private async Task TrustAsync()
        {
            if (Selected == null) return;
            await _history.SetDecisionAsync(Selected.Id, UserDecision.Trusted);
            StatusText = $"'{Selected.FileName}' güvenilir olarak işaretlendi; aynı dosya her yerde \"Güvenilen\" görünecek.";
        }

        [RelayCommand]
        private async Task IgnoreAsync()
        {
            if (Selected == null) return;
            await _history.SetDecisionAsync(Selected.Id, UserDecision.Ignored);
            StatusText = $"'{Selected.FileName}' yok sayıldı.";
        }

        [RelayCommand]
        private async Task ClearDecisionAsync()
        {
            if (Selected == null) return;
            await _history.SetDecisionAsync(Selected.Id, UserDecision.None);
            StatusText = "Karar kaldırıldı.";
        }

        [RelayCommand]
        private async Task SaveNoteAsync()
        {
            if (Selected == null) return;
            await _history.SetNoteAsync(Selected.Id, NoteText);
            StatusText = "Not kaydedildi.";
        }

        [RelayCommand]
        private void CopyHash()
        {
            if (string.IsNullOrEmpty(Selected?.Record.Sha256)) return;
            try
            {
                Clipboard.SetText(Selected.Record.Sha256);
                StatusText = "SHA-256 panoya kopyalandı.";
            }
            catch (Exception ex)
            {
                StatusText = $"Kopyalanamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        private void OpenLocation()
        {
            if (Selected == null) return;
            try
            {
                if (File.Exists(Selected.FilePath))
                    Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", Selected.FilePath }, UseShellExecute = false });
                else
                    StatusText = "Dosya artık yok.";
            }
            catch (Exception ex)
            {
                StatusText = $"Konum açılamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ReanalyzeAsync()
        {
            if (Selected == null) return;
            if (!File.Exists(Selected.FilePath))
            {
                StatusText = "Dosya artık yok; yeniden analiz edilemez.";
                return;
            }
            await AnalyzeAndShowAsync(Selected.FilePath, Selected.Record.Source == AnalysisSource.Unknown ? AnalysisSource.Analyzer : Selected.Record.Source, "Yeniden analiz");
        }

        [RelayCommand]
        private async Task InspectDiffAsync(DiffRow? row)
        {
            if (row?.Item.After == null) return;
            string path = row.Item.After.FilePath.Trim().Trim('"');
            if (!File.Exists(path))
            {
                StatusText = "Girdinin gösterdiği dosya bulunamadı.";
                return;
            }
            await AnalyzeAndShowAsync(path, AnalysisSource.Analyzer, $"Değişiklik: {row.Name}");
        }

        private async Task AnalyzeAndShowAsync(string path, AnalysisSource source, string detail)
        {
            StatusText = $"{Path.GetFileName(path)} analiz ediliyor...";
            try
            {
                Models.ThreatAnalysisResult result;
                using (AnalysisContext.Begin(source, detail))
                {
                    result = await _analyzer.AnalyzeFileAsync(path);
                }

                var dialog = new Views.Dialogs.ThreatAnalysisDialog(result, _analyzer);
                if (Application.Current.MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
                dialog.ShowDialog();
                StatusText = "Analiz tamamlandı ve geçmişe eklendi.";
            }
            catch (Exception ex)
            {
                StatusText = $"Analiz başarısız: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task RemoveSelectedAsync()
        {
            if (Selected == null) return;
            var confirm = MessageBox.Show($"'{Selected.FileName}' analizi geçmişten kaldırılsın mı?", "Geçmişten Kaldır",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            await _history.RemoveAsync(new[] { Selected.Id });
            StatusText = "Kayıt geçmişten kaldırıldı.";
        }

        [RelayCommand]
        private async Task ClearAllAsync()
        {
            var confirm = MessageBox.Show(
                "Analizör geçmişinin tamamı (tüm analizler, kararlar, notlar ve kalıcılık tarama anlık görüntüleri) silinecek. Bu işlem geri alınamaz.\n\nDevam edilsin mi?",
                "Geçmişi Temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;
            await _history.ClearAllAsync();
            StatusText = "Analizör geçmişi temizlendi.";
        }

        [RelayCommand]
        private async Task ExportAsync(string? format)
        {
            var fmt = format switch { "Json" => ExportFormat.Json, "Html" => ExportFormat.Html, _ => ExportFormat.Csv };
            string ext = fmt switch { ExportFormat.Json => "json", ExportFormat.Html => "html", _ => "csv" };
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Analizör geçmişini dışa aktar",
                FileName = $"Bakim_Analiz_Gecmisi_{DateTime.Now:yyyyMMdd_HHmm}.{ext}",
                Filter = fmt switch
                {
                    ExportFormat.Json => "JSON (*.json)|*.json",
                    ExportFormat.Html => "HTML raporu (*.html)|*.html",
                    _ => "CSV (*.csv)|*.csv"
                }
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var filter = new AnalysisHistoryFilter
                {
                    Text = SearchText,
                    Source = SelectedSource,
                    OnlyVirusTotalHits = OnlyVirusTotalHits,
                    OnlyUndecided = OnlyUndecided
                };
                await _history.ExportAsync(dialog.FileName, fmt, filter, MaskUserNameOnExport);
                StatusText = $"Dışa aktarıldı: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"Dışa aktarılamadı: {ex.Message}";
            }
        }

        #region Değişiklikler

        private bool _suppressDiffReload;

        public void RefreshSnapshots()
        {
            _snapshotsDirty = false;
            var list = _history.ListSnapshots();
            _suppressDiffReload = true;
            try
            {
                Snapshots.Clear();
                foreach (var s in list) Snapshots.Add(new SnapshotOption(s));
                NewerSnapshot = Snapshots.ElementAtOrDefault(0);
                OlderSnapshot = Snapshots.ElementAtOrDefault(1);
            }
            finally
            {
                _suppressDiffReload = false;
            }
            LoadDiff();
        }

        private void LoadDiff()
        {
            if (_suppressDiffReload) return;
            DiffRows.Clear();
            if (OlderSnapshot == null || NewerSnapshot == null || OlderSnapshot.Header.Id == NewerSnapshot.Header.Id)
            {
                AddedCount = ChangedCount = RemovedCount = 0;
                HasDiff = false;
                DiffSummary = Snapshots.Count < 2
                    ? "Karşılaştırma için en az iki kalıcılık taraması gerekir. Analizör her taramayı otomatik kaydeder."
                    : "Karşılaştırmak için iki farklı tarama seçin.";
                return;
            }

            // Eski → yeni sırası: kullanıcı ters seçtiyse düzelt.
            var (older, newer) = OlderSnapshot.Header.TakenAtUtc <= NewerSnapshot.Header.TakenAtUtc
                ? (OlderSnapshot.Header, NewerSnapshot.Header)
                : (NewerSnapshot.Header, OlderSnapshot.Header);
            var diff = _history.Diff(older.Id, newer.Id);
            foreach (var d in diff) DiffRows.Add(new DiffRow(d));

            AddedCount = diff.Count(d => d.Kind == DiffKind.Added);
            ChangedCount = diff.Count(d => d.Kind == DiffKind.Changed);
            RemovedCount = diff.Count(d => d.Kind == DiffKind.Removed);
            HasDiff = diff.Count > 0;
            DiffSummary = diff.Count == 0
                ? $"{Describe(older)} ile {Describe(newer)} arasında değişiklik yok."
                : $"{Describe(older)} ↔ {Describe(newer)}: {AddedCount} eklendi, {ChangedCount} değişti, {RemovedCount} kaldırıldı.";
        }

        public static string Describe(SnapshotHeader h) =>
            $"{h.TakenAtUtc.ToLocalTime():d MMM HH:mm} ({TriggerText(h.Trigger)})";

        public static string TriggerText(string trigger) => trigger switch
        {
            "Scheduled" => "zamanlanmış",
            "AfterInstall" => "kurulum sonrası",
            "Startup" => "açılış",
            _ => "elle"
        };

        #endregion
    }
}
