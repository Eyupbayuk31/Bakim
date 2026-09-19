using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class StartupViewModel : ObservableObject
    {
        private readonly IStartupService _startupService;
        private List<StartupProgramItem> _allPrograms = new();

        public StartupViewModel() : this(null)
        {
        }

        public StartupViewModel(IStartupService? startupService)
        {
            _startupService = startupService ?? new StartupService();
            StartupPrograms = new ObservableCollection<StartupProgramItem>();

            _ = RefreshAsync();
        }

        public ObservableCollection<StartupProgramItem> StartupPrograms { get; }

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "Windows başlangıcında otomatik çalışan uygulamalar listeleniyor...";

        [ObservableProperty]
        private string _filterText = string.Empty;

        [ObservableProperty]
        private int _totalProgramsCount;

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
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
                }
                else
                {
                    // Başarısız olursa toggle durumunu eski haline döndür
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
        public void OpenFileLocation(StartupProgramItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;

            _startupService.OpenFileLocation(item.FilePath);
            StatusText = $"Klasör açıldı: {item.Name}";
        }

        private void ApplyFilter()
        {
            StartupPrograms.Clear();
            var filtered = string.IsNullOrWhiteSpace(FilterText)
                ? _allPrograms
                : _allPrograms.Where(p => p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                                          p.FilePath.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

            foreach (var prog in filtered.OrderByDescending(p => p.ImpactLevel).ThenBy(p => p.Name))
            {
                StartupPrograms.Add(prog);
            }
        }
    }
}
