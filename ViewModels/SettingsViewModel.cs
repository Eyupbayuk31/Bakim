using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Bakım.Helpers;
using Bakım.Services;
using Bakım.Views.Dialogs;
using Wpf.Ui.Appearance;

namespace Bakım.ViewModels
{
    public class AppSettingsData
    {
        public string Theme { get; set; } = "MicaDark";
        public bool IsMicaEnabled { get; set; } = true;
        public int RefreshIntervalSeconds { get; set; } = 2;
        public int AutoRamCleanIntervalMinutes { get; set; } = 0;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool NotifyOnHighRam { get; set; } = true;
        public bool AutoCleanOnExit { get; set; } = false;
        public bool AlwaysRunAsAdmin { get; set; } = false;
        public bool TaskSchedulerAutoStart { get; set; } = false;
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryValueName = "BakimApp";
        private readonly string _settingsFilePath;
        private readonly string _logsDirectoryPath;
        private readonly ThemeService _themeService;
        private readonly IGitHubUpdateService _updateService;
        private bool _isInitializing = true;

        public SettingsViewModel() : this(null)
        {
        }

        public SettingsViewModel(IGitHubUpdateService? updateService)
        {
            _updateService = updateService ?? new GitHubUpdateService();
            _themeService = new ThemeService();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appData, "Bakim");
            _settingsFilePath = Path.Combine(appFolder, "appsettings.json");
            _logsDirectoryPath = Path.Combine(appFolder, "Logs");

            try
            {
                if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
                if (!Directory.Exists(_logsDirectoryPath)) Directory.CreateDirectory(_logsDirectoryPath);
            }
            catch { }

            IsAdmin = UacHelper.IsAdministrator();
            LoadSettings();
            _isInitializing = false;
        }

        #region Appearance & Theme Properties

        [ObservableProperty]
        private bool _isMicaTheme = true;

        [ObservableProperty]
        private bool _isAmoledTheme;

        [ObservableProperty]
        private bool _isCyberpunkTheme;

        [ObservableProperty]
        private bool _isMicaEnabled = true;

        [ObservableProperty]
        private string _currentThemeStatus = "Mica Koyu (Varsayılan Fluent 2.0)";

        #endregion

        #region Performance & Automation Properties

        [ObservableProperty]
        private int _refreshIntervalSeconds = 2;

        [ObservableProperty]
        private int _autoRamCleanIntervalMinutes = 0;

        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private bool _minimizeToTray;

        [ObservableProperty]
        private bool _notifyOnHighRam = true;

        [ObservableProperty]
        private bool _autoCleanOnExit;

        [ObservableProperty]
        private bool _isAlwaysRunAsAdmin;

        [ObservableProperty]
        private bool _isTaskSchedulerAutoStart;

        #endregion

        #region Update & Status Properties

        [ObservableProperty]
        private string _updateStatus = "Yazılım güncel (v2.6.2)";

        [ObservableProperty]
        private bool _isCheckingUpdate;

        #endregion

        #region Metadata Properties

        [ObservableProperty]
        private bool _isAdmin;

        [ObservableProperty]
        private string _appVersion = "v2.6.2 (Fluent Professional)";

        [ObservableProperty]
        private string _frameworkVersion = ".NET 10.0 Windows Desktop";

        [ObservableProperty]
        private string _developerName = "Eyüp";

        [ObservableProperty]
        private string _architecture = Environment.Is64BitOperatingSystem ? "64-Bit (x64 Native)" : "32-Bit (x86)";

        [ObservableProperty]
        private string _osVersion = Environment.OSVersion.VersionString;

        [ObservableProperty]
        private string _installationStatus = AdminElevationService.GetInstallationStatusText();

        [ObservableProperty]
        private string _installedDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        [ObservableProperty]
        private bool _isInstalled = AdminElevationService.IsInstalledApplication();

        #endregion

        #region Change Handlers & Persistence

