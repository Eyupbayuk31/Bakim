using System;
using System.Globalization;

namespace Bakım.Core.Text
{
    /// <summary>
    /// Bayt değerlerini okunur metne çevirir. Kod tabanında altıdan fazla kopyası
    /// vardı ve 0 bayt için kimi "0 B", kimi "0 MB" yazıyordu.
    /// </summary>
    public static class ByteFormatter
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        /// <summary>1536 → "1,5 KB" (Türkçe ondalık ayırıcıyla).</summary>
        public static string Format(long bytes, CultureInfo? culture = null)
        {
            culture ??= CultureInfo.GetCultureInfo("tr-TR");
            if (bytes <= 0) return "0 B";

            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            string format = unit == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
            return value.ToString(format, culture) + " " + Units[unit];
        }
    }
}
