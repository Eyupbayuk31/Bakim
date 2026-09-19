using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public partial class ResidualCleanupViewModel : ObservableObject
    {
        private readonly IResidualScannerEngine _scannerEngine;
        private readonly ICollectionView _filteredView;

        public InstalledAppItem TargetApp { get; }

        public ObservableCollection<ResidualItem> Residuals { get; } = new();
        public ICollectionView FilteredResiduals => _filteredView;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private bool _isAllSelected = true;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _selectedCount;

        [ObservableProperty]
        private long _selectedSizeBytes;

        [ObservableProperty]
        private string _formattedSelectedSize = "0 B";

        [ObservableProperty]
        private bool _isCleaning;

        [ObservableProperty]
        private double _cleanProgress;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _wasCleaned;

        [ObservableProperty]
        private int _cleanedCount;

        public event Action<bool>? RequestClose;

        public string AppName => !string.IsNullOrWhiteSpace(TargetApp?.DisplayName) 
            ? TargetApp.DisplayName 
            : "Bilinmeyen Program";

        public string Publisher => !string.IsNullOrWhiteSpace(TargetApp?.Publisher) 
            ? TargetApp.Publisher 
            : "Yayımcı Belirtilmemiş";

        public string DisplayIconPath => TargetApp?.DisplayIconPath ?? string.Empty;

        public ResidualCleanupViewModel(InstalledAppItem targetApp, IEnumerable<ResidualItem> items, IResidualScannerEngine? scannerEngine = null)
        {
            TargetApp = targetApp;
            _scannerEngine = scannerEngine ?? new ResidualScannerEngine();

            foreach (var item in items)
            {
                item.PropertyChanged += OnItemPropertyChanged;
                Residuals.Add(item);
            }

            _filteredView = CollectionViewSource.GetDefaultView(Residuals);
            _filteredView.Filter = FilterResidualItem;

            UpdateCalculations();
            StatusText = $"{TotalCount} adet kalıntı öğesi tespit edildi. Temizlemek istediklerinizi seçin.";
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResidualItem.IsSelected))
            {
                UpdateCalculations();
            }
        }

        private bool FilterResidualItem(object obj)
        {
            if (obj is not ResidualItem item) return false;

            return SelectedFilter switch
            {
                "Folders" => item.Type == ResidualType.Folder,
                "Files" => item.Type == ResidualType.File,
                "Registry" => item.Type == ResidualType.RegistryKey,
                _ => true
            };
        }

        [RelayCommand]
        public void SetFilter(string filter)
        {
            SelectedFilter = filter;
            _filteredView.Refresh();
        }

        [RelayCommand]
        public void ToggleSelectAll()
        {
            bool newValue = !IsAllSelected;
            IsAllSelected = newValue;

            foreach (var item in Residuals)
            {
                item.IsSelected = newValue;
            }

            UpdateCalculations();
        }

        [RelayCommand]
        public void SelectAll(bool select)
        {
            IsAllSelected = select;
            foreach (var item in Residuals)
            {
                item.IsSelected = select;
            }
            UpdateCalculations();
        }

        private void UpdateCalculations()
        {
            TotalCount = Residuals.Count;
            var selectedItems = Residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
            SelectedCount = selectedItems.Count;
            SelectedSizeBytes = selectedItems.Sum(r => r.SizeInBytes);
            FormattedSelectedSize = FormatBytes(SelectedSizeBytes);
            IsAllSelected = TotalCount > 0 && SelectedCount == TotalCount;
        }

        [RelayCommand]
        public async Task CleanSelectedAsync()
        {
            var selected = Residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen temizlenecek en az bir kalıntı öğesi seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"{AppName} uygulamasına ait seçilen {selected.Count} adet kalıntı ({FormattedSelectedSize}) kalıcı olarak silinecektir.\n\nİşlemi onaylıyor musunuz?",
                "Kalıntıları Temizle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsCleaning = true;
            CleanProgress = 0;
            StatusText = "Kalıntılar güvenli motor ile temizleniyor...";

            var progress = new Progress<string>(msg =>
            {
                StatusText = msg;
            });

            try
            {
                int count = await _scannerEngine.CleanResidualItemsAsync(selected, progress);
                CleanedCount = count;
                WasCleaned = true;

                StatusText = $"{count} adet kalıntı başarıyla temizlendi!";
                MessageBox.Show(
                    $"{count} adet kalıntı ({FormattedSelectedSize}) başarıyla temizlendi!",
                    "Kalıntı Temizliği Tamamlandı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                RequestClose?.Invoke(true);
            }
            catch (Exception ex)
            {
                StatusText = $"Hata oluştu: {ex.Message}";
                MessageBox.Show($"Kalıntılar temizlenirken hata meydana geldi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsCleaning = false;
            }
        }

        [RelayCommand]
        public void Cancel()
        {
            RequestClose?.Invoke(false);
        }

        [RelayCommand]
        public void OpenLocation(ResidualItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;

            try
            {
                if (item.Type == ResidualType.Folder && Directory.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{item.Path}\"",
                        UseShellExecute = true
                    });
                }
                else if (item.Type == ResidualType.File && File.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.Path}\"",
                        UseShellExecute = true
                    });
                }
                else if (item.Type == ResidualType.RegistryKey)
                {
                    Clipboard.SetText(item.Path);
                    MessageBox.Show($"Kayıt defteri yolu panoya kopyalandı:\n{item.Path}", "Panoya Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Konum açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CopyPath(ResidualItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;

            try
            {
                Clipboard.SetText(item.Path);
                StatusText = $"Yol panoya kopyalandı: {item.Path}";
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblBytes = bytes;
            while (dblBytes >= 1024 && i < suffixes.Length - 1)
            {
                dblBytes /= 1024;
                i++;
            }
            return $"{dblBytes:0.##} {suffixes[i]}";
        }
    }
}
