using System;

namespace Bakım.Core.Health
{
    public enum SupportState { Supported, EndingSoon, Ended, Unknown }

    public sealed record WindowsSupportInfo(string Release, DateTime? EndOfSupport, SupportState State, string Text);

    /// <summary>
    /// Windows sürümünün Home/Pro destek süresi (§5.8, "Windows sürümü desteği bitiyor mu?").
    /// Tarihler Microsoft yaşam döngüsü sayfalarındandır; bilinmeyen derlemede tahmin yapılmaz.
    /// </summary>
    public static class WindowsLifecycle
    {
        private static readonly (int Build, string Release, DateTime End)[] Table =
        {
            (26200, "Windows 11 25H2", new DateTime(2027, 10, 12)),
            (26100, "Windows 11 24H2", new DateTime(2026, 10, 13)),
            (22631, "Windows 11 23H2", new DateTime(2025, 11, 11)),
            (22621, "Windows 11 22H2", new DateTime(2024, 10, 8)),
            (22000, "Windows 11 21H2", new DateTime(2023, 10, 10)),
            (19045, "Windows 10 22H2", new DateTime(2025, 10, 14)),
            (19044, "Windows 10 21H2", new DateTime(2023, 6, 13)),
            (19043, "Windows 10 21H1", new DateTime(2022, 12, 13)),
            (19042, "Windows 10 20H2", new DateTime(2022, 5, 10)),
        };

        public static WindowsSupportInfo Describe(int build, DateTime today)
        {
            foreach (var (b, release, end) in Table)
            {
                if (build != b) continue;
                int days = (int)(end.Date - today.Date).TotalDays;
                string date = end.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
                if (days < 0)
                    return new(release, end, SupportState.Ended,
                        $"{release}: güvenlik güncellemeleri {date} tarihinde sona erdi. Windows Update'ten yeni sürüme geçin.");
                if (days <= 90)
                    return new(release, end, SupportState.EndingSoon,
                        $"{release}: destek {days} gün sonra ({date}) bitiyor. Windows Update'ten yeni sürüme geçmeniz önerilir.");
                return new(release, end, SupportState.Supported, $"{release}: {date} tarihine kadar destekleniyor.");
            }
            if (build > Table[0].Build)
                return new($"Windows (derleme {build})", null, SupportState.Supported, $"Derleme {build}: güncel ya da Insider sürüm.");
            return new($"Windows (derleme {build})", null, SupportState.Unknown, $"Derleme {build}: destek tarihi bilinmiyor.");
        }
    }
}
