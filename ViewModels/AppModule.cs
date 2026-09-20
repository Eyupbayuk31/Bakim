using System;
using System.Collections.Generic;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Uygulamanın gezinilebilir modülleri.
    ///
    /// v3.1'e kadar navigasyon serbest metin anahtarlarıyla yapılıyordu ve
    /// eşleşmeyen her anahtar sessizce Panoya düşüyordu (`_ => Dashboard`).
    /// Yazım hatası bulmak imkânsızdı. Artık anahtarlar tek bir kayıt
    /// tablosundan doğrulanır ve eşleşmeyen anahtar günlüğe yazılır.
    /// </summary>
    public enum AppModule
    {
        Dashboard,
        Cleaner,
        Optimizer,
        Startup,
        SystemInfo,
        NetworkMonitor,
        ServiceManager,
        PrivacyDebloat,
        CrashAnalyzer,
        Uninstaller,
        Analyzer,
        WindowsTweaker,
        Settings
    }

    /// <summary>XAML'deki metin anahtarlarını modüllere çeviren tek kayıt tablosu.</summary>
    public static class AppModuleRegistry
    {
        private static readonly Dictionary<string, AppModule> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Dashboard"] = AppModule.Dashboard,
                ["Cleaner"] = AppModule.Cleaner,
                ["Optimizer"] = AppModule.Optimizer,
                ["Startup"] = AppModule.Startup,
                ["SystemInfo"] = AppModule.SystemInfo,
                ["Network"] = AppModule.NetworkMonitor,
                ["NetworkMonitor"] = AppModule.NetworkMonitor,
                ["ServiceManager"] = AppModule.ServiceManager,
                ["PrivacyDebloat"] = AppModule.PrivacyDebloat,
                ["CrashAnalyzer"] = AppModule.CrashAnalyzer,
                ["Uninstaller"] = AppModule.Uninstaller,
                ["Analyzer"] = AppModule.Analyzer,
                // Eski anahtarlar alias olarak korunur: kayıtlı durum, komut
                // paleti kısayolları ve dış bağlantılar kırılmasın.
                ["Autoruns"] = AppModule.Analyzer,
                ["Persistence"] = AppModule.Analyzer,
                ["Analizor"] = AppModule.Analyzer,
                ["Tweaker"] = AppModule.WindowsTweaker,
                ["WindowsTweaker"] = AppModule.WindowsTweaker,
                ["Settings"] = AppModule.Settings,
            };

        /// <summary>
        /// Anahtarı çözer. Tanınmayan anahtar sessizce yutulmaz: false döner,
        /// çağıran taraf günlüğe yazar.
        /// </summary>
        public static bool TryResolve(string? key, out AppModule module)
        {
            module = AppModule.Dashboard;
            return !string.IsNullOrWhiteSpace(key) && Map.TryGetValue(key.Trim(), out module);
        }

        /// <summary>Tanımlı tüm metin anahtarları — test ve tanılama içindir.</summary>
        public static IReadOnlyCollection<string> Keys => Map.Keys;
    }
}
