using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakım.Core.Analyzer
{
    public enum DefenderScanState
    {
        /// <summary>Defender tarama yaptı ve tehdit bulmadı.</summary>
        Clean,
        /// <summary>Defender tehdit buldu.</summary>
        ThreatFound,
        /// <summary>Defender kapalı ya da başka bir antivirüs etkin: ikinci görüş alınamadı.</summary>
        Unavailable,
        /// <summary>Tarama çalıştı ama sonuç okunamadı (zaman aşımı, beklenmeyen çıktı).</summary>
        Failed
    }

    public sealed record DefenderScanResult(DefenderScanState State, IReadOnlyList<string> Threats, string Message)
    {
        public static DefenderScanResult Unavailable(string message) => new(DefenderScanState.Unavailable, Array.Empty<string>(), message);
        public static DefenderScanResult Failed(string message) => new(DefenderScanState.Failed, Array.Empty<string>(), message);
    }

    /// <summary>
    /// <c>MpCmdRun.exe -Scan -ScanType 3 -File</c> çıktısını yorumlar (ANL Faz 3).
    ///
    /// Defender kapalıyken MpCmdRun hiçbir şey taramadan, çıktısız ve 0 koduyla döner; çıkış koduna
    /// bakan bir entegrasyon her dosyayı "temiz" sayardı. Bu yüzden "temiz" ancak çıktı taramanın
    /// yapıldığını gösteriyorsa verilir.
    /// </summary>
    public static class DefenderOutput
    {
        private static readonly Regex ThreatLine = new(@"^\s*Threat\s*:\s*(?<name>.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FoundCount = new(@"found\s+(?<n>\d+)\s+threats?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static DefenderScanResult Parse(string? stdout, int exitCode)
        {
            string text = stdout ?? string.Empty;
            var threats = ThreatLine.Matches(text).Select(m => m.Groups["name"].Value.Trim()).Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Araç hatası ("CmdTool: Failed with hr = 0x…"): çıkış kodu ne olursa olsun tehdit değildir.
            if (threats.Count == 0 && text.Contains("failed with hr", StringComparison.OrdinalIgnoreCase))
            {
                return text.Contains("0x800106ba", StringComparison.OrdinalIgnoreCase) || text.Contains("0x80508023", StringComparison.OrdinalIgnoreCase)
                    ? DefenderScanResult.Unavailable("Windows Defender hizmeti çalışmıyor (kapalı ya da başka bir antivirüs etkin).")
                    : DefenderScanResult.Failed("Windows Defender taraması hata verdi.");
            }

            bool scanned = text.Contains("Scan finished", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("found no threats", StringComparison.OrdinalIgnoreCase) ||
                           FoundCount.IsMatch(text);

            if (exitCode == 2 || threats.Count > 0 || (FoundCount.Match(text) is { Success: true } m && int.Parse(m.Groups["n"].Value) > 0))
            {
                return new DefenderScanResult(DefenderScanState.ThreatFound,
                    threats.Count > 0 ? threats : new[] { "Adı bildirilmeyen tehdit" },
                    "Windows Defender bu dosyada tehdit buldu.");
            }

            if (exitCode == 0 && scanned)
                return new DefenderScanResult(DefenderScanState.Clean, Array.Empty<string>(), "Windows Defender tehdit bulmadı.");

            if (exitCode == 0 && text.Trim().Length == 0)
                return DefenderScanResult.Unavailable("Windows Defender tarama yapamadı (kapalı ya da başka bir antivirüs etkin).");

            return DefenderScanResult.Failed($"Windows Defender sonucu okunamadı (çıkış kodu {exitCode}).");
        }
    }

    /// <summary>AMSI sonucu (AMSI_RESULT): 32768 ve üstü "tespit edildi".</summary>
    public static class AmsiResult
    {
        public const int Detected = 32768;
        public static bool IsMalware(int result) => result >= Detected;
        public static bool IsBlockedByAdmin(int result) => result is >= 16384 and <= 20479;
    }
}
