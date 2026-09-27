using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.GameMode
{
    /// <summary>Çalışan bir süreç (öncelik ve kırpma kararları için).</summary>
    public readonly record struct RunningProcess(int Pid, string Name);

    /// <summary>
    /// Oyun Modu v2 kuralları. v1 her açılışta TÜM süreçlerin belleğini boşaltıyordu: bellek aslında boşalmıyor,
    /// sayfalar bekleme listesine gidip geri çekilirken takılma yapıyordu; oyun açıkken oyunun kendisi de kırpılıyordu.
    /// v2 yalnızca ölçülebilir etkisi olan şeyleri yapar ve oyuna, ön plandaki pencereye ve Bakım'a dokunmaz.
    /// </summary>
    public static class GameModeRules
    {
        /// <summary>Bellek kırpma yalnızca gerçek sıkışmada: fiziksel bellek yükü bu eşiğin üstündeyse.</summary>
        public const int MemoryPressureThreshold = 85;

        public static bool ShouldTrim(uint memoryLoadPercent) => memoryLoadPercent >= MemoryPressureThreshold;

        /// <summary>
        /// Önceliği düşürülecek (ve bellek sıkışınca kırpılacak) arka plan süreçleri: bilinen arka plan uygulamaları
        /// ile kullanıcının listesi. Oyun, ön plandaki süreç ve Bakım her koşulda dışarıda kalır.
        /// </summary>
        public static IReadOnlyList<RunningProcess> SelectBackground(
            IEnumerable<RunningProcess> running,
            IEnumerable<string> backgroundNames,
            string? gameName,
            int foregroundPid,
            int selfPid)
        {
            var names = new HashSet<string>(backgroundNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
            return running
                .Where(p => names.Contains(p.Name))
                .Where(p => p.Pid != foregroundPid && p.Pid != selfPid && p.Pid > 4)
                .Where(p => gameName == null || !string.Equals(p.Name, gameName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Arka plan işlemci yüzdesi: sistemin toplam meşgul süresinden oyunun işlemci süresi çıkarılır.
        /// <paramref name="systemBusy"/> tüm çekirdeklerin toplamıdır (GetSystemTimes: çekirdek + kullanıcı − boşta).
        /// </summary>
        public static double BackgroundCpuPercent(TimeSpan systemBusy, TimeSpan gameCpu, TimeSpan wall, int logicalCores)
        {
            if (wall <= TimeSpan.Zero || logicalCores <= 0) return 0;
            double capacity = wall.TotalMilliseconds * logicalCores;
            double background = Math.Max(0, systemBusy.TotalMilliseconds - gameCpu.TotalMilliseconds);
            return Math.Clamp(background / capacity * 100.0, 0, 100);
        }

        /// <summary>"%12 → %4" gibi; ölçüm yoksa boş.</summary>
        public static string CpuChangeText(double? baseline, double? session)
        {
            if (baseline is not { } b || session is not { } s) return string.Empty;
            return $"%{b:F0} → %{s:F0}";
        }
    }

    /// <summary>Oturum süresince arka plan işlemci örneklerinin ortalaması.</summary>
    public sealed class CpuAverager
    {
        private double _sum;
        private int _count;

        public void Add(double percent)
        {
            _sum += percent;
            _count++;
        }

        public double? Average => _count == 0 ? null : _sum / _count;
    }

    /// <summary>Riot Games kurulumundaki bilinen oyunlar (Riot, Steam/Epic gibi bir kütüphane dosyası tutmaz).</summary>
    public static class RiotGames
    {
        public sealed record Product(string Id, string DisplayName, string RelativeExe);

        public static readonly IReadOnlyList<Product> Known = new[]
        {
            new Product("valorant.live", "VALORANT", @"ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe"),
            new Product("league_of_legends.live", "League of Legends", @"Game\League of Legends.exe"),
            new Product("teamfighttactics.live", "Teamfight Tactics", @"TFT\Binaries\Win64\TFTClient-Win64-Shipping.exe"),
        };

        /// <summary>product_settings.yaml içinden <c>product_install_full_path: "C:/Riot Games/VALORANT/live"</c>.</summary>
        public static string? ParseInstallPath(string? yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml)) return null;
            foreach (var raw in yaml.Split('\n'))
            {
                string line = raw.Trim();
                const string key = "product_install_full_path:";
                if (!line.StartsWith(key, StringComparison.Ordinal)) continue;
                string value = line[key.Length..].Trim().Trim('"', '\'').Replace('/', '\\');
                return value.Length > 0 ? value : null;
            }
            return null;
        }
    }
}
