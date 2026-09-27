using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakım.Core.Uninstall
{
    /// <summary>Kaldırma kaydının izleme için gereken özeti.</summary>
    public sealed record UninstallSnapshotEntry(string KeyPath, string DisplayName, string? InstallLocation, bool IsSystemComponent);

    /// <summary>
    /// Bakım dışından yapılan kaldırmaları yakalama kuralları: Windows Ayarlar'dan, programın kendi kaldırıcısından ya da
    /// winget ile kaldırılan program için "kalıntıları tara" önerilir. Kayıt kaybolması her zaman kaldırma değildir:
    /// MSI güncellemeleri eski ürün kodunu silip yenisini yazar; bunlar aynı ad kökü ya da aynı kurulum klasörüyle ayrılır.
    /// </summary>
    public static class UninstallWatchRules
    {
        /// <summary>Bakım'ın başlattığı kaldırma bu süre içinde kayıt silerse bildirim çıkmaz (sihirbaz kalıntıyı zaten gösterir).</summary>
        public static readonly TimeSpan BakimInitiatedWindow = TimeSpan.FromHours(2);

        private static readonly Regex VersionToken = new(@"\([^)]*\b(x64|x86|64-bit|32-bit|64 bit|32 bit)\b[^)]*\)|\bv?\d+(\.\d+)+\b|\b(x64|x86)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Alt anahtar adlarının sıradan bağımsız parmak izi: ucuz değişiklik denetimi (tam okuma yalnızca değişince).</summary>
        public static string Fingerprint(IEnumerable<string> subKeyNames)
        {
            var sb = new StringBuilder();
            foreach (var name in subKeyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) sb.Append(name.ToLowerInvariant()).Append('|');
            return sb.ToString();
        }

        /// <summary>"Everything 1.4.1.1032 (x64)" → "everything": sürüm ve mimari atılmış ad kökü.</summary>
        public static string NameStem(string displayName) =>
            Spaces.Replace(VersionToken.Replace(displayName ?? string.Empty, " "), " ").Trim().ToLowerInvariant();

        private static string NormDir(string? dir) => (dir ?? string.Empty).Trim().Trim('"').TrimEnd('\\', '/').ToLowerInvariant();

        /// <summary>
        /// Önceki ve sonraki anlık görüntü arasında gerçekten kaldırılmış (güncellenmemiş) programlar.
        /// Sistem bileşenleri ve Bakım'ın kendi başlattığı kaldırmalar dışarıda kalır.
        /// </summary>
        public static IReadOnlyList<UninstallSnapshotEntry> Removed(
            IEnumerable<UninstallSnapshotEntry> before,
            IEnumerable<UninstallSnapshotEntry> after,
            IReadOnlyDictionary<string, DateTime>? bakimInitiated = null,
            DateTime? nowUtc = null)
        {
            var afterList = after.ToList();
            var afterKeys = new HashSet<string>(afterList.Select(e => e.KeyPath), StringComparer.OrdinalIgnoreCase);
            var afterStems = new HashSet<string>(afterList.Select(e => NameStem(e.DisplayName)).Where(s => s.Length > 0), StringComparer.Ordinal);
            var afterDirs = new HashSet<string>(afterList.Select(e => NormDir(e.InstallLocation)).Where(d => d.Length > 3), StringComparer.Ordinal);
            var now = nowUtc ?? DateTime.UtcNow;

            var removed = new List<UninstallSnapshotEntry>();
            foreach (var entry in before)
            {
                if (afterKeys.Contains(entry.KeyPath)) continue;
                if (entry.IsSystemComponent || string.IsNullOrWhiteSpace(entry.DisplayName)) continue;
                if (afterStems.Contains(NameStem(entry.DisplayName))) continue;           // güncelleme: aynı program yeni kayıtla
                string dir = NormDir(entry.InstallLocation);
                if (dir.Length > 3 && afterDirs.Contains(dir)) continue;                   // yeniden kurulum / yeni sürüm aynı klasörde
                if (bakimInitiated != null && bakimInitiated.TryGetValue(entry.KeyPath, out var at) && now - at <= BakimInitiatedWindow) continue;
                removed.Add(entry);
            }
            return removed;
        }
    }
}
