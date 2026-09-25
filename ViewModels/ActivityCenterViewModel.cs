using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Core.Activity;
using Bakım.Services;
using Bakım.Services.Activity;

namespace Bakım.ViewModels
{
    /// <summary>Etkinlik listesindeki bir satır.</summary>
    public sealed partial class ActivityRow : ObservableObject
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public ActivityRow(ActivityEntry entry, DateTime nowLocal)
        {
            Entry = entry;
            var local = entry.AtUtc.ToLocalTime();
            TimeText = local.ToString("HH:mm", Tr);
            DateText = local.ToString("d MMMM yyyy HH:mm", Tr);
            DayGroup = ActivityQuery.DayLabel(local, nowLocal).ToUpper(Tr);
        }

        public ActivityEntry Entry { get; }
        public string Id => Entry.Id;
        public string TimeText { get; }
        public string DateText { get; }
        public string DayGroup { get; }
        public string Title => Entry.Title;
        public string Module => Entry.Module;
        public string Summary => Entry.Summary;
        public bool HasSummary => !string.IsNullOrWhiteSpace(Entry.Summary);
        public string KindText => ActivityQuery.KindLabel(Entry.Kind);
        public string OutcomeText => ActivityQuery.OutcomeLabel(Entry.Outcome);
        public bool ShowOutcome => Entry.Outcome != ActivityOutcome.Succeeded;
        public string UndoText => ActivityQuery.UndoLabel(Entry.Undo);
        public bool HasUndoText => UndoText.Length > 0;
        public string? UndoMessage => Entry.UndoMessage;
        public bool HasUndoMessage => !string.IsNullOrWhiteSpace(Entry.UndoMessage);
        public bool CanUndo => Entry.CanUndo && !IsUndoing;
        public bool IsRecycleBinUndo => Entry.UndoHandler == UndoHandlers.RecycleBin;
        public string UndoButtonText => IsRecycleBinUndo ? "Geri Dönüşüm Kutusu" : "Geri al";
        public bool HasDeepLink => !string.IsNullOrEmpty(Entry.DeepLink);
        public IReadOnlyList<ActivityItem> Items => Entry.Items;
        public bool HasItems => Entry.Items.Count > 0;
        public string ItemsHeader => Entry.Items.Count >= ActivityEntry.MaxInlineItems
            ? $"İlk {ActivityEntry.MaxInlineItems} öğe"
            : $"{Entry.Items.Count} öğe";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanUndo))]
        private bool _isUndoing;

        public Models.Intent OutcomeIntent => Entry.Outcome switch
        {
            ActivityOutcome.Failed => Models.Intent.Critical,
            ActivityOutcome.PartiallySucceeded or ActivityOutcome.Cancelled => Models.Intent.Caution,
            _ => Models.Intent.Success
        };

        public Models.Intent UndoIntent => Entry.Undo switch
        {
            UndoState.Undoable => Models.Intent.Accent,
            UndoState.Undone => Models.Intent.Success,
            UndoState.UndoFailed => Models.Intent.Critical,
            _ => Models.Intent.Neutral
        };

        public Wpf.Ui.Controls.SymbolRegular KindSymbol => Entry.Kind switch
        {
            ActivityKind.Clean => Wpf.Ui.Controls.SymbolRegular.Broom24,
            ActivityKind.Uninstall => Wpf.Ui.Controls.SymbolRegular.Apps24,
            ActivityKind.SetupSession => Wpf.Ui.Controls.SymbolRegular.BoxCheckmark24,
            ActivityKind.Tweak => Wpf.Ui.Controls.SymbolRegular.Wrench24,
            ActivityKind.StartupChange => Wpf.Ui.Controls.SymbolRegular.Rocket24,
            ActivityKind.ServiceChange => Wpf.Ui.Controls.SymbolRegular.DeveloperBoard24,
            ActivityKind.FirewallRule => Wpf.Ui.Controls.SymbolRegular.ShieldLock24,
            ActivityKind.Quarantine => Wpf.Ui.Controls.SymbolRegular.Shield24,
            ActivityKind.Restore => Wpf.Ui.Controls.SymbolRegular.ArrowUndo20,
            ActivityKind.GameModeSession => Wpf.Ui.Controls.SymbolRegular.Games24,
            ActivityKind.Analysis or ActivityKind.PersistenceScan => Wpf.Ui.Controls.SymbolRegular.ShieldTask24,
            ActivityKind.AppUpdate or ActivityKind.StoreUpdate => Wpf.Ui.Controls.SymbolRegular.ArrowSync24,
            ActivityKind.StoreInstall => Wpf.Ui.Controls.SymbolRegular.Box24,
            _ => Wpf.Ui.Controls.SymbolRegular.History24
        };
    }

    /// <summary>
    /// Etkinlik Merkezi (MASTER_PLAN §7): Bakım'ın sistemde yaptığı her değişikliğin zaman
    /// çizelgesi, filtreleri ve tek tıkla geri alma.
    /// </summary>
    public partial class ActivityCenterViewModel : ObservableObject, IModuleViewModel, INavigationParameterTarget
    {
        private readonly IActivityService _activity;
        private readonly INavigationService _navigation;
        private readonly DispatcherTimer _refreshDebounce;
        private bool _isActive;
        private bool _isDirty = true;

        public ActivityCenterViewModel(IActivityService activity, INavigationService navigation)
        {
            _activity = activity;
            _navigation = navigation;

            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _refreshDebounce.Tick += (_, _) =>
            {
                _refreshDebounce.Stop();
                Refresh();
            };

            // Kayıtlar arka plan iş parçacığından gelebilir; görünmüyorsa yalnızca "kirli" işaretlenir.
            _activity.Changed += (_, _) =>
            {
                _isDirty = true;
                if (!_isActive) return;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _refreshDebounce.Stop();
                    _refreshDebounce.Start();
                });
            };

            KindOptions = new List<KeyValuePair<ActivityKind?, string>> { new(null, "Tüm türler") };
            KindOptions.AddRange(Enum.GetValues<ActivityKind>().Select(k => new KeyValuePair<ActivityKind?, string>(k, ActivityQuery.KindLabel(k))));
            OutcomeOptions = new List<KeyValuePair<ActivityOutcome?, string>> { new(null, "Tüm sonuçlar") };
            OutcomeOptions.AddRange(Enum.GetValues<ActivityOutcome>().Select(o => new KeyValuePair<ActivityOutcome?, string>(o, ActivityQuery.OutcomeLabel(o))));
        }

        public ObservableCollection<ActivityRow> Rows { get; } = new();
        public List<KeyValuePair<ActivityKind?, string>> KindOptions { get; }
        public List<KeyValuePair<ActivityOutcome?, string>> OutcomeOptions { get; }

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private ActivityKind? _selectedKind;
        [ObservableProperty] private ActivityOutcome? _selectedOutcome;
        [ObservableProperty] private bool _undoableOnly;
        [ObservableProperty] private string _range = "All";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        private ActivityRow? _selected;

        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _undoableCount;
        [ObservableProperty] private int _todayCount;
        [ObservableProperty] private int _failedCount;
        [ObservableProperty] private bool _isEmpty = true;
        [ObservableProperty] private string _statusText = string.Empty;

        public bool HasSelection => Selected != null;

        partial void OnSearchTextChanged(string value) => ScheduleRefresh();
        partial void OnSelectedKindChanged(ActivityKind? value) => Refresh();
        partial void OnSelectedOutcomeChanged(ActivityOutcome? value) => Refresh();
        partial void OnUndoableOnlyChanged(bool value) => Refresh();
        partial void OnRangeChanged(string value) => Refresh();

        /// <summary>"kind=Uninstall" gibi parametre: filtreler sıfırlanır, yalnızca o tür gösterilir.</summary>
        public void ApplyNavigationParameter(string parameter)
        {
            ActivityKind? kind = null;
            foreach (string part in parameter.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("kind", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(kv[1], ignoreCase: true, out ActivityKind parsed))
                    kind = parsed;
            }
            if (kind == null) return;

            SearchText = string.Empty;
            SelectedOutcome = null;
            UndoableOnly = false;
            Range = "All";
            SelectedKind = kind;
            _isDirty = true;
        }

        public Task OnActivatedAsync()
        {
            _isActive = true;
            if (_isDirty) Refresh();
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync()
        {
            _isActive = false;
            _refreshDebounce.Stop();
            return Task.CompletedTask;
        }

        private void ScheduleRefresh()
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        [RelayCommand]
        private void SetRange(string range) => Range = range;

        [RelayCommand]
        public void Refresh()
        {
            _isDirty = false;
            var all = _activity.Entries;
            var nowLocal = DateTime.Now;

            DateTime? from = Range switch
            {
                "Today" => nowLocal.Date.ToUniversalTime(),
                "7" => DateTime.UtcNow.AddDays(-7),
                "30" => DateTime.UtcNow.AddDays(-30),
                _ => null
            };
            var filter = new ActivityFilter(SearchText, SelectedKind, SelectedOutcome, UndoableOnly, from);
            var visible = ActivityQuery.Filter(all, filter).Take(1000).ToList();

            string? selectedId = Selected?.Id;
            Rows.Clear();
            foreach (var e in visible) Rows.Add(new ActivityRow(e, nowLocal));
            Selected = selectedId == null ? null : Rows.FirstOrDefault(r => r.Id == selectedId);

            TotalCount = all.Count;
            UndoableCount = all.Count(e => e.CanUndo);
            TodayCount = all.Count(e => e.AtUtc.ToLocalTime().Date == nowLocal.Date);
            FailedCount = all.Count(e => e.Outcome == ActivityOutcome.Failed);
            IsEmpty = Rows.Count == 0;
            StatusText = visible.Count == all.Count
                ? $"{all.Count:N0} kayıt"
                : $"{visible.Count:N0} / {all.Count:N0} kayıt gösteriliyor";
        }

        [RelayCommand]
        private async Task UndoAsync(ActivityRow? row)
        {
            row ??= Selected;
            if (row == null || !row.CanUndo) return;

            if (!row.IsRecycleBinUndo)
            {
                var confirm = System.Windows.MessageBox.Show(
                    $"Bu işlem geri alınsın mı?\n\n{row.Title}\n{row.Summary}\n\n" +
                    "Değiştirilen değerler özgün hallerine döndürülür. HKLM gibi korumalı konumlar için bir kez yönetici izni istenebilir.",
                    "Geri Al", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes) return;
            }

            row.IsUndoing = true;
            StatusText = $"Geri alınıyor: {row.Title}";
            try
            {
                var result = await _activity.UndoAsync(row.Id);
                StatusText = result.Message;
                if (!result.Manual)
                {
                    System.Windows.MessageBox.Show(result.Message,
                        result.Success ? "Geri Alındı" : "Geri Alma Tamamlanamadı",
                        MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            finally
            {
                row.IsUndoing = false;
            }
            Refresh();
        }

        [RelayCommand]
        private void OpenDeepLink(ActivityRow? row)
        {
            row ??= Selected;
            if (row?.Entry.DeepLink is { Length: > 0 } link) _navigation.Navigate(link);
        }

        [RelayCommand]
        private void Export()
        {
            var entries = Rows.Select(r => r.Entry).ToList();
            if (entries.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Etkinlik kaydını dışa aktar",
                FileName = $"Bakim_Etkinlik_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                Filter = "CSV (*.csv)|*.csv"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                // UTF-8 BOM: Excel Türkçe karakterleri doğru açar.
                File.WriteAllText(dialog.FileName, ActivityQuery.ToCsv(entries), new UTF8Encoding(true));
                StatusText = $"{entries.Count:N0} kayıt dışa aktarıldı: {dialog.FileName}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = $"Dışa aktarılamadı: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ClearAll()
        {
            var confirm = System.Windows.MessageBox.Show(
                "Etkinlik kaydının tamamı ve geri alma yedekleri silinsin mi?\n\nBu işlemden sonra listelenen hiçbir değişiklik Bakım'dan geri alınamaz.",
                "Etkinlik Kaydını Temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            _activity.Clear();
            Selected = null;
            Refresh();
        }
    }
}
