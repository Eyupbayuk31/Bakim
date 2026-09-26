using System;
using Bakım.Core.ServiceControl;

namespace Bakım.Core.Analyzer
{
    /// <summary>Analizör'deki kalıcılık konumu türleri (Models.PersistenceCategory ile aynı adlar).</summary>
    public enum AutorunCategory
    {
        RegistryRun,
        StartupFolder,
        ScheduledTask,
        WindowsService,
        WmiEventConsumer,
        WinlogonIfeo,
        ShellExtension,
        Driver,
        InspectedFile
    }

    /// <summary>Politikanın değerlendirdiği girdi bilgileri.</summary>
    /// <param name="RegistryKeyPath">Run girdisinin anahtarı (HKLM\Software\…\Run).</param>
    /// <param name="ServiceStart">Hizmet "Start" değeri: 0 Boot, 1 System, 2 Otomatik, 3 El ile, 4 Devre dışı.</param>
    /// <param name="ServiceType">Hizmet "Type" değeri: 0x1/0x2 sürücü, 0x10/0x20 süreç.</param>
    public sealed record AutorunFacts(
        AutorunCategory Category,
        string? RegistryKeyPath = null,
        string? ShortcutPath = null,
        string? TaskPath = null,
        string? ServiceName = null,
        int? ServiceStart = null,
        int? ServiceType = null,
        bool DisabledByBakim = false);

    public sealed record ActionDecision(bool Allowed, string Reason)
    {
        public static ActionDecision Allow { get; } = new(true, string.Empty);
        public static ActionDecision Deny(string reason) => new(false, reason);
    }

    /// <summary>
    /// Hangi girdide hangi eylemin güvenli olduğu (ANL Z1). Tek kaynak: arayüz anahtarı ve sil düğmesini
    /// buradan açar, motor da eylemden önce buraya sorar.
    ///
    /// Eskiden her satırda açma/kapama anahtarı vardı; açılış (Boot/System) sürücüleri ve kritik
    /// hizmetler dahil. Yeniden açma her zaman "Otomatik" yazıyordu: bir disk sürücüsünü kapatıp
    /// açmak sistemi açılmaz hale getirebilirdi.
    /// </summary>
    public static class AutorunActionPolicy
    {
        public static ActionDecision CanToggle(AutorunFacts f) => f.Category switch
        {
            AutorunCategory.RegistryRun => IsApprovableRunKey(f.RegistryKeyPath)
                ? ActionDecision.Allow
                : ActionDecision.Deny("RunOnce ve politika anahtarlarındaki girdiler Windows'ta kapatılamaz; gerekiyorsa silinebilir."),
            AutorunCategory.StartupFolder => string.IsNullOrEmpty(f.ShortcutPath)
                ? ActionDecision.Deny("Başlangıç klasöründeki kısayol bulunamadı.")
                : ActionDecision.Allow,
            AutorunCategory.ScheduledTask => IsWindowsTask(f.TaskPath)
                ? ActionDecision.Deny("Windows'un kendi zamanlanmış görevleri Analizör'den değiştirilemez.")
                : string.IsNullOrEmpty(f.TaskPath) ? ActionDecision.Deny("Görev yolu okunamadı.") : ActionDecision.Allow,
            AutorunCategory.WindowsService => CanToggleService(f),
            AutorunCategory.Driver => ActionDecision.Deny("Sürücüler Analizör'den kapatılamaz: yanlış sürücüyü kapatmak Windows'un açılmasını engelleyebilir."),
            AutorunCategory.WmiEventConsumer => ActionDecision.Deny("WMI abonelikleri kapatılamaz; incelemek için dosya analizini kullanın."),
            AutorunCategory.WinlogonIfeo => ActionDecision.Deny("Winlogon ve IFEO değerleri oturum açılışını doğrudan etkiler; buradan değiştirilemez."),
            AutorunCategory.ShellExtension => ActionDecision.Deny("Gezgin eklentileri buradan kapatılamaz."),
            _ => ActionDecision.Deny("Bu girdi bir başlangıç kaydı değil, incelenen bir dosyadır.")
        };

        public static ActionDecision CanDelete(AutorunFacts f) => f.Category switch
        {
            AutorunCategory.RegistryRun => string.IsNullOrEmpty(f.RegistryKeyPath)
                ? ActionDecision.Deny("Kayıt yolu okunamadı.")
                : ActionDecision.Allow,
            AutorunCategory.StartupFolder => string.IsNullOrEmpty(f.ShortcutPath)
                ? ActionDecision.Deny("Başlangıç klasöründeki kısayol bulunamadı.")
                : ActionDecision.Allow,
            AutorunCategory.ScheduledTask => IsWindowsTask(f.TaskPath)
                ? ActionDecision.Deny("Windows'un kendi zamanlanmış görevleri silinemez.")
                : string.IsNullOrEmpty(f.TaskPath) ? ActionDecision.Deny("Görev yolu okunamadı.") : ActionDecision.Allow,
            AutorunCategory.WindowsService or AutorunCategory.Driver =>
                ActionDecision.Deny("Hizmetler ve sürücüler silinmez; kapatmak ya da kaldırıcıyı kullanmak güvenlidir."),
            _ => ActionDecision.Deny("Bu girdi türü silinemez.")
        };

        /// <summary>
        /// Yalnızca kendi sürecinde çalışan, kritik olmayan, "Otomatik" (ya da Bakım'ın kapattığı) hizmetler.
        /// </summary>
        private static ActionDecision CanToggleService(AutorunFacts f)
        {
            if (string.IsNullOrWhiteSpace(f.ServiceName)) return ActionDecision.Deny("Hizmet adı okunamadı.");
            if (IsDriverType(f.ServiceType) || f.ServiceStart is 0 or 1)
                return ActionDecision.Deny("Sürücüler Analizör'den kapatılamaz: yanlış sürücüyü kapatmak Windows'un açılmasını engelleyebilir.");
            if (CriticalServicePolicy.IsCritical(f.ServiceName))
                return ActionDecision.Deny("Kritik Windows hizmeti: kapatılması sistemi kararsız hale getirir.");
            if (f.ServiceStart == 2 || (f.ServiceStart == 4 && f.DisabledByBakim)) return ActionDecision.Allow;
            return ActionDecision.Deny("Yalnızca otomatik başlayan hizmetler buradan kapatılabilir; ayrıntılı ayar için Hizmet Yöneticisi'ni kullanın.");
        }

        public static bool IsDriverType(int? serviceType) => serviceType is int t && (t & 0x3) != 0 && (t & 0x30) == 0;

        public static bool IsApprovableRunKey(string? keyPath) =>
            !string.IsNullOrEmpty(keyPath) && keyPath.EndsWith(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase);

        public static bool IsWindowsTask(string? taskPath) =>
            !string.IsNullOrEmpty(taskPath) && taskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kapatılan hizmetin yeniden açılırken geri yükleneceği başlangıç türü: kaydedilen özgün değer;
        /// yoksa "Otomatik". Boot/System hiçbir zaman yazılmaz (politika zaten izin vermez).
        /// </summary>
        public static (int Start, bool Delayed) RestoreStartType(int? savedStart, bool savedDelayed) =>
            savedStart is 2 or 3 ? (savedStart.Value, savedStart == 2 && savedDelayed) : (2, savedDelayed);
    }
}
