using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakım.Core.Analyzer
{
    public sealed record ScriptFinding(string Title, string Detail, int Impact, bool Critical);

    /// <summary>
    /// Betik içeriği analizi (ANL Faz 3): PowerShell, VBScript, JScript, toplu iş ve HTA. Tek bir anahtar
    /// kelime kanıt değildir (kurulum betikleri de dosya indirir, görev oluşturur); güçlü sinyal
    /// birleşimlerdir: indir + çalıştır, gizlenmiş yük + çalıştır, güvenlik yazılımını kapatma, kimlik
    /// bilgisi çalma.
    /// </summary>
    public static class ScriptRules
    {
        public static readonly IReadOnlySet<string> ScriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".bat", ".cmd", ".hta"
        };

        private static readonly string[] Download =
        {
            "downloadstring", "downloadfile", "downloaddata", "invoke-webrequest", "invoke-restmethod",
            "net.webclient", "start-bitstransfer", "bitsadmin /transfer", "certutil -urlcache", "certutil.exe -urlcache",
            "msxml2.xmlhttp", "msxml2.serverxmlhttp", "winhttp.winhttprequest", "urldownloadtofile", "curl http", "wget http"
        };

        private static readonly string[] Execute =
        {
            "iex", "invoke-expression", "[scriptblock]::create", ".invokescript", "start-process", "wscript.shell",
            "shell.application", "shellexecute", "eval(", "execute(", "executeglobal", "createobject(\"wscript.shell\")", "rundll32", "regsvr32", "mshta"
        };

        private static readonly string[] Evasion =
        {
            "add-mppreference", "set-mppreference -disable", "disablerealtimemonitoring", "amsiutils", "amsiinitfailed",
            "amsiscanbuffer", "set-mppreference -exclusion", "netsh advfirewall set allprofiles state off", "vssadmin delete shadows",
            "wevtutil cl", "bcdedit /set {default} recoveryenabled no"
        };

        private static readonly string[] Credential =
        {
            "mimikatz", "sekurlsa", "comsvcs.dll, minidump", "comsvcs.dll minidump", "procdump -ma lsass", "lsass.dmp", "invoke-mimikatz"
        };

        private static readonly string[] Persistence =
        {
            "schtasks /create", "register-scheduledtask", @"currentversion\run", "new-service", "sc create", "__eventfilter",
            "commandlineeventconsumer", @"start menu\programs\startup"
        };

        private static readonly Regex Base64Blob = new(@"[A-Za-z0-9+/]{200,}={0,2}", RegexOptions.Compiled);
        private static readonly Regex CharCodes = new(@"(\[char\]\s*\d+|chr\(\s*\d+\s*\)|chrw\(\s*\d+\s*\))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Karartma temizliği: PowerShell ters tırnağı, cmd şapkası ve '+' ile bölünmüş dizeler birleştirilir
        /// ("I`E`X", "i^e^x", "'Inv'+'oke'").
        /// </summary>
        public static string Normalize(string text)
        {
            string t = text.ToLowerInvariant().Replace("`", string.Empty).Replace("^", string.Empty);
            t = Regex.Replace(t, @"['""]\s*\+\s*['""]", string.Empty);
            return Regex.Replace(t, @"\s+", " ");
        }

        public static IReadOnlyList<ScriptFinding> Analyze(string? text)
        {
            var findings = new List<ScriptFinding>();
            if (string.IsNullOrWhiteSpace(text)) return findings;

            string n = Normalize(text);
            // Kısa takma adlar kelime sınırıyla: "irm" "confirm" içinde de geçer.
            bool download = Download.Any(n.Contains) || Regex.IsMatch(n, @"(^|[^a-z0-9-])(iwr|irm|curl|wget)\s+['""]?https?://");
            bool execute = Execute.Any(k => k == "iex" ? Regex.IsMatch(n, @"(^|[^a-z])iex([^a-z]|$)") : n.Contains(k));
            bool evasion = Evasion.Any(n.Contains);
            bool credential = Credential.Any(n.Contains);
            bool persistence = Persistence.Any(n.Contains);
            bool encoded = n.Contains("frombase64string") || Base64Blob.IsMatch(text) ||
                           Regex.IsMatch(n, @"(^|\s)-(e|ec|enc|encodedcommand)\s+[a-z0-9+/=]{20,}");
            bool obfuscated = CharCodes.Matches(text).Count >= 10 || n.Contains("string.fromcharcode") || n.Contains("-bxor") ||
                              text.Split('\n').Any(line => line.Length > 3000);

            if (credential)
                findings.Add(new ScriptFinding("Kimlik bilgisi çalma araçları", "Betik parola ve oturum bilgilerini çalmak için kullanılan araç ya da tekniklere (mimikatz, LSASS bellek dökümü) başvuruyor.", 45, true));
            if (evasion)
                findings.Add(new ScriptFinding("Güvenlik yazılımını devre dışı bırakma", "Betik Windows Defender istisnası ekliyor, korumayı kapatıyor, AMSI'yi atlatıyor ya da gölge kopyaları/olay günlüklerini siliyor.", 35, true));
            if (download && execute)
                findings.Add(new ScriptFinding("İndir ve çalıştır", "Betik internetten içerik indirip doğrudan çalıştırıyor; zararlı yükleyicilerin (dropper) tipik davranışı.", 35, true));
            else if (download)
                findings.Add(new ScriptFinding("İnternetten indirme", "Betik internetten dosya ya da içerik indiriyor (kurulum ve güncelleme betiklerinde de olağandır).", 5, false));
            if (encoded)
                findings.Add(new ScriptFinding("Kodlanmış (base64) içerik", "Betikte kodlanmış uzun bir yük var; içerik gizlenmiş olabilir.", execute ? 20 : 10, false));
            if (obfuscated)
                findings.Add(new ScriptFinding("Karartılmış kod", "Karakter kodlarıyla kurulan dizeler, XOR ya da aşırı uzun satırlar: kodun okunması bilerek zorlaştırılmış.", 15, false));
            if (persistence)
                findings.Add(new ScriptFinding("Kalıcılık oluşturma", "Betik zamanlanmış görev, hizmet, Run girdisi ya da WMI aboneliği oluşturuyor.", 10, false));

            return findings;
        }

        /// <summary>Bulguların toplam etkisi (üst sınır 70: betik tek başına "kesin zararlı" hükmü vermez).</summary>
        public static int TotalImpact(IEnumerable<ScriptFinding> findings) => Math.Min(70, findings.Sum(f => f.Impact));
    }
}
