using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakım.Core.History
{
    public enum ExportFormat
    {
        Csv,
        Json,
        Html
    }

    /// <summary>Geçmişin CSV / JSON / HTML dışa aktarımı; isteğe bağlı kullanıcı adı maskeleme.</summary>
    public static class HistoryExport
    {
        private static readonly Regex UserFolder = new(@"^([A-Za-z]:\\Users\\)[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>"C:\Users\Ali\AppData\x.exe" → "C:\Users\***\AppData\x.exe".</summary>
        public static string MaskUserName(string? path) =>
            string.IsNullOrEmpty(path) ? string.Empty : UserFolder.Replace(path, "$1***");

        public static string Render(IEnumerable<AnalysisRecord> records, ExportFormat format, bool maskUserName)
        {
            var list = records.Select(r => maskUserName ? r with { FilePath = MaskUserName(r.FilePath) } : r).ToList();
            return format switch
            {
                ExportFormat.Csv => ToCsv(list),
                ExportFormat.Json => JsonSerializer.Serialize(list, new JsonSerializerOptions(AnalysisHistoryStore.Json) { WriteIndented = true }),
                _ => ToHtml(list)
            };
        }

        public static string ToCsv(IEnumerable<AnalysisRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tarih (UTC);Dosya;Yol;Risk;Karar;Kaynak;İmza;İmzalayan;SHA-256;VirusTotal;Kullanıcı kararı;Not");
            foreach (var r in records)
            {
                string vt = r.VirusTotalMalicious.HasValue && r.VirusTotalTotal.HasValue ? $"{r.VirusTotalMalicious}/{r.VirusTotalTotal}" : "";
                sb.AppendLine(string.Join(';', new[]
                {
                    r.AnalyzedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Csv(r.FileName), Csv(r.FilePath),
                    r.RiskScore.ToString(CultureInfo.InvariantCulture),
                    Csv(AnalysisVerdicts.Display(r.Verdict)),
                    Csv(AnalysisVerdicts.Display(r.Source)),
                    Csv(RecordComparer.SignatureDisplay(r.SignatureStatus)),
                    Csv(r.Signer), Csv(r.Sha256), Csv(vt),
                    Csv(AnalysisVerdicts.Display(r.Decision)), Csv(r.Note)
                }));
            }
            return sb.ToString();
        }

        public static string ToHtml(IEnumerable<AnalysisRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"UTF-8\"><title>Bakım - Analizör Geçmişi</title>");
            sb.AppendLine("<style>body{font-family:'Segoe UI',sans-serif;margin:24px;color:#1f2937}table{border-collapse:collapse;width:100%}" +
                          "th,td{border-bottom:1px solid #e5e7eb;padding:6px 8px;text-align:left;font-size:13px;vertical-align:top}" +
                          "th{background:#f3f4f6}.r{font-weight:600}.Dangerous{color:#b91c1c}.Suspicious{color:#c2410c}.Caution{color:#a16207}.Clean{color:#15803d}</style></head><body>");
            sb.AppendLine("<h1>Analizör Geçmişi</h1><table><thead><tr><th>Tarih (UTC)</th><th>Dosya</th><th>Risk</th><th>Kaynak</th><th>İmza</th><th>VirusTotal</th><th>Karar</th></tr></thead><tbody>");
            foreach (var r in records)
            {
                string vt = r.VirusTotalMalicious.HasValue && r.VirusTotalTotal.HasValue ? $"{r.VirusTotalMalicious}/{r.VirusTotalTotal}" : "—";
                sb.Append("<tr>")
                  .Append("<td>").Append(r.AnalyzedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("</td>")
                  .Append("<td><b>").Append(Html(r.FileName)).Append("</b><br><small>").Append(Html(r.FilePath)).Append("</small></td>")
                  .Append("<td class=\"r ").Append(r.Verdict).Append("\">").Append(r.RiskScore).Append(" · ").Append(Html(AnalysisVerdicts.Display(r.Verdict))).Append("</td>")
                  .Append("<td>").Append(Html(AnalysisVerdicts.Display(r.Source))).Append("</td>")
                  .Append("<td>").Append(Html(RecordComparer.SignatureDisplay(r.SignatureStatus)))
                  .Append(string.IsNullOrEmpty(r.Signer) ? "" : "<br><small>" + Html(r.Signer) + "</small>").Append("</td>")
                  .Append("<td>").Append(vt).Append("</td>")
                  .Append("<td>").Append(Html(AnalysisVerdicts.Display(r.Decision))).Append("</td>")
                  .AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></body></html>");
            return sb.ToString();
        }

        private static string Html(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        /// <summary>CSV hücresi: ; " ve satır sonu içerirse tırnaklanır; formül enjeksiyonuna karşı =,+,-,@ ile başlayana ' eklenir.</summary>
        private static string Csv(string? s)
        {
            string v = s ?? string.Empty;
            if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v;
            return v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