        partial void OnIsMicaEnabledChanged(bool value) => AutoSaveSettings();
        partial void OnRefreshIntervalSecondsChanged(int value) => AutoSaveSettings();
        partial void OnAutoRamCleanIntervalMinutesChanged(int value) => AutoSaveSettings();
        partial void OnMinimizeToTrayChanged(bool value) => AutoSaveSettings();
        partial void OnNotifyOnHighRamChanged(bool value) => AutoSaveSettings();
        partial void OnAutoCleanOnExitChanged(bool value) => AutoSaveSettings();

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (_isInitializing) return;
            if (value && IsTaskSchedulerAutoStart)
            {
                IsTaskSchedulerAutoStart = false;
                AdminElevationService.SetTaskSchedulerAutoStart(false);
            }
            ApplyAutostartRegistry(value);
            AutoSaveSettings();
        }

        partial void OnIsAlwaysRunAsAdminChanged(bool value)
        {
            if (_isInitializing) return;
            AdminElevationService.SetAlwaysRunAsAdmin(value);
            AutoSaveSettings();
        }

        partial void OnIsTaskSchedulerAutoStartChanged(bool value)
        {
            if (_isInitializing) return;
            if (value && StartWithWindows)
            {
                StartWithWindows = false;
                ApplyAutostartRegistry(false);
            }
            AdminElevationService.SetTaskSchedulerAutoStart(value);
            AutoSaveSettings();
        }

        private void LoadSettings()
        {
            try
            {
                IsAlwaysRunAsAdmin = AdminElevationService.IsAlwaysRunAsAdminEnabled();
                IsTaskSchedulerAutoStart = AdminElevationService.IsTaskSchedulerAutoStartEnabled();
                IsInstalled = AdminElevationService.IsInstalledApplication();
                InstallationStatus = AdminElevationService.GetInstallationStatusText();

                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                    if (data != null)
                    {
                        IsMicaEnabled = data.IsMicaEnabled;
                        RefreshIntervalSeconds = data.RefreshIntervalSeconds;
                        AutoRamCleanIntervalMinutes = data.AutoRamCleanIntervalMinutes;
                        MinimizeToTray = data.MinimizeToTray;
                        NotifyOnHighRam = data.NotifyOnHighRam;
                        AutoCleanOnExit = data.AutoCleanOnExit;

                        switch (data.Theme)
                        {
                            case "AmoledBlack":
                                SetAmoledTheme();
                                break;
                            case "CyberpunkPurple":
                                SetCyberpunkTheme();
                                break;
                            case "MicaDark":
                            default:
                                SetMicaDarkTheme();
                                break;
                        }
                    }
                }
                else
                {
                    SetMicaDarkTheme();
                }

                LoadAutostartPreference();
            }
            catch
            {
                SetMicaDarkTheme();
            }
        }

        private void AutoSaveSettings()
        {
            if (_isInitializing) return;

            try
            {
                string theme = IsAmoledTheme ? "AmoledBlack" : (IsCyberpunkTheme ? "CyberpunkPurple" : "MicaDark");
                var data = new AppSettingsData
                {
                    Theme = theme,
                    IsMicaEnabled = IsMicaEnabled,
                    RefreshIntervalSeconds = RefreshIntervalSeconds,
                    AutoRamCleanIntervalMinutes = AutoRamCleanIntervalMinutes,
                    StartWithWindows = StartWithWindows,
                    MinimizeToTray = MinimizeToTray,
                    NotifyOnHighRam = NotifyOnHighRam,
                    AutoCleanOnExit = AutoCleanOnExit,
                    AlwaysRunAsAdmin = IsAlwaysRunAsAdmin,
                    TaskSchedulerAutoStart = IsTaskSchedulerAutoStart
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }

        #endregion

        #region Theme Commands

        [RelayCommand]
        public void SetMicaDarkTheme()
        {
            _themeService.ApplyTheme(AppThemeKind.MicaDark);
            IsMicaTheme = true;
            IsAmoledTheme = false;
            IsCyberpunkTheme = false;
            CurrentThemeStatus = "Mica Koyu (Varsayılan Fluent 2.0)";
            AutoSaveSettings();
        }

        [RelayCommand]
        public void SetAmoledTheme()
        {
            _themeService.ApplyTheme(AppThemeKind.AmoledBlack);
            IsMicaTheme = false;
            IsAmoledTheme = true;
            IsCyberpunkTheme = false;
            CurrentThemeStatus = "AMOLED Siyah (Kusursuz Derin Kontrast)";
            AutoSaveSettings();
        }

        [RelayCommand]
        public void SetCyberpunkTheme()
        {
            _themeService.ApplyTheme(AppThemeKind.CyberpunkPurple);
            IsMicaTheme = false;
            IsAmoledTheme = false;
            IsCyberpunkTheme = true;
            CurrentThemeStatus = "Cyberpunk Mor (Neon Vurgular)";
            AutoSaveSettings();
        }

        #endregion

        #region Registry & System Commands

        private void LoadAutostartPreference()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                if (key != null)
                {
                    var val = key.GetValue(AppRegistryValueName) as string;
                    StartWithWindows = !string.IsNullOrEmpty(val);
                }
            }
            catch
            {
                StartWithWindows = false;
            }
        }

        private void ApplyAutostartRegistry(bool enable)
        {
            if (_isInitializing) return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (enable)
                    {
                        var exePath = AdminElevationService.GetExePath();
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(AppRegistryValueName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        if (key.GetValue(AppRegistryValueName) != null)
                        {
                            key.DeleteValue(AppRegistryValueName, false);
                        }
                    }
                }
            }
            catch { }
        }

        [RelayCommand]
        public void RestartAsAdmin()
        {
            UacHelper.RestartAsAdministrator();
        }

        [RelayCommand]
        public void OpenLogsFolder()
        {
            try
            {
                if (!Directory.Exists(_logsDirectoryPath))
                {
                    Directory.CreateDirectory(_logsDirectoryPath);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_logsDirectoryPath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        public void ExportSettings()
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Filter = "JSON Dosyaları (*.json)|*.json|Tüm Dosyalar (*.*)|*.*",
                    FileName = "Bakim_Ayarlar_Yedek.json",
                    Title = "Ayarları Dışa Aktar"
                };

                if (sfd.ShowDialog() == true)
                {
                    string theme = IsAmoledTheme ? "AmoledBlack" : (IsCyberpunkTheme ? "CyberpunkPurple" : "MicaDark");
                    var data = new AppSettingsData
                    {
                        Theme = theme,
                        IsMicaEnabled = IsMicaEnabled,
                        RefreshIntervalSeconds = RefreshIntervalSeconds,
                        AutoRamCleanIntervalMinutes = AutoRamCleanIntervalMinutes,
                        StartWithWindows = StartWithWindows,
                        MinimizeToTray = MinimizeToTray,
                        NotifyOnHighRam = NotifyOnHighRam,
                        AutoCleanOnExit = AutoCleanOnExit,
                        AlwaysRunAsAdmin = IsAlwaysRunAsAdmin,
                        TaskSchedulerAutoStart = IsTaskSchedulerAutoStart
                    };

                    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, json);
                    MessageBox.Show("Ayarlar başarıyla dışa aktarıldı!", "Yedekleme Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yedekleme sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ImportSettings()
        {
            try
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "JSON Dosyaları (*.json)|*.json|Tüm Dosyalar (*.*)|*.*",
                    Title = "Ayarları İçe Aktar"
                };

                if (ofd.ShowDialog() == true)
                {
                    string json = File.ReadAllText(ofd.FileName);
                    var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                    if (data != null)
                    {
                        IsMicaEnabled = data.IsMicaEnabled;
                        RefreshIntervalSeconds = data.RefreshIntervalSeconds;
                        AutoRamCleanIntervalMinutes = data.AutoRamCleanIntervalMinutes;
                        StartWithWindows = data.StartWithWindows;
                        MinimizeToTray = data.MinimizeToTray;
                        NotifyOnHighRam = data.NotifyOnHighRam;
                        AutoCleanOnExit = data.AutoCleanOnExit;
                        IsAlwaysRunAsAdmin = data.AlwaysRunAsAdmin;
                        IsTaskSchedulerAutoStart = data.TaskSchedulerAutoStart;

                        switch (data.Theme)
                        {
                            case "AmoledBlack":
                                SetAmoledTheme();
                                break;
                            case "CyberpunkPurple":
                                SetCyberpunkTheme();
                                break;
                            case "MicaDark":
                            default:
                                SetMicaDarkTheme();
                                break;
                        }

                        AutoSaveSettings();
                        MessageBox.Show("Ayarlar başarıyla içe aktarıldı ve uygulandı!", "İçe Aktarma Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"İçe aktarma sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public async Task CheckForUpdatesAsync()
        {
            IsCheckingUpdate = true;
            UpdateStatus = "GitHub Releases üzerinden güncellemeler denetleniyor...";

            try
            {
                var result = await AutoUpdateService.CheckForUpdatesAsync();

                if (!string.IsNullOrEmpty(result.ErrorMessage) && !result.IsUpdateAvailable)
                {
                    UpdateStatus = $"Kontrol: {result.ErrorMessage}";
                    MessageBox.Show(
                        $"Güncelleme sunucusundan bilgi alındı:\n{result.ErrorMessage}",
                        "Güncelleme Kontrolü",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (result.IsUpdateAvailable)
                {
                    UpdateStatus = $"Yeni Sürüm Mevcut: v{result.LatestVersion}!";

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new UpdateDialogView(result);
                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                        {
                            dialog.Owner = Application.Current.MainWindow;
                        }
                        dialog.ShowDialog();
                    });
                }
                else
                {
                    UpdateStatus = $"Yazılım güncel (v{result.CurrentVersion} - Son kontrol: {DateTime.Now:HH:mm})";

                    MessageBox.Show(
                        $"Tebrikler! En güncel Bakım sürümünü (v{result.CurrentVersion}) kullanıyorsunuz.\nSisteminiz en son performans ve güvenlik modülleriyle korunmaktadır.",
                        "Yazılım Güncel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus = "Güncelleme kontrolünde hata oluştu.";
                MessageBox.Show($"Beklenmeyen hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsCheckingUpdate = false;
            }
        }

        [RelayCommand]
        public void ResetPreferences()
        {
            var confirm = MessageBox.Show(
                "Tüm uygulama ayarlarını fabrika varsayılanlarına sıfırlamak istediğinize emin misiniz?",
                "Ayarları Sıfırla",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            SetMicaDarkTheme();
            IsMicaEnabled = true;
            RefreshIntervalSeconds = 2;
            AutoRamCleanIntervalMinutes = 0;
            StartWithWindows = false;
            MinimizeToTray = false;
            NotifyOnHighRam = true;
            AutoCleanOnExit = false;
            IsAlwaysRunAsAdmin = false;
            IsTaskSchedulerAutoStart = false;

            AdminElevationService.SetAlwaysRunAsAdmin(false);
            AdminElevationService.SetTaskSchedulerAutoStart(false);

            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    File.Delete(_settingsFilePath);
                }
            }
            catch { }

            AutoSaveSettings();
            MessageBox.Show("Tüm ayarlar başarıyla varsayılan değerlerine döndürüldü.", "Sıfırlama Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion
    }
}
