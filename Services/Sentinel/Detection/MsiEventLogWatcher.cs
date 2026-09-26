using System;
using System.Collections.Generic;
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
    /// <summary>MsiInstaller 1033: "Windows Installer ürünü yükledi" (ad, sürüm, dil, durum, üretici).</summary>
    public sealed record MsiInstallEvent(long RecordId, DateTime TimeUtc, string ProductName, string Version, string Manufacturer, int Status, string? UserSid)
    {
        public bool Succeeded => Status == 0;
        public bool BySystem => string.Equals(UserSid, "S-1-5-18", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MsiInstaller 1040: işlem başlangıcı. İlk veri kurulumda paket yolu (.msi), bakım/onarımda ürün
    /// kodudur; ikinci veri işlemi başlatan süreçtir (winget MSI'ı kendi sürecinde kurar).
    /// </summary>
    public sealed record MsiTransactionStart(DateTime TimeUtc, string? ProductCode, string? PackagePath, int? ProcessId);

    public static class MsiEventLogWatcher
    {
        private static string XPathTime(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Verilen andan sonra tamamlanan MSI ürün kurulumları (1033), eskiden yeniye. Kurulum
        /// süreci taramalar arasında başlayıp bittiyse (winget'in sessiz MSI kurulumları ~1 sn sürer)
        /// nöbetçi bu kayıtla kurulumu sonradan yakalar.
        /// </summary>
        public static List<MsiInstallEvent> QueryCompletedInstalls(DateTime sinceUtc)
        {
            var result = new List<MsiInstallEvent>();
            try
            {
                string query = $"*[System[Provider[@Name='MsiInstaller'] and EventID=1033 and TimeCreated[@SystemTime>='{XPathTime(sinceUtc)}']]]";
                using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query));
                for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
                {
                    using (record)
                    {
                        string Prop(int i) => record.Properties.Count > i ? record.Properties[i].Value?.ToString()?.Trim() ?? string.Empty : string.Empty;
                        string name = Prop(0);
                        if (name.Length == 0) continue;
                        int.TryParse(Prop(3), out int status);
                        result.Add(new MsiInstallEvent(record.RecordId ?? 0, record.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow,
                            name, Prop(1), Prop(4), status, record.UserId?.Value));
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Debug($"MSI olay günlüğü okunamadı: {ex.Message}", nameof(MsiEventLogWatcher));
            }
            result.Sort((a, b) => a.TimeUtc.CompareTo(b.TimeUtc));
            return result;
        }

        /// <summary>
        /// <paramref name="beforeUtc"/> anından önceki son MSI işlem başlangıcı (1040). Windows Installer
        /// işlemleri sistem genelinde sıralı yürüdüğü için bir 1033 kaydından önceki son 1040 aynı işleme aittir.
        /// </summary>
        public static MsiTransactionStart? FindTransactionStart(DateTime beforeUtc, TimeSpan lookback)
        {
            try
            {
                string query = $"*[System[Provider[@Name='MsiInstaller'] and EventID=1040 and TimeCreated[@SystemTime>='{XPathTime(beforeUtc - lookback)}' and @SystemTime<='{XPathTime(beforeUtc)}']]]";
                using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query) { ReverseDirection = true });
                using var record = reader.ReadEvent();
                if (record == null) return null;
                string? first = record.Properties.Count > 0 ? record.Properties[0].Value?.ToString()?.Trim() : null;
                int? pid = record.Properties.Count > 1 && int.TryParse(record.Properties[1].Value?.ToString(), out int p) ? p : null;
                bool isCode = Guid.TryParse(first, out _);
                return new MsiTransactionStart(record.TimeCreated?.ToUniversalTime() ?? beforeUtc,
                    isCode ? first : null,
                    !isCode && !string.IsNullOrWhiteSpace(first) ? first : null,
                    pid);
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLog.Debug($"MSI işlem başlangıcı okunamadı: {ex.Message}", nameof(MsiEventLogWatcher));
                return null;
            }
        }

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
