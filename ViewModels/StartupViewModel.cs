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
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class StartupViewModel : ObservableObject
    {
        private readonly IStartupService _startupService;
        private List<StartupProgramItem> _allPrograms = new();

        public StartupViewModel(IStartupService startupService)
        {
            _startupService = startupService;
            StartupPrograms = new ObservableCollection<StartupProgramItem>();

            _ = RefreshAsync();
        }

        public ObservableCollection<StartupProgramItem> StartupPrograms { get; }

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
                _allPrograms = await _startupService.GetStartupProgramsAsync();
                TotalProgramsCount = _allPrograms.Count;
                CalculateStats();
                ApplyFilter();
                StatusText = $"{TotalProgramsCount} başlangıç ögesi açılış etkisine göre listelendi.";
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
                bool success = await _startupService.SetStartupProgramStateAsync(item, newState);

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

        [RelayCommand]
        public async Task DeleteProgramAsync(StartupProgramItem? item)
        {
            if (item == null) return;

            var result = MessageBox.Show(
                $"'{item.Name}' uygulamasını başlangıçtan tamamen silmek istediğinize emin misiniz?\n\nKonum: {item.RegistryPath}\nDosya: {item.FilePath}\n\nBu işlem geri alınamaz.",
                "Başlangıçtan Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                bool ok = await _startupService.DeleteStartupProgramAsync(item);
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
                bool ok = await _startupService.AddNewStartupProgramAsync(appName, filePath);
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
                    "Sistem açılışını yavaşlatan yüksek etkili aktif bir başlatıcı bulunamadı. Açılış süreniz zaten oldukça optimize!",
                    "Açılış Optimizasyonu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string appList = string.Join("\n• ", highImpacts.Select(p => p.Name));
            var result = MessageBox.Show(
                $"Aşağıdaki {highImpacts.Count} yüksek etkili uygulamanın Windows açılışını geciktirdiği tespit edildi:\n\n• {appList}\n\nBu uygulamaları başlangıçta devre dışı bırakarak açılış sürenizi hızlandırmak ister misiniz?\n(Programlar silinmez, sadece açılışta otomatik çalışmaz.)",
                "Akıllı Açılış Hızlandırma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            IsBusy = true;
            int disabledCount = 0;
            try
            {
                foreach (var app in highImpacts)
                {
                    bool ok = await _startupService.SetStartupProgramStateAsync(app, false);
                    if (ok) disabledCount++;
                }

                CalculateStats();
                ApplyFilter();
                StatusText = $"{disabledCount} adet yüksek etkili uygulama devre dışı bırakıldı, açılış hızlandırıldı!";
                MessageBox.Show(
                    $"{disabledCount} adet uygulama başarıyla devre dışı bırakıldı!\nTahmini açılış kazancı: ~{disabledCount * 1.5:F1} saniye.",
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
            int high = _allPrograms.Count(p => p.ImpactLevel == 3);

            // Tahmini açılış gecikmesi: Yüksek etki ~1.5s, Orta etki ~0.6s, Düşük etki ~0.2s (yalnızca etkin olanlar için)
            double delaySec = _allPrograms.Where(p => p.IsEnabled).Sum(p => p.ImpactLevel switch
            {
                3 => 1.5,
                2 => 0.6,
                _ => 0.2
            });

            Stats = new StartupSummaryStats
            {
                TotalCount = total,
                EnabledCount = enabled,
                DisabledCount = disabled,
                HighImpactCount = high,
                EstimatedBootDelaySeconds = Math.Round(delaySec, 1)
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
}
