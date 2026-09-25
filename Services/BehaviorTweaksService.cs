using System.Diagnostics;
using Microsoft.Win32;
using Bakım.Models;
using Bakım.Helpers;

namespace Bakım.Services
{
    public interface IBehaviorTweaksService
    {
        Task<List<SystemTweakItem>> GetBehaviorTweaksAsync();
        Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable);
        Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks);
        Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks);
    }

    public class BehaviorTweaksService : IBehaviorTweaksService
    {
        // Caps Lock tuşunu donanım seviyesinde sıfırlayan Scancode Map (0x3A -> 0x00)
        private static readonly byte[] CapsLockDisableScancodeMap =
        [
            0x00, 0x00, 0x00, 0x00, // Version
            0x00, 0x00, 0x00, 0x00, // Flags
            0x02, 0x00, 0x00, 0x00, // Count: 1 mapping + 1 null terminator = 2
            0x00, 0x00, 0x3A, 0x00, // Map 0x003A (Caps Lock) to 0x0000 (Null)
            0x00, 0x00, 0x00, 0x00  // Null Terminator
        ];

        public async Task<List<SystemTweakItem>> GetBehaviorTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<SystemTweakItem>
                {
                    // 1. Ads and Unwanted Apps
                    new()
                    {
                        Id = "ads_unwanted",
                        Category = "Davranışlar (Behavior)",
                        Title = "Otomatik İstenmeyen Uygulama İndirmelerini Kapat",
                        Description = "Windows'un arka planda sessizce oyun ve reklam uygulamaları (Candy Crush, TikTok vb.) indirmesini ve Başlat önerilerini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "DismissCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0)
                    },

                    // 2. Automatic Registry Backup
                    new()
                    {
                        Id = "auto_reg_backup",
                        Category = "Davranışlar (Behavior)",
                        Title = "Otomatik Kayıt Defteri (RegBack) Yedeklemesini Aktif Et",
                        Description = "Windows 10/11'de kapatılan otomatik sistem kovanı (Registry) yedeklemesini System32\\config\\RegBack altında yeniden başlatır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "Save24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager", "EnableDirBackup", 1)
                    },

                    // 3. Disable Aero Shake
                    new()
                    {
                        Id = "disable_aero_shake",
                        Category = "Davranışlar (Behavior)",
                        Title = "Aero Shake (Pencere Sallayarak Küçültme) Özelliğini Kapat",
                        Description = "Bir pencerenin başlığından tutup sallandığında diğer tüm açık pencerelerin simge durumuna küçülmesini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Window24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", 1)
                    },

                    // 4. Disable Aero Snap
                    new()
                    {
                        Id = "disable_aero_snap",
                        Category = "Davranışlar (Behavior)",
                        Title = "Aero Snap (Pencere Kenara Yaslama) Özelliğini Kapat",
                        Description = "Pencereleri ekranın kenarlarına sürükleyerek yarım veya çeyrek ekrana otomatik boyutlandırma davranışını devre dışı bırakır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "SplitHorizontal24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "WindowArrangementActive", "0")
                    },

                    // 5. Disable App Lookup in Store
                    new()
                    {
                        Id = "disable_store_lookup",
                        Category = "Davranışlar (Behavior)",
                        Title = "Birlikte Aç Menüsünde 'Store'da Uygulama Ara'yı Kapat",
                        Description = "Bilinmeyen bir dosya türü açıldığında 'Microsoft Store üzerinde bir uygulama arayın' önerisini kapatıp yerel program listesini gösterir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Apps24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoUseStoreOpenWith", 1)
                    },

                    // 6. Disable Automatic Maintenance
                    new()
                    {
                        Id = "disable_auto_maintenance",
                        Category = "Davranışlar (Behavior)",
                        Title = "Otomatik Sistem Bakımını (Disk & Güncelleme Taraması) Kapat",
                        Description = "Bilgisayar boştayken Windows'un arka planda ağır bakım ve disk taraması yaparak %100 disk kullanımına sebep olmasını engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Timer24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1)
                    },

                    // 7. Disable Downloads Blocking (SmartScreen Zone Identifier)
                    new()
                    {
                        Id = "disable_zone_identifier",
                        Category = "Davranışlar (Behavior)",
                        Title = "İnternetten İndirilen Dosyaların 'Engellendi' İşaretini Kaldır",
                        Description = "Tarayıcıdan indirilen dosya ve arşivlere iliştirilen NTFS Zone.Identifier etiketini kaldırarak 'Bu dosya engellendi' uyarısını sonlandırır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "CheckmarkCircle24",
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", 1)
                    },

                    // 8. Disable Driver Updates
                    new()
                    {
                        Id = "disable_driver_updates",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Update Otomatik Sürücü Güncellemelerini Kapat",
                        Description = "Windows Update'in ekran kartı, ses ve yonga seti sürücülerini otomatik indirip mevcut kararlı sürücüleri ezmesini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "ArrowDownload24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUIDriverInQualityUpdate", 1)
                    },

                    // 9. Disable MRT From Installing
                    new()
                    {
                        Id = "disable_mrt_install",
                        Category = "Davranışlar (Behavior)",
                        Title = "Kötü Amaçlı Yazılım Temizleme Aracı'nın (MRT) İndirilmesini Kapat",
                        Description = "Windows Update üzerinden her ay yüzlerce megabaytlık Microsoft Kötü Amaçlı Yazılımları Temizleme Aracı'nın (MRT) zorla indirilmesini engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "ShieldDismiss24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\MRT", "DontOfferThroughWUAU", 1)
                    },

                    // 10. Disable SmartScreen
                    new()
                    {
                        Id = "disable_smartscreen",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Defender SmartScreen Uyarılarını Tamamen Kapat",
                        Description = "İnternetten indirilen veya imzasız çalıştırılabilir (.exe) dosyaların başlatılmasını engelleyen SmartScreen mavi ekran uyarılarını kapatır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Shield24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen", 0) ||
                                    CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0)
                    },

                    // 11. Disable User Folder Backup to OneDrive
                    new()
                    {
                        Id = "onedrive_user_folders",
                        Category = "Davranışlar (Behavior)",
                        Title = "Kullanıcı Klasörlerinin OneDrive'a Zorla Yedeklenmesini Engelle",
                        Description = "Masaüstü, Belgeler ve Resimler klasörlerinin kullanıcı onayı olmadan otomatik OneDrive bulutuna taşınmasını engeller.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "CloudDismiss24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\OneDrive", "PreventOneDriveFileSync", 1)
                    },

                    // 12. Disable Windows Update
                    new()
                    {
                        Id = "disable_windows_update",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Update Hizmetlerini Durdur ve Devre Dışı Bırak",
                        Description = "Windows Update (wuauserv) ve Arka Plan Akıllı Aktarım (BITS) hizmetlerini tamamen durdurup otomatik güncelleme denetimini kilitler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "ClockDismiss24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4) ||
                                    CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1)
                    },

                    // 13. Enable Crash on Ctrl+Scroll Lock
                    new()
                    {
                        Id = "crash_ctrl_scroll",
                        Category = "Davranışlar (Behavior)",
                        Title = "Sağ Ctrl + 2x Scroll Lock ile Test BSOD Tetiklemeyi Aç",
                        Description = "Geliştiriciler ve hata ayıklayıcılar için klavye kombinasyonuyla kontrollü sistem çökme dökümü (Memory Dump) üretme kısayolunu açar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "DeveloperBoard24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "CrashOnCtrlScroll", 1)
                    },

                    // 14. Enable Emoji Picker
                    new()
                    {
                        Id = "enable_emoji_picker",
                        Category = "Davranışlar (Behavior)",
                        Title = "Win + Nokta (.) Emoji ve İfade Seçici Panelini Etkinleştir",
                        Description = "Klavyeden Win + . veya Win + ; tuşlarına basıldığında modern Windows Emoji, GIF ve Kaomoji paneli kısayolunu açık tutar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Emoji24",
                        IsEnabled = !CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Input\Settings", "EnableExpressiveInputShellHotkey", 0)
                    },

                    // 15. Error Reporting
                    new()
                    {
                        Id = "disable_error_reporting",
                        Category = "Davranışlar (Behavior)",
                        Title = "Windows Hata Raporlama ve Çökme Bildirimlerini (WER) Kapat",
                        Description = "Uygulama veya sistem çöktüğünde Microsoft sunucularına arka planda tanılama dökümü gönderen Windows Hata Raporlama servisini pasife alır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Alert24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)
                    },

                    // 16. Keep Thumbnail Cache
                    new()
                    {
                        Id = "keep_thumbnail_cache",
                        Category = "Davranışlar (Behavior)",
                        Title = "Küçük Resim (Thumbnail) Önbelleğini Kalıcı Olarak Koru",
                        Description = "Disk Temizleme ve otomatik bakımın resim/video küçük resimlerini silmesini engelleyerek klasörlerin her seferinde hızlı açılmasını sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Image24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache", "Autorun", 0)
                    },

                    // 17. Menu Show Delay
                    new()
                    {
                        Id = "menu_show_delay",
                        Category = "Davranışlar (Behavior)",
                        Title = "Başlat ve Menü Açılış Gecikmesini Sıfırla (0 ms Hızlı Menüler)",
                        Description = "Windows'un sağ tık, Başlat ve alt menüleri açarken beklettiği 400 milisaniyelik gecikmeyi 0 ms yaparak arayüzü anında tepki verir hale getirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "TopSpeed24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0")
                    },

                    // 18. New Apps Notification
                    new()
                    {
                        Id = "disable_new_app_alert",
                        Category = "Davranışlar (Behavior)",
                        Title = "'Yeni Bir Uygulamanız Var' Bildirim Balonlarını Kapat",
                        Description = "Yeni bir program veya uygulama kurulduğunda ekranda beliren 'Bu dosya türünü açabilecek yeni bir uygulamanız var' uyarısını kapatır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Alert24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoNewAppAlert", 1)
                    },

                    // 19. Redefine Extra Keys on Keyboard (Disable Caps Lock)
                    new()
                    {
                        Id = "disable_caps_lock_key",
                        Category = "Davranışlar (Behavior)",
                        Title = "Klavyedeki Caps Lock Tuşunu Tamamen Devre Dışı Bırak",
                        Description = "Yazı yazarken yanlışlıkla Caps Lock tuşuna basılıp büyük harfe geçilmesini donanım seviyesinde (Scancode Map) devre dışı bırakır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = false,
                        IconSymbol = "Keyboard24",
                        IsEnabled = CheckRegistryBinary(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map")
                    },

                    // 20. Restore Point Frequency
                    new()
                    {
                        Id = "restore_point_frequency",
                        Category = "Davranışlar (Behavior)",
                        Title = "Sistem Geri Yükleme Noktası Oluşturma Sıklık Sınırını Kaldır",
                        Description = "Windows'un 24 saat içinde tek bir geri yükleme noktası oluşturulmasına izin veren sınırlamasını kaldırarak istendiği an yedek almayı sağlar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "ArrowCounterclockwise24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0)
                    },

                    // 21. Screen Saver Grace Period
                    new()
                    {
                        Id = "screensaver_grace_period",
                        Category = "Davranışlar (Behavior)",
                        Title = "Ekran Koruyucu Şifre İsteme Tolerans Süresini Sıfırla (Anında Kilit)",
                        Description = "Ekran koruyucu açıldıktan sonra tanınan 5 saniyelik şifresiz giriş toleransını kaldırarak ekran kararır kararmaz hemen şifre ister.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "LockClosed24",
                        IsEnabled = CheckRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "ScreenSaverGracePeriod", "0")
                    },

                    // 22. Show BSOD, Disable Smiley
                    new()
                    {
                        Id = "bsod_parameters",
                        Category = "Davranışlar (Behavior)",
                        Title = "Detaylı Mavi Ekran (BSOD) Hata Kodlarını Göster",
                        Description = "Sistem çöktüğünde sade gülen yüz yerine tam STOP hata kodunu, bellek adresini ve çökmeye neden olan sürücüyü ekrana yansıtır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "HeartPulse24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1)
                    },

                    // 23. Sound for Print Screen key
                    new()
                    {
                        Id = "sound_print_screen",
                        Category = "Davranışlar (Behavior)",
                        Title = "Print Screen Tuşu ile Ekran Alındığında Ses Çal",
                        Description = "Klavyeden Print Screen (PrtScn) tuşuna basılarak ekran görüntüsü alındığında sesli geribildirim efekti çalar.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Speaker224",
                        IsEnabled = CheckPrintScreenSoundEnabled()
                    },

                    // 24. Split Threshold for Svchost
                    new()
                    {
                        Id = "svchost_grouping",
                        Category = "Davranışlar (Behavior)",
                        Title = "Svchost Süreçlerini Grupla (RAM Tasarrufu)",
                        Description = "Windows 10/11'de servislerin onlarca ayrı svchost.exe açması yerine tek grupta toplanmasını sağlayarak RAM kullanımını azaltır.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = true,
                        IsRecommended = true,
                        IconSymbol = "TopSpeed24",
                        IsEnabled = IsSvchostGroupingEnabled()
                    },

                    // 25. USB Write Protection
                    new()
                    {
                        Id = "usb_write_protect",
                        Category = "Davranışlar (Behavior)",
                        Title = "USB Yazma Korumasını Etkinleştir (Salt-Okunur)",
                        Description = "Bilgisayara takılan tüm USB bellekleri salt-okunur yapar; dosya silinmesini, veri hırsızlığını ve virüs bulaşmasını önler.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "UsbStick24",
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies", "WriteProtect", 1)
                    },

                    // 26. Windows Installer in Safe Mode
                    new()
                    {
                        Id = "safemode_msi",
                        Category = "Davranışlar (Behavior)",
                        Title = "Güvenli Modda Windows Installer (MSI) Kurulumuna İzin Ver",
                        Description = "Windows Güvenli Modda (Safe Mode) normalde engellenen MSI paketlerinin kurulabilmesi ve kaldırılabilmesi için MSIServer servisini yetkilendirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = true,
                        RequiresRestart = false,
                        IsRecommended = true,
                        IconSymbol = "Apps24",
                        IsEnabled = CheckSafeModeMsiEnabled()
                    },

                    // 27. XMouse Options
                    new()
                    {
                        Id = "xmouse_window_tracking",
                        Category = "Davranışlar (Behavior)",
                        Title = "XMouse: Fareyle Üzerine Gelinen Pencereyi Otomatik Odakla",
                        Description = "Fare imleci bir pencerenin üzerine getirildiğinde tıklamaya gerek kalmadan pencereyi otomatik olarak aktif ve odaklı hale getirir.",
                        Type = TweakType.Toggle,
                        RequiresAdmin = false,
                        RequiresRestart = false,
                        IsRecommended = false,
                        IconSymbol = "Cursor24",
                        IsEnabled = IsXMouseEnabled()
                    }
                };

                return list;
            });
        }

        public async Task<bool> ApplyTweakAsync(SystemTweakItem tweak, bool enable)
        {
            return await Task.Run(() =>
            {
                using var writes = WriteScope.Begin();
                try
                {
                    switch (tweak.Id)
                    {
                        // 1. Ads and Unwanted Apps
                        case "ads_unwanted":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", enable ? 0 : 1);
                            break;

                        // 2. Automatic Registry Backup
                        case "auto_reg_backup":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager", "EnableDirBackup", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager", "EnableDirBackup");
                            }
                            break;

                        // 3. Disable Aero Shake
                        case "disable_aero_shake":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "DisallowShaking", enable ? 1 : 0);
                            break;

                        // 4. Disable Aero Snap
                        case "disable_aero_snap":
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "WindowArrangementActive", enable ? "0" : "1");
                            break;

                        // 5. Disable App Lookup in Store
                        case "disable_store_lookup":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoUseStoreOpenWith", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoUseStoreOpenWith");
                            }
                            break;

                        // 6. Disable Automatic Maintenance
                        case "disable_auto_maintenance":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled");
                            }
                            break;

                        // 7. Disable Downloads Blocking (SmartScreen Zone Identifier)
                        case "disable_zone_identifier":
                            int zoneVal = enable ? 1 : 2;
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", zoneVal);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", zoneVal);
                            break;

                        // 8. Disable Driver Updates
                        case "disable_driver_updates":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUIDriverInQualityUpdate", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUIDriverInQualityUpdate");
                            }
                            break;

                        // 9. Disable MRT From Installing
                        case "disable_mrt_install":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\MRT", "DontOfferThroughWUAU", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\MRT", "DontOfferThroughWUAU");
                            }
                            break;

                        // 10. Disable SmartScreen
                        case "disable_smartscreen":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0);
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControlEnabled", 0);
                                SetRegistryString(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControl", "Anywhere");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreenHost", "EnableSmartScreen");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControlEnabled");
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen", "ConfigureAppInstallControl");
                            }
                            break;

                        // 11. Disable User Folder Backup to OneDrive
                        case "onedrive_user_folders":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\OneDrive", "PreventOneDriveFileSync", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\OneDrive", "PreventOneDriveFileSync");
                            }
                            break;

                        // 12. Disable Windows Update
                        case "disable_windows_update":
                            SetWindowsUpdateState(enable);
                            break;

                        // 13. Enable Crash on Ctrl+Scroll Lock
                        case "crash_ctrl_scroll":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\i8042prt\Parameters", "CrashOnCtrlScroll", 1);
                                SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "CrashOnCtrlScroll", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\i8042prt\Parameters", "CrashOnCtrlScroll");
                                DeleteRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "CrashOnCtrlScroll");
                            }
                            break;

                        // 14. Enable Emoji Picker
                        case "enable_emoji_picker":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Input\Settings", "EnableExpressiveInputShellHotkey", enable ? 1 : 0);
                            break;

                        // 15. Error Reporting
                        case "disable_error_reporting":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled");
                            }
                            break;

                        // 16. Keep Thumbnail Cache
                        case "keep_thumbnail_cache":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache", "Autorun", enable ? 0 : 1);
                            break;

                        // 17. Menu Show Delay
                        case "menu_show_delay":
                            SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", enable ? "0" : "400");
                            break;

                        // 18. New Apps Notification
                        case "disable_new_app_alert":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoNewAppAlert", 1);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "NoNewAppAlert");
                            }
                            break;

                        // 19. Redefine Extra Keys on Keyboard (Disable Caps Lock)
                        case "disable_caps_lock_key":
                            if (enable)
                            {
                                SetRegistryBinary(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map", CapsLockDisableScancodeMap);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Keyboard Layout", "Scancode Map");
                            }
                            break;

                        // 20. Restore Point Frequency
                        case "restore_point_frequency":
                            if (enable)
                            {
                                SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency");
                            }
                            break;

                        // 21. Screen Saver Grace Period
                        case "screensaver_grace_period":
                            if (enable)
                            {
                                SetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "ScreenSaverGracePeriod", "0");
                            }
                            else
                            {
                                DeleteRegistryValue(Registry.CurrentUser, @"Control Panel\Desktop", "ScreenSaverGracePeriod");
                            }
                            break;

                        // 22. Show BSOD, Disable Smiley
                        case "bsod_parameters":
                            SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", enable ? 1 : 0);
                            break;

                        // 23. Sound for Print Screen key
                        case "sound_print_screen":
                            string sound = enable ? @"C:\Windows\Media\Windows Ding.wav" : string.Empty;
                            SetRegistryString(Registry.CurrentUser, @"AppEvents\Schemes\Apps\.Default\SnapShot\.Current", "", sound);
                            break;

                        // 24. Split Threshold for Svchost
                        case "svchost_grouping":
                            int threshold = enable ? int.MaxValue : 3670016;
                            SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", threshold);
                            break;

                        // 25. USB Write Protection
                        case "usb_write_protect":
                            SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\StorageDevicePolicies", "WriteProtect", enable ? 1 : 0);
                            break;

                        // 26. Windows Installer in Safe Mode
                        case "safemode_msi":
                            if (enable)
                            {
                                SetRegistryString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer", "", "Service");
                                SetRegistryString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\MSIServer", "", "Service");
                            }
                            else
                            {
                                DeleteRegistryKey(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer");
                                DeleteRegistryKey(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\MSIServer");
                            }
                            break;

                        // 27. XMouse Options
                        case "xmouse_window_tracking":
                            SetXMouse(enable);
                            break;

                        default:
                            return false;
                    }

                    if (!writes.Succeeded)
                    {
                        tweak.LastError = writes.Describe();
                        return false;
                    }

                    tweak.LastError = null;
                    tweak.IsEnabled = enable;
                    return true;
                }
                catch (Exception ex)
                {
                    tweak.LastError = ex.Message;
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAllRecommendedAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsRecommended && !tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, true);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        public async Task<bool> RestoreDefaultsAsync(List<SystemTweakItem> tweaks)
        {
            bool allOk = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, false);
                    if (!ok) allOk = false;
                }
            }
            return allOk;
        }

        #region Registry Helper Routines

        private static bool CheckRegistryDword(RegistryKey root, string subKey, string valueName, int expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName);
                if (val == null) return false;
                return Convert.ToInt32(val) == expectedValue;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryString(RegistryKey root, string subKey, string valueName, string expectedValue)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName)?.ToString();
                return string.Equals(val, expectedValue, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRegistryBinary(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return false;
                var val = key.GetValue(valueName) as byte[];
                return val != null && val.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSvchostGroupingEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
                if (key == null) return false;
                var val = key.GetValue("SvcHostSplitThresholdInKB");
                if (val == null) return false;
                long num = Convert.ToInt64(val);
                return num > 3670016;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckSafeModeMsiEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SafeBoot\Minimal\MSIServer");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckPrintScreenSoundEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\SnapShot\.Current");
                if (key == null) return false;
                var val = key.GetValue("")?.ToString();
                return !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsXMouseEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (key == null) return false;
                var trackVal = key.GetValue("ActiveWindowTracking");
                if (trackVal != null && Convert.ToInt32(trackVal) == 1) return true;

                var mask = key.GetValue("UserPreferencesMask") as byte[];
                if (mask != null && mask.Length > 0)
                {
                    return (mask[0] & 0x01) != 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void SetXMouse(bool enable)
        {
            const string desktop = @"Control Panel\Desktop";
            SetRegistryDword(Registry.CurrentUser, desktop, "ActiveWindowTracking", enable ? 1 : 0);

            byte[]? mask;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(desktop, false);
                mask = key?.GetValue("UserPreferencesMask") as byte[];
            }
            catch (Exception ex)
            {
                WriteScope.Report($@"HKEY_CURRENT_USER\{desktop} → UserPreferencesMask: {ex.Message}");
                return;
            }

            if (mask == null || mask.Length == 0) return;
            if (enable)
                mask[0] |= 0x01;
            else
                mask[0] = (byte)(mask[0] & ~0x01);

            SetRegistryBinary(Registry.CurrentUser, desktop, "UserPreferencesMask", mask);
        }

        private static void SetWindowsUpdateState(bool disableUpdates)
        {
            try
            {
                if (disableUpdates)
                {
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 4);
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\bits", "Start", 4);
                    SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1);

                    RunCommandHidden("net.exe", "stop", "wuauserv", "/y");
                    RunCommandHidden("net.exe", "stop", "bits", "/y");
                }
                else
                {
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 3);
                    SetRegistryDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\bits", "Start", 3);
                    DeleteRegistryValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate");

                    RunCommandHidden("net.exe", "start", "bits");
                    RunCommandHidden("net.exe", "start", "wuauserv");
                }
            }
            catch { }
        }

        /// <summary>
        /// En iyi çaba ile hizmet durdurma/başlatma. Hizmetin kalıcı durumu "Start" değeriyle
        /// ayarlandığı için buradaki hata (ör. zaten durmuş hizmet) ince ayarı başarısız saymaz.
        /// </summary>
        private static void RunCommandHidden(string fileName, params string[] args)
        {
            ProcessRunner.Run(fileName, args, TimeSpan.FromSeconds(15));
        }

        private static bool SetRegistryDword(RegistryKey root, string subKey, string valueName, int value) =>
            VerifiedRegistry.SetDword(root, subKey, valueName, value);

        private static bool SetRegistryString(RegistryKey root, string subKey, string valueName, string value) =>
            VerifiedRegistry.SetString(root, subKey, valueName, value);

        private static bool SetRegistryBinary(RegistryKey root, string subKey, string valueName, byte[] value) =>
            VerifiedRegistry.SetBinary(root, subKey, valueName, value);

        private static bool DeleteRegistryValue(RegistryKey root, string subKey, string valueName) =>
            VerifiedRegistry.DeleteValue(root, subKey, valueName);

        private static bool DeleteRegistryKey(RegistryKey root, string subKey) =>
            VerifiedRegistry.DeleteKeyTree(root, subKey);

        #endregion
    }
}
