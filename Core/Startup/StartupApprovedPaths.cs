using System;

namespace Bakım.Core.Startup
{
    /// <summary>
    /// Başlangıç girdilerinin etkin/devre dışı durumunu tutan
    /// Explorer\StartupApproved anahtarlarının seçimi.
    ///
    /// Windows'un kullandığı (ve Görev Yöneticisi'nin okuduğu) anahtarlar:
    ///   • ...\Explorer\StartupApproved\Run        → 64 bit Run ve HKCU Run girdileri
    ///   • ...\Explorer\StartupApproved\Run32      → WOW6432Node Run girdileri
    ///   • ...\Explorer\StartupApproved\StartupFolder → Başlangıç klasörü kısayolları
    /// Eski kod 32 bit girdiler için var olmayan
    /// "Software\WOW6432Node\...\StartupApproved\Run" anahtarına yazıyordu;
    /// devre dışı bırakma etkisizdi.
    /// </summary>
    public static class StartupApprovedPaths
    {
        public const string Base = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        public static string ForRunKey(string? runKeyPath) =>
            !string.IsNullOrEmpty(runKeyPath) && runKeyPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                ? Base + @"\Run32"
                : Base + @"\Run";

        public static string ForStartupFolder => Base + @"\StartupFolder";

        /// <summary>
        /// Windows biçimi: ilk bayt 0x02 (etkin) ya da 0x03 (devre dışı),
        /// 4-11. baytlar devre dışı bırakılma zamanı (FILETIME, UTC).
        /// </summary>
        public static byte[] BuildValue(bool enabled, DateTime? whenUtc = null, byte[]? existing = null)
        {
            var data = new byte[12];
            if (existing != null) Array.Copy(existing, data, Math.Min(existing.Length, 12));

            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
            {
                long ft = (whenUtc ?? DateTime.UtcNow).ToFileTimeUtc();
                BitConverter.GetBytes(ft).CopyTo(data, 4);
            }
            else
            {
                Array.Clear(data, 4, 8);
            }
            return data;
        }

        /// <summary>Değer yoksa ya da ilk bayt çift ise (0x02, 0x06) etkin sayılır.</summary>
        public static bool IsEnabled(byte[]? value) => value == null || value.Length == 0 || (value[0] & 0x01) == 0;
    }
}
