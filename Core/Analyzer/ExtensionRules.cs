using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Analyzer
{
    /// <summary>Tarayıcı eklentisi değerlendirmesi: puan ve kullanıcıya gösterilecek açıklama.</summary>
    public sealed record ExtensionAssessment(int Risk, string? Notes, IReadOnlyList<string> PowerfulPermissions);

    /// <summary>
    /// Tarayıcı eklentisi risk kuralları (ANL Faz 1). İzin kullanımı yaygındır: reklam engelleyiciler ve
    /// parola yöneticileri tüm sitelere erişir. Güçlü sinyal, mağaza dışı bir adresten politika ile
    /// zorla kurulmasıdır (kullanıcı kaldıramaz; reklam/izleme yazılımlarının sık kullandığı yol).
    /// </summary>
    public static class ExtensionRules
    {
        private static readonly HashSet<string> Powerful = new(StringComparer.OrdinalIgnoreCase)
        {
            "<all_urls>", "*://*/*", "http://*/*", "https://*/*", "webRequest", "webRequestBlocking", "declarativeNetRequest",
            "proxy", "debugger", "nativeMessaging", "management", "cookies", "clipboardRead", "downloads"
        };

        private static readonly string[] StoreHosts = { "clients2.google.com", "edge.microsoft.com", "addons.mozilla.org", "extension-updates.opera.com" };

        public static bool IsStoreUpdateUrl(string? updateUrl) =>
            string.IsNullOrWhiteSpace(updateUrl) || StoreHosts.Any(h => updateUrl.Contains(h, StringComparison.OrdinalIgnoreCase));

        public static ExtensionAssessment Evaluate(bool forcedByPolicy, string? updateUrl, IEnumerable<string> permissions)
        {
            var notes = new List<string>();
            int risk = 0;
            if (forcedByPolicy)
            {
                bool store = IsStoreUpdateUrl(updateUrl);
                risk += store ? 10 : 35;
                notes.Add(store ? "Politika ile zorla kurulmuş (kullanıcı kaldıramaz)." : $"Mağaza dışı adresten politika ile zorla kurulmuş: {updateUrl}");
            }

            var powerful = permissions.Where(Powerful.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool allSites = powerful.Any(p => p.Contains('*') || p == "<all_urls>");
            bool intercept = powerful.Any(p => p.StartsWith("webRequest", StringComparison.OrdinalIgnoreCase) || p is "proxy" or "debugger" or "declarativeNetRequest");
            if (allSites && intercept)
            {
                risk += 15;
                notes.Add("Tüm sitelerde ağ trafiğini görebilir/değiştirebilir.");
            }
            if (powerful.Contains("nativeMessaging", StringComparer.OrdinalIgnoreCase))
                notes.Add("Bilgisayardaki bir programla konuşabilir (nativeMessaging).");
            if (powerful.Contains("debugger", StringComparer.OrdinalIgnoreCase))
            {
                risk += 10;
                notes.Add("Sayfalara hata ayıklayıcı olarak bağlanabilir (debugger).");
            }

            string? text = notes.Count > 0 ? string.Join(" ", notes) : powerful.Count > 0 ? "İzinler: " + string.Join(", ", powerful) : null;
            return new ExtensionAssessment(risk, text, powerful);
        }

        /// <summary>Chromium "__MSG_ad__" yer tutucusu mu? Anahtarı döndürür.</summary>
        public static string? MessageKey(string? name) =>
            name != null && name.StartsWith("__MSG_", StringComparison.Ordinal) && name.EndsWith("__", StringComparison.Ordinal) && name.Length > 8
                ? name[6..^2]
                : null;
    }
}
