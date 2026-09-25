using System;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// Windows Application Olay Günlüğünden MsiInstaller olaylarını
    /// (1040: Başlama, 1042: Bitiş, 11707: Başarılı, 11708: Başarısız)
    /// standart kullanıcı haklarıyla dinleyen ve msiexec işlemlerinin
    /// gerçek bitişini tespit eden servis.
    /// </summary>
    public static class MsiEventLogWatcher
    {
        /// <summary>
        /// Belirli bir zamandan sonra gerçekleşen son MSI durumunu sorgular.
        /// </summary>
        public static (bool HasMsiEvent, bool IsCompleted, string? ProductName) QueryLatestMsiStatus(DateTime sinceUtc)
        {
            try
            {
                // MsiInstaller kaynaklı Application log sorgusu
                string query = "*[System[Provider[@Name='MsiInstaller'] and (EventID=1040 or EventID=1042 or EventID=11707 or EventID=11708)]]";
                var eventQuery = new EventLogQuery("Application", PathType.LogName, query)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(eventQuery);
                for (var eventInstance = reader.ReadEvent(); eventInstance != null; eventInstance = reader.ReadEvent())
                {
                    using (eventInstance)
                    {
                        var timeCreated = eventInstance.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue;
                        if (timeCreated < sinceUtc)
                        {
                            break; // Verilen zamandan daha eski olaylara bakmaya gerek yok
                        }

                        int eventId = eventInstance.Id;
                        string desc = eventInstance.FormatDescription() ?? string.Empty;

                        // 1042 (İşlem sonu), 11707 (Başarılı), 11708 (Başarısız)
                        if (eventId is 1042 or 11707 or 11708)
                        {
                            return (true, true, ExtractProductName(desc));
                        }

                        // 1040 (İşlem başladı)
                        if (eventId == 1040)
                        {
                            return (true, false, ExtractProductName(desc));
                        }
                    }
                }
            }
            catch
            {
                // EventLog okunamıyorsa (ör. kısıtlı ortam) sessizce geç
            }

            return (false, false, null);
        }

        private static string? ExtractProductName(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;

            // "Product: {Name} -- Installation completed..." deseni
            int idx = description.IndexOf("Product:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rem = description.Substring(idx + 8).Trim();
                int endIdx = rem.IndexOfAny(new[] { '-', '.', '\r', '\n' });
                if (endIdx > 0)
                {
                    return rem.Substring(0, endIdx).Trim();
                }
            }

            return null;
        }
    }
}
