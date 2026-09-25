using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bakım.Core.Health
{
    /// <summary>Bir günün Windows kararlılık endeksi (1–10, Güvenilirlik İzleyicisi).</summary>
    public sealed record ReliabilityDay(DateTime Date, double Index);

    /// <summary>Güvenilirlik zaman çizelgesi (§5.10.4): günlük en düşük endeks ve eğilim.</summary>
    public static class ReliabilityTimeline
    {
        /// <summary>Saatlik ölçümleri güne indirger (o günün en düşük değeri), eskiden yeniye.</summary>
        public static IReadOnlyList<ReliabilityDay> Daily(IEnumerable<(DateTime Time, double Index)> samples, int days, DateTime today)
        {
            DateTime from = today.Date.AddDays(-(days - 1));
            return samples
                .Where(s => s.Time.Date >= from && s.Time.Date <= today.Date && s.Index >= 1 && s.Index <= 10)
                .GroupBy(s => s.Time.Date)
                .Select(g => new ReliabilityDay(g.Key, Math.Round(g.Min(s => s.Index), 1)))
                .OrderBy(d => d.Date)
                .ToList();
        }

        public static string Describe(IReadOnlyList<ReliabilityDay> days)
        {
            if (days.Count == 0)
                return "Windows güvenilirlik verisi yok (Güvenilirlik İzleyicisi veri toplamıyor olabilir).";
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var last = days[^1];
            var worst = days.OrderBy(d => d.Index).First();
            string text = $"Son değer {last.Index.ToString("0.0", tr)}/10";
            if (worst.Index < last.Index - 0.5)
                text += $"; en düşük {worst.Index.ToString("0.0", tr)} ({worst.Date.ToString("d MMM", tr)})";
            text += last.Index >= 9 ? " · kararlı." : last.Index >= 6 ? " · son günlerde sorunlar var." : " · sık çökme ya da hata var.";
            return text;
        }
    }
}
