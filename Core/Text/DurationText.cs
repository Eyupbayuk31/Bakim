using System;

namespace Bakım.Core.Text
{
    /// <summary>Süreleri kısa Türkçe metne çevirir: "45 sn", "12 dk", "1 sa 5 dk", "2 gün 3 sa".</summary>
    public static class DurationText
    {
        public static string Describe(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds} sn";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} dk";
            if (span.TotalHours < 24)
                return span.Minutes == 0 ? $"{(int)span.TotalHours} sa" : $"{(int)span.TotalHours} sa {span.Minutes} dk";
            return span.Hours == 0 ? $"{(int)span.TotalDays} gün" : $"{(int)span.TotalDays} gün {span.Hours} sa";
        }
    }
}
