using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Health
{
    /// <summary>
    /// Sağlık puanının girdileri (MASTER_PLAN §5.1). Hepsi KALICI sinyallerdir: anlık CPU yükü
    /// puanı düşürmez. Okunamayan sinyal null'dır ve puandan düşülmez ("Bilinmiyor" görünür).
    /// </summary>
    public sealed record HealthSignals
    {
        public DateTime NowUtc { get; init; } = DateTime.UtcNow;
        public string SystemDrive { get; init; } = "C:";
        public int? SystemDriveUsedPercent { get; init; }
        public long? SystemDriveFreeBytes { get; init; }
        public int? EnabledStartupCount { get; init; }
        public int? BsodLast7Days { get; init; }
        public int? CriticalEventsLast7Days { get; init; }
        public bool? DefenderRealtimeOn { get; init; }
        public bool? RebootPending { get; init; }
        /// <summary>Etkinlik Merkezi'ndeki son temizlik (null: hiç yok ya da bilinmiyor).</summary>
        public DateTime? LastCleanUtc { get; init; }
        /// <summary>Etkinlik kaydı okunabildi mi (false ise temizlik bileşeni Bilinmiyor).</summary>
        public bool ActivityKnown { get; init; } = true;
    }

    public enum HealthLevel { Good, Attention, Problem, Unknown }

    /// <param name="DeepLink">İlgili modülün gezinme anahtarı ("Storage", "Startup" …).</param>
    public sealed record HealthComponent(string Key, string Title, string Detail, int Deduction, HealthLevel Level,
        string? DeepLink = null, string? ActionText = null);

    public sealed record HealthReport(int Score, IReadOnlyList<HealthComponent> Components)
    {
        public HealthLevel Level => Score >= 90 ? HealthLevel.Good : Score >= 70 ? HealthLevel.Attention : HealthLevel.Problem;

        public string Title => Level switch
        {
            HealthLevel.Good => "Sistem iyi durumda",
            HealthLevel.Attention => "Dikkat gerektiren noktalar var",
            _ => "Sorunlar var"
        };

        /// <summary>Puanı düşüren bileşenler, en çok düşürenden başlayarak.</summary>
        public IReadOnlyList<HealthComponent> Issues =>
            Components.Where(c => c.Deduction > 0).OrderByDescending(c => c.Deduction).ToList();

        public string Summary => Issues.Count switch
        {
            0 => "Kalıcı sinyallerin hiçbiri sorun göstermiyor.",
            1 => "1 konu puanı düşürüyor.",
            _ => $"{Issues.Count} konu puanı düşürüyor."
        };
    }

    public static class HealthScore
    {
        public static HealthReport Evaluate(HealthSignals s)
        {
            var parts = new List<HealthComponent>
            {
                Disk(s),
                Startup(s),
                Stability(s),
                Defender(s),
                Reboot(s),
                Cleanup(s)
            };
            int score = Math.Clamp(100 - parts.Sum(p => p.Deduction), 0, 100);
            return new HealthReport(score, parts);
        }

        private static HealthComponent Disk(HealthSignals s)
        {
            const string title = "Sistem sürücüsü";
            if (s.SystemDriveUsedPercent is not { } used)
                return new("disk", title, "Doluluk okunamadı", 0, HealthLevel.Unknown, "Storage");

            string free = s.SystemDriveFreeBytes is { } b ? $" · {Text.ByteFormatter.Format(b)} boş" : "";
            string detail = $"{s.SystemDrive} %{used} dolu{free}";
            return used switch
            {
                >= 95 => new("disk", title, detail, 30, HealthLevel.Problem, "Storage", "Yer aç"),
                >= 90 => new("disk", title, detail, 20, HealthLevel.Problem, "Storage", "Yer aç"),
                >= 80 => new("disk", title, detail, 8, HealthLevel.Attention, "Storage", "Büyük dosyalar"),
                _ => new("disk", title, detail, 0, HealthLevel.Good, "Storage")
            };
        }

        private static HealthComponent Startup(HealthSignals s)
        {
            const string title = "Başlangıç programları";
            if (s.EnabledStartupCount is not { } n)
                return new("startup", title, "Liste okunamadı", 0, HealthLevel.Unknown, "Startup");
            string detail = $"{n} program Windows ile açılıyor";
            return n switch
            {
                > 20 => new("startup", title, detail, 12, HealthLevel.Problem, "Startup", "Gözden geçir"),
                > 12 => new("startup", title, detail, 6, HealthLevel.Attention, "Startup", "Gözden geçir"),
                _ => new("startup", title, detail, 0, HealthLevel.Good, "Startup")
            };
        }

        private static HealthComponent Stability(HealthSignals s)
        {
            const string title = "Kararlılık (son 7 gün)";
            if (s.BsodLast7Days is null && s.CriticalEventsLast7Days is null)
                return new("stability", title, "Olay günlüğü okunamadı", 0, HealthLevel.Unknown, "CrashAnalyzer");

            int bsod = s.BsodLast7Days ?? 0;
            int critical = s.CriticalEventsLast7Days ?? 0;
            if (bsod > 0)
                return new("stability", title, $"{bsod} mavi ekran · {critical} kritik olay", Math.Min(30, 15 + 5 * (bsod - 1)),
                    HealthLevel.Problem, "CrashAnalyzer", "Çökmeleri incele");
            if (critical >= 10)
                return new("stability", title, $"{critical} kritik olay", 6, HealthLevel.Attention, "CrashAnalyzer", "Olayları incele");
            return new("stability", title, critical == 0 ? "Mavi ekran ya da kritik olay yok" : $"Mavi ekran yok · {critical} kritik olay",
                0, HealthLevel.Good, "CrashAnalyzer");
        }

        private static HealthComponent Defender(HealthSignals s)
        {
            const string title = "Microsoft Defender";
            return s.DefenderRealtimeOn switch
            {
                true => new("defender", title, "Gerçek zamanlı koruma açık", 0, HealthLevel.Good),
                false => new("defender", title, "Gerçek zamanlı koruma kapalı", 25, HealthLevel.Problem, null, "Windows Güvenliği"),
                null => new("defender", title, "Durum okunamadı (başka bir antivirüs kullanılıyor olabilir)", 0, HealthLevel.Unknown)
            };
        }

        private static HealthComponent Reboot(HealthSignals s)
        {
            const string title = "Windows Update";
            return s.RebootPending switch
            {
                true => new("reboot", title, "Güncellemeler yeniden başlatma bekliyor", 5, HealthLevel.Attention),
                false => new("reboot", title, "Bekleyen yeniden başlatma yok", 0, HealthLevel.Good),
                null => new("reboot", title, "Durum okunamadı", 0, HealthLevel.Unknown)
            };
        }

        private static HealthComponent Cleanup(HealthSignals s)
        {
            const string title = "Son temizlik";
            if (!s.ActivityKnown) return new("cleanup", title, "Bilinmiyor", 0, HealthLevel.Unknown, "Cleaner");
            if (s.LastCleanUtc is not { } last)
                return new("cleanup", title, "Bakım ile henüz temizlik yapılmadı", 5, HealthLevel.Attention, "Cleaner", "Temizle");
            int days = (int)Math.Floor((s.NowUtc - last).TotalDays);
            string detail = days <= 0 ? "Bugün" : $"{days} gün önce";
            return days > 30
                ? new("cleanup", title, detail, 5, HealthLevel.Attention, "Cleaner", "Temizle")
                : new("cleanup", title, detail, 0, HealthLevel.Good, "Cleaner");
        }
    }
}
