using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Bakım.Core.Startup;
using Bakım.Models;
using Bakım.Core.Activity;
using Bakım.Helpers;
using Bakım.Services;
using Bakım.Services.Activity;

namespace Bakım.ViewModels
{
    public partial class StartupViewModel : ObservableObject
    {
        private readonly IStartupService _startupService;
        private readonly IActivityService _activity;
        private List<StartupProgramItem> _allPrograms = new();

        public StartupViewModel(IStartupService startupService, IActivityService activity)
        {
            _startupService = startupService;
            _activity = activity;
            StartupPrograms = new ObservableCollection<StartupProgramItem>();

            _ = RefreshAsync();
        }

        public ObservableCollection<StartupProgramItem> StartupPrograms { get; }

        /// <summary>Son açılışların süresi (Windows ölçümü; soldan sağa eskiden yeniye).</summary>
        public ObservableCollection<BootHistoryBar> BootHistory { get; } = new();

        private BootPerformanceData _bootData = BootPerformanceData.Unavailable("Yükleniyor");

        [ObservableProperty] private string _bootMeasurementNote = string.Empty;
        [ObservableProperty] private bool _hasBootHistory;

        [ObservableProperty]
        private StartupSummaryStats _stats = new();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Windows başlangıcında otomatik çalışan uygulamalar listeleniyor...";

        [ObservableProperty]
        private string _filterText = string.Empty;

