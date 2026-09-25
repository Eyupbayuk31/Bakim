using System;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// Kurulumla ilgisi olmayan, arka planda sürekli yazılan konumlar (NÖB 3.1). Bu olaylar rapora
    /// girmez ve geri almada asla hedef olmaz: kurulum sırasında açık olan tarayıcının önbelleği,
    /// Windows'un kendi günlükleri, Bakım'ın kendi dosyaları.
    /// </summary>
    public static class SentinelNoise
    {
        /// <summary>Yol içinde geçerse gürültü sayılan parçalar (küçük harf, "\" ile).</summary>
        private static readonly string[] PathFragments =
        {
            // Tarayıcılar (profil verisi ve önbellekler)
            @"\google\chrome\user data\",
            @"\microsoft\edge\user data\",
            @"\bravesoftware\brave-browser\user data\",
            @"\vivaldi\user data\",
            @"\opera software\",
            @"\yandex\yandexbrowser\user data\",
            @"\mozilla\firefox\profiles\",
            @"\cache2\",
            @"\code cache\",
            @"\gpucache\",
            @"\shadercache\",
            @"\service worker\cachestorage\",
            // Windows
            @"\microsoft\windows\inetcache\",
            @"\microsoft\windows\webcache\",
            @"\microsoft\windows\explorer\",
            @"\microsoft\windows\notifications\",
            @"\microsoft\windows\recent\",
            @"\microsoft\windows\caches\",
            @"\windows\prefetch\",
            @"\windows\softwaredistribution\",
            @"\windows\logs\",
            @"\windows\temp\msi",
            @"\microsoft\windows defender\scans\",
            @"\microsoft\windows\wer\",
            @"\crashdumps\",
            @"\d3dscache\",
            @"\nvidia\dxcache\",
            @"\nvidia\glcache\",
            @"\amd\dxcache\",
            @"\packages\microsoft.windows.",
            @"\connecteddevicesplatform\",
            @"\microsoft\onedrive\logs\",
            @"\microsoft\onedrive\setup\logs\",
            @"\$recycle.bin\",
            @"\system volume information\",
            // Bakım'ın kendisi
            @"\appdata\roaming\bakım\",
            @"\appdata\local\bakım\",
            @"\appdata\local\bakim\",
            @"\appdata\roaming\bakim\",
        };

        private static readonly string[] NoiseExtensions = { ".etl", ".evtx", ".pf", ".log.tmp" };

        public static bool IsNoisePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            string lower = path.Replace('/', '\\').ToLowerInvariant();
            if (lower.Contains(@"\thumbcache_") || lower.Contains(@"\iconcache_")) return true;
            if (NoiseExtensions.Any(e => lower.EndsWith(e, StringComparison.Ordinal))) return true;
            return PathFragments.Any(f => lower.Contains(f, StringComparison.Ordinal));
        }

        /// <summary>Kurulumdan bağımsız sürekli değişen kayıt defteri alanları.</summary>
        private static readonly string[] RegistryFragments =
        {
            @"\muicache", @"\userassist\", @"\bagmru", @"\bags\", @"\recentdocs", @"\featureusage\",
            @"\cloudstore\", @"\bam\state\", @"\compatibility assistant\", @"\shell\bagmru", @"\sessioninfo\",
        };

        public static bool IsNoiseRegistryKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            string lower = key.ToLowerInvariant();
            return RegistryFragments.Any(f => lower.Contains(f, StringComparison.Ordinal));
        }
    }
}
