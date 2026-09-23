using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;
using Bakım.Views.Dialogs;
using Wpf.Ui.Appearance;

namespace Bakım.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryValueName = "BakimApp";

        private readonly IAppSettingsService _settingsService;
        private readonly IThemeService _themeService;
        private readonly ILogService _log;
        private readonly IGitHubUpdateService _updateService;
        private readonly IVirusTotalCheckService _virusTotalService;
        private bool _isInitializing = true;

        public SettingsViewModel(
            IGitHubUpdateService updateService,
            IAppSettingsService settingsService,
            IThemeService themeService,
            ILogService log,
            IVirusTotalCheckService virusTotalService)
        {
            _updateService = updateService;
            _settingsService = settingsService;
            _themeService = themeService;
            _log = log;
            _virusTotalService = virusTotalService;

            IsAdmin = UacHelper.IsAdministrator();
            LoadSettings();
            InitializeReleaseHistory();
            _isInitializing = false;
        }

        private string SettingsFilePath => _settingsService.SettingsFilePath;
        private string LogsDirectoryPath => _log.LogDirectory;

        #region Category Navigation Properties

        [ObservableProperty]
        private string _selectedCategory = "General"; // General, Performance, Startup, Security, Modules, About

        public bool IsGeneralSelected => string.Equals(SelectedCategory, "General", StringComparison.OrdinalIgnoreCase);
        public bool IsPerformanceSelected => string.Equals(SelectedCategory, "Performance", StringComparison.OrdinalIgnoreCase);
        public bool IsStartupSelected => string.Equals(SelectedCategory, "Startup", StringComparison.OrdinalIgnoreCase);
        public bool IsSecuritySelected => string.Equals(SelectedCategory, "Security", StringComparison.OrdinalIgnoreCase);
        public bool IsModulesSelected => string.Equals(SelectedCategory, "Modules", StringComparison.OrdinalIgnoreCase);
        public bool IsAboutSelected => string.Equals(SelectedCategory, "About", StringComparison.OrdinalIgnoreCase);

        partial void OnSelectedCategoryChanged(string value)
        {
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsPerformanceSelected));
            OnPropertyChanged(nameof(IsStartupSelected));
            OnPropertyChanged(nameof(IsSecuritySelected));
            OnPropertyChanged(nameof(IsModulesSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
        }

        [RelayCommand]
        private void SelectCategory(string category)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                SelectedCategory = category;
            }
        }

        #endregion

        #region Appearance & Theme Properties

        [ObservableProperty]
        private bool _isMicaTheme = true;

        [ObservableProperty]
        private bool _isAmoledTheme;

        [ObservableProperty]
        private bool _isCyberpunkTheme;

        [ObservableProperty]
        private bool _isLightTheme;

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

        [ObservableProperty]
        private bool _promptRestorePointBeforeUninstall = true;

        [ObservableProperty]
        private bool _createRestorePointOnUninstall = true;

        [ObservableProperty]
        private string _autostartHealthBadgeText = "Denetleniyor...";

        [ObservableProperty]
        private string _autostartHealthBadgeBrush = "TextFillColorSecondaryBrush";

        [ObservableProperty]
        private bool _isAutostartBlocked;

        [ObservableProperty]
        private string _autostartDiagnosticDetails = string.Empty;

        #endregion

        #region Update & Status Properties

        [ObservableProperty]
        private string _updateStatus = $"Yazılım güncel (v{AutoUpdateService.GetCurrentVersion().ToString(3)})";

        [ObservableProperty]
        private bool _isCheckingUpdate;

        #endregion

        #region Metadata Properties

        [ObservableProperty]
        private bool _isAdmin;

        [ObservableProperty]
        private string _appVersion = $"v{AutoUpdateService.GetCurrentVersion().ToString(3)} (Fluent Professional)";

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

        [ObservableProperty]
        private string _virusTotalApiKey = string.Empty;

        [ObservableProperty]
        private string _virusTotalApiStatus = "API anahtarı girilmedi.";

        [ObservableProperty]
        private bool _isTestingVirusTotalKey;

        /// <summary>Ayrıntılı (Debug) günlükleme. Sorun bildirirken açılması istenir.</summary>
        [ObservableProperty]
        private bool _verboseLogging;

        #endregion

        #region Change Handlers & Persistence

        partial void OnIsMicaEnabledChanged(bool value)
        {
            if (_isInitializing) return;
            _themeService.ApplyBackdrop(value);
            AutoSaveSettings();
        }

        partial void OnVerboseLoggingChanged(bool value)
        {
            if (_isInitializing) return;
            _log.MinimumLevel = value ? LogLevel.Debug : LogLevel.Info;
            _log.Info($"Ayrıntılı günlükleme {(value ? "açıldı" : "kapatıldı")}.", nameof(SettingsViewModel));
            AutoSaveSettings();
        }

        partial void OnRefreshIntervalSecondsChanged(int value) => AutoSaveSettings();
        partial void OnAutoRamCleanIntervalMinutesChanged(int value) => AutoSaveSettings();
        partial void OnMinimizeToTrayChanged(bool value) => AutoSaveSettings();
        partial void OnNotifyOnHighRamChanged(bool value) => AutoSaveSettings();
        partial void OnAutoCleanOnExitChanged(bool value) => AutoSaveSettings();
        partial void OnPromptRestorePointBeforeUninstallChanged(bool value) => AutoSaveSettings();
        partial void OnCreateRestorePointOnUninstallChanged(bool value) => AutoSaveSettings();

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (_isInitializing) return;

            if (value)
            {
                // Önerilen & Güvenilir Yol: Task Scheduler (UAC uyarısız en yüksek yetki)
                bool ok = AdminElevationService.SetTaskSchedulerAutoStart(true);
                if (!ok)
                {
                    // Fallback: Registry Run (RUNASADMIN engeli olmaması için uyumluluğu temizle)
                    AdminElevationService.SetAlwaysRunAsAdmin(false);
                    IsAlwaysRunAsAdmin = false;
                    ApplyAutostartRegistry(true);
                }
            }
            else
            {
                AdminElevationService.SetTaskSchedulerAutoStart(false);
                ApplyAutostartRegistry(false);
            }

            RefreshAutostartHealthStatus();
            AutoSaveSettings();
        }

        partial void OnIsAlwaysRunAsAdminChanged(bool value)
        {
            if (_isInitializing) return;
            AdminElevationService.SetAlwaysRunAsAdmin(value);

            // Eğer RUNASADMIN açıldıysa ve Registry Run varsa, Task Scheduler'a geçirilmelidir
            if (value && StartWithWindows && !AdminElevationService.IsTaskSchedulerAutoStartEnabled())
            {
                AdminElevationService.SetTaskSchedulerAutoStart(true);
            }

            RefreshAutostartHealthStatus();
            AutoSaveSettings();
        }

        partial void OnIsTaskSchedulerAutoStartChanged(bool value)
        {
            if (_isInitializing) return;

            if (value)
            {
                AdminElevationService.SetTaskSchedulerAutoStart(true);
            }
            else
            {
                AdminElevationService.SetTaskSchedulerAutoStart(false);
            }

            RefreshAutostartHealthStatus();
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

                ApplyToProperties(_settingsService.Current);

                // Tema açılışta App tarafından zaten uygulandı; burada yalnızca
                // seçim durumu eşitlenir, yeniden boyama yapılmaz (titreme olmaz).
                SyncThemeSelection(_themeService.CurrentTheme);

                RefreshAutostartHealthStatus();
            }
            catch (Exception ex)
            {
                _log.Error("Ayarlar yüklenemedi, varsayılanlar gösteriliyor.", ex, nameof(SettingsViewModel));
                SyncThemeSelection(AppThemeKind.MicaDark);
            }
        }

        /// <summary>Kalıcı ayar nesnesini görünür özelliklere aktarır.</summary>
        private void ApplyToProperties(AppSettingsData data)
        {
            IsMicaEnabled = data.IsMicaEnabled;
            RefreshIntervalSeconds = data.RefreshIntervalSeconds;
            AutoRamCleanIntervalMinutes = data.AutoRamCleanIntervalMinutes;
            MinimizeToTray = data.MinimizeToTray;
            NotifyOnHighRam = data.NotifyOnHighRam;
            AutoCleanOnExit = data.AutoCleanOnExit;
            PromptRestorePointBeforeUninstall = data.PromptRestorePointBeforeUninstall;
            CreateRestorePointOnUninstall = data.CreateRestorePointOnUninstall;
            VerboseLogging = data.VerboseLogging;

            VirusTotalApiKey = VirusTotalCheckService.ResolveApiKey(data);
            VirusTotalApiStatus = string.IsNullOrWhiteSpace(VirusTotalApiKey)
                ? "API anahtarı girilmedi."
                : "API anahtarı kayıtlı.";
        }

        /// <summary>Görünür özelliklerden kalıcı ayar nesnesi üretir.</summary>
        private AppSettingsData BuildData()
        {
            // Mevcut kaydı temel al ki burada yönetilmeyen alanlar (şema sürümü,
            // korumalı API anahtarı) kaybolmasın.
            var data = _settingsService.Current.Clone();

            data.Theme = _themeService.CurrentTheme.ToString();
            data.IsMicaEnabled = IsMicaEnabled;
            data.RefreshIntervalSeconds = RefreshIntervalSeconds;
            data.AutoRamCleanIntervalMinutes = AutoRamCleanIntervalMinutes;
            data.StartWithWindows = StartWithWindows;
            data.MinimizeToTray = MinimizeToTray;
            data.NotifyOnHighRam = NotifyOnHighRam;
            data.AutoCleanOnExit = AutoCleanOnExit;
            data.AlwaysRunAsAdmin = IsAlwaysRunAsAdmin;
            data.TaskSchedulerAutoStart = IsTaskSchedulerAutoStart;
            data.PromptRestorePointBeforeUninstall = PromptRestorePointBeforeUninstall;
            data.CreateRestorePointOnUninstall = CreateRestorePointOnUninstall;
            data.VerboseLogging = VerboseLogging;

            return data;
        }

        private void AutoSaveSettings()
        {
            if (_isInitializing) return;
            _settingsService.Save(BuildData());
        }

        /// <summary>
        /// Diğer ViewModel'lerin (ör. UninstallerViewModel) güncel ayarlara erişimi.
        /// Mümkünse bellekteki tekil örnek kullanılır, aksi halde diskten okunur.
        /// </summary>
        public static AppSettingsData LoadCurrentSettings()
        {
            var service = App.TryGetService<IAppSettingsService>();
            if (service != null) return service.Current;

            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Bakim", "appsettings.json");

                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(path))
                           ?? new AppSettingsData();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Ayarlar diskten okunamadı.", ex, nameof(SettingsViewModel));
            }

            return new AppSettingsData();
        }

        #endregion

        #region Theme Commands

        [RelayCommand]
        public void SetMicaDarkTheme() => SelectTheme(AppThemeKind.MicaDark);

        [RelayCommand]
        public void SetAmoledTheme() => SelectTheme(AppThemeKind.AmoledBlack);

        [RelayCommand]
        public void SetCyberpunkTheme() => SelectTheme(AppThemeKind.CyberpunkPurple);

        [RelayCommand]
        public void SetLightTheme() => SelectTheme(AppThemeKind.FluentLight);

        private void SelectTheme(AppThemeKind kind)
        {
            // ThemeService temayı uygular ve tercihi kendisi kalıcı hale getirir.
            _themeService.ApplyTheme(kind);
            SyncThemeSelection(kind);
        }

        /// <summary>Seçili tema düğmelerini ve durum metnini tazeler; yeniden boyama yapmaz.</summary>
        private void SyncThemeSelection(AppThemeKind kind)
        {
            IsMicaTheme = kind == AppThemeKind.MicaDark;
            IsAmoledTheme = kind == AppThemeKind.AmoledBlack;
            IsCyberpunkTheme = kind == AppThemeKind.CyberpunkPurple;
            IsLightTheme = kind == AppThemeKind.FluentLight;

            CurrentThemeStatus = kind switch
            {
                AppThemeKind.AmoledBlack => "AMOLED Siyah (Kusursuz Derin Kontrast)",
                AppThemeKind.CyberpunkPurple => "Cyberpunk Mor (Neon Vurgular)",
                AppThemeKind.FluentLight => "Fluent Açık (Gündüz Modu)",
                _ => "Mica Koyu (Varsayılan Fluent 2.0)"
            };
        }

        #endregion

        #region Registry & System Commands

        private void LoadAutostartPreference()
        {
            RefreshAutostartHealthStatus();
        }

        public void RefreshAutostartHealthStatus()
        {
            var state = AdminElevationService.GetAutostartHealthState();
            bool prevInit = _isInitializing;
            _isInitializing = true;
            try
            {
                switch (state)
                {
                    case AutostartHealthState.TaskSchedulerActive:
                        AutostartHealthBadgeText = "Aktif (Görev Zamanlayıcı - UAC Uyarısız Yönetici)";
                        AutostartHealthBadgeBrush = "SystemFillColorSuccessBrush";
                        IsAutostartBlocked = false;
                        AutostartDiagnosticDetails = "Windows açılışında en yüksek yetkiyle, UAC onayı sormadan otomatik ve sessiz başlar.";
                        StartWithWindows = true;
                        IsTaskSchedulerAutoStart = true;
                        break;

                    case AutostartHealthState.RegistryRunActive:
                        AutostartHealthBadgeText = "Aktif (Kayıt Defteri - Standart Kullanıcı)";
                        AutostartHealthBadgeBrush = "SystemFillColorCautionBrush";
                        IsAutostartBlocked = false;
                        AutostartDiagnosticDetails = "Windows açılışında standart kullanıcı haklarıyla sessizce başlar.";
                        StartWithWindows = true;
                        IsTaskSchedulerAutoStart = false;
                        break;

                    case AutostartHealthState.BlockedByAppCompatAdmin:
                        AutostartHealthBadgeText = "ENGELLENDİ! (Windows UAC Kısıtlaması Saptandı)";
                        AutostartHealthBadgeBrush = "SystemFillColorCriticalBrush";
                        IsAutostartBlocked = true;
                        AutostartDiagnosticDetails = "Uygulama 'Yönetici Olarak Çalıştır' olarak işaretli olduğu için Windows açılışta Registry Run kaydını sessizce iptal etmektedir. Düzeltmek için 'Başlangıcı Onar & Kur' butonuna tıklayın.";
                        StartWithWindows = true;
                        IsTaskSchedulerAutoStart = false;
                        break;

                    case AutostartHealthState.Disabled:
                    default:
                        AutostartHealthBadgeText = "Devre Dışı (Windows ile başlamaz)";
                        AutostartHealthBadgeBrush = "TextFillColorTertiaryBrush";
                        IsAutostartBlocked = false;
                        AutostartDiagnosticDetails = "Uygulama bilgisayar açıldığında çalışmaz.";
                        StartWithWindows = false;
                        IsTaskSchedulerAutoStart = false;
                        break;
                }
            }
            finally
            {
                _isInitializing = prevInit;
            }
        }

        [RelayCommand]
        public void RepairAutostart()
        {
            try
            {
                AutostartDiagnosticDetails = "Windows başlangıç yapılandırması onarılıyor...";
                _log.Info("Başlangıç onarım işlemi başlatıldı.", nameof(SettingsViewModel));
                bool ok = AdminElevationService.RepairAutostartConfiguration();
                RefreshAutostartHealthStatus();
                if (ok || AdminElevationService.IsTaskSchedulerAutoStartEnabled())
                {
                    AutostartDiagnosticDetails = "Başlangıç onarıldı: Görev Zamanlayıcı UAC bypass görevi başarıyla kuruldu!";
                    _log.Info("Başlangıç başarıyla onarıldı ve Görev Zamanlayıcıya kaydedildi.", nameof(SettingsViewModel));
                }
                else
                {
                    AutostartDiagnosticDetails = "Onarım tamamlanamadı. Lütfen yönetici hakları onayını doğrulayın.";
                }
            }
            catch (Exception ex)
            {
                AutostartDiagnosticDetails = $"Onarım hatası: {ex.Message}";
                _log.Error("Başlangıç onarımı sırasında istisna oluştu.", ex, nameof(SettingsViewModel));
            }
        }

        [RelayCommand]
        public void RefreshAutostartStatus()
        {
            RefreshAutostartHealthStatus();
            _log.Info("Başlangıç durumu kullanıcı tarafından tazelendi.", nameof(SettingsViewModel));
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
                            key.SetValue(AppRegistryValueName, $"\"{exePath}\" --autostart");
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
                string path = LogsDirectoryPath;

                if (string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show(
                        "Günlük dosyası konumu belirlenemedi.",
                        "Günlükler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Directory.CreateDirectory(path);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _log.Error("Günlük klasörü açılamadı.", ex, nameof(SettingsViewModel));
                MessageBox.Show($"Günlük klasörü açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    var data = BuildData();

                    // Sırlar yedeğe yazılmaz: DPAPI ile şifrelenmiş anahtar yalnızca
                    // onu üreten Windows kullanıcısında çözülebilir, taşınması anlamsızdır.
                    data.VirusTotalApiKey = string.Empty;
                    data.VirusTotalApiKeyProtected = string.Empty;

                    string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, json);

                    _log.Info($"Ayarlar dışa aktarıldı: {sfd.FileName}", nameof(SettingsViewModel));
                    MessageBox.Show(
                        "Ayarlar başarıyla dışa aktarıldı!\n\nGüvenlik gereği VirusTotal API anahtarı yedeğe dahil edilmedi.",
                        "Yedekleme Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Ayar dışa aktarımı başarısız.", ex, nameof(SettingsViewModel));
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

                    if (data == null)
                    {
                        MessageBox.Show("Dosya okunabildi fakat geçerli ayar içermiyor.",
                            "İçe Aktarma", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Yedek dosyası sır taşımaz; mevcut API anahtarı korunur.
                    data.VirusTotalApiKey = string.Empty;
                    data.VirusTotalApiKeyProtected = _settingsService.Current.VirusTotalApiKeyProtected;

                    IsAlwaysRunAsAdmin = data.AlwaysRunAsAdmin;
                    IsTaskSchedulerAutoStart = data.TaskSchedulerAutoStart;
                    StartWithWindows = data.StartWithWindows;

                    ApplyToProperties(data);
                    SelectTheme(ThemeService.ParseKind(data.Theme));
                    _themeService.ApplyBackdrop(data.IsMicaEnabled);

                    AutoSaveSettings();

                    _log.Info($"Ayarlar içe aktarıldı: {ofd.FileName}", nameof(SettingsViewModel));
                    MessageBox.Show("Ayarlar başarıyla içe aktarıldı ve uygulandı!", "İçe Aktarma Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Ayar içe aktarımı başarısız.", ex, nameof(SettingsViewModel));
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

            var defaults = new AppSettingsData();
            ApplyToProperties(defaults);

            StartWithWindows = false;
            IsAlwaysRunAsAdmin = false;
            IsTaskSchedulerAutoStart = false;

            AdminElevationService.SetAlwaysRunAsAdmin(false);
            AdminElevationService.SetTaskSchedulerAutoStart(false);

            _themeService.ApplyBackdrop(defaults.IsMicaEnabled);

            // Kayıtlı dosyayı silmek yerine varsayılanları yazıyoruz:
            // böylece ayar dosyası her zaman tutarlı ve okunabilir kalır.
            _settingsService.Save(defaults);
            ApplyToProperties(_settingsService.Current);

            _log.Info("Ayarlar fabrika varsayılanlarına sıfırlandı.", nameof(SettingsViewModel));
            MessageBox.Show("Tüm ayarlar başarıyla varsayılan değerlerine döndürüldü.", "Sıfırlama Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region VirusTotal API Management

        [RelayCommand]
        public async Task SaveAndTestVirusTotalKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(VirusTotalApiKey))
            {
                VirusTotalApiStatus = "Lütfen geçerli bir API anahtarı girin.";
                return;
            }

            IsTestingVirusTotalKey = true;
            VirusTotalApiStatus = "VirusTotal API anahtarı doğrulanıyor...";

            try
            {
                // Tekil örnek: anahtar kaydedildiğinde tarama motorları da anında görür.
                bool isValid = await _virusTotalService.ValidateApiKeyAsync(VirusTotalApiKey);
                if (isValid)
                {
                    // Anahtar DPAPI ile şifrelenerek saklanır; appsettings.json'a düz metin yazılmaz.
                    _virusTotalService.SaveApiKey(VirusTotalApiKey);
                    VirusTotalApiStatus = "Başarılı! API anahtarı doğrulandı ve şifrelenerek kaydedildi. ✓";
                }
                else
                {
                    VirusTotalApiStatus = "Geçersiz API Anahtarı! Lütfen kontrol edin. ✗";
                }
            }
            catch (Exception ex)
            {
                _log.Error("VirusTotal anahtar doğrulaması başarısız.", ex, nameof(SettingsViewModel));
                VirusTotalApiStatus = $"Doğrulama hatası: {ex.Message}";
            }
            finally
            {
                IsTestingVirusTotalKey = false;
            }
        }

        #endregion

        #region Sürüm Günlüğü (Release Changelog)

        public ObservableCollection<ReleaseChangelogItem> ReleaseHistory { get; } = new();

        [ObservableProperty]
        private ReleaseChangelogItem? _latestRelease;

        [RelayCommand]
        public void ToggleChangelog(ReleaseChangelogItem? item)
        {
            if (item != null)
            {
                item.IsExpanded = !item.IsExpanded;
            }
        }

        private void InitializeReleaseHistory()
        {
            ReleaseHistory.Clear();

            var v3175 = new ReleaseChangelogItem
            {
                Version = "v3.17.5",
                ReleaseDate = "23 Eylül 2026",
                Title = "Yüksek Hızlı İndirme & Akıcı Kaydırma & Sıfır UI Kilitlenmesi",
                IsLatest = true,
                IsExpanded = true,
                Highlights = new List<string>
                {
                    "Ultra Yüksek Hızlı İndirme Motoru: abbodi1406 resmi GitHub Gigabit CDN'inden doğrudan Visual C++ AIO tek dosya (.exe - 32.1 MB) entegrasyonu sağlandı. 10 kat daha hızlı indirme ve anında sessiz kurulum.",
                    "Sıfır UI Kilitlenmesi (Non-Blocking Dispatcher): İndirme bayt akışının UI mesaj kuyruğunu boğması engellendi. Stopwatch tabanlı 150ms sınırlayıcı ve arka plan iş parçacığı mimarisiyle indirme sırasında sol menü ve pencereler 60 FPS akıcı kaldı.",
                    "Pürüzsüz Piksel Kaydırma (ScrollViewer Fix): Uygulama kataloğu ve hazır paketler için CanContentScroll='False' ve fare tekerleği yönlendirmesi eklenerek takılma ve kaymama sorunu tamamen çözüldü.",
                    "Gelişmiş Canlı Konsol Kontrolü: Konsol çekmecesine Canlı İndirme Hızı (MB/s), Otomatik Kaydırma, Günlükleri Temizle, Panoya Kopyala ve Genişlet/Küçült (110px / 260px) kontrolleri eklendi.",
                    "Yerel .NET 10 SDK Derleme Entegrasyonu: Kullanıcı PC'sinde yerel SDK kurularak tüm sürümlerin deploy öncesi bilgisayarda derlenip doğrulanması güvenceye alındı."
                }
            };

            var v3171 = new ReleaseChangelogItem
            {
                Version = "v3.17.4",
                ReleaseDate = "23 Eylül 2026",
                Title = "Sıfır Sertifika Engeli & Doğrudan Engelsiz Kurulum",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Sıfır Sertifika & Engelsiz Otonom Güncelleme: Tüm sertifika imzalama ve kontrol engelleri kaldırılarak doğrudan GitHub Releases üzerinden hızlı ve sorunsuz güncelleme sağlandı.",
                    "Segmented Görünüm: Mağaza modülü Uygulama Kataloğu ve Hazır Paketler olarak iki ferah sekmeye ayrıldı."
                }
            };

            var v3170 = new ReleaseChangelogItem
            {
                Version = "v3.17.0",
                ReleaseDate = "23 Eylül 2026",
                Title = "Yazılım & All-in-One Runtimes Mağazası (13. Modül & Format Kurtarıcı)",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "13. Bağımsız Modül: Yazılım & Runtimes Mağazası (Store Hub): Format sonrası ihtiyaç duyulan tüm temel kütüphaneler ve popüler yazılımlar tek merkezde toplandı.",
                    "TechPowerUp Visual C++ All-in-One Entegrasyonu: 2005'ten 2022'ye kadar olan tüm x86/x64 VC++ Redistributable paketlerini TechPowerUp sunucularından doğrudan ve güvenli indiren, otomatik çıkaran ve install_all.bat ile sessiz kuran otonom kurulum motoru.",
                    "DirectX End-User Runtimes Otomasyonu: Microsoft resmi sunucularından dxwebsetup.exe indirip /Q parametresiyle arka planda sessiz ve eksiksiz kurma yeteneği.",
                    "45+ Popüler Uygulama Kataloğu: 7 kategori altında (Runtimes, Oyun & İstemciler, Müzik & Medya, Yazılım & Sistem Araçları, Web Tarayıcıları, İletişim, Geliştirici & Kodlama) Steam, Discord, Chrome, Spotify, VS Code vb. en popüler uygulamalar.",
                    "4 Adet Akıllı Hazır Paket (Quick Presets): 'Format Kurtarıcı', 'Oyuncu Paketi', 'Ofis & Medya Paketi' ve 'Geliştirici Paketi' ile tek tıkla toplu seçim ve toplu kurulum.",
                    "Akıllı Kurulu Yazılım Tespiti: Windows Kayıt Defteri (Registry 32-bit & 64-bit) üzerinden sistemde halihazırda kurulu olan uygulamaları anında tespit edip 'Yüklü' rozeti verme.",
                    "Toplu Kurulum Sırası & Canlı Konsol: Sıraya eklenen tüm uygulamaları sırayla indiren ve sessiz kuran, canlı log çıktısı ve ilerleme çubuğu sunan interaktif kurulum paneli."
                }
            };

            var v3163 = new ReleaseChangelogItem
            {
                Version = "v3.16.3",
                ReleaseDate = "23 Eylül 2026",
                Title = "Tüm Modüllerde Küçülen Pencere ve Araç Çubuğu Çakışmalarının Kökten Giderilmesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Global Responsive WrapPanel Mimarisi: Pencere küçültüldüğünde ve yüksek DPI ölçeklemelerinde filtre butonlarının, arama kutularının ve eylem butonlarının üst üste binmesi projedeki tüm modüllerde kökten çözüldü.",
                    "Temizleyici Modülü (Cleaner): Kategori hapları, hızlı hazır ayarlar ve dosya filtre çubuğu WrapPanel mimarisine geçirilerek esnek satır kaydırma sağlandı; eski emojiler resmi Fluent ikonlarıyla değiştirildi.",
                    "Hata Analizi Modülü (Crash Analyzer): Olay filtre butonları ile arama kutusu 2 satırlı esnek hiyerarşiye ayrılarak dar pencerelerde sıfır çakışma sağlandı.",
                    "Başlangıç Yöneticisi (Startup): Başlangıç tipi filtre çipleri dar ekranda taşmayı önleyecek şekilde esnek WrapPanel düzenine kavuşturuldu.",
                    "Ağ İzleyici (Network Monitor): Canlı bağlantı protokol ve durum filtre butonları dar pencerelerde arama kutusuna basmayacak şekilde uyarlandı.",
                    "Yazılım Kaldırıcı (Uninstaller): Katman 1 segment butonları ve Katman 2 sıralama/toplu işlem araç çubukları tam responsive WrapPanel ile güçlendirildi.",
                    "Gizlilik & Bloatware (Privacy): Gizlilik kuralları kategori çipleri WrapPanel yapısına geçirilerek arama kutusuyla çarpışması önlendi."
                }
            };

            var v3162 = new ReleaseChangelogItem
            {
                Version = "v3.16.2",
                ReleaseDate = "23 Eylül 2026",
                Title = "Büyük Dosya Analizörü Araç Çubuğu Tam Responsive Revizyonu",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Tam Responsive WrapPanel Araç Çubuğu: Pencere küçültüldüğünde ve farklı DPI ölçeklemelerinde Hedef Sürücü, Boyut Eşiği ve Tarama butonlarının üst üste binmesi ve kesilmesi kökten çözüldü.",
                    "Bağımsız Tarama & Eylem Satırı: Sürücü seçimi ve birincil 'Büyük Dosyaları Tara' / 'Durdur' aksiyonları kartın en üstüne bağımsız bir satır olarak taşınarak sıfır çakışma ve ferah kullanım sağlandı.",
                    "Esnek Kategori ve Eşik Çipleri: Boyut Eşiği ve Tür Filtresi butonları WrapPanel içine alınarak ekran daraldığında alt satıra pürüzsüzce kayması sağlandı.",
                    "Genişletilmiş Sıralama Seçici: 'Boyut (Büyükten)' metninin 'Boyut (Büyükt...' şeklinde kırpılması MinWidth artırılarak engellendi.",
                    "Dinamik Tarama Çubuğu: Tarama yapılmadığı anlarda gereksiz gri boşluk yaratan ilerleme çubuğu gizlenerek arayüz ferahlatıldı."
                }
            };

            var v3161 = new ReleaseChangelogItem
            {
                Version = "v3.16.1",
                ReleaseDate = "23 Eylül 2026",
                Title = "Donanım & Telemetri Yönetim Paneli Yenilenmesi ve Arayüz Düzeltmeleri",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Çift Kartlı Modern Alt Panel Mimarisi: Donanım ve Telemetri sekmesinin alt kısmındaki sıkışık ve taşan tek kart, 'Sistem Raporu & Dışa Aktarım' ve 'Windows Yönetim Konsolları' olarak 2 bağımsız esnek karta ayrıldı.",
                    "Sıfır Metin Kırpılması & Esnek Buton Düzeni: Küçük pencere ve farklı DPI ölçeklerinde yaşanan başlık/açıklama metni kırpılmaları ve buton taşmaları WrapPanel ve dinamik Grid yapısıyla tamamen giderildi.",
                    "Segoe MDL2 Glitch Onarımı: HTML raporu butonundaki yazı tipi sembol eşleme hatasından kaynaklanan 'R' harfi bozukluğu giderilerek resmi Fluent DocumentBulletList sembolü ile değiştirildi.",
                    "Hızlı Görev Yöneticisi Erişimi: Windows yönetim konsolları paneline tek tıkla doğrudan Görev Yöneticisi'ni (taskmgr.exe) açan kısayol butonu entegre edildi."
                }
            };

            var v3160 = new ReleaseChangelogItem
            {
                Version = "v3.16.0",
                ReleaseDate = "23 Eylül 2026",
                Title = "Büyük Dosya Analizörü Kapsamlı Yenilenmesi & Profesyonel Depolama Yönetimi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Kusursuz Responsive Araç Çubuğu: Sürücü seçimi, eşik çipleri (100 MB, 500 MB, 1 GB, 2 GB, 5 GB), tür filtreleri, canlı arama ve sıralama menüsü 2 satırlı esnek düzene kavuşturuldu; dar pencerede taşma ve kırpılmalar tamamen yok edildi.",
                    "Sürücü Depolama Özeti & Hero Boş Durum: Tarama öncesinde devasa karanlık boşluk yerine seçili sürücünün kapasite, kullanım, boş alan ve görsel doluluk grafiği ile 4 tanı kılavuz kartı eklendi.",
                    "Çoklu Seçim & Toplu İşlemler: 'Tümünü Seç' onay kutusu, seçilen dosya sayısı ve toplam boyut rozeti, tek tıkla toplu 'Geri Dönüşüme Taşı' ve 'Kalıcı Olarak Sil' yetenekleri sunuldu.",
                    "Canlı Arama & Çoklu Sıralama: Dosya adı ve uzantıya göre anlık arama (SearchBox) ve boyuta, tarihe ve isme göre çift yönlü sıralama motoru eklendi.",
                    "Gelişmiş Kategori Dağılımı: Taranan dosyaların türlerine göre (Videolar, Disk İmajları, Arşivler, Kurulumlar, Diğer) boyut ve oran dağılımı görselleştirildi."
                }
            };

            var v3154 = new ReleaseChangelogItem
            {
                Version = "v3.15.4",
                ReleaseDate = "22 Eylül 2026",
                Title = "Donanım & Disk Modülü Kök Onarımı ve Akıllı Filtreleme Deneyimi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Komut Parametresi Kök Onarımı: Büyük dosya analizöründeki eşik butonlarının tip uyuşmazlığı (String -> Int64 ArgumentException) evrensel ve savunmacı komut yapısıyla kökten çözüldü.",
                    "Dinamik Eşik & Filtre Vurgulaması: 500 MB, 1 GB, 2 GB ve 5 GB eşik çipleri ile Tür Filtresi butonları artık seçili olan seçeneği anlık olarak parlatıyor (Primary vurgusu).",
                    "Dosya Yolunu Kopyalama: Büyük dosyalar listesine tek tıkla dosya yolunu panoya kopyalama aksiyonu eklendi.",
                    "Otomatik Test Güvencesi: Komut parametrelerinin tip güvenliğini doğrulayan yeni xUnit testleri entegre edildi (139 testin tamamı başarılı)."
                }
            };

            var v3153 = new ReleaseChangelogItem
            {
                Version = "v3.15.3",
                ReleaseDate = "22 Eylül 2026",
                Title = "Windows ile Başlama (Autostart) Kökten Onarımı & Akıllı Güncelleme Mimarisi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Kusursuz Windows Başlangıç Motoru: 'RUNASADMIN' ve Registry Run çakışması Task Scheduler XML mimarisiyle kökten çözüldü.",
                    "Canlı Başlangıç Durum Göstergesi & Tek Tıkla Onar: Ayarlar ekranında gerçek başlangıç durum rozeti ve onarım aracı.",
                    "Akıllı Güncelleme Mimarisi: Güncellemeler %LOCALAPPDATA% altına indirilerek Smart App Control engeli giderildi."
                }
            };

            var v3152 = new ReleaseChangelogItem
            {
                Version = "v3.15.2",
                ReleaseDate = "22 Eylül 2026",
                Title = "Windows ile Başlama (Autostart) Onarımı & XAML Güvencesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Kusursuz Windows Başlangıç Motoru ve ControlAppearance düzeltmesi ilk dağıtımı."
                }
            };

            var v3151 = new ReleaseChangelogItem
            {
                Version = "v3.15.1",
                ReleaseDate = "21 Eylül 2026",
                Title = "Sistem Bilgisi Simgesi Düzeltmesi & Otomatik XAML Doğrulama Koruması",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Sistem Bilgisi XAML İkon Düzeltmesi: Donanım ve depolama modülündeki HTML raporu oluşturma butonunda tanımsız olan 'OpenInNewWindow20' simgesi yerine geçerli 'ArrowExport20' simgesi entegre edilerek çalışma zamanı XamlParseException hatası giderildi.",
                    "Otomatik XAML Sembol Doğrulayıcı (SymbolValidator): Gelecekte hatalı veya uydurma Fluent sembollerinin arayüze eklenmesini derleme/test seviyesinde önleyen otomatik xUnit test mekanizması entegre edildi.",
                    "Sürüm Bütünlüğü & Güvenli Dağıtım: v3.15.1 için tüm bağımlılıklar, manifestolar ve güncelleme servisleri senkronize edildi."
                }
            };

            var v3150 = new ReleaseChangelogItem
            {
                Version = "v3.15.0",
                ReleaseDate = "21 Eylül 2026",
                Title = "Donanım, Depolama & S.M.A.R.T. Modülü Kapsamlı Görsel & Fonksiyonel Revizyonu",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Modern Fluent Segmented Sekme Çubuğu: Aktif alt sekme vurgusu ve ikonlarıyla zenginleştirilmiş modern gezinti paneli (Donanım & Telemetri, S.M.A.R.T. Sağlık, Büyük Dosya Analizörü).",
                    "Bozuk CPU Göstergesinin Giderilmesi & Canlı Yük Göstergesi: Minik ve bozuk halka yerine dinamik renk kodlu kristal netliğinde CPU Yük Göstergesi rozeti (%15 YÜK) ve çekirdek/izlek dökümü entegre edildi.",
                    "Zenginleştirilmiş 6 KPI Donanım Kartı: İşlemci, Ekran Kartı (VRAM, çözünürlük & Hz), Fiziksel Bellek (RAM), Anakart & BIOS, Ağ Kartı (LAN/Wi-Fi hızı & IPv4) ve İşletim Sistemi & Uptime (Çalışma Süresi).",
                    "Etkileşimli Sürücü Kartları: Sabit sürücüler için doğrudan kart üzerinde Disk Temizleme, Büyük Dosyaları Tara ve Gezginde Aç eylemleri.",
                    "Platform Teşhisi & Bellenim Güvenliği: Alt kısımdaki boşluğu değerlendiren Secure Boot, TPM 2.0, Donanım Sanallaştırma ve UAC teşhis paneli.",
                    "Tek Tıkla Sistem Raporu & Hızlı Yönetim: Tüm sistem envanterini modern HTML raporu olarak dışa aktarma, panoya kopyalama, Disk Yönetimi ve Aygıt Yöneticisi kısayolları.",
                    "Gelişmiş S.M.A.R.T. & Büyük Dosya Analizörü: Tek tıkla SSD TRIM optimizasyonu, boyut eşik hapları (500MB+, 1GB+, 2GB+, 5GB+), kategori filtreleri ve Geri Dönüşüm Kutusuna Taşı (Güvenli Silme)."
                }
            };

            var v3140 = new ReleaseChangelogItem
            {
                Version = "v3.14.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Windows Tweaker Master-Detail Mimarisi & Zengin Bilgilendirici Hover Kartları",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Tekil ve Temiz Kategori Yönetimi: Sol menüdeki 13 alt butonluk akordiyon kalabalığı, sayfa içi açılır pencere (popup) ve 5'li hap buton karmaşası tamamen kaldırılarak tek merkezli Master-Detail dikey kategori rayına dönüştürüldü.",
                    "Zengin Kategori Hover Kartları: Her kategorinin üzerine gelindiğinde ne işe yaradığı, sisteme katkısı ve aktif/toplam ayar durumunu gösteren modern bilgilendirme pencereleri eklendi.",
                    "Anlaşılır İnce Ayar Hover Kartları: Teknik Kayıt Defteri (Registry) kodları ve REG_DWORD karmaşası yerine kullanıcıya ayarın gerçekte ne yaptığı, güvenlik durumu, yeniden başlatma gerekip gerekmediği ve nasıl geri alınacağını açıklayan Türkçe yardım kartları eklendi.",
                    "Anti-AI & Profesyonel Tasarım Standartları: Arayüzdeki yapay zeka hissi veren tüm ögeler temizlendi, tamamen yerel Windows 11 Fluent tasarım dili ve resmi simgelerle kurumsal seviyeye getirildi."
                }
            };

            var v3130 = new ReleaseChangelogItem
            {
                Version = "v3.13.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Bellek & Süreçler V2.0: Segmente RAM Barı, Sysinternals RAMMap & Teftiş Çekmecesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Çok Katmanlı Segmente Bellek Barı: Windows 11 Görev Yöneticisi mimarisinde Kullanımda, Değiştirilmiş, Bekleme Listesi (Önbellek) ve Boş RAM oransal renkli gösterimi.",
                    "4'lü Teknik Donanım KPI Kartı: RAM hızı ve yuvaları (MT/s), Sanal Bellek (Commit Charge), Çekirdek Havuzları (Paged/Non-Paged) ve Donanıma Ayrılmış bellek.",
                    "Sysinternals RAMMap Temizleme Motorları: Bekleme Listesi (Clear Standby List - oyunlarda FPS drop ve takılmayı önleyen), Sayfaları Diske Yaz (Flush Modified) ve Derin Boşaltma (Purge All).",
                    "Orijinal Win32 İkonlu Süreç Tablosu: Çalışan uygulamaların gerçek yüksek çözünürlüklü ikonları, anlık arama çubuğu, kategori hapları ve canlı CPU % tüketim ölçümü.",
                    "Sağ Teftiş Çekmecesi (Process Inspector): Süreç dosya yolu, yayıncı bilgisi, uptime, bellek dağılımı, işlemi dondurma/devam ettirme (Suspend/Resume) ve tek tıkla VirusTotal analizi."
                }
            };

            var v3120 = new ReleaseChangelogItem
            {
                Version = "v3.12.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Fluent Tray Flyout & Ghost Mode (Görünmez Başlangıç) Entegrasyonu",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "WPF Fluent Tray Flyout: Eski ve hantal sağ tık (WinForms) menüsü kaldırılarak, Windows 11 Action Center hissiyatında animasyonlu ve gölgeli özel WPF menüsü tasarlandı.",
                    "Ghost Mode (Görünmez Başlangıç): Windows ile başla denildiğinde uygulama ekranda belirmek yerine `--autostart` parametresiyle tamamen arka planda çalışmaya başlıyor.",
                    "Deep Freeze Game Mode İyileştirmeleri: Oyun modu açıldığında, artık sadece güç planı değişmekle kalmıyor, arka plandaki tüm RAM denetim döngüleri de duraklatılarak %0 CPU tüketimi sağlanıyor.",
                    "Memory Leak & Dispose Koruması: Eski sistem tepsisi menüsünden kaynaklı NullReferenceException ve bellek sızıntıları giderildi."
                }
            };

            var v3111 = new ReleaseChangelogItem
            {
                Version = "v3.11.1",
                ReleaseDate = "20 Eylül 2026",
                Title = "Sistem Tepsisinde Kesintisiz Nöbet (Close-to-Tray), Sessiz Başlangıç & Ultra Oyun Modu",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Sistem Tepsisinde Kesintisiz Nöbet (Close-to-Tray): Çarpı (X) butonuna tıklandığında uygulama kapanmak yerine sistem tepsisine küçülür ve arka planda bilgisayarı korumaya devam eder; tamamen kapatmak için tepsiden 'Çıkış' seçilir.",
                    "Sessiz Windows Başlangıcı (--autostart / --tray): Bilgisayar açılırken ekrana pencere fırlatmadan doğrudan arka planda sistem tepsisinde hazır bekler.",
                    "Ultra Oyun Modu (Game Turbo Engine): Oyun oynarken arka plandaki tüm RAM denetimleri, soket taramaları, yenileme döngüleri ve bildirimler tamamen dondurulur; CPU ve RAM %100 oyuna odaklanır.",
                    "Tek Tıkla Oyun Öncesi Bellek & Güç Optimizasyonu: Oyun Modu açıldığı an derin bellek boşaltması yapılır ve Windows Güç Planı otomatik olarak Yüksek Performansa kilitlenir.",
                    "Başlık Çubuğu Mini Telemetri & Canlı Çip: TitleBar üzerinde anlık CPU ve RAM yükü canlı olarak izlenebilir; Oyun Modu tek tıkla başlık çubuğundan veya sistem tepsisinden açılıp kapatılabilir."
                }
            };

            var v390 = new ReleaseChangelogItem
            {
                Version = "v3.9.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Işık Hızında Başlangıç (Login Ekranı Kaldırıldı) & Dinamik Windows Kullanıcı Rozeti",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Doğrudan Ana Panel Başlangıcı (0 Saniye Gecikme): Sistem optimizasyon araçlarında gereksiz parola ve giriş formu sürtünmesi kökten kaldırıldı; uygulama çift tıklandığı an doğrudan Ana Yönetim Paneline açılır.",
                    "Dinamik Windows Kullanıcı Rozeti: Başlık çubuğundaki statik kullanıcı ismi yerine oturum açmış aktif Windows kullanıcısı (Environment.UserName) bağlandı.",
                    "Başlık Çubuğu Sadeleştirmesi: İşlevsiz hale gelen oturumu kapat butonu kaldırılarak pencere başlığı modern ve ferah bir görünüme kavuşturuldu."
                }
            };

            var v380 = new ReleaseChangelogItem
            {
                Version = "v3.8.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Sistem Temizliği Fluent 2, 20 Temizlik Hedefi, Canlı Disk Röntgeni & Teftiş Çekmecesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "20 Genişletilmiş Temizlik Hedefi: Windows Sistem Kalıntıları (Teslim İyileştirme, Shader, WER, CBS/DISM Logları, Thumbnail DB), Web Tarayıcıları (Edge, Chrome, Firefox, Brave, Discord, Telegram) ve Oyun/Medya/Dev (Steam, Spotify, Epic, NuGet/npm/pip) kategorilerine kavuşturuldu.",
                    "Canlı C: Sürücüsü Sağlık & Doluluk Röntgeni: 4. KPI kartı olarak anlık disk doluluk çubuğu, toplam boyut, boş alan ve potansiyel temizlik ferahlığı göstergesi eklendi.",
                    "Akıllı Hızlı Temizlik Profilleri: 🛡️ Hızlı & Güvenli, 🚀 Kapsamlı Derin, 🎮 Oyun & Medya profilleri ile tek tıkla hedef belirleme sağlandı.",
                    "Taranan Dosyalar Canlı Arama & Boyut Filtresi: Binlerce dosya arasında anlık isim/uzantı araması (.log, .tmp, .dmp) ve >1MB, >10MB, >100MB boyut filtreleme butonları eklendi.",
                    "Sağ Teftiş Çekmecesi (Inspection Drawer): Seçilen dosyanın tam yolu, dizini, değiştirilme tarihi, Explorer'da açma, yolu kopyalama ve tek tıkla 'Temizlikten Muaf Tut' (Hariç Bırak) yeteneği sisteme kazandırıldı."
                }
            };

            var v370 = new ReleaseChangelogItem
            {
                Version = "v3.7.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Başlangıç Uygulamaları Fluent 2, KPI Kartları, Win32 İkon Çıkarma & Açılış Hızlandırma",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Windows 11 Fluent 2 Başlangıç Röntgeni: 4 adet etkileşimli KPI istatistik kartı (Toplam Başlatıcı, Etkin, Devre Dışı, Tahmini Boot Gecikmesi) ve modern pill filtreleme çubuğu.",
                    "Yüksek Çözünürlüklü Yerel İkon & Yayıncı Çıkarıcı: Discord, Steam, Spotify, Chrome ve Riot gibi uygulamaların gerçek simgeleri Win32 GDI API'si ile bellek sızıntısız arayüze aktarıldı.",
                    "Genişletilmiş 5 Noktalı Kayıt & Klasör Taraması: Standart Run anahtarlarının yanı sıra 64-bit/32-bit WOW6432Node kayıtları, Kullanıcı Başlangıç Klasörü ve Ortak Sistem Başlangıç Klasörü tam denetime alındı.",
                    "Tek Tıkla Açılışı Hızlandır (Boot Optimizer): Sistemi yavaşlatan yüksek etkili arka plan başlatıcıları tek tıkla analiz edilerek güvenle devre dışı bırakma optimizasyonu sağlandı.",
                    "Başlangıç Uygulaması Ekleme & Kalıcı Silme: Yeni program veya kısayol ekleme, kayıt defterinden veya diskten kalıcı temizleme ve sağ detay teftiş paneli eklendi."
                }
            };

            var v360 = new ReleaseChangelogItem
            {
                Version = "v3.6.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Ağ Röntgeni Seçim Kilidi, Sanal Adaptör Filtresi & DI Sertleştirmesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Ağ Röntgeni Kesintisiz İnceleme (Sticky Inspection): Canlı izleme esnasında arka plan yenilemelerinde seçili soketin ve teftiş çekmecesinin anında kapanıp sıfırlanma sorunu kökten çözüldü.",
                    "Akıllı Ağ Donanım Filtresi: NDIS filtre sürücüleri (QoS Packet Scheduler, WFP LightWeight Filters) ve IP atanmamış alt arayüzler gizlenerek yalnızca gerçek, aktif Ethernet ve Wi-Fi kartları listelendi.",
                    "Tek Tıkla Ağ Onarım Araçları: Ağ Adaptörleri sekmesine doğrudan DNS Sıfırlama (Flush DNS) ve IP Yenileme (ipconfig /renew) butonları eklendi.",
                    "Windows Tweaker Çökme Çözümü & DI Bütünlüğü: TweakerCategoriesViewModel bağımlılık enjeksiyonuna bağlandı ve gelecekte hiçbir ViewModel'in unutulamayacağı dinamik reflection kalkanı kuruldu."
                }
            };

            var v350 = new ReleaseChangelogItem
            {
                Version = "v3.5.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Ağ Operasyon Merkezi Fluent 2 Arayüz Kusursuzlaştırması & Tema Kontrastı",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Windows 11 Fluent Segmente Sekme Çubuğu: Katı renkli blok butonlar yerine yükseltilmiş kart yüzeyi ve pürüzsüz geçişli pill kontrolü.",
                    "Bağlantı & Süreç Röntgeni Paneli İyileştirmesi: Çekmece varsayılanda kapalı başlayarak tabloya tam ekran ferahlığı sağlandı, yerel soket ve IP kırpılmaları kökten çözüldü.",
                    "Aktif Filtre Çipleri: Tümü, TCP, UDP, Dış Bağlantılar, Dinleme ve Şüpheli butonları aktif durum göstergesiyle modernize edildi.",
                    "Tüm Temalarda Kusursuz Kontrast: TextOnAccent tanımları sisteme kazandırılarak 4 temada da yüksek okunabilirlik ve WCAG AA kontrastı garanti altına alındı."
                }
            };

            var v340 = new ReleaseChangelogItem
            {
                Version = "v3.4.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Ağ Operasyon Merkezi, 1000 Mbps Çoklu Akış Hız Testi & Sürüm Günlüğü",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "1000 Mbps Çoklu Akış Hız Testi: Gigabit fiber bağlantılara özel Cloudflare CDN üzerinden 3 paralel soketle kesintisiz 6 saniyelik bant genişliği, canlı Mbps, Ping ve Jitter ölçümü.",
                    "5 Sekmeli Ağ Merkezi: Canlı Bağlantılar, Dinlenen Portlar, Ağ Adaptörleri & IP Donanımı (Gateway, DNS, MAC, Veri Sayacı) ve Ağ Teşhis Araçları.",
                    "Ağ Teşhis Araçları: Canlı Ping (RTT gecikme) testi, Tek Tıkla DNS Önbellek Temizleme (Flush DNS) ve TCP Port Açıklık Denetleyicisi.",
                    "Sağ Teftiş Çekmecesi (Röntgen Paneli): Sürecin dijital imza doğrulaması, ters DNS çözümü, tehdit analizi ve tek tıkla Güvenlik Duvarı engelleme/sonlandırma.",
                    "Arayüz Dikey Hizalama İyileştirmesi: Ayarlar ve tercihler ekranındaki kartların dikeyde ortalanma hatası giderildi; tepeden hizalama uygulandı.",
                    "Zengin Sürüm Günlüğü (Changelog): En son güncelleme yenilikleri ve geçmiş sürümlerin detaylı sürüm notları paneli eklendi."
                }
            };

            var v330 = new ReleaseChangelogItem
            {
                Version = "v3.3.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Ağ Operasyon Merkezi (NOC) & Gigabit Hız Testi Altyapısı",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Ağ ve Bağlantı İzleyici modülü GlassWire ve TCPView düzeyinde baştan tasarlandı.",
                    "Şüpheli portlar ve Temp dizininden dış ağa bağlanan süreçler için güvenlik tehdit analizi eklendi.",
                    "Ağ kartlarının donanım hızları, IP yapılandırmaları ve oturum trafik sayaçları eklendi."
                }
            };

            var v320 = new ReleaseChangelogItem
            {
                Version = "v3.2.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Otomatik Güncelleme & Sürüm Bütünlüğü",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "GitHub Releases CI/CD otomatik güncelleme akışı iyileştirildi.",
                    "Uygulama bildirimlerinde sürüm tutarlılığı ve manifest kontrolleri sağlandı."
                }
            };

            var v310 = new ReleaseChangelogItem
            {
                Version = "v3.1.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Kategori Menülü Ayarlar Ekranı & Şeffaf Sidebar",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Ayarlar ekranı 6 kategorili modern yan menü (Navigation Rail) mimarisine geçirildi.",
                    "Gereksiz teknik açıklamalar sadeleştirildi, 1 cümlelik net bilgilendirme sağlandı.",
                    "Sol gezinti çubuğu (Sidebar) şeffaflaştırılarak başlık çubuğuyla olan renk uyumsuzluğu tamamen giderildi."
                }
            };

            var v300 = new ReleaseChangelogItem
            {
                Version = "v3.0.0",
                ReleaseDate = "19 Eylül 2026",
                Title = "Büyük Tasarım Sistemi & Güvenlik Sertleştirmesi",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Dört Tema Desteği: Mica Koyu, AMOLED Saf Siyah, Cyberpunk Neon Mor ve Fluent Açık.",
                    "Güvenlik: Kaynağa gömülü şifreler kaldırıldı, OWASP PBKDF2-SHA256 parola türetme ve DPAPI şifreleme eklendi.",
                    "Çalışmayan sistem ayarları (Tepsi simgesi, RAM temizleme, Otomatik çıkış temizliği) gerçek Windows API'lerine bağlandı."
                }
            };

            var v250 = new ReleaseChangelogItem
            {
                Version = "v2.5.0",
                ReleaseDate = "17 Eylül 2026",
                Title = "Bakım Sistem Optimizer İlk Sürüm",
                IsLatest = false,
                IsExpanded = false,
                Highlights = new List<string>
                {
                    "Sistem Temizliği, Bellek Optimizasyonu, Başlangıç Programları ve Süreç Yöneticisi modülleri yayınlandı."
                }
            };

            LatestRelease = v3175;

            ReleaseHistory.Add(v3175);
            ReleaseHistory.Add(v3171);
            ReleaseHistory.Add(v3170);
            ReleaseHistory.Add(v3163);
            ReleaseHistory.Add(v3162);
            ReleaseHistory.Add(v3161);
            ReleaseHistory.Add(v3160);
            ReleaseHistory.Add(v3154);
            ReleaseHistory.Add(v3153);
            ReleaseHistory.Add(v3152);
            ReleaseHistory.Add(v3151);
            ReleaseHistory.Add(v3150);
            ReleaseHistory.Add(v3140);
            ReleaseHistory.Add(v3130);
            ReleaseHistory.Add(v3120);
            ReleaseHistory.Add(v3111);
            ReleaseHistory.Add(v390);
            ReleaseHistory.Add(v380);
            ReleaseHistory.Add(v370);
            ReleaseHistory.Add(v360);
            ReleaseHistory.Add(v350);
            ReleaseHistory.Add(v340);
            ReleaseHistory.Add(v330);
            ReleaseHistory.Add(v320);
            ReleaseHistory.Add(v310);
            ReleaseHistory.Add(v300);
            ReleaseHistory.Add(v250);
        }

        #endregion
    }
}
