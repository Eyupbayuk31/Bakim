using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;
using Bakım.Core.Health;

namespace Bakım.Services
{
    /// <summary>Windows Güvenilirlik İzleyicisi'nin kararlılık endeksini okur (Win32_ReliabilityStabilityMetrics).</summary>
    public static class ReliabilityReader
    {
        public static Task<IReadOnlyList<ReliabilityDay>> ReadAsync(int days = 30) => Task.Run<IReadOnlyList<ReliabilityDay>>(() =>
        {
            var samples = new List<(DateTime, double)>();
            try
            {
                string since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now.AddDays(-days));
                using var searcher = new ManagementObjectSearcher(@"root\cimv2",
                    $"SELECT SystemStabilityIndex, TimeGenerated FROM Win32_ReliabilityStabilityMetrics WHERE TimeGenerated >= '{since}'");
                searcher.Options.Timeout = TimeSpan.FromSeconds(20);
                foreach (ManagementBaseObject o in searcher.Get())
                {
                    using (o)
                    {
                        if (o["TimeGenerated"] is string t && o["SystemStabilityIndex"] is double index)
                            samples.Add((ManagementDateTimeConverter.ToDateTime(t), index));
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                AppLog.Debug($"Güvenilirlik verisi okunamadı: {ex.Message}", nameof(ReliabilityReader));
            }
            return ReliabilityTimeline.Daily(samples, days, DateTime.Today);
        });
    }
}
