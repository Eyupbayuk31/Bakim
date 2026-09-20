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

                ApplyToProperties(_settingsService.Current);

                // Tema açılışta App tarafından zaten uygulandı; burada yalnızca
                // seçim durumu eşitlenir, yeniden boyama yapılmaz (titreme olmaz).
                SyncThemeSelection(_themeService.CurrentTheme);

                LoadAutostartPreference();
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

            var v370 = new ReleaseChangelogItem
            {
                Version = "v3.7.0",
                ReleaseDate = "20 Eylül 2026",
                Title = "Başlangıç Uygulamaları Fluent 2, KPI Kartları, Win32 İkon Çıkarma & Açılış Hızlandırma",
                IsLatest = true,
                IsExpanded = true,
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

            LatestRelease = v370;

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
