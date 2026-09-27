using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Analyzer
{
    /// <summary>Canlı kalıcılık izlemesinin saf kuralları (ANL Faz 4).</summary>
    public static class PersistenceWatchRules
    {
        /// <summary>
        /// Girdi kimliği: konum + ad + ham komut. Komut dahildir: var olan bir Run değerinin hedefinin
        /// değiştirilmesi (ele geçirme) de "yeni girdi" olarak yakalanır.
        /// </summary>
        public static string Key(string category, string location, string name, string rawCommand) =>
            $"{category}|{location}|{name}|{rawCommand}".ToLowerInvariant();

        /// <summary>Önceki kimlik kümesinde olmayan girdiler.</summary>
        public static IReadOnlyList<T> Added<T>(IReadOnlySet<string> before, IEnumerable<T> after, Func<T, string> key) =>
            after.Where(item => !before.Contains(key(item))).ToList();

        /// <summary>
        /// Bildirim gerekir mi? Doğrulanmış Microsoft imzalı girdiler (Windows güncellemeleri) hiç, Oyun
        /// Modu açıkken yalnızca Tehlikeli, aksi halde Dikkat ve üstü bildirilir. Düşük riskli eklemeler
        /// yalnızca Etkinlik Merkezi'ne yazılır.
        /// </summary>
        public static bool ShouldNotify(int riskScore, bool verifiedMicrosoft, bool gameModeActive) =>
            !verifiedMicrosoft && riskScore >= (gameModeActive ? Risk.RiskScale.Dangerous : Risk.RiskScale.Caution);
    }

    /// <summary>Bir kurulum raporunun iz bıraktığı konumlar (Kurulum Nöbetçisi).</summary>
    public sealed record SetupTrail(string AppName, DateTime InstalledAtUtc, IReadOnlyCollection<string> Keys);

    /// <summary>
    /// "Bu girdiyi hangi kurulum ekledi?" (ANL Faz 4). Nöbetçi raporlarındaki Run değerleri, hizmet
    /// anahtarları ve görev yolları dizinlenir; Analizör satırı aynı anahtarla eşleşirse kaynağı yazılır.
    /// Aynı anahtar birden çok raporda varsa en yenisi kazanır.
    /// </summary>
    public sealed class ProvenanceIndex
    {
        private readonly Dictionary<string, (string App, DateTime When)> _map = new(StringComparer.OrdinalIgnoreCase);

        public static ProvenanceIndex Build(IEnumerable<SetupTrail> trails)
        {
            var index = new ProvenanceIndex();
            foreach (var trail in trails.OrderBy(t => t.InstalledAtUtc))
            {
                foreach (string key in trail.Keys.Where(k => !string.IsNullOrWhiteSpace(k)))
                    index._map[Normalize(key)] = (trail.AppName, trail.InstalledAtUtc);
            }
            return index;
        }

        public int Count => _map.Count;

        public (string App, DateTime When)? Find(IEnumerable<string?> candidateKeys)
        {
            foreach (string? key in candidateKeys)
            {
                if (!string.IsNullOrWhiteSpace(key) && _map.TryGetValue(Normalize(key), out var hit)) return hit;
            }
            return null;
        }

        /// <summary>"HKLM\Software\…" ile "HKEY_LOCAL_MACHINE\SOFTWARE\…" ve sondaki "\" farkları eşitlenir.</summary>
        public static string Normalize(string key)
        {
            string k = key.Trim().TrimEnd('\\');
            if (k.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)) k = "HKLM" + k[18..];
            else if (k.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase)) k = "HKCU" + k[17..];
            return k.ToLowerInvariant();
        }

        /// <summary>Analizör girdisinin olası anahtarları: Run değeri, hizmet anahtarı, görev yolu.</summary>
        public static IEnumerable<string?> CandidateKeys(string? registryKeyPath, string? registryValueName, string? serviceName, string? taskPath)
        {
            if (!string.IsNullOrEmpty(registryKeyPath) && !string.IsNullOrEmpty(registryValueName))
                yield return $@"{registryKeyPath}\{registryValueName}";
            if (!string.IsNullOrEmpty(serviceName))
                yield return $@"HKLM\SYSTEM\CurrentControlSet\Services\{serviceName}";
            if (!string.IsNullOrEmpty(taskPath))
                yield return taskPath;
        }
    }
}
