using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bakım.Core.ServiceControl
{
    /// <summary>
    /// Hizmet güvenlik politikası — tek kaynak (MASTER_PLAN §5.7). Kritik hizmetler durdurulamaz ve
    /// başlangıç türü değiştirilemez; güvenlik hizmetleri kapatılırken ayrıca uyarılır.
    /// </summary>
    public static class CriticalServicePolicy
    {
        private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "DcomLaunch", "RpcEptMapper", "EventLog", "PlugPlay", "SamSs", "LSM",
            "BrokerInfrastructure", "SystemEventsBroker", "KeyIso", "VaultSvc", "CryptSvc", "ProfSvc",
            "Winmgmt", "Power", "CoreMessagingRegistrar", "Schedule", "UserManager", "StateRepository",
            "gpsvc", "EventSystem", "SENS", "Dhcp", "Dnscache", "nsi", "BFE", "mpssvc", "WinDefend",
            "SecurityHealthService", "wscsvc", "TrustedInstaller", "AppXSvc", "ClipSVC", "LanmanWorkstation",
            "AudioEndpointBuilder", "Audiosrv", "Themes", "ShellHWDetection", "TimeBrokerSvc", "DeviceInstall",
        };

        private static readonly HashSet<string> SecurityRelevant = new(StringComparer.OrdinalIgnoreCase)
        {
            "WinDefend", "mpssvc", "BFE", "wscsvc", "SecurityHealthService", "wuauserv", "UsoSvc",
            "WaaSMedicSvc", "Sense", "WdNisSvc", "SgrmBroker", "BITS",
        };

        public static bool IsCritical(string? serviceName) => serviceName != null && Critical.Contains(serviceName);

        /// <summary>Kapatılması sistemi daha az korunaklı yapar (güncelleme, koruma, güvenlik duvarı).</summary>
        public static bool ReducesSecurityWhenDisabled(string? serviceName) => serviceName != null && SecurityRelevant.Contains(serviceName);

        public static IReadOnlyCollection<string> CriticalServices => Critical;
    }

    /// <param name="TargetMode">"Manual" (gerektiğinde başlar) ya da "Disabled".</param>
    public sealed record ServiceProfileChange(string ServiceName, string TargetMode);

    public sealed record ServiceProfile(string Id, string Title, string Description, string Icon, IReadOnlyList<ServiceProfileChange> Changes);

    /// <param name="CurrentMode">"Auto", "Manual", "Disabled" (Win32_Service.StartMode).</param>
    public sealed record ServiceProfileStep(string ServiceName, string CurrentMode, string TargetMode);

    /// <summary>
    /// Güvenli önerilen hizmet profilleri: kullanıcının kullanmadığı bir özelliğe ait hizmetler.
    /// Çoğunluk "El ile" yapılır (gerekirse yine başlar); kapatma yalnızca açıkça gereksizlerde.
    /// </summary>
    public static class ServiceProfiles
    {
        public static IReadOnlyList<ServiceProfile> All { get; } = new[]
        {
            new ServiceProfile("no-printer", "Yazıcı kullanmıyorum",
                "Yazdırma biriktiricisi ve yazıcı bildirimleri gerektiğinde başlar; arka planda açık kalmaz.", "Print24",
                new[] { new ServiceProfileChange("Spooler", "Manual"), new ServiceProfileChange("PrintNotify", "Manual") }),
            new ServiceProfile("no-xbox", "Xbox kullanmıyorum",
                "Xbox Live oturumu, oyun kaydı ve aksesuar hizmetleri gerektiğinde başlar.", "Games24",
                new[]
                {
                    new ServiceProfileChange("XblAuthManager", "Manual"), new ServiceProfileChange("XblGameSave", "Manual"),
                    new ServiceProfileChange("XboxGipSvc", "Manual"), new ServiceProfileChange("XboxNetApiSvc", "Manual"),
                }),
            new ServiceProfile("no-fax", "Faks kullanmıyorum", "Faks hizmeti kapatılır.", "DocumentPrint24",
                new[] { new ServiceProfileChange("Fax", "Disabled") }),
            new ServiceProfile("no-remote-registry", "Uzaktan kayıt defteri erişimi kapalı",
                "Ağdaki başka bir bilgisayarın bu bilgisayarın kayıt defterini düzenlemesine izin veren hizmet kapatılır.", "ShieldLock24",
                new[] { new ServiceProfileChange("RemoteRegistry", "Disabled") }),
            new ServiceProfile("less-telemetry", "Daha az telemetri",
                "Bağlı Kullanıcı Deneyimleri ve Telemetri ile WAP push hizmeti kapatılır.", "EyeOff24",
                new[] { new ServiceProfileChange("DiagTrack", "Disabled"), new ServiceProfileChange("dmwappushservice", "Disabled") }),
            new ServiceProfile("no-maps-demo", "Çevrimdışı haritalar ve mağaza demosu",
                "İndirilen haritalar yöneticisi gerektiğinde başlar; perakende demo hizmeti kapatılır.", "Map24",
                new[] { new ServiceProfileChange("MapsBroker", "Manual"), new ServiceProfileChange("RetailDemo", "Disabled") }),
        };

        /// <summary>
        /// Uygulanacak adımlar: yüklü olmayan hizmetler, zaten hedefte (ya da daha kısıtlı) olanlar ve
        /// kritik hizmetler atlanır.
        /// </summary>
        public static IReadOnlyList<ServiceProfileStep> Preview(ServiceProfile profile, IReadOnlyDictionary<string, string> currentModes)
        {
            var steps = new List<ServiceProfileStep>();
            foreach (var change in profile.Changes)
            {
                if (CriticalServicePolicy.IsCritical(change.ServiceName)) continue;
                if (!currentModes.TryGetValue(change.ServiceName, out string? current)) continue;
                if (Rank(current) >= Rank(change.TargetMode)) continue;
                steps.Add(new ServiceProfileStep(change.ServiceName, current, change.TargetMode));
            }
            return steps;
        }

        /// <summary>Kısıtlayıcılık sırası: Otomatik &lt; El ile &lt; Devre dışı.</summary>
        private static int Rank(string mode) => mode.ToLowerInvariant() switch
        {
            "disabled" => 2,
            "manual" or "demand" => 1,
            _ => 0
        };

        public static string ModeLabel(string mode) => mode.ToLowerInvariant() switch
        {
            "auto" or "automatic" => "Otomatik",
            "manual" or "demand" => "El ile",
            "disabled" => "Devre dışı",
            _ => mode
        };
    }

    public static class DriverAge
    {
        private static readonly string[] Formats = { "yyyyMMdd", "yyyy-MM-dd", "dd.MM.yyyy", "M/d/yyyy", "MM/dd/yyyy", "d.M.yyyy" };

        /// <summary>"20190314000000.******+000" (WMI) ya da görüntü biçimlerinden tarih.</summary>
        public static DateTime? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text == "-") return null;
            string t = text.Trim();
            if (t.Length >= 8 && t[..8].All(char.IsDigit) &&
                DateTime.TryParseExact(t[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wmi))
                return wmi;
            return DateTime.TryParseExact(t, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }

        /// <summary>Üçüncü taraf ve 5 yıldan eski sürücü (Microsoft'un kutu içi sürücüleri hariç).</summary>
        public static bool IsOldThirdParty(string? driverDate, string? manufacturerOrSigner, DateTime now)
        {
            if (manufacturerOrSigner != null && manufacturerOrSigner.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return false;
            var date = Parse(driverDate);
            // Windows'un genel sürücüleri 2006-06-21 tarihlidir: gerçek yaş değil.
            if (date == null || date.Value.Year <= 2006) return false;
            return date.Value < now.AddYears(-5);
        }
    }
}
