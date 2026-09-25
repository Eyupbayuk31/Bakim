using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    public enum RiskSeverity { Info, Medium, High, Critical }

    /// <summary>Kurulum kararı (NÖB 4.4).</summary>
    public enum RiskVerdict { Clean, Info, Caution, Suspicious, Dangerous }

    /// <summary>Bulgu üzerinde yapılabilecek tek tık müdahale (NÖB 5.2).</summary>
    public enum FindingAction
    {
        None,
        /// <summary>Run/RunOnce değerini .reg yedeğiyle sil. Target: "HKLM\...\Run\Ad".</summary>
        RemoveStartupValue,
        /// <summary>Kayıt değerini .reg yedeğiyle sil (IFEO Debugger …). Target: "HKLM\...\Değer [32]".</summary>
        RemoveRegistryValue,
        /// <summary>Hizmeti durdur ve devre dışı bırak (geri alınabilir). Target: hizmet adı.</summary>
        DisableService,
        /// <summary>Görevi devre dışı bırak. Target: görev yolu.</summary>
        DisableTask,
        /// <summary>Güvenlik duvarı kuralını yedekleyerek kaldır. Target: kural kimliği.</summary>
        RemoveFirewallRule,
        /// <summary>Proxy'yi kapat (ProxyEnable=0). Target: kayıt anahtarı.</summary>
        DisableProxy,
        /// <summary>Kök sertifikayı yedekleyerek kaldır. Target: fiziksel depo anahtarı.</summary>
        RemoveRootCertificate,
        /// <summary>Defender istisnasını kaldır. Target: "Paths: C:\x".</summary>
        RemoveDefenderExclusion,
    }

    /// <param name="Technique">İlgili MITRE ATT&amp;CK tekniği (ör. T1547.001).</param>
    public sealed record RiskFinding(RiskSeverity Severity, string Title, string Detail, string? Technique = null,
        FindingAction Action = FindingAction.None, string? Target = null);

    /// <param name="Target">Çalıştırılan dosya (komut satırından ayrıştırılmış), bilinmiyorsa null.</param>
    /// <param name="Signed">Geçerli imza: true/false; denetlenemediyse null.</param>
    /// <param name="Key">Kayıt defteri konumu ("HKLM\...\Run\Ad"); müdahale için.</param>
    public sealed record StartupAddition(string Name, string Command, string? Target, bool? Signed, string? Key = null);

    public sealed record ServiceAddition(string Name, string ImagePath, bool IsDriver, bool AutoStart, bool? Signed);

    /// <param name="ExtensionMismatch">MZ başlıklı ama uzantısı yürütülebilir değil (.dat, .jpg …).</param>
    public sealed record ExecutableDrop(string Path, bool? Signed, bool ExtensionMismatch);

    public sealed class SetupRiskInput
    {
        public List<StartupAddition> Startup { get; } = new();
        public List<ServiceAddition> Services { get; } = new();
        public List<ExecutableDrop> Executables { get; } = new();
        public List<SystemChange> SystemChanges { get; } = new();
        /// <summary>Oturumda yeni oluşan Uninstall kayıtlarının adları (paket yazılım tespiti).</summary>
        public List<string> NewPrograms { get; } = new();
        public bool? InstallerSigned { get; set; }
        public bool InstallerFromInternet { get; set; }
        /// <summary>Windows klasörü (küçük harf karşılaştırılır); test için değiştirilebilir.</summary>
        public string WindowsDirectory { get; set; } = @"C:\Windows";
    }

    public sealed record SetupRiskResult(int Score, RiskVerdict Verdict, IReadOnlyList<RiskFinding> Findings)
    {
        public IEnumerable<RiskFinding> Top(int count) => Findings.Where(f => f.Severity > RiskSeverity.Info).Take(count);

        public static SetupRiskResult Empty { get; } = new(0, RiskVerdict.Clean, Array.Empty<RiskFinding>());
    }

    /// <summary>
    /// Kural tabanlı kurulum risk motoru (NÖB 4.1–4.4). Puan: Kritik 40, Yüksek 20, Orta 8, Bilgi 0.
    /// Geçerli imzalı kurulumda (kritik bulgu yoksa) puan ¼ azalır; imzasız ve İnternet'ten
    /// gelen kurulumda yüksek bulgu varsa 10 eklenir. Karar: 0 → Temiz, &lt;16 → Bilgi, &lt;40 → Dikkat,
    /// &lt;70 → Şüpheli, üstü Tehlikeli.
    /// </summary>
    public static class SetupRiskEngine
    {
        public static SetupRiskResult Evaluate(SetupRiskInput input)
        {
            var findings = new List<RiskFinding>();
            EvaluateSystemChanges(input, findings);
            EvaluateStartup(input, findings);
            EvaluateServices(input, findings);
            EvaluateExecutables(input, findings);

            // Paket yazılım (NÖB 3.5): çalışma zamanı paketleri (VC++, .NET, DirectX …) sayılmaz.
            var programs = input.NewPrograms.Where(p => !IsRuntimePackage(p)).ToList();
            if (programs.Count >= 2)
            {
                findings.Add(new RiskFinding(RiskSeverity.Medium, $"Bu kurulum {programs.Count} program yükledi",
                    string.Join(", ", programs.Take(6)) + (programs.Count > 6 ? " …" : "") +
                    ". İstemediğiniz programı Kaldırıcı'dan kaldırabilirsiniz."));
            }

            findings = findings.OrderByDescending(f => f.Severity).ToList();
            int score = findings.Sum(f => Weight(f.Severity));
            bool hasCritical = findings.Any(f => f.Severity == RiskSeverity.Critical);
            bool hasHigh = findings.Any(f => f.Severity == RiskSeverity.High);

            if (input.InstallerSigned == true && !hasCritical) score = score * 3 / 4;
            if (input.InstallerSigned == false && input.InstallerFromInternet && hasHigh) score += 10;
            score = Math.Clamp(score, 0, 100);

            return new SetupRiskResult(score, VerdictFor(score, findings.Count), findings);
        }

        public static int Weight(RiskSeverity severity) => severity switch
        {
            RiskSeverity.Critical => 40,
            RiskSeverity.High => 20,
            RiskSeverity.Medium => 8,
            _ => 0
        };

        public static RiskVerdict VerdictFor(int score, int findingCount) => score switch
        {
            0 when findingCount == 0 => RiskVerdict.Clean,
            < 16 => RiskVerdict.Info,
            < 40 => RiskVerdict.Caution,
            < 70 => RiskVerdict.Suspicious,
            _ => RiskVerdict.Dangerous
        };

        public static string VerdictLabel(RiskVerdict verdict) => verdict switch
        {
            RiskVerdict.Clean => "Temiz",
            RiskVerdict.Info => "Bilgi",
            RiskVerdict.Caution => "Dikkat",
            RiskVerdict.Suspicious => "Şüpheli",
            RiskVerdict.Dangerous => "Tehlikeli",
            _ => "Bilinmiyor"
        };

        public static string SeverityLabel(RiskSeverity severity) => severity switch
        {
            RiskSeverity.Critical => "Kritik",
            RiskSeverity.High => "Yüksek",
            RiskSeverity.Medium => "Orta",
            _ => "Bilgi"
        };

        private static void EvaluateSystemChanges(SetupRiskInput input, List<RiskFinding> findings)
        {
            foreach (var c in input.SystemChanges.Where(c => c.Kind != ChangeKind.Removed))
            {
                string value = c.After ?? string.Empty;
                switch (c.Area)
                {
                    case SystemArea.RootCertificate:
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "Kök sertifika eklendi",
                            $"{Shorten(value)} — bu sertifika şifreli bağlantıları dinleyebilir.", "T1553.004",
                            c.Key.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase) ? FindingAction.None : FindingAction.RemoveRootCertificate, c.Key));
                        break;
                    case SystemArea.DefenderExclusion:
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "Microsoft Defender istisnası eklendi",
                            $"{c.Key} artık taranmıyor.", "T1562.001", FindingAction.RemoveDefenderExclusion, c.Key));
                        break;
                    case SystemArea.Ifeo:
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "Program açılışı yönlendirildi (IFEO)",
                            $"{c.Key} → {Shorten(value)}", "T1546.012", FindingAction.RemoveRegistryValue, c.Key));
                        break;
                    case SystemArea.Winlogon:
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "Windows oturum açma kabuğu değişti",
                            $"{c.Key}: {Shorten(c.Before)} → {Shorten(value)}", "T1547.004"));
                        break;
                    case SystemArea.AppInit when value.Trim().Length > 0 && value.Trim() != "0":
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "AppInit_DLLs ayarlandı",
                            $"{c.Key} = {Shorten(value)}", "T1546.010"));
                        break;
                    case SystemArea.Hosts when c.Kind == ChangeKind.Added:
                        findings.Add(new RiskFinding(RiskSeverity.Critical, "hosts dosyasına yönlendirme eklendi", c.Key));
                        break;
                    case SystemArea.Proxy when value.Trim().Length > 0 && value.Trim() != "0":
                        findings.Add(new RiskFinding(RiskSeverity.High, "Proxy ayarlandı", $"{c.Key} = {Shorten(value)}",
                            Action: FindingAction.DisableProxy, Target: c.Key));
                        break;
                    case SystemArea.BrowserPolicy:
                        findings.Add(new RiskFinding(RiskSeverity.High, "Tarayıcıya politika ile ayar zorlandı",
                            $"{c.Key} = {Shorten(value)}", "T1176"));
                        break;
                    case SystemArea.FirewallRule when c.Kind == ChangeKind.Added && IsInboundAllow(value):
                        findings.Add(new RiskFinding(RiskSeverity.High, "Gelen bağlantıya izin veren güvenlik duvarı kuralı",
                            FirewallSummary(value), Action: FindingAction.RemoveFirewallRule, Target: c.Key));
                        break;
                    case SystemArea.FirewallRule when c.Kind == ChangeKind.Added:
                        findings.Add(new RiskFinding(RiskSeverity.Info, "Güvenlik duvarı kuralı eklendi", FirewallSummary(value)));
                        break;
                    case SystemArea.ScheduledTask when c.Kind == ChangeKind.Added:
                        findings.Add(new RiskFinding(IsSuspiciousLocation(value) ? RiskSeverity.High : RiskSeverity.Medium,
                            "Zamanlanmış görev eklendi", $"{c.Key} → {Shorten(value)}", "T1053.005", FindingAction.DisableTask, c.Key));
                        break;
                    case SystemArea.ShellExtension when c.Kind == ChangeKind.Added:
                        findings.Add(new RiskFinding(RiskSeverity.Medium, "Sağ tık menüsüne uzantı eklendi", c.Key));
                        break;
                    case SystemArea.EnvironmentPath:
                        findings.Add(new RiskFinding(RiskSeverity.Medium, "PATH ortam değişkeni değişti",
                            PathAdditions(c.Before, value)));
                        break;
                }
            }
        }

        private static void EvaluateStartup(SetupRiskInput input, List<RiskFinding> findings)
        {
            foreach (var s in input.Startup)
            {
                string target = s.Target ?? s.Command;
                var action = s.Key != null ? FindingAction.RemoveStartupValue : FindingAction.None;
                if (IsSuspiciousLocation(target))
                    findings.Add(new RiskFinding(RiskSeverity.High, "Geçici/ortak klasördeki program açılışa eklendi",
                        $"{s.Name} → {Shorten(target)}", "T1547.001", action, s.Key));
                else if (s.Signed == false)
                    findings.Add(new RiskFinding(RiskSeverity.High, "İmzasız program açılışa eklendi",
                        $"{s.Name} → {Shorten(target)}", "T1547.001", action, s.Key));
                else
                    findings.Add(new RiskFinding(RiskSeverity.Medium, "Windows açılışına eklendi",
                        $"{s.Name} → {Shorten(target)}", "T1547.001", action, s.Key));
            }
        }

        private static void EvaluateServices(SetupRiskInput input, List<RiskFinding> findings)
        {
            foreach (var s in input.Services)
            {
                string kind = s.IsDriver ? "sürücü" : "hizmet";
                if (s.Signed == false)
                    findings.Add(new RiskFinding(RiskSeverity.High, $"İmzasız {kind} eklendi", $"{s.Name} → {Shorten(s.ImagePath)}", "T1543.003",
                        FindingAction.DisableService, s.Name));
                else if (s.AutoStart)
                    findings.Add(new RiskFinding(RiskSeverity.Medium, $"Otomatik başlayan {kind} eklendi", $"{s.Name} → {Shorten(s.ImagePath)}", "T1543.003",
                        FindingAction.DisableService, s.Name));
                else
                    findings.Add(new RiskFinding(RiskSeverity.Info, $"{char.ToUpperInvariant(kind[0])}{kind[1..]} eklendi", $"{s.Name} → {Shorten(s.ImagePath)}"));
            }
        }

        private static void EvaluateExecutables(SetupRiskInput input, List<RiskFinding> findings)
        {
            string windows = input.WindowsDirectory.TrimEnd('\\').ToLowerInvariant() + "\\";
            foreach (var e in input.Executables)
            {
                string lower = e.Path.ToLowerInvariant();
                bool inSystem = lower.StartsWith(windows + "system32\\", StringComparison.Ordinal) ||
                                lower.StartsWith(windows + "syswow64\\", StringComparison.Ordinal) ||
                                (lower.StartsWith(windows, StringComparison.Ordinal) && lower.IndexOf('\\', windows.Length) < 0);
                if (inSystem && e.Signed == false)
                    findings.Add(new RiskFinding(RiskSeverity.Critical, "Windows sistem klasörüne imzasız program bırakıldı", e.Path, "T1036"));
                if (e.ExtensionMismatch)
                    findings.Add(new RiskFinding(RiskSeverity.High, "Uzantısı gizlenmiş yürütülebilir dosya",
                        $"{e.Path} — içerik program (MZ) ama uzantısı farklı.", "T1036"));
            }
        }

        private static readonly string[] RuntimeMarkers =
        {
            "Microsoft Visual C++", "Microsoft .NET", ".NET Runtime", "Windows Desktop Runtime", "ASP.NET Core",
            "DirectX", "Windows SDK", "Windows Software Development Kit", "WebView2", "vcredist", "Microsoft Update Health"
        };

        public static bool IsRuntimePackage(string name) =>
            RuntimeMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

        /// <summary>%TEMP%, ProgramData/Roaming/Public kökü, İndirilenler: kalıcı program için olağan dışı.</summary>
        public static bool IsSuspiciousLocation(string? pathOrCommand)
        {
            if (string.IsNullOrWhiteSpace(pathOrCommand)) return false;
            string p = pathOrCommand.Replace("\"", string.Empty).Trim().Replace('/', '\\').ToLowerInvariant();
            if (p.Contains(@"\appdata\local\temp\") || p.Contains(@"\windows\temp\") || p.Contains(@"\downloads\") ||
                p.Contains(@"\users\public\"))
                return true;
            // Doğrudan kökte duran exe: "C:\ProgramData\x.exe", "...\AppData\Roaming\x.exe"
            string[] roots = { @":\programdata\", @"\appdata\roaming\" };
            foreach (string root in roots)
            {
                int i = p.IndexOf(root, StringComparison.Ordinal);
                if (i < 0) continue;
                string rest = p[(i + root.Length)..];
                int space = rest.IndexOf(' ');
                if (space >= 0) rest = rest[..space];
                if (!rest.Contains('\\') && rest.EndsWith(".exe", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsInboundAllow(string ruleData) =>
            ruleData.Contains("Action=Allow", StringComparison.OrdinalIgnoreCase) &&
            ruleData.Contains("Dir=In", StringComparison.OrdinalIgnoreCase);

        private static string FirewallSummary(string ruleData)
        {
            var parts = ruleData.Split('|');
            string? Get(string key)
            {
                string? part = parts.FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
                return part?.Substring(key.Length + 1);
            }
            string name = Get("Name") ?? "Adsız kural";
            string? app = Get("App");
            string? port = Get("LPort");
            return name + (app != null ? $" · {app}" : "") + (port != null ? $" · port {port}" : "");
        }

        private static string PathAdditions(string? before, string after)
        {
            var old = new HashSet<string>((before ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var added = after.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => !old.Contains(p)).ToList();
            return added.Count > 0 ? "Eklenen: " + string.Join("; ", added) : "Sıra ya da içerik değişti.";
        }

        private static string Shorten(string? text, int max = 160)
        {
            if (string.IsNullOrEmpty(text)) return "(boş)";
            return text.Length <= max ? text : text[..(max - 1)] + "…";
        }
    }
}
