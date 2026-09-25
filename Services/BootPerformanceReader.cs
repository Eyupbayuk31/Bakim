using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Bakım.Core.Startup;

namespace Bakım.Services
{
    /// <param name="Reason">Veri yoksa kullanıcıya gösterilecek neden.</param>
    public sealed record BootPerformanceData(bool Available, string? Reason, IReadOnlyList<BootRecord> BootsNewestFirst,
        IReadOnlyDictionary<string, AppImpact> AppImpacts)
    {
        public static BootPerformanceData Unavailable(string reason) =>
            new(false, reason, Array.Empty<BootRecord>(), new Dictionary<string, AppImpact>());
    }

    /// <summary>
    /// Windows'un açılış ölçümlerini okur (Diagnostics-Performance günlüğü; olay 100 açılış süresi,
    /// olay 101 açılışı yavaşlatan uygulama). Günlük genellikle yönetici izni ister.
    /// </summary>
    public static class BootPerformanceReader
    {
        private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

        public static BootPerformanceData Read(int maxBoots = 10, int days = 60)
        {
            var boots = new List<BootRecord>();
            var degradations = new List<StartupDegradation>();
            try
            {
                string query = $"*[System[(EventID={BootEventParser.BootEventId} or EventID={BootEventParser.AppDegradationEventId}) " +
                               $"and TimeCreated[timediff(@SystemTime) <= {(long)TimeSpan.FromDays(days).TotalMilliseconds}]]]";
                using var reader = new EventLogReader(new EventLogQuery(LogName, PathType.LogName, query) { ReverseDirection = true });
                for (int i = 0; i < 2000; i++)
                {
                    using var record = reader.ReadEvent();
                    if (record == null) break;
                    string xml = record.ToXml();
                    if (record.Id == BootEventParser.BootEventId)
                    {
                        if (boots.Count < maxBoots && BootEventParser.ParseBoot(xml) is { } boot) boots.Add(boot);
                    }
                    else if (BootEventParser.ParseDegradation(xml) is { } degradation)
                    {
                        degradations.Add(degradation);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return BootPerformanceData.Unavailable("Windows'un açılış ölçümleri yönetici izniyle okunabilir.");
            }
            catch (EventLogNotFoundException)
            {
                return BootPerformanceData.Unavailable("Bu sistemde açılış ölçüm günlüğü yok.");
            }
            catch (EventLogException ex)
            {
                AppLog.Warning("Açılış ölçüm günlüğü okunamadı.", ex, nameof(BootPerformanceReader));
                return BootPerformanceData.Unavailable("Açılış ölçüm günlüğü okunamadı.");
            }

            if (boots.Count == 0 && degradations.Count == 0)
                return BootPerformanceData.Unavailable("Windows henüz açılış ölçümü kaydetmemiş.");
            return new BootPerformanceData(true, null, boots, BootEventParser.Aggregate(degradations));
        }
    }
}