        [ObservableProperty]
        private int _totalProgramsCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsAllFilter))]
        [NotifyPropertyChangedFor(nameof(IsEnabledFilter))]
        [NotifyPropertyChangedFor(nameof(IsDisabledFilter))]
        [NotifyPropertyChangedFor(nameof(IsHighImpactFilter))]
        [NotifyPropertyChangedFor(nameof(IsRegistryFilter))]
        [NotifyPropertyChangedFor(nameof(IsFolderFilter))]
        private string _selectedFilter = "All"; // All, Enabled, Disabled, HighImpact, Registry, Folder

        public bool IsAllFilter => SelectedFilter == "All";
        public bool IsEnabledFilter => SelectedFilter == "Enabled";
        public bool IsDisabledFilter => SelectedFilter == "Disabled";
        public bool IsHighImpactFilter => SelectedFilter == "HighImpact";
        public bool IsRegistryFilter => SelectedFilter == "Registry";
        public bool IsFolderFilter => SelectedFilter == "Folder";

        [ObservableProperty]
        private StartupProgramItem? _selectedProgram;

        [ObservableProperty]
        private bool _isDrawerOpen;

        partial void OnSelectedProgramChanged(StartupProgramItem? value)
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
            SelectedProgram = null;
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            ApplyFilter();
        }

        /// <summary>Windows'un ölçtüğü yavaşlamayı satırlara ve açılış geçmişine uygular (tahmin yok).</summary>
        private void ApplyMeasurements()
        {
            foreach (var item in _allPrograms)
            {
                string key = BootEventParser.KeyOf(item.CleanExePath);
                if (_bootData.Available && key.Length > 0 && _bootData.AppImpacts.TryGetValue(key, out var impact))
                {
                    item.ImpactLevel = impact.Level;
                    item.ImpactText = $"Açılışı {BootEventParser.FormatSeconds(impact.AvgDegradationMs)} yavaşlattı";
                    item.EstimatedDelayText = $"Windows ölçümü · {impact.Count} açılış · en fazla {BootEventParser.FormatSeconds(impact.MaxDegradationMs)}";
                }
                else
                {
                    item.ImpactLevel = 0;
                    item.ImpactText = "Ölçüm yok";
                    item.EstimatedDelayText = _bootData.Available
                        ? "Windows bu uygulama için yavaşlama kaydetmedi"
                        : _bootData.Reason ?? string.Empty;
                }
            }

            BootHistory.Clear();
            var boots = _bootData.BootsNewestFirst;
            int max = boots.Count == 0 ? 1 : boots.Max(b => b.BootMs);
            foreach (var boot in boots.Reverse())
                BootHistory.Add(new BootHistoryBar(boot, max));
            HasBootHistory = BootHistory.Count > 0;

            int? trend = BootEventParser.TrendMs(boots);
            BootMeasurementNote = !_bootData.Available
                ? _bootData.Reason ?? string.Empty
                : trend is int t && Math.Abs(t) >= 1000
                    ? $"Son açılış, önceki açılışların ortalamasından {BootEventParser.FormatSeconds(Math.Abs(t))} {(t < 0 ? "kısa" : "uzun")}."
                    : boots.Count > 0 ? "Son açılışlar benzer sürede." : string.Empty;
        }

        [RelayCommand]
        public void SetFilter(string filter)
        {
            SelectedFilter = filter;
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Başlangıç programları taranıyor...";

            try
            {
                var programsTask = _startupService.GetStartupProgramsAsync();
                var bootTask = Task.Run(() => BootPerformanceReader.Read());
                _allPrograms = await programsTask;
                _bootData = await bootTask;
                ApplyMeasurements();
                TotalProgramsCount = _allPrograms.Count;
                CalculateStats();
                ApplyFilter();
                StatusText = _bootData.Available
                    ? $"{TotalProgramsCount} başlangıç öğesi; etki Windows'un açılış ölçümlerinden."
                    : $"{TotalProgramsCount} başlangıç öğesi. {_bootData.Reason}";
            }
            catch (Exception ex)
            {
                StatusText = $"Hata oluştu: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ToggleProgramStateAsync(StartupProgramItem? item)
        {
            if (item == null || item.IsActionBusy) return;

            item.IsActionBusy = true;
            try
            {
                bool newState = item.IsEnabled;
                bool success;
                using (var capture = RegistryCapture.Begin())
                {
                    success = await _startupService.SetStartupProgramStateAsync(item, newState);
                    _activity.RecordRegistryChange(ActivityKind.StartupChange, "Başlangıç",
                        $"'{item.Name}' {(newState ? "etkinleştirildi" : "devre dışı bırakıldı")}",
                        success ? item.RegistryPath : "Yetki yetersiz: durum değiştirilemedi",
                        success ? ActivityOutcome.Succeeded : ActivityOutcome.Failed, capture.Items,
                        deepLink: "Startup", handler: UndoHandlers.StartupApproved);
                }

                if (success)
                {
                    StatusText = newState
                        ? $"'{item.Name}' başlangıçta etkinleştirildi."
                        : $"'{item.Name}' başlangıçta devre dışı bırakıldı.";
                    CalculateStats();
                }
                else
                {
                    item.IsEnabled = !newState;
                    StatusText = "Yetki yetersiz: Başlangıç durumu değiştirilemedi (Yönetici olarak başlatın).";
                }
            }
            catch (Exception ex)
            {
                item.IsEnabled = !item.IsEnabled;
                StatusText = $"Hata: {ex.Message}";
            }
            finally
            {
                item.IsActionBusy = false;
            }
        }

        private static bool IsFolderItem(StartupProgramItem item) =>
            item.LocationType.Contains("Klasör") || item.RegistryPath.Contains("Klasör");

        [RelayCommand]
        public async Task DeleteProgramAsync(StartupProgramItem? item)
        {
            if (item == null) return;

            var result = MessageBox.Show(
                $"'{item.Name}' uygulamasını başlangıçtan silmek istediğinize emin misiniz?\n\nKonum: {item.RegistryPath}\nDosya: {item.FilePath}\n\n" +
                (IsFolderItem(item)
                    ? "Kısayol Geri Dönüşüm Kutusu'na taşınır."
                    : "Kayıt defteri girdisi yedeklenir; Etkinlik Merkezi'nden geri alabilirsiniz."),
                "Başlangıçtan Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                bool ok;
                using (var capture = RegistryCapture.Begin())
                {
                    ok = await _startupService.DeleteStartupProgramAsync(item);
                    var outcome = ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed;
                    string title = $"'{item.Name}' başlangıçtan silindi";
                    if (IsFolderItem(item))
                        _activity.RecordRecycled(ActivityKind.StartupChange, "Başlangıç", title, "Kısayol Geri Dönüşüm Kutusu'na taşındı",
                            outcome, new[] { new ActivityItem(item.FilePath, "Geri Dönüşüm Kutusu", ok ? "Tamam" : "Başarısız") }, deepLink: "Startup");
                    else
                        _activity.RecordRegistryChange(ActivityKind.StartupChange, "Başlangıç", title, item.RegistryPath,
                            outcome, capture.Items, deepLink: "Startup", handler: UndoHandlers.StartupApproved);
                }
                if (ok)
                {
                    _allPrograms.Remove(item);
                    TotalProgramsCount = _allPrograms.Count;
                    if (SelectedProgram == item)
                    {
                        CloseDrawer();
                    }
                    CalculateStats();
                    ApplyFilter();
                    StatusText = $"'{item.Name}' başlangıçtan başarıyla silindi.";
                }
                else
                {
                    MessageBox.Show(
                        "Uygulama başlangıçtan silinemedi. Yetkinizin yeterli olduğundan emin olun (Yönetici olarak çalıştırın).",
                        "Silme Başarısız",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Silme hatası: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task AddNewStartupProgramAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Başlangıca Eklenecek Uygulamayı Seçin",
                Filter = "Çalıştırılabilir Dosyalar (*.exe;*.lnk)|*.exe;*.lnk|Tüm Dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            string filePath = dialog.FileName;
            string appName = Path.GetFileNameWithoutExtension(filePath);

            IsBusy = true;
            try
            {
                bool ok;
                using (var capture = RegistryCapture.Begin())
                {
                    ok = await _startupService.AddNewStartupProgramAsync(appName, filePath);
                    _activity.RecordRegistryChange(ActivityKind.StartupChange, "Başlangıç", $"'{appName}' başlangıca eklendi", filePath,
                        ok ? ActivityOutcome.Succeeded : ActivityOutcome.Failed, capture.Items, deepLink: "Startup", handler: UndoHandlers.StartupApproved);
                }
                if (ok)
                {
                    await RefreshAsync();
                    StatusText = $"'{appName}' başarıyla başlangıç uygulamalarına eklendi!";
                }
                else
                {
                    MessageBox.Show("Yeni uygulama başlangıca eklenirken bir hata oluştu.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Ekleme hatası: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task OptimizeBootAsync()
        {
            var highImpacts = _allPrograms.Where(p => p.IsEnabled && p.ImpactLevel == 3).ToList();
            if (highImpacts.Count == 0)
            {
                MessageBox.Show(
                    _bootData.Available
                        ? "Windows'un açılış ölçümlerinde açılışı 1 saniyeden fazla yavaşlatan etkin bir uygulama yok."
                        : $"Açılışı yavaşlatan uygulamalar Windows'un ölçümlerinden belirlenir. {_bootData.Reason}",
                    "Açılış Optimizasyonu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string appList = string.Join("\n• ", highImpacts.Select(p => p.Name));
            var result = MessageBox.Show(
                $"Windows'un ölçümüne göre şu {highImpacts.Count} uygulama açılışı 1 saniyeden fazla yavaşlattı:\n\n• {appList}\n\nBunlar açılışta devre dışı bırakılsın mı?\n(Programlar silinmez, yalnızca açılışta otomatik çalışmaz; Etkinlik Merkezi'nden geri alınabilir.)",
                "Akıllı Açılış Hızlandırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            int disabledCount = 0;
            try
            {
                using (var capture = RegistryCapture.Begin())
                {
                    var items = new List<ActivityItem>();
                    foreach (var app in highImpacts)
                    {
                        bool ok = await _startupService.SetStartupProgramStateAsync(app, false);
                        if (ok) disabledCount++;
                        items.Add(new ActivityItem(app.Name, "Devre dışı bırak", ok ? "Tamam" : "Başarısız"));
                    }
                    _activity.RecordRegistryChange(ActivityKind.StartupChange, "Başlangıç",
                        $"Açılış hızlandırıldı: {disabledCount} uygulama devre dışı",
                        string.Join(", ", highImpacts.Select(a => a.Name)),
                        ActivityQuery.OutcomeFromCounts(disabledCount, highImpacts.Count - disabledCount), capture.Items,
                        deepLink: "Startup", handler: UndoHandlers.StartupApproved);
                }

                CalculateStats();
                ApplyFilter();
                StatusText = $"{disabledCount} uygulama açılışta devre dışı bırakıldı.";
                MessageBox.Show(
                    $"{disabledCount} uygulama açılışta devre dışı bırakıldı.\nEtkisini bir sonraki açılıştan sonra bu sayfadaki açılış geçmişinde görebilirsiniz.",
                    "Hızlandırma Tamamlandı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void OpenFileLocation(StartupProgramItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.CleanExePath)) return;

            _startupService.OpenFileLocation(item.CleanExePath);
            StatusText = $"Klasör açıldı: {item.Name}";
        }

        [RelayCommand]
        public void CopyRegistryPath(StartupProgramItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.RegistryPath)) return;

            try
            {
                Clipboard.SetText(item.RegistryPath);
                StatusText = "Kayıt konumu panoya kopyalandı.";
            }
            catch { }
        }

        private void CalculateStats()
        {
            int total = _allPrograms.Count;
            int enabled = _allPrograms.Count(p => p.IsEnabled);
            int disabled = _allPrograms.Count(p => !p.IsEnabled);

            Stats = new StartupSummaryStats
            {
                TotalCount = total,
                EnabledCount = enabled,
                DisabledCount = disabled,
                HighImpactCount = _allPrograms.Count(p => p.IsEnabled && p.ImpactLevel == 3),
                LastBootMs = _bootData.BootsNewestFirst.FirstOrDefault()?.BootMs,
                BootDetail = _bootData.Available ? "Son açılış · Windows ölçümü" : "Açılış ölçümü yok"
            };
        }

        private void ApplyFilter()
        {
            StartupPrograms.Clear();
            var query = _allPrograms.AsEnumerable();

            // 1. Kategori / Durum filtresi
            query = SelectedFilter switch
            {
                "Enabled" => query.Where(p => p.IsEnabled),
                "Disabled" => query.Where(p => !p.IsEnabled),
                "HighImpact" => query.Where(p => p.ImpactLevel == 3),
                "Registry" => query.Where(p => p.LocationType.Contains("Kayıt Defteri")),
                "Folder" => query.Where(p => p.LocationType.Contains("Klasör")),
                _ => query
            };

            // 2. Metin araması
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                string text = FilterText.Trim();
                query = query.Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                         p.FilePath.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                         p.Publisher.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                         p.RegistryPath.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var prog in query.OrderByDescending(p => p.ImpactLevel).ThenBy(p => p.Name))
            {
                StartupPrograms.Add(prog);
            }
        }
    }

    /// <summary>Açılış geçmişi çubuğu (yükseklik en uzun açılışa göre 8–64 px).</summary>
    public sealed class BootHistoryBar
    {
        private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

        public BootHistoryBar(BootRecord boot, int maxMs)
        {
            Height = 8 + 56.0 * boot.BootMs / Math.Max(1, maxMs);
            Tooltip = $"{boot.TimeUtc.ToLocalTime().ToString("d MMM HH:mm", Tr)} · {BootEventParser.FormatSeconds(boot.BootMs)}";
        }

        public double Height { get; }
        public string Tooltip { get; }
    }
}
