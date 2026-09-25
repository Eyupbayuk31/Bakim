using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bakım.Core.Startup
{
    /// <summary>Windows'un ölçtüğü bir açılış (Diagnostics-Performance, olay 100).</summary>
    public sealed record BootRecord(DateTime TimeUtc, int BootMs, int MainPathMs, int PostBootMs);

    /// <summary>Açılışı yavaşlatan uygulama kaydı (olay 101).</summary>
    public sealed record StartupDegradation(DateTime TimeUtc, string FileName, string FriendlyName, int TotalMs, int DegradationMs);

    /// <summary>Bir uygulamanın ölçülen açılış etkisi (birden çok olayın özeti).</summary>
    public sealed record AppImpact(string FileName, int Count, int MaxDegradationMs, int AvgDegradationMs, DateTime LastUtc)
    {
        /// <summary>3 yüksek (≥1 sn), 2 orta (≥300 ms), 1 düşük.</summary>
        public int Level => AvgDegradationMs >= 1000 ? 3 : AvgDegradationMs >= 300 ? 2 : 1;
    }

    /// <summary>
    /// Microsoft-Windows-Diagnostics-Performance/Operational olaylarını ayrıştırır (MASTER_PLAN §5.6).
    /// Tahmini etki yok: yalnızca Windows'un gerçekten ölçüp kaydettiği süreler kullanılır.
    /// </summary>
    public static class BootEventParser
    {
        public const int BootEventId = 100;
        public const int AppDegradationEventId = 101;

        public static BootRecord? ParseBoot(string? xml)
        {
            var (time, data) = Read(xml);
            if (time == null || !TryInt(data, "BootTime", out int boot) || boot <= 0) return null;
            TryInt(data, "MainPathBootTime", out int main);
            TryInt(data, "BootPostBootTime", out int post);
            return new BootRecord(time.Value, boot, main, post);
        }

        public static StartupDegradation? ParseDegradation(string? xml)
        {
            var (time, data) = Read(xml);
            if (time == null || !data.TryGetValue("Name", out string? name) || string.IsNullOrWhiteSpace(name)) return null;
            TryInt(data, "TotalTime", out int total);
            TryInt(data, "DegradationTime", out int degradation);
            data.TryGetValue("FriendlyName", out string? friendly);
            return new StartupDegradation(time.Value, name.Trim(), friendly?.Trim() ?? string.Empty, total, degradation);
        }

        /// <summary>Dosya adına göre gruplar (küçük harf, yol olmadan): "Discord.exe" → etki.</summary>
        public static IReadOnlyDictionary<string, AppImpact> Aggregate(IEnumerable<StartupDegradation> events)
        {
            return events
                .GroupBy(e => KeyOf(e.FileName))
                .Where(g => g.Key.Length > 0)
                .ToDictionary(
                    g => g.Key,
                    g => new AppImpact(g.First().FileName, g.Count(), g.Max(e => e.DegradationMs),
                        (int)Math.Round(g.Average(e => (double)e.DegradationMs)), g.Max(e => e.TimeUtc)),
                    StringComparer.OrdinalIgnoreCase);
        }

        public static string KeyOf(string? fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return string.Empty;
            string name = fileNameOrPath.Trim().Trim('"');
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) name = name[(slash + 1)..];
            return name.ToLowerInvariant();
        }

        /// <summary>"34 567 ms" → "34,6 sn" (Türkçe ondalık).</summary>
        public static string FormatSeconds(int ms) =>
            (ms / 1000.0).ToString(ms >= 100000 ? "0" : "0.0", CultureInfo.GetCultureInfo("tr-TR")) + " sn";

        /// <summary>Son açılışın önceki açılışların ortalamasına göre farkı (ms). Yetersiz veri: null.</summary>
        public static int? TrendMs(IReadOnlyList<BootRecord> newestFirst)
        {
            if (newestFirst.Count < 3) return null;
            double previous = newestFirst.Skip(1).Take(9).Average(b => (double)b.BootMs);
            return (int)Math.Round(newestFirst[0].BootMs - previous);
        }

        private static (DateTime? Time, Dictionary<string, string> Data) Read(string? xml)
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(xml)) return (null, data);
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (System.Xml.XmlException)
            {
                return (null, data);
            }

            DateTime? time = null;
            var created = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimeCreated");
            if (created?.Attribute("SystemTime")?.Value is { } st &&
                DateTime.TryParse(st, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                time = parsed;

            foreach (var d in doc.Descendants().Where(e => e.Name.LocalName == "Data"))
            {
                string? name = d.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(name)) data[name] = d.Value;
            }
            return (time, data);
        }

        private static bool TryInt(Dictionary<string, string> data, string key, out int value)
        {
            value = 0;
            return data.TryGetValue(key, out string? s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
