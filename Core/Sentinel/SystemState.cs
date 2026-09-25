using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    /// <summary>Kurulumun dokunabileceği sistem alanları (NÖB 2.3, 2.5).</summary>
    public enum SystemArea
    {
        Ifeo,
        Winlogon,
        AppInit,
        EnvironmentPath,
        Proxy,
        BrowserPolicy,
        DefenderExclusion,
        RootCertificate,
        Hosts,
        FirewallRule,
        ScheduledTask,
        ShellExtension,
    }

    public enum ChangeKind { Added, Modified, Removed }

    /// <param name="Key">Alan içindeki kimlik ("HKLM\...\Debugger", görev yolu, sertifika parmak izi …).</param>
    public sealed record SystemChange(SystemArea Area, ChangeKind Kind, string Key, string? Before, string? After);

    /// <summary>
    /// Sistem durumu anlık görüntüsü: "ALAN|anahtar" → değer. Sensör (Windows) doldurur;
    /// karşılaştırma burada, saf ve test edilebilir.
    /// </summary>
    public static class SystemStateSnapshot
    {
        public const char Separator = '|';

        public static string MakeKey(SystemArea area, string key) => $"{area}{Separator}{key}";

        public static bool TryParseKey(string taggedKey, out SystemArea area, out string key)
        {
            area = default;
            key = string.Empty;
            int sep = taggedKey.IndexOf(Separator);
            if (sep <= 0 || !Enum.TryParse(taggedKey[..sep], out area)) return false;
            key = taggedKey[(sep + 1)..];
            return true;
        }

        /// <summary>
        /// İki görüntü arasındaki farklar. Önceki görüntüde hiç bulunmayan alan (ör. yetki yokken
        /// okunamayan Defender istisnaları) "eklendi" sayılmaz: <paramref name="preAreas"/> verilirse
        /// yalnızca önceden okunabilmiş alanlar karşılaştırılır.
        /// </summary>
        public static List<SystemChange> Diff(IReadOnlyDictionary<string, string> pre, IReadOnlyDictionary<string, string> post,
            IReadOnlySet<SystemArea>? preAreas = null, IReadOnlySet<SystemArea>? postAreas = null)
        {
            var changes = new List<SystemChange>();
            foreach (var (tagged, after) in post)
            {
                if (!TryParseKey(tagged, out var area, out var key)) continue;
                if (preAreas != null && !preAreas.Contains(area)) continue;
                if (!pre.TryGetValue(tagged, out var before))
                    changes.Add(new SystemChange(area, ChangeKind.Added, key, null, after));
                else if (!string.Equals(before, after, StringComparison.Ordinal))
                    changes.Add(new SystemChange(area, ChangeKind.Modified, key, before, after));
            }
            foreach (var (tagged, before) in pre)
            {
                if (post.ContainsKey(tagged) || !TryParseKey(tagged, out var area, out var key)) continue;
                if (postAreas != null && !postAreas.Contains(area)) continue;
                changes.Add(new SystemChange(area, ChangeKind.Removed, key, before, null));
            }
            return changes.OrderBy(c => c.Area).ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Görüntüde değeri okunabilmiş alanlar (Diff'in preAreas/postAreas girdisi).</summary>
        public static HashSet<SystemArea> AreasOf(IEnumerable<string> taggedKeys, IEnumerable<SystemArea>? readableButEmpty = null)
        {
            var set = new HashSet<SystemArea>(readableButEmpty ?? Enumerable.Empty<SystemArea>());
            foreach (string k in taggedKeys)
            {
                if (TryParseKey(k, out var area, out _)) set.Add(area);
            }
            return set;
        }

        /// <summary>
        /// Windows'un kendi bakım değişiklikleri (kurulum penceresine denk gelse de kuruluma ait değil):
        /// çalıştırılabilir eylemi olmayan (COM işleyicili) görevler ve \Microsoft\Windows\ altında
        /// System32'den çalışan görevler.
        /// </summary>
        public static bool IsLikelyWindowsNoise(SystemChange change)
        {
            if (change.Area != SystemArea.ScheduledTask) return false;
            string action = change.After ?? change.Before ?? string.Empty;
            if (action.Trim().Length == 0) return true;
            return change.Key.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase) &&
                   (action.Contains(@"\system32\", StringComparison.OrdinalIgnoreCase) ||
                    action.Contains("%windir%", StringComparison.OrdinalIgnoreCase) ||
                    action.Contains("%SystemRoot%", StringComparison.OrdinalIgnoreCase));
        }

        public static string AreaLabel(SystemArea area) => area switch
        {
            SystemArea.Ifeo => "Görüntü Dosyası Yürütme Seçenekleri (IFEO)",
            SystemArea.Winlogon => "Winlogon",
            SystemArea.AppInit => "AppInit_DLLs",
            SystemArea.EnvironmentPath => "PATH ortam değişkeni",
            SystemArea.Proxy => "Proxy ayarı",
            SystemArea.BrowserPolicy => "Tarayıcı politikası",
            SystemArea.DefenderExclusion => "Defender istisnası",
            SystemArea.RootCertificate => "Kök sertifika",
            SystemArea.Hosts => "hosts dosyası",
            SystemArea.FirewallRule => "Güvenlik duvarı kuralı",
            SystemArea.ScheduledTask => "Zamanlanmış görev",
            SystemArea.ShellExtension => "Sağ tık menüsü uzantısı",
            _ => area.ToString()
        };
    }
}
