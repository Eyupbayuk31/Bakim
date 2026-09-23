using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.ViewModels
{
    public enum WizardStep
    {
        Ready = 1,
        Uninstalling = 2,
        Scanning = 3,
        Review = 4,
        Completed = 5
    }

    public partial class DeepUninstallWizardViewModel : ObservableObject
    {
        private readonly IDeepUninstallerService _deepUninstaller;
        private readonly IResidualScannerEngine _residualScanner;
        private readonly ICollectionView _filteredView;

        public InstalledAppItem TargetApp { get; }

        public ObservableCollection<ResidualItem> Residuals { get; } = new();
        public ICollectionView FilteredResiduals => _filteredView;

        [ObservableProperty]
        private WizardStep _currentStep = WizardStep.Ready;

        public int StepNumber => (int)CurrentStep;

        partial void OnCurrentStepChanged(WizardStep value)
        {
            OnPropertyChanged(nameof(StepNumber));
        }

        // Step 1 Options
        [ObservableProperty]
        private bool _createRestorePoint = true;

        [ObservableProperty]
        private bool _killRelatedProcesses = true;

        [ObservableProperty]
        private bool _backupRegistryBeforeClean = true;

        // Step 2 & 3 Status
        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _liveProcessStatus = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        // Step 4 Filtering & Selection
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All"; // All, Registry, Folders, Files

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

        // Step 5 Report
        [ObservableProperty]
        private int _cleanedCount;

        [ObservableProperty]
        private long _cleanedSizeBytes;

        [ObservableProperty]
        private string _formattedCleanedSize = "0 B";

        [ObservableProperty]
        private string _registryBackupFilePath = string.Empty;

        [ObservableProperty]
        private bool _hasRegistryBackup;

        public event Action<bool>? RequestClose;

        public string AppName => !string.IsNullOrWhiteSpace(TargetApp.DisplayName)
            ? TargetApp.DisplayName
            : "Program";

        public string Publisher => !string.IsNullOrWhiteSpace(TargetApp.Publisher)
            ? TargetApp.Publisher
            : "Bilinmeyen Yayıncı";

        public string DisplayVersion => !string.IsNullOrWhiteSpace(TargetApp.DisplayVersion)
            ? TargetApp.DisplayVersion
            : "1.0.0";

        public string InstallLocation => !string.IsNullOrWhiteSpace(TargetApp.InstallLocation)
            ? TargetApp.InstallLocation
            : (!string.IsNullOrWhiteSpace(TargetApp.DisplayIconPath) ? Path.GetDirectoryName(TargetApp.DisplayIconPath) ?? string.Empty : "Bilinmeyen Konum");

        public string FormattedSize => TargetApp.FormattedSize;

        public DeepUninstallWizardViewModel(
            InstalledAppItem targetApp,
            IDeepUninstallerService deepUninstaller,
            IResidualScannerEngine residualScanner)
        {
            TargetApp = targetApp;
            _deepUninstaller = deepUninstaller;
            _residualScanner = residualScanner;

            _filteredView = CollectionViewSource.GetDefaultView(Residuals);
            _filteredView.Filter = FilterResidualItem;

            StatusMessage = $"{AppName} için kaldırma ve kalıntı temizleme işlemi başlatılmaya hazır.";
        }

        partial void OnSearchTextChanged(string value)
        {
            _filteredView.Refresh();
        }

        private bool FilterResidualItem(object obj)
        {
            if (obj is not ResidualItem item) return false;

            // 1. Category Filter
            bool categoryMatch = SelectedFilter switch
            {
                "Registry" => item.Type == ResidualType.RegistryKey,
                "Folders" => item.Type == ResidualType.Folder,
                "Files" => item.Type == ResidualType.File,
                _ => true
            };

            if (!categoryMatch) return false;

            // 2. Search Text
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            string query = SearchText.Trim().ToLowerInvariant();
            return item.Path.ToLowerInvariant().Contains(query) ||
                   item.Description.ToLowerInvariant().Contains(query) ||
                   item.TypeName.ToLowerInvariant().Contains(query);
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResidualItem.IsSelected))
            {
                UpdateCalculations();
            }
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

        #region Commands: Stepper Actions

        [RelayCommand]
        public async Task StartUninstallAsync()
        {
            CurrentStep = WizardStep.Uninstalling;
            IsBusy = true;

            try
            {
                // 1. Create Restore Point if selected
                if (CreateRestorePoint)
                {
                    StatusMessage = "Sistem kararlılığını korumak için Windows Geri Yükleme Noktası oluşturuluyor...";
                    LiveProcessStatus = "Geri Yükleme Noktası oluşturuluyor...";
                    await _deepUninstaller.CreateRestorePointAsync(TargetApp.DisplayName);
                }

                // 2. Kill related running processes if selected
                if (KillRelatedProcesses)
                {
                    StatusMessage = "İlişkili arka plan süreçleri denetleniyor...";
                    LiveProcessStatus = "Süreçler kontrol ediliyor...";
                    KillProcessesForApp(TargetApp);
                }

                // 3. Launch Uninstaller
                bool launched = false;
                if (!string.IsNullOrWhiteSpace(TargetApp.UninstallString) || !string.IsNullOrWhiteSpace(TargetApp.QuietUninstallString))
                {
                    StatusMessage = $"{AppName} resmi kaldırıcısı çalıştırılıyor... Lütfen kaldırma adımlarını tamamlayın.";
                    LiveProcessStatus = "Resmi kaldırıcı penceresi açık — kapatılması bekleniyor...";
                    launched = await _deepUninstaller.LaunchUninstallAsync(TargetApp, silent: false);
                }

                if (!launched)
                {
                    LiveProcessStatus = "Resmi kaldırıcı bulunamadı veya doğrudan zorla kaldırma modu aktif.";
                }

                // 4. Automatically proceed to Stage 3: Deep Scan
                await ScanResidualsInternalAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kaldırma işlemi sırasında hata oluştu: {ex.Message}";
                // Gracefully fallback to residual scan anyway
                await ScanResidualsInternalAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ScanResidualsInternalAsync()
        {
            CurrentStep = WizardStep.Scanning;
            IsBusy = true;
            StatusMessage = "Kaldırma tamamlandı. Kayıt Defteri ve dosya sistemindeki derin artıklar taranıyor...";
            LiveProcessStatus = "Kayıt defteri ve dizinler derinlemesine taranıyor...";

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    LiveProcessStatus = msg;
                });

                var items = await _residualScanner.ScanResidualItemsAsync(TargetApp, progress);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Residuals.Clear();
                    foreach (var itm in items)
                    {
                        itm.PropertyChanged += OnItemPropertyChanged;
                        Residuals.Add(itm);
                    }
                    UpdateCalculations();
                });

                CurrentStep = WizardStep.Review;
                StatusMessage = $"{TotalCount} adet artık kalıntı tespit edildi. Silinmesini istemediğiniz ögelerin işaretini kaldırabilirsiniz.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kalıntı taraması sırasında hata: {ex.Message}";
                CurrentStep = WizardStep.Review;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task CleanSelectedResidualsAsync()
        {
            var selected = Residuals.Where(r => r.IsSelected && !r.IsDeleted).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Lütfen temizlenecek en az bir kalıntı seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = "Seçilen kalıntılar güvenli motor ile temizleniyor...";

            try
            {
                // 1. Registry Backup before clean if enabled
                if (BackupRegistryBeforeClean)
                {
                    var regKeys = selected.Where(s => s.Type == ResidualType.RegistryKey).ToList();
                    if (regKeys.Count > 0)
                    {
                        StatusMessage = "Kayıt defteri yedeği alınıyor...";
                        string backupPath = ExportRegistryBackupSafe(regKeys, TargetApp.DisplayName);
                        if (!string.IsNullOrWhiteSpace(backupPath))
                        {
                            RegistryBackupFilePath = backupPath;
                            HasRegistryBackup = true;
                        }
                    }
                }

                // 2. Clean residuals
                var progress = new Progress<string>(msg =>
                {
                    StatusMessage = msg;
                });

                int cleanedCount = await _residualScanner.CleanResidualItemsAsync(selected, progress);
                CleanedCount = cleanedCount;
                CleanedSizeBytes = selected.Sum(s => s.SizeInBytes);
                FormattedCleanedSize = FormatBytes(CleanedSizeBytes);

                CurrentStep = WizardStep.Completed;
                StatusMessage = $"{CleanedCount} adet kalıntı başarıyla temizlendi!";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kalıntılar temizlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void SkipCleanup()
        {
            RequestClose?.Invoke(false);
        }

        [RelayCommand]
        public void Close()
        {
            RequestClose?.Invoke(true);
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
            SelectAll(newValue);
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

        [RelayCommand]
        public void SelectSafeOnly()
        {
            foreach (var item in Residuals)
            {
                item.IsSelected = item.IsSafeToDelete;
            }
            UpdateCalculations();
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
                    MessageBox.Show($"Kayıt defteri anahtar yolu panoya kopyalandı:\n\n{item.Path}", "Panoya Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
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
                StatusMessage = $"Yol panoya kopyalandı: {item.Path}";
            }
            catch { }
        }

        [RelayCommand]
        public void OpenBackupFolder()
        {
            if (string.IsNullOrWhiteSpace(RegistryBackupFilePath) || !File.Exists(RegistryBackupFilePath)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{RegistryBackupFilePath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion

        #region Helpers

        private static void KillProcessesForApp(InstalledAppItem app)
        {
            try
            {
                string loc = app.InstallLocation?.TrimEnd('\\') ?? string.Empty;
                if (string.IsNullOrWhiteSpace(loc)) return;

                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        string path = p.MainModule?.FileName ?? string.Empty;
                        if (path.StartsWith(loc, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string ExportRegistryBackupSafe(List<ResidualItem> regItems, string appName)
        {
            try
            {
                string safeName = System.Text.RegularExpressions.Regex.Replace(appName, @"[^a-zA-Z0-9_\-]", "_");
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "Backups");
                Directory.CreateDirectory(folder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFile = Path.Combine(folder, $"RegBackup_{safeName}_{timestamp}.reg");

                var sb = new StringBuilder();
                sb.AppendLine("Windows Registry Editor Version 5.00");
                sb.AppendLine($"; Bakım Sistem Optimizer - Kaldırma Öncesi Otomatik Kayıt Defteri Yedeği");
                sb.AppendLine($"; Hedef Uygulama: {appName}");
                sb.AppendLine($"; Tarih: {DateTime.Now:g}");
                sb.AppendLine();

                foreach (var item in regItems)
                {
                    sb.AppendLine($"; Yedeklenen Anahtar: {item.Path}");
                    sb.AppendLine($"[-{item.Path}]");
                    sb.AppendLine();
                }

                File.WriteAllText(backupFile, sb.ToString(), Encoding.Unicode);
                return backupFile;
            }
            catch
            {
                return string.Empty;
            }
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

        #endregion
    }
}
