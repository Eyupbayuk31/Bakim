using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Analyzer
{
    /// <summary>
    /// Dosya kod mu (çalıştırılabilir, betik, kurulum paketi, kısayol)? "İmzasız", "Temp'ten çalışıyor" ve "VirusTotal'de
    /// kaydı yok" puanları yalnızca kod benzeri dosyalara anlamlıdır. Eskiden sağ tıkla analiz edilen düz bir .txt
    /// dosyası bu üç kuraldan 60 (Şüpheli) alıyordu. İçerik uzantıdan önce gelir: uzantısı .txt olan bir PE (MZ) kod sayılır.
    /// </summary>
    public static class ExecutableContent
    {
        public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".scr", ".com", ".cpl", ".ocx", ".drv", ".efi", ".mui",
            ".msi", ".msp", ".msix", ".msixbundle", ".appx", ".appxbundle",
            ".lnk", ".url", ".jar", ".reg", ".inf", ".psd1", ".wsh", ".vb", ".py", ".pyw"
        };

        public static bool IsCodeLike(string fileName, ReadOnlySpan<byte> header)
        {
            if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z') return true;
            int dot = fileName.LastIndexOf('.');
            if (dot < 0) return false;
            string ext = fileName[dot..];
            return Extensions.Contains(ext) || ScriptRules.ScriptExtensions.Contains(ext);
        }
    }

    /// <summary>Kritik Windows ikilileri ve yasal konumları (ANL Z3).</summary>
    public static class SystemBinaryLocations
    {
        /// <summary>
        /// Ad → yasal dizin(ler) (%windir%'e göre). explorer.exe System32'de değil %windir%'dedir;
        /// eski kural bu yüzden Windows'un kendi explorer.exe'sini "sistem dosyası taklidi" (+40) sayıyordu.
        /// </summary>
        private static readonly Dictionary<string, string[]> Legit = new(StringComparer.OrdinalIgnoreCase)
        {
            ["explorer.exe"] = new[] { "", "SysWOW64" },
            ["svchost.exe"] = new[] { "System32", "SysWOW64" },
            ["csrss.exe"] = new[] { "System32" },
            ["lsass.exe"] = new[] { "System32" },
            ["smss.exe"] = new[] { "System32" },
            ["services.exe"] = new[] { "System32" },
            ["winlogon.exe"] = new[] { "System32" },
            ["wininit.exe"] = new[] { "System32" },
            ["taskhostw.exe"] = new[] { "System32" },
            ["rundll32.exe"] = new[] { "System32", "SysWOW64" },
            ["dwm.exe"] = new[] { "System32" },
            ["spoolsv.exe"] = new[] { "System32" },
            ["conhost.exe"] = new[] { "System32" },
            ["sihost.exe"] = new[] { "System32" },
            ["dllhost.exe"] = new[] { "System32", "SysWOW64" },
            ["ctfmon.exe"] = new[] { "System32", "SysWOW64" },
        };

        public static bool IsCriticalName(string? fileName) => fileName != null && Legit.ContainsKey(fileName);

        /// <summary>Kritik bir ad yasal dizini dışında mı (taklit)? Kritik olmayan adlar için false.</summary>
        public static bool IsImpersonation(string fullPath, string windowsDirectory)
        {
            string name = CommandLineResolver.FileName(fullPath);
            if (!Legit.TryGetValue(name, out var dirs)) return false;
            string dir = fullPath[..Math.Max(0, fullPath.Length - name.Length)].TrimEnd('\\', '/');
            string windows = windowsDirectory.TrimEnd('\\');
            return !dirs.Any(d => string.Equals(dir, d.Length == 0 ? windows : windows + "\\" + d, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>VirusTotal sonucu için karar tabanı (ANL Z4).</summary>
    public readonly record struct VirusTotalFloor(int MinimumScore, int Impact, string Title, bool Critical);

    public static class VirusTotalRules
    {
        /// <summary>
        /// VT uzlaşısı baskın kanıttır: imza ya da başka "temiz" işaretler bunu düşüremez. Eskiden en fazla
        /// +25 ekleniyordu; geçerli (çalıntı) sertifikayla imzalı ve 60 motorun zararlı dediği dosya
        /// "GÜVENLİ" önerisi alıyordu.
        /// </summary>
        public static VirusTotalFloor? Evaluate(int malicious, int suspicious, int total)
        {
            if (total <= 0 || (malicious <= 0 && suspicious <= 0)) return null;
            double weighted = malicious + suspicious * 0.5;
            if (malicious >= 10 || weighted / total >= 0.15)
                return new VirusTotalFloor(80, 45, "VirusTotal: çok sayıda antivirüs zararlı buldu", true);
            if (malicious >= 3)
                return new VirusTotalFloor(55, 30, "VirusTotal: birden çok antivirüs zararlı buldu", true);
            if (malicious >= 1)
                return new VirusTotalFloor(0, 15, "VirusTotal: az sayıda tespit (yanlış pozitif olabilir)", false);
            return suspicious >= 3 ? new VirusTotalFloor(0, 10, "VirusTotal: şüpheli işaretler", false) : null;
        }
    }

    /// <summary>İçe aktarılan API'lerin birleşim kuralları (ANL Z5).</summary>
    public sealed record ImportFinding(string Title, string Detail, int Impact, bool Critical);

    public static class ImportRules
    {
        private static readonly string[] InjectionWrite = { "WriteProcessMemory" };
        private static readonly string[] InjectionAlloc = { "VirtualAllocEx", "VirtualProtectEx" };
        private static readonly string[] InjectionExec = { "CreateRemoteThread", "RtlCreateUserThread", "QueueUserAPC", "SetThreadContext", "NtUnmapViewOfSection", "ZwUnmapViewOfSection" };

        /// <summary>
        /// Tek başına API içe aktarımı kanıt değildir: <c>GetKeyState</c> ve <c>GetClipboardData</c> her
        /// arayüzlü uygulamada, <c>VirtualAllocEx</c>/<c>WriteProcessMemory</c> hata ayıklayıcı, katman
        /// (Discord/OBS/Steam) ve oyun araçlarında bulunur. Yalnızca tam enjeksiyon zinciri (ayır + yaz +
        /// çalıştır) sayılır ve güvenilir imzalı dosyada kritik değil bilgi olur.
        /// </summary>
        public static IReadOnlyList<ImportFinding> Evaluate(IEnumerable<string> importedFunctions, bool trustedSigned)
        {
            var set = new HashSet<string>(importedFunctions.Select(Normalize), StringComparer.OrdinalIgnoreCase);
            var findings = new List<ImportFinding>();

            bool chain = InjectionWrite.Any(set.Contains) && InjectionAlloc.Any(set.Contains) && InjectionExec.Any(set.Contains);
            if (chain)
            {
                string names = string.Join(", ", InjectionAlloc.Concat(InjectionWrite).Concat(InjectionExec).Where(set.Contains).Take(4));
                findings.Add(trustedSigned
                    ? new ImportFinding("Başka süreçlere kod yazabilen API zinciri", $"İmzalı yazılımlarda (hata ayıklayıcı, oyun katmanı, güvenlik aracı) olağandır: {names}.", 0, false)
                    : new ImportFinding("Süreç enjeksiyonu API zinciri", $"Dosya başka bir sürecin belleğine yer ayırma, yazma ve orada kod çalıştırma API'lerinin tamamını içe aktarıyor: {names}.", 25, true));
            }

            bool lowLevelHook = set.Contains("SetWindowsHookEx") && set.Contains("GetAsyncKeyState");
            if (lowLevelHook && !trustedSigned)
                findings.Add(new ImportFinding("Klavye kancası ve tuş sorgusu birlikte", "Küresel kanca kurma ve arka planda tuş durumu okuma birlikte kullanılıyor; tuş kaydedicilerde görülen bir birleşimdir.", 10, false));

            bool downloader = (set.Contains("URLDownloadToFile") || set.Contains("InternetOpenUrl")) && set.Contains("WinExec");
            if (downloader && !trustedSigned)
                findings.Add(new ImportFinding("İndir ve çalıştır birleşimi", "İnternetten dosya indirme ve doğrudan çalıştırma API'leri birlikte.", 10, false));

            return findings;
        }

        /// <summary>A/W son eklerini atar: "SetWindowsHookExW" → "SetWindowsHookEx".</summary>
        public static string Normalize(string name) =>
            name.Length > 1 && (name.EndsWith('A') || name.EndsWith('W')) && char.IsLower(name[^2]) ? name[..^1] : name;
    }

    /// <summary>Komut satırı argümanı sezgileri (ANL AN-22): alt dize değil, belirteç eşleşmesi.</summary>
    public static class ArgumentRules
    {
        private static readonly HashSet<string> HiddenWindow = new(StringComparer.OrdinalIgnoreCase)
        { "-w", "-windowstyle", "/w", "-win" };
        private static readonly HashSet<string> Encoded = new(StringComparer.OrdinalIgnoreCase)
        { "-enc", "-encodedcommand", "-e", "-ec" };

        public static bool HasHiddenWindow(IReadOnlyList<string> tokens)
        {
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (HiddenWindow.Contains(tokens[i]) && tokens[i + 1].Equals("hidden", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool HasEncodedCommand(IReadOnlyList<string> tokens) => tokens.Any(Encoded.Contains);

        public static bool HasNoProfileBypass(IReadOnlyList<string> tokens) =>
            tokens.Any(t => t.Equals("-nop", StringComparison.OrdinalIgnoreCase) || t.Equals("-noprofile", StringComparison.OrdinalIgnoreCase)) &&
            tokens.Any(t => t.Equals("bypass", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>CSV dışa aktarma (ANL AN-16): tırnak kaçışı ve formül enjeksiyonu önleme.</summary>
    public static class CsvText
    {
        public static string Field(string? value)
        {
            string v = value ?? string.Empty;
            if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
