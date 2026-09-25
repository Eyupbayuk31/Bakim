using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IPrivacyDebloatService
    {
        Task<List<PrivacyTweakItem>> GetPrivacyTweaksAsync();
        Task<bool> ApplyTweakAsync(PrivacyTweakItem tweak, bool enable);
        Task<bool> ApplyAllRecommendedAsync(List<PrivacyTweakItem> tweaks);
        Task<bool> RestoreAllDefaultsAsync(List<PrivacyTweakItem> tweaks);

        Task<List<BloatwareAppItem>> GetInstalledBloatwareAsync();
        Task<bool> RemoveBloatwareAsync(BloatwareAppItem app);

        Task<bool> CreateRestorePointAsync(string description);
        string LastRestorePointMessage { get; }
    }

    public class PrivacyDebloatService : IPrivacyDebloatService
    {
        private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";

        private static readonly string[] TelemetryHostsEntries = new[]
        {
            "0.0.0.0 telemetry.microsoft.com",
            "0.0.0.0 v10.events.data.microsoft.com",
            "0.0.0.0 v20.events.data.microsoft.com",
            "0.0.0.0 vortex.data.microsoft.com",
            "0.0.0.0 diagnostic.support.microsoft.com",
            "0.0.0.0 watson.telemetry.microsoft.com"
        };

        #region Privacy Tweaks Definitions

        public async Task<List<PrivacyTweakItem>> GetPrivacyTweaksAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<PrivacyTweakItem>
                {
                    // 1. Telemetri & Tanılama
                    new()
                    {
                        Id = "telemetry_data",
                        Category = "Telemetri & Tanılama",
                        Title = "Windows Tanılama ve Telemetri Verilerini Kapat",
                        Description = "Kullanıcı alışkanlıkları ve sistem günlüklerinin Microsoft sunucularına gönderilmesini engeller.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)
                    },
                    new()
                    {
                        Id = "telemetry_services",
                        Category = "Telemetri & Tanılama",
                        Title = "DiagTrack ve WAP Push Telemetri Servislerini Durdur",
                        Description = "Arka planda sürekli veri toplayan DiagTrack ve dmwappushservice servislerini pasife alır.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckServiceDisabled("DiagTrack")
                    },
                    new()
                    {
                        Id = "error_reporting",
                        Category = "Telemetri & Tanılama",
                        Title = "Windows Hata Raporlama (WER) Servisini Kapat",
                        Description = "Uygulama çökmelerinde bellek dökümlerinin Microsoft'a iletilmesini engeller.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)
                    },
                    new()
                    {
                        Id = "hosts_telemetry",
                        Category = "Telemetri & Tanılama",
                        Title = "Telemetri Sunucularını Hosts Dosyasında Blokla",
                        Description = "Microsoft veri toplama alan adlarını yerel 0.0.0.0 IP adresine yönlendirerek ağ düzeyinde engeller.",
                        RiskLevel = "Gelişmiş",
                        IsRecommended = false,
                        IsEnabled = CheckHostsBlocked()
                    },

                    // 2. Reklamlar & Öneriler
                    new()
                    {
                        Id = "advertising_id",
                        Category = "Reklamlar & Öneriler",
                        Title = "Kişiselleştirilmiş Reklam Kimliğini (Advertising ID) Kapat",
                        Description = "Uygulamaların kullanıcı profilini izleyerek hedeflenmiş reklam göstermesini engeller.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)
                    },
                    new()
                    {
                        Id = "start_suggestions",
                        Category = "Reklamlar & Öneriler",
                        Title = "Başlat Menüsü Uygulama Önerilerini ve Reklamları Kapat",
                        Description = "Başlat menüsünde önerilen veya sponsorlu uygulama tanıtımlarını gizler.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0)
                    },
                    new()
                    {
                        Id = "tailored_experiences",
                        Category = "Reklamlar & Öneriler",
                        Title = "Tanılama Verilerine Dayalı Özel Deneyimleri Kapat",
                        Description = "Microsoft'un kullanım verilerinize göre kişiselleştirilmiş ipuçları ve öneriler sunmasını durdurur.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)
                    },
                    new()
                    {
                        Id = "lockscreen_tips",
                        Category = "Reklamlar & Öneriler",
                        Title = "Kilit Ekranı İpuçları ve Promosyonları Kapat",
                        Description = "Windows Spotlight kilit ekranında gösterilen reklam ve ipuçlarını devre dışı bırakır.",
                        RiskLevel = "İsteğe Bağlı",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0)
                    },

                    // 3. Konum & İzinler
                    new()
                    {
                        Id = "location_tracking",
                        Category = "Konum & İzinler",
                        Title = "Windows Konum Takibini ve Sensörlerini Devre Dışı Bırak",
                        Description = "İşletim sisteminin ve mağaza uygulamalarının fiziksel cihaz konumunu okumasını kısıtlar.",
                        RiskLevel = "İsteğe Bağlı",
                        IsRecommended = false,
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1)
                    },
                    new()
                    {
                        Id = "activity_history",
                        Category = "Konum & İzinler",
                        Title = "Etkinlik Geçmişi (Timeline) ve Bulut Eşitlemesini Kapat",
                        Description = "Açılan belgelerin ve uygulama aktivitelerinin bulut hesabınızla eşleşmesini engeller.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0)
                    },
                    new()
                    {
                        Id = "bing_search",
                        Category = "Konum & İzinler",
                        Title = "Windows Arama Çubuğunda Bing Web Aramasını Kapat",
                        Description = "Başlat menüsünde dosya ararken internet araması yapılmasını engelleyerek aramayı hızlandırır.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0)
                    },
                    new()
                    {
                        Id = "cortana_voice",
                        Category = "Konum & İzinler",
                        Title = "Cortana Sesli Asistan Entegrasyonunu Kapat",
                        Description = "Cortana arka plan süreçlerini ve ses tanıma telemetrisini devre dışı bırakır.",
                        RiskLevel = "Önerilen",
                        IsRecommended = true,
                        IsEnabled = CheckRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0)
                    }
                };

                return list;
            });
        }

        #endregion

        #region Tweak Execution & Rollback

        public async Task<bool> ApplyTweakAsync(PrivacyTweakItem tweak, bool enable)
        {
            return await Task.Run(() =>
            {
                try
                {
                    switch (tweak.Id)
                    {
                        case "telemetry_data":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", enable ? 0 : 1);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", enable ? 0 : 1);
                            break;

                        case "telemetry_services":
                            ConfigureServiceState("DiagTrack", enable);
                            ConfigureServiceState("dmwappushservice", enable);
                            break;

                        case "error_reporting":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", enable ? 1 : 0);
                            break;

                        case "hosts_telemetry":
                            ApplyHostsBlocking(enable);
                            break;

                        case "advertising_id":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", enable ? 1 : 0);
                            break;

                        case "start_suggestions":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", enable ? 0 : 1);
                            break;

                        case "tailored_experiences":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", enable ? 0 : 1);
                            break;

                        case "lockscreen_tips":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", enable ? 0 : 1);
                            break;

                        case "location_tracking":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", enable ? 1 : 0);
                            break;

                        case "activity_history":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", enable ? 0 : 1);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", enable ? 0 : 1);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", enable ? 0 : 1);
                            break;

                        case "bing_search":
                            SetRegistryDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", enable ? 0 : 1);
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", enable ? 1 : 0);
                            break;

                        case "cortana_voice":
                            SetRegistryDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", enable ? 0 : 1);
                            break;
                    }

                    tweak.IsEnabled = enable;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ApplyAllRecommendedAsync(List<PrivacyTweakItem> tweaks)
        {
            bool success = true;
            foreach (var tweak in tweaks.Where(t => t.IsRecommended))
            {
                if (!tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, true);
                    if (!ok) success = false;
                }
            }
            return success;
        }

        public async Task<bool> RestoreAllDefaultsAsync(List<PrivacyTweakItem> tweaks)
        {
            bool success = true;
            foreach (var tweak in tweaks)
            {
                if (tweak.IsEnabled)
                {
                    bool ok = await ApplyTweakAsync(tweak, false);
                    if (!ok) success = false;
                }
            }
            return success;
        }

        #endregion

        #region Bloatware Management

        public async Task<List<BloatwareAppItem>> GetInstalledBloatwareAsync()
        {
            return await Task.Run(() =>
            {
                var candidateList = new List<BloatwareAppItem>
                {
                    new() { PackageName = "Microsoft.549981C3F5F10", DisplayName = "Cortana Sesli Asistan", Category = "Gereksiz Asistan", Description = "Microsoft Cortana yardımcı programı.", IsEssential = false },
                    new() { PackageName = "Microsoft.BingNews", DisplayName = "Microsoft Haberler", Category = "Haber & İçerik", Description = "MSN haber akışı ve arka plan bildirimleri.", IsEssential = false },
                    new() { PackageName = "Microsoft.BingWeather", DisplayName = "Microsoft Hava Durumu", Category = "Hava Durumu", Description = "MSN hava durumu canlı kutusu.", IsEssential = false },
                    new() { PackageName = "Microsoft.XboxGamingOverlay", DisplayName = "Xbox Game Bar", Category = "Oyun Katmanı", Description = "Win+G oyun yakalama ve arka plan katmanı.", IsEssential = false },
                    new() { PackageName = "Microsoft.MicrosoftSolitaireCollection", DisplayName = "Solitaire Collection", Category = "Oyunlar", Description = "Microsoft kart oyunları koleksiyonu.", IsEssential = false },
                    new() { PackageName = "Microsoft.ZuneMusic", DisplayName = "Medya Oynatıcı / Groove", Category = "Medya Oynatıcı", Description = "Varsayılan Windows medya oynatıcısı.", IsEssential = false },
                    new() { PackageName = "Microsoft.ZuneVideo", DisplayName = "Filmler & TV", Category = "Video Oynatıcı", Description = "Microsoft video izleme mağaza uygulaması.", IsEssential = false },
                    new() { PackageName = "Microsoft.People", DisplayName = "Microsoft Kişiler", Category = "İletişim", Description = "Windows kişiler ve adres defteri bileşeni.", IsEssential = false },
                    new() { PackageName = "Microsoft.YourPhone", DisplayName = "Telefon Bağlantısı (Link)", Category = "Mobil Bağlantı", Description = "Telefon eşleştirme ve arka plan servisleri.", IsEssential = false },
                    new() { PackageName = "Microsoft.WindowsFeedbackHub", DisplayName = "Geri Bildirim Merkezi", Category = "Telemetri & Tanı", Description = "Kullanıcı geri bildirim ve telemetri aracı.", IsEssential = false },
                    new() { PackageName = "Microsoft.GetHelp", DisplayName = "Yardım Alın", Category = "Yardım & Destek", Description = "Microsoft web destek yönlendiricisi.", IsEssential = false },
                    new() { PackageName = "Microsoft.Getstarted", DisplayName = "İpuçları (Tips)", Category = "Tanıtım", Description = "Windows başlangıç ve kullanım ipuçları.", IsEssential = false },
                    new() { PackageName = "Microsoft.MicrosoftOfficeHub", DisplayName = "Microsoft 365 Tanıtım", Category = "Ofis Tanıtım", Description = "Office web yönlendirme merkezi.", IsEssential = false },

                    // Essential Protected Apps (Cannot be removed)
                    new() { PackageName = "Microsoft.WindowsCalculator", DisplayName = "Hesap Makinesi", Category = "Hayati Sistem Bileşeni", Description = "Temel matematik ve hesaplama aracı.", IsEssential = true },
                    new() { PackageName = "Microsoft.WindowsStore", DisplayName = "Microsoft Store", Category = "Hayati Sistem Bileşeni", Description = "Uygulama güncelleme ve mağaza motoru.", IsEssential = true },
                    new() { PackageName = "Microsoft.Windows.Photos", DisplayName = "Fotoğraflar", Category = "Hayati Sistem Bileşeni", Description = "Görüntü ve fotoğraf görüntüleme bileşeni.", IsEssential = true }
                };

                // Check actual installed status via PowerShell
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Get-AppxPackage | Select-Object -ExpandProperty Name\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(4000);

                        foreach (var app in candidateList)
                        {
                            app.IsInstalled = output.Contains(app.PackageName, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                catch { }

                return candidateList.OrderByDescending(a => a.IsEssential).ThenByDescending(a => a.IsInstalled).ToList();
            });
        }

        public async Task<bool> RemoveBloatwareAsync(BloatwareAppItem app)
        {
            if (app.IsEssential) return false;

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"Get-AppxPackage -Name *{app.PackageName}* | Remove-AppxPackage\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(10000);
                    app.IsInstalled = false;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        #endregion

        #region System Restore Point

        /// <summary>
        /// Tek geri yükleme noktası servisine yönlendirir (WMI; 24 saat sınırı ve yönetici
        /// durumu dürüstçe raporlanır). Eskiden görünür bir PowerShell penceresi runas ile
        /// açılıyordu.
        /// </summary>
        public async Task<bool> CreateRestorePointAsync(string description)
        {
            var service = App.TryGetService<Bakım.Services.Safety.IRestorePointService>()
                          ?? new Bakım.Services.Safety.RestorePointService(AppLog.Current);
            var result = await service.CreateAsync(description);
            LastRestorePointMessage = result.Message;
            return result.Created;
        }

        /// <summary>Son geri yükleme noktası denemesinin kullanıcıya gösterilecek sonucu.</summary>
        public string LastRestorePointMessage { get; private set; } = string.Empty;

        #endregion

        #region Helpers

        private static bool CheckRegistryDword(RegistryKey root, string subKeyPath, string valueName, int expected)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath, false);
                if (key != null)
                {
                    var val = key.GetValue(valueName);
                    if (val != null)
                    {
                        return Convert.ToInt32(val) == expected;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void SetRegistryDword(RegistryKey root, string subKeyPath, string valueName, int val)
        {
            try
            {
                using var key = root.CreateSubKey(subKeyPath, true);
                key?.SetValue(valueName, val, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static bool CheckServiceDisabled(string serviceName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", false);
                if (key != null)
                {
                    var startVal = key.GetValue("Start");
                    if (startVal != null)
                    {
                        return Convert.ToInt32(startVal) == 4; // 4 = Disabled
                    }
                }
            }
            catch { }
            return false;
        }

        private static void ConfigureServiceState(string serviceName, bool disable)
        {
            try
            {
                string startMode = disable ? "disabled" : "demand";
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"config \"{serviceName}\" start= {startMode}",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                if (disable)
                {
                    var stopPsi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"stop \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var stopProc = Process.Start(stopPsi);
                    stopProc?.WaitForExit(3000);
                }
            }
            catch { }
        }

        private static bool CheckHostsBlocked()
        {
            try
            {
                if (!File.Exists(HostsFilePath)) return false;
                string content = File.ReadAllText(HostsFilePath);
                return content.Contains("telemetry.microsoft.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyHostsBlocking(bool block)
        {
            try
            {
                if (!File.Exists(HostsFilePath)) return;
                string content = File.ReadAllText(HostsFilePath);

                if (block)
                {
                    if (!content.Contains("telemetry.microsoft.com", StringComparison.OrdinalIgnoreCase))
                    {
                        using var sw = File.AppendText(HostsFilePath);
                        sw.WriteLine();
                        sw.WriteLine("# [Bakım Privacy Guard Telemetry Block]");
                        foreach (var entry in TelemetryHostsEntries)
                        {
                            sw.WriteLine(entry);
                        }
                    }
                }
                else
                {
                    var lines = File.ReadAllLines(HostsFilePath);
                    var cleanLines = lines.Where(l => !TelemetryHostsEntries.Any(t => l.Contains(t.Split(' ')[1])) &&
                                                      !l.Contains("[Bakım Privacy Guard"));
                    File.WriteAllLines(HostsFilePath, cleanLines);
                }
            }
            catch { }
        }

        #endregion
    }
}
