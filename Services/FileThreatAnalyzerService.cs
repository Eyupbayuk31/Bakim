using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services.Safety;

namespace Bakım.Services
{
    public interface IFileThreatAnalyzerService
    {
        Task<ThreatAnalysisResult> AnalyzeFileAsync(string filePath, string? commandArgs = null, PersistenceItem? autorunItem = null);
        /// <summary>Süreci güvenlik kuralıyla sonlandırır (kritik sistem süreçleri reddedilir).</summary>
        Task<OperationResult> KillProcessAsync(int processId);

        /// <summary>
        /// Dosyayı Geri Dönüşüm Kutusu'na taşır; kullanımdaysa yeniden başlatmada silinmek üzere
        /// işaretler. Windows ve korumalı klasörlerdeki dosyalar reddedilir.
        /// </summary>
        Task<OperationResult> RemoveFileAsync(string filePath);

        /// <summary>
        /// Windows Defender ile ikinci görüş (ANL Faz 3): tehdit bulunursa puan en az 90 olur ve karar
        /// yeniden yazılır; Defender kapalıysa sonuç "alınamadı" olarak işaretlenir, puan değişmez.
        /// </summary>
        Task ApplyDefenderOpinionAsync(ThreatAnalysisResult result, System.Threading.CancellationToken ct = default);

        /// <summary>Dosyayı karantinaya alır (Etkinlik Merkezi'nden geri alınabilir).</summary>
        Task<Analyzer.QuarantineOutcome> QuarantineFileAsync(string filePath, string reason);
    }

    public class FileThreatAnalyzerService : IFileThreatAnalyzerService
    {
        private readonly IVirusTotalCheckService _virusTotalService;




        private readonly Analyzer.ISecondOpinionService? _secondOpinion;
        private readonly Analyzer.IQuarantineService? _quarantine;

        private Analyzer.ISecondOpinionService SecondOpinion => _secondOpinion ?? App.TryGetService<Analyzer.ISecondOpinionService>() ?? new Analyzer.SecondOpinionService();
        private Analyzer.IQuarantineService Quarantine => _quarantine ?? App.TryGetService<Analyzer.IQuarantineService>() ?? new Analyzer.QuarantineService();

        public FileThreatAnalyzerService(IVirusTotalCheckService virusTotalService, Analyzer.ISecondOpinionService? secondOpinion = null, Analyzer.IQuarantineService? quarantine = null)
            : this(virusTotalService)
        {
            _secondOpinion = secondOpinion;
            _quarantine = quarantine;
        }

        public FileThreatAnalyzerService(IVirusTotalCheckService virusTotalService)
        {
            _virusTotalService = virusTotalService;
        }

        public async Task<ThreatAnalysisResult> AnalyzeFileAsync(string filePath, string? commandArgs = null, PersistenceItem? autorunItem = null)
        {
            return await Task.Run(async () =>
            {
                var result = new ThreatAnalysisResult
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    OriginAutorunItem = autorunItem
                };

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    result.RiskScore = 0;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Dosya Bulunamadı (Yetim Kayıt)",
                        Description = "Hedef dosya disk üzerinde mevcut değil. Geride kalmış yetim bir başlangıç veya kalıcılık kaydı.",
                        Severity = ThreatSeverity.Info,
                        ScoreImpact = 0
                    });
                    result.Recommendation = "Bu başlangıç veya kalıcılık kaydını güvenle kaldırabilirsiniz.";
                    return result;
                }

                int score = 0;
                int vtFloor = 0;
                int secondOpinionFloor = 0;
                var fileInfo = new FileInfo(filePath);
                result.FileSizeBytes = fileInfo.Length;
                result.FileSizeFormatted = FormatBytes(fileInfo.Length);

                // 0. Calculate Hashes
                result.Sha256 = _virusTotalService.ComputeSha256(filePath);
                result.Md5 = ComputeFileMd5(filePath);
                result.Sha1 = ComputeFileSha1(filePath);

                // Check Active Running Processes
                CheckActiveRunningProcess(filePath, result);

                // =========================================================================
                // VEKTÖR 1: DİJİTAL İMZA & YAYINCI DOĞRULAMASI (%25)
                // =========================================================================
                // Gömülü İMZA + WINDOWS KATALOĞU denetimi.
                // Yalnızca gömülü imzaya bakmak, katalogla imzalanan Windows
                // bileşenlerini (svchost.exe, birçok sürücü) "imzasız" gösterip
                // meşru sistem dosyalarına haksız risk puanı yazıyordu.
                var signature = Helpers.SignatureInspector.Inspect(filePath);
                var sigStatus = signature.Status;
                bool codeLike = IsCodeLike(filePath);
                string signerName = signature.Signer;

                result.SignerName = signerName;
                result.IsCatalogSigned = signature.IsCatalogSigned;
                result.SignatureCatalogPath = signature.CatalogPath;
                result.CertificateExpiry = signature.CertificateExpiry;
                result.IsCertificateExpired = signature.IsCertificateExpired;

                if (sigStatus == SignatureStatus.Verified)
                {
                    result.IsSigned = true;
                    result.DigitalSignatureText = signature.Describe();

                    // Tam eşleşme (S-12): "AMD" artık "Hamdi Yazılım"ı, "Intel" "Intellisoft"u güvenilir yapmaz.
                    bool isTrustedVendor = Bakım.Core.Security.TrustedPublishers.IsTrusted(signerName);
                    if (isTrustedVendor)
                    {
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "Güvenilir Yayıncı İmzası",
                            Description = $"Dosya, tanınmış ve meşru bir üretici ({signerName}) tarafından dijital olarak imzalanmış.",
                            Severity = ThreatSeverity.Clean,
                            ScoreImpact = 0
                        });
                    }
                    else
                    {
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = signature.IsCatalogSigned
                                ? "Windows Kataloğu ile İmzalı"
                                : "Geçerli Dijital Sertifika",
                            Description = signature.IsCatalogSigned
                                ? $"Dosya bir Windows güvenlik kataloğu tarafından doğrulandı (imzacı: {signerName}). Sistem bileşenlerinde beklenen ve güvenli bir durumdur."
                                : $"Dosyanın Authenticode imzası geçerli. İmzacı: {signerName}",
                            Severity = ThreatSeverity.Clean,
                            ScoreImpact = 0
                        });
                    }

                    // Süresi dolmuş sertifika: imza zaman damgalıysa dosya hâlâ
                    // geçerlidir, bu yüzden risk puanı yazılmaz — yalnızca bilgi verilir.
                    if (signature.IsCertificateExpired)
                    {
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "Sertifika Süresi Dolmuş",
                            Description = $"İmzalama sertifikasının geçerliliği {signature.CertificateExpiry:dd.MM.yyyy} tarihinde sona ermiş. İmza zaman damgalıysa dosya yine de meşrudur.",
                            Severity = ThreatSeverity.Info,
                            ScoreImpact = 0
                        });
                    }
                }
                else if (sigStatus == SignatureStatus.InvalidOrTampered)
                {
                    score += 35;
                    result.IsSigned = false;
                    result.DigitalSignatureText = "Bozulmuş / Sahte Sertifika";
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Geçersiz / Bozulmuş Dijital İmza",
                        Description = "Dosyanın sertifikası doğrulanmadı! Dosya derlendikten sonra değiştirilmiş veya sahte sertifikayla imzalanmış olabilir.",
                        Severity = ThreatSeverity.Critical,
                        ScoreImpact = 35
                    });
                }
                else if (!codeLike)
                {
                    // Belge, görsel, metin: imzalanmaz ve kendi başına çalışmaz; imzasızlık risk değildir.
                    result.IsSigned = false;
                    result.DigitalSignatureText = "Uygulanmaz";
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Çalıştırılabilir Dosya Değil",
                        Description = "Dosya bir program, betik ya da kurulum paketi değil; dijital imza, konum ve VirusTotal kaydı kuralları uygulanmadı.",
                        Severity = ThreatSeverity.Info,
                        ScoreImpact = 0
                    });
                }
                else
                {
                    score += 25;
                    result.IsSigned = false;
                    result.DigitalSignatureText = "İmzasız";
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "İmzasız Yazılım",
                        Description = "Dosyanın geçerli bir dijital imzası bulunmuyor. Yayıncı kimliği doğrulanamadığı için kaynağı meçhul.",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 25
                    });
                }

                // =========================================================================
                // VEKTÖR 0: PE İKİLİSİ VE İSTİSMAR KALKANLARI
                // İmzadan SONRA çalışır: içe aktarım ve kalkan bulguları imzanın güvenine göre
                // değerlendirilir (ANL Z5).
                // =========================================================================
                ParsePeBinary(filePath, result, ref score);
                EvaluateImports(result, ref score);

                // =========================================================================
                // VEKTÖR 2: ÇALIŞMA KONUMU & SAHTE SİSTEM DOSYASI TAKLİDİ (%25)
                // =========================================================================
                string lowerPath = filePath.ToLowerInvariant();

                // Sistem dosyası taklidi (ANL Z3): kritik ad yasal dizini dışında mı? explorer.exe'nin yasal
                // yeri %windir%'dir; eski kural onu da "taklit" sayıp Windows'un kendi dosyasına +40 yazıyordu.
                if (Bakım.Core.Analyzer.SystemBinaryLocations.IsCriticalName(result.FileName))
                {
                    if (Bakım.Core.Analyzer.SystemBinaryLocations.IsImpersonation(filePath, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
                    {
                        score += 40;
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "Kritik Windows Sistem Dosyası Taklidi",
                            Description = $"Dosya '{result.FileName}' adını kullanıyor ancak meşru System32 dizini yerine '{Path.GetDirectoryName(filePath)}' konumunda! Bu doğrudan kötü niyetli bir Truva Atı (Trojan) göstergesidir.",
                            Severity = ThreatSeverity.Critical,
                            ScoreImpact = 40
                        });
                    }
                }

                // Temp Dirs (yalnızca kod: çalışmayacak bir dosyanın bulunduğu klasör risk değildir)
                if (codeLike && (lowerPath.Contains(@"\appdata\local\temp") || lowerPath.Contains(@"\windows\temp") || lowerPath.Contains(@"\temporary internet files\")))
                {
                    score += 25;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Şüpheli Çalışma Dizininde (Temp)",
                        Description = "Dosya geçici klasörden (Temp) çalışacak şekilde ayarlanmış. Virüsler ve dropper'lar sıklıkla buraya saklanır.",
                        Severity = ThreatSeverity.Critical,
                        ScoreImpact = 25
                    });
                }
                // Public or Roaming Root
                else if (codeLike && lowerPath.Contains(@"\users\public"))
                {
                    score += 20;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Genel / Paylaşılan Dizin (Public)",
                        Description = "Dosya, tüm kullanıcıların yazma yetkisine sahip olduğu 'Public' paylaşımlı dizininden çalışıyor.",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 20
                    });
                }
                else if (codeLike && lowerPath.Contains(@"\appdata\roaming\") && !lowerPath.Replace(@"\appdata\roaming\", "").Contains(@"\"))
                {
                    score += 15;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Doğrudan Roaming Kök Dizini",
                        Description = "Yazılım kendine ait bir üretici klasörü açmadan doğrudan Roaming kökünden çalışacak şekilde konumlanmış.",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 15
                    });
                }
                else if (lowerPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant()) ||
                         lowerPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant()))
                {
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Standart Kurulum Dizini",
                        Description = "Dosya güvenli Program Files dizininde konumlandırılmıştır.",
                        Severity = ThreatSeverity.Clean,
                        ScoreImpact = 0
                    });
                }

                // =========================================================================
                // VEKTÖR 3: KALICILIK VE GİZLİ PARAMETRELER (%15)
                // =========================================================================
                // Belirteç eşleşmesi (AN-22): eskiden "/s" her "/s…" ile, "-enc" "--encoding" ile eşleşiyordu.
                var argTokens = Bakım.Core.Analyzer.CommandLineResolver.Tokenize(commandArgs);
                if (Bakım.Core.Analyzer.ArgumentRules.HasHiddenWindow(argTokens) ||
                    Bakım.Core.Analyzer.ArgumentRules.HasEncodedCommand(argTokens) ||
                    Bakım.Core.Analyzer.ArgumentRules.HasNoProfileBypass(argTokens))
                {
                    score += 20;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Gizli Konsol / Şifreli Parametreler",
                        Description = $"Dosya başlangıçta konsol penceresini gizleyerek veya kodlanmış parametrelerle çalışacak şekilde yapılandırılmış: '{commandArgs}'",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 20
                    });
                }

                // Script host executions
                if (lowerPath.EndsWith(".vbs") || lowerPath.EndsWith(".hta") || lowerPath.EndsWith(".js") || lowerPath.EndsWith(".bat") || lowerPath.EndsWith(".ps1"))
                {
                    score += 15;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Betik Dosyası Tabanlı Başlangıç",
                        Description = "Başlangıç girdisi doğrudan bir betik dosyası (.vbs/.bat/.ps1/.hta) çalıştırmaktadır. Betikler güvenlik duvarlarını atlatmak için sıkça kullanılır.",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 15
                    });
                }

                // =========================================================================
                // VEKTÖR 3b: BETİK İÇERİĞİ (ANL Faz 3) — birleşim kuralları + AMSI
                // =========================================================================
                if (Bakım.Core.Analyzer.ScriptRules.ScriptExtensions.Contains(Path.GetExtension(filePath)) && fileInfo.Length <= 2L * 1024 * 1024)
                {
                    string? scriptText = null;
                    try
                    {
                        scriptText = File.ReadAllText(filePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AppLog.Debug($"Betik okunamadı: {filePath} — {ex.Message}", nameof(FileThreatAnalyzerService));
                    }
                    if (scriptText != null)
                    {
                        var scriptFindings = Bakım.Core.Analyzer.ScriptRules.Analyze(scriptText);
                        score += Bakım.Core.Analyzer.ScriptRules.TotalImpact(scriptFindings);
                        foreach (var f in scriptFindings)
                        {
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = f.Title,
                                Description = f.Detail,
                                Severity = f.Critical ? ThreatSeverity.Critical : f.Impact > 5 ? ThreatSeverity.Warning : ThreatSeverity.Info,
                                ScoreImpact = f.Impact
                            });
                        }

                        if (SecondOpinion.ScanContentWithAmsi(scriptText, result.FileName) == true)
                        {
                            secondOpinionFloor = 90;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "Antivirüs (AMSI) bu betiği zararlı buldu",
                                Description = "Betik içeriği sistemdeki antivirüsün AMSI arayüzüyle tarandı ve zararlı olarak işaretlendi.",
                                Severity = ThreatSeverity.Critical,
                                ScoreImpact = 50
                            });
                        }
                    }
                }

                // =========================================================================
                // VEKTÖR 4: METADATA, ÇİFT UZANTI & RLO (%15)
                // =========================================================================
                // Double extension check
                if (Regex.IsMatch(result.FileName, @"\.(pdf|docx|xlsx|jpg|png|mp4|zip|rar)\.(exe|scr|pif|bat|cmd|vbs)$", RegexOptions.IgnoreCase))
                {
                    score += 35;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Çift Uzantı Hilesi (Double Extension)",
                        Description = $"Dosya adında '{result.FileName}' sahte belge/resim uzantısı tespit edildi. Kullanıcıyı yanıltmaya çalışan tipik bir oltalama yöntemidir.",
                        Severity = ThreatSeverity.Critical,
                        ScoreImpact = 35
                    });
                }

                // RLO Unicode Trick (Right-to-Left Override \u202E)
                if (result.FileName.Contains('\u202E'))
                {
                    score += 40;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "Unicode RLO Karakter Tuzağı (U+202E)",
                        Description = "Dosya adında uzantıyı kullanıcıya ters gösteren gizli Unicode RLO karakteri bulundu! Kesinlikle zararlı bir gizlenme tekniğidir.",
                        Severity = ThreatSeverity.Critical,
                        ScoreImpact = 40
                    });
                }

                // Inspect PE FileVersionInfo
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(filePath);
                    result.CompanyName = vi.CompanyName ?? string.Empty;
                    result.ProductName = vi.ProductName ?? string.Empty;
                    result.FileVersion = vi.FileVersion ?? string.Empty;

                    bool isMetadataEmpty = string.IsNullOrWhiteSpace(vi.CompanyName) &&
                                           string.IsNullOrWhiteSpace(vi.ProductName) &&
                                           string.IsNullOrWhiteSpace(vi.FileDescription);

                    if (isMetadataEmpty && lowerPath.EndsWith(".exe"))
                    {
                        score += 15;
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "PE Metadata Bilgisi Eksik",
                            Description = "Dosyanın sürüm bilgilerinde şirket adı, ürün açıklaması ve telif hakkı tamamen boş. Amatör veya hızlı derlenmiş zararlılarda sıkça görülür.",
                            Severity = ThreatSeverity.Warning,
                            ScoreImpact = 15
                        });
                    }
                }
                catch { }

                // =========================================================================
                // VEKTÖR 5: SHANNON ENTROPİ ANALİZİ (PACKER / CRYPTER TESPİTİ) (%10)
                // =========================================================================
                try
                {
                    double entropy = CalculateShannonEntropy(filePath);
                    result.EntropyScore = Math.Round(entropy, 2);

                    if (entropy >= 7.3)
                    {
                        score += 20;
                        result.EntropyText = $"Yüksek Entropi ({result.EntropyScore:F2} / 8.0) - Paketlenmiş / Şifreli";
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "Yüksek Bilgi Entropisi (Packer / Crypter)",
                            Description = $"Dosyanın entropi değeri {result.EntropyScore:F2} / 8.0. Bu yüksek rastlantısallık, kodların UPX, Themida, VMProtect veya özel bir Crypter ile gizlendiğine işaret eder.",
                            Severity = ThreatSeverity.Warning,
                            ScoreImpact = 20
                        });
                    }
                    else
                    {
                        result.EntropyText = $"Normal Entropi ({result.EntropyScore:F2} / 8.0) - Standart Kod";
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "Standart Kod Entropisi",
                            Description = $"Bayt dağılımı ({result.EntropyScore:F2} / 8.0) normal derlenmiş bir ikili dosyayla uyumludur.",
                            Severity = ThreatSeverity.Clean,
                            ScoreImpact = 0
                        });
                    }
                }
                catch { }

                // =========================================================================
                // VEKTÖR 6: MARK OF THE WEB (ZONE.IDENTIFIER ADS) (%5)
                // =========================================================================
                try
                {
                    string zoneFile = filePath + ":Zone.Identifier";
                    if (File.Exists(zoneFile))
                    {
                        string zoneContent = File.ReadAllText(zoneFile);
                        result.HasMarkOfTheWeb = true;

                        score += 10;
                        result.Factors.Add(new ThreatFactor
                        {
                            Title = "İnternetten İndirilmiş (Mark of the Web)",
                            Description = "Dosya internet tarayıcısı veya e-posta yoluyla indirilmiş ve doğrudan sisteme yerleştirilmiştir (NTFS Zone.Identifier akışı doğrulandı).",
                            Severity = ThreatSeverity.Warning,
                            ScoreImpact = 10
                        });
                    }
                }
                catch { }

                // =========================================================================
                // VEKTÖR 7: VIRUSTOTAL HİBRİT İSTİHBARATI (%20)
                // =========================================================================
                if (!string.IsNullOrWhiteSpace(result.Sha256))
                {
                    try
                    {
                        var (malicious, total, msg) = await _virusTotalService.CheckHashAsync(result.Sha256);
                        result.VirusTotalMalicious = malicious;
                        result.VirusTotalTotal = total;
                        result.VirusTotalSummary = msg;

                        // VT uzlaşısı baskın kanıttır (ANL Z4): imza ya da "temiz" işaretler bunu düşüremez.
                        var vt = Bakım.Core.Analyzer.VirusTotalRules.Evaluate(Math.Max(0, malicious), _virusTotalService.LastSuspicious(result.Sha256), total);
                        if (vt is { } floor)
                        {
                            score += floor.Impact;
                            vtFloor = floor.MinimumScore;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = floor.Title,
                                Description = $"{total} antivirüs motorundan {malicious} tanesi bu dosyayı zararlı olarak etiketledi." +
                                              (floor.Critical ? " Bu, dosya imzalı olsa bile güçlü bir kanıttır (çalıntı sertifikayla imzalanmış zararlılar vardır)." : " Az sayıda tespit yanlış pozitif olabilir."),
                                Severity = floor.Critical ? ThreatSeverity.Critical : ThreatSeverity.Warning,
                                ScoreImpact = floor.Impact
                            });
                        }
                        else if (total > 0 && malicious == 0)
                        {
                            // Bonus discount for clean VT report
                            score = Math.Max(0, score - 10);
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "VirusTotal Taraması Temiz",
                                Description = $"{total} güvenlik motorunun hiçbirinde tehdit kaydı bulunamadı.",
                                Severity = ThreatSeverity.Clean,
                                ScoreImpact = 0
                            });
                        }
                        else if (codeLike && total == 0 && (msg.Contains("VT Kaydı Yok") || msg.Contains("Bulunamadı")))
                        {
                            score += 10;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "VirusTotal Veritabanında Kaydı Yok (Nadir / İlk Kez Görülen Dosya)",
                                Description = "Bu dosya dünyada daha önce VirusTotal'e hiç gönderilmemiş veya taranmamış. Meşru yazılımların büyük çoğunluğu küresel VT veritabanında yer alır. Kaydın olmaması, dosyanın çok yeni derlendiğini, nadir olduğunu veya özel üretilmiş bir zararlı/dropper olabileceğine işaret eder.",
                                Severity = ThreatSeverity.Warning,
                                ScoreImpact = 10
                            });
                        }
                    }
                    catch { }
                }

                // Son puan: VT ve antivirüs (AMSI) tabanı toplamın altına düşemez.
                result.RiskScore = Math.Clamp(Math.Max(score, Math.Max(vtFloor, secondOpinionFloor)), 0, 100);
                ApplyRecommendation(result);
                return result;
            });
        }

        /// <summary>
        /// Öneri metni kanıtla orantılı (ANL Z3/Z10): "derhal kalıcı olarak silin" gibi geri alınamaz
        /// öneriler verilmez; önce kaynağın doğrulanması ve geri alınabilir adımlar (karantina).
        /// </summary>
        private static void ApplyRecommendation(ThreatAnalysisResult result)
        {

                if (result.RiskScore >= Bakım.Core.Risk.RiskScale.Suspicious)
                {
                    result.Recommendation = "YÜKSEK RİSK: Dosyada zararlı yazılımlarda sık görülen birden çok güçlü işaret var. Kaynağını tanımıyorsanız süreci durdurun, başlangıç girdisini kapatın ve dosyayı karantinaya alın (geri alınabilir); emin değilseniz önce VirusTotal ve Windows Defender ile ikinci görüş alın.";
                }
                else if (result.RiskScore >= Bakım.Core.Risk.RiskScale.Caution)
                {
                    result.Recommendation = "DİKKAT: Dosyada gözden geçirilmesi gereken işaretler var (imzasız kod, alışılmadık konum ya da parametreler). Kaynağını biliyorsanız sorun olmayabilir; bilmiyorsanız başlangıç girdisini kapatıp inceleyin.";
                }
                else
                {
                    result.Recommendation = "TEMİZ GÖRÜNÜYOR: Belirgin bir tehdit işareti bulunmadı. Bu sezgisel bir değerlendirmedir; dosya içeriği bir antivirüs gibi taranmadı.";
                }
        }

        public async Task ApplyDefenderOpinionAsync(ThreatAnalysisResult result, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(result.FilePath) || !File.Exists(result.FilePath)) return;
            var scan = await SecondOpinion.ScanFileWithDefenderAsync(result.FilePath, ct);
            result.DefenderState = scan.State;
            result.DefenderSummary = scan.State == Bakım.Core.Analyzer.DefenderScanState.ThreatFound
                ? $"Tehdit: {string.Join(", ", scan.Threats)}"
                : scan.Message;

            if (scan.State == Bakım.Core.Analyzer.DefenderScanState.ThreatFound)
            {
                result.Factors.Insert(0, new ThreatFactor
                {
                    Title = "Windows Defender tehdit buldu",
                    Description = $"Windows Defender bu dosyayı zararlı olarak tanımladı: {string.Join(", ", scan.Threats)}.",
                    Severity = ThreatSeverity.Critical,
                    ScoreImpact = 60
                });
                result.RiskScore = Math.Max(result.RiskScore, 90);
            }
            else if (scan.State == Bakım.Core.Analyzer.DefenderScanState.Clean)
            {
                result.Factors.Add(new ThreatFactor
                {
                    Title = "Windows Defender tehdit bulmadı",
                    Description = "Dosya Windows Defender ile tarandı; bilinen bir tehdit imzası yok. Yeni ya da hedefli zararlılar imza tabanlı taramadan kaçabilir.",
                    Severity = ThreatSeverity.Clean,
                    ScoreImpact = 0
                });
            }
            ApplyRecommendation(result);
        }

        public Task<Analyzer.QuarantineOutcome> QuarantineFileAsync(string filePath, string reason) => Quarantine.QuarantineAsync(filePath, reason);

        /// <summary>
        /// İçe aktarım bulguları: tek tek API değil, birleşimler (ANL Z5). Eskiden GetKeyState ve
        /// GetClipboardData "keylogger", tek başına VirtualAllocEx "kritik enjeksiyon" sayılıyordu;
        /// Notepad++, OBS ve explorer.exe yanlış alarm alıyordu.
        /// </summary>
        private static void EvaluateImports(ThreatAnalysisResult result, ref int score)
        {
            var names = result.ImportedDlls.SelectMany(g => g.Functions).Select(f => f.Name);
            foreach (var finding in Bakım.Core.Analyzer.ImportRules.Evaluate(names, result.IsSigned))
            {
                score += finding.Impact;
                result.Factors.Add(new ThreatFactor
                {
                    Title = finding.Title,
                    Description = finding.Detail,
                    Severity = finding.Critical ? ThreatSeverity.Critical : finding.Impact > 0 ? ThreatSeverity.Warning : ThreatSeverity.Info,
                    ScoreImpact = finding.Impact
                });
            }
        }

        private static double CalculateShannonEntropy(string filePath)
        {
            const int sampleSize = 2 * 1024 * 1024; // Sample up to 2MB for fast non-blocking evaluation
            byte[] buffer;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int bytesToRead = (int)Math.Min(stream.Length, sampleSize);
                buffer = new byte[bytesToRead];
                int read = stream.Read(buffer, 0, bytesToRead);
                if (read < bytesToRead)
                {
                    Array.Resize(ref buffer, read);
                }
            }

            if (buffer.Length == 0) return 0.0;

            int[] map = new int[256];
            for (int i = 0; i < buffer.Length; i++)
            {
                map[buffer[i]]++;
            }

            double entropy = 0.0;
            double len = buffer.Length;
            for (int i = 0; i < 256; i++)
            {
                if (map[i] > 0)
                {
                    double p = map[i] / len;
                    entropy -= p * Math.Log2(p);
                }
            }

            return entropy;
        }

        private static void CheckActiveRunningProcess(string filePath, ThreatAnalysisResult result)
        {
            try
            {
                string targetPath = Path.GetFullPath(filePath);
                var processes = Process.GetProcesses();
                try
                {
                    foreach (var proc in processes)
                    {
                        // MainModule yerine sınırlı sorgu: korumalı süreçlerde istisna atmaz, daha hızlı.
                        string? procPath = Helpers.NativeProcess.TryGetImagePath(proc.Id);
                        if (!string.IsNullOrWhiteSpace(procPath) && procPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsActiveProcess = true;
                            result.ActiveProcessId = proc.Id;
                            try { result.ActiveProcessMemory = FormatBytes(proc.WorkingSet64); } catch { }
                            return;
                        }
                    }
                }
                finally
                {
                    foreach (var proc in processes) proc.Dispose();
                }
            }
            catch { }
        }

        public Task<OperationResult> KillProcessAsync(int processId)
        {
            var safeProcess = App.TryGetService<ISafeProcessService>() ?? new SafeProcessService(AppLog.Current);
            return safeProcess.TerminateProcessAsync(processId);
        }

        /// <summary>
        /// Eskiden: öznitelikler sıfırlanıyor, ana modülü eşleşen HER süreç (svchost dahil)
        /// öldürülüyor, dosya kalıcı siliniyor, olmazsa korumasız MoveFileEx ile işaretleniyordu.
        /// Artık PathSafetyGuard'dan geçer, süreç öldürmez, Geri Dönüşüm Kutusu'nu kullanır.
        /// </summary>
        public async Task<OperationResult> RemoveFileAsync(string filePath)
        {
            var safeDelete = App.TryGetService<ISafeDeleteService>() ?? new SafeDeleteService(AppLog.Current);
            var result = await safeDelete.DeletePathAsync(filePath, isDirectory: false,
                new DeletePolicy(Permanent: false, AllowOutsideKnownRoots: true));

            if (result.Outcome is DeleteOutcome.InUse or DeleteOutcome.AccessDenied)
            {
                var scheduled = safeDelete.ScheduleDeleteOnReboot(filePath, isDirectory: false);
                if (scheduled.Succeeded) return scheduled;
            }
            return result;
        }

        /// <summary>Kod benzeri mi (PE başlığı, betik, kurulum paketi…)? Okunamazsa güvenli taraf: kod say.</summary>
        private static bool IsCodeLike(string filePath)
        {
            Span<byte> header = stackalloc byte[2];
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int read = stream.Read(header);
                return Bakım.Core.Analyzer.ExecutableContent.IsCodeLike(Path.GetFileName(filePath), header[..read]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Dosya başlığı okunamadı, kod varsayıldı: {ex.Message}", nameof(FileThreatAnalyzerService));
                return true;
            }
        }

        private static string ComputeFileMd5(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var md5 = System.Security.Cryptography.MD5.Create();
                byte[] hash = md5.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ComputeFileSha1(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                byte[] hash = sha1.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static readonly Dictionary<string, (string Category, string Description)> SuspiciousApiCatalog = new(StringComparer.OrdinalIgnoreCase)
        {
            // Process Injection & Memory Corruption
            { "VirtualAllocEx", ("Bellek Enjeksiyonu", "Hedef sürecin bellek alanında dinamik bellek ayırır (Process Hollowing / Shellcode enjeksiyonu göstergesi).") },
            { "WriteProcessMemory", ("Bellek Enjeksiyonu", "Başka bir sürecin bellek sayfalarına kod/veri yazar.") },
            { "CreateRemoteThread", ("Process Hollowing", "Uzak süreç içinde yabancı thread başlatarak enjekte edilen kodu çalıştırır.") },
            { "RtlCreateUserThread", ("Process Hollowing", "Düşük seviyeli native API ile uzak süreçte gizli thread başlatır.") },
            { "NtUnmapViewOfSection", ("Process Hollowing", "Meşru bir sürecin kod bölümünü bellekten unmap edip zararlı payload ile doldurur.") },
            { "ZwUnmapViewOfSection", ("Process Hollowing", "Kernel düzeyinde süreç belleği boşaltma rutini.") },
            { "QueueUserAPC", ("Bellek Enjeksiyonu", "Uzak thread APC kuyruğuna shellcode yerleştirir (Early Bird APC Enjeksiyonu).") },
            { "SetThreadContext", ("Bellek Enjeksiyonu", "Thread CPU yazmaçlarını (RIP/EIP) değiştirerek kod akışını kaçırır.") },
            { "VirtualProtectEx", ("Bellek Enjeksiyonu", "Hedef sürecin bellek sayfalarını çalıştırılabilir (PAGE_EXECUTE_READWRITE) hale getirir.") },

            // Process & Token Manipulation
            { "AdjustTokenPrivileges", ("Süreç / Token Manipülasyonu", "SeDebugPrivilege gibi kritik sistem ayrıcalıklarını etkinleştirir.") },
            { "DuplicateTokenEx", ("Süreç / Token Manipülasyonu", "Sistem belirteçlerini çoğaltarak yetki yükseltir.") },
            { "ImpersonateLoggedOnUser", ("Süreç / Token Manipülasyonu", "Oturum açmış kullanıcının kimliğine bürünür.") },

            // Spyware, Keylogging & Screen Capture
            { "SetWindowsHookEx", ("Klavye / Casusluk", "Küresel Windows kancası kurarak tüm klavye/fare hareketlerini yakalar.") },
            { "SetWindowsHookExA", ("Klavye / Casusluk", "Küresel Windows kancası kurarak tuş basımlarını yakalar.") },
            { "SetWindowsHookExW", ("Klavye / Casusluk", "Küresel Windows kancası kurarak tuş basımlarını yakalar.") },
            { "GetAsyncKeyState", ("Klavye / Casusluk", "Arka planda basılan klavye tuşlarını gizlice dinler (Keylogger).") },

            // Anti-Debugging & Evasion
            { "IsDebuggerPresent", ("Anti-Debug / Evasion", "Sürecin bir hata ayıklayıcı (debugger) altında çalışıp çalışmadığını denetler.") },
            { "CheckRemoteDebuggerPresent", ("Anti-Debug / Evasion", "Uzak bir debugger varlığını sorgulayarak analizi engeller.") },
            { "NtQueryInformationProcess", ("Anti-Debug / Evasion", "ProcessDebugPort veya ProcessDebugFlags ile gelişmiş debugger tespiti yapar.") },
            { "ZwQueryInformationProcess", ("Anti-Debug / Evasion", "Kernel seviyesinde süreç hata ayıklama bilgilerini sorgular.") },
            { "NtSetInformationThread", ("Anti-Debug / Evasion", "ThreadHideFromDebugger kullanarak iş parçacığını hata ayıklayıcılardan gizler.") },
            { "ZwSetInformationThread", ("Anti-Debug / Evasion", "Kernel çağrısıyla iş parçacığını debugger'lardan gizler.") },
            { "OutputDebugStringA", ("Anti-Debug / Evasion", "Hata ayıklayıcı varlığını tespit etmek için debug çıktısı gönderir.") },
            { "OutputDebugStringW", ("Anti-Debug / Evasion", "Hata ayıklayıcı varlığını sorgulamak için debug çıktısı gönderir.") },
            { "FindWindowA", ("Anti-Analiz / Pencere", "Cheat Engine, x64dbg, IDA Pro gibi analiz araçlarının pencere başlıklarını arar.") },
            { "FindWindowW", ("Anti-Analiz / Pencere", "Tersine mühendislik araçlarının pencere başlıklarını tarar.") },

            // Credential Access & Memory Dumping
            { "MiniDumpWriteDump", ("Kimlik Hırsızlığı / LSASS", "Hedef sürecin (özellikle lsass.exe) tam bellek dökümünü diske yazar (parola hırsızlığı).") },
            { "OpenProcessToken", ("Süreç / Token Manipülasyonu", "Süreç erişim belirtecini açarak ayrıcalık yükseltme hazırlar.") },

            // Kernel Driver Loading & Rootkit (BYOVD)
            { "NtLoadDriver", ("Kernel Sürücü / BYOVD", "Doğrudan Windows çekirdeğine (Ring 0) sürücü yükler; rootkit ve koruma bypass tekniğidir.") },
            { "ZwLoadDriver", ("Kernel Sürücü / BYOVD", "Kernel düzeyinde izinsiz veya savunmasız sürücü yükleme çağrısıdır.") },
            { "CreateServiceA", ("Kalıcılık / Registry", "Yeni bir Windows servisi veya çekirdek sürücü servisi tanımlar.") },
            { "CreateServiceW", ("Kalıcılık / Registry", "Yeni bir Windows servisi veya çekirdek sürücü servisi kaydeder.") },
            { "StartServiceA", ("Kalıcılık / Registry", "Kayıtlı bir servis veya kernel sürücüsünü başlatır.") },
            { "StartServiceW", ("Kalıcılık / Registry", "Kayıtlı bir servis veya kernel sürücüsünü başlatır.") },
            { "DeviceIoControl", ("Kernel Sürücü / BYOVD", "Kernel sürücüleriyle doğrudan haberleşerek bellek ve donanım manipülasyonu yapar.") },

            // C2 & Network Communication
            { "InternetOpenUrlA", ("Ağ / C2 İletişimi", "Uzak C2 sunucusuna HTTP/HTTPS bağlantısı açar.") },
            { "InternetOpenUrlW", ("Ağ / C2 İletişimi", "Uzak C2 sunucusuna HTTP/HTTPS bağlantısı açar.") },
            { "URLDownloadToFileA", ("Ağ / C2 İletişimi", "İnternetten gizlice ikincil zararlı dosya indirir.") },
            { "URLDownloadToFileW", ("Ağ / C2 İletişimi", "İnternetten gizlice ikincil zararlı dosya indirir.") },

            // Persistence & Registry Tampering
        };

        private static void ParsePeBinary(string filePath, ThreatAnalysisResult result, ref int score)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 64) return;

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

                // 1. DOS Header
                ushort e_magic = reader.ReadUInt16();
                if (e_magic != 0x5A4D) return; // 'MZ' signature

                stream.Position = 0x3C;
                int e_lfanew = reader.ReadInt32();
                if (e_lfanew <= 0 || e_lfanew + 24 > stream.Length) return;

                // 2. NT Signature
                stream.Position = e_lfanew;
                uint ntSignature = reader.ReadUInt32();
                if (ntSignature != 0x00004550) return; // 'PE\0\0'

                // 3. File Header (20 bytes)
                ushort machine = reader.ReadUInt16();
                ushort numberOfSections = reader.ReadUInt16();

                // GÜVENLİK: Bu ayrıştırıcı GÜVENİLMEYEN dosyaları okur.
                // PE spesifikasyonu en fazla 96 bölüme izin verir; uydurma bir
                // NumberOfSections (ör. 65535) döngüyü gereksiz yere şişirir ve
                // bellek tüketir. Sınırın üstü bozuk kabul edilir.
                const int MaxPeSections = 96;
                if (numberOfSections > MaxPeSections)
                {
                    AppLog.Warning(
                        $"PE bölüm sayısı makul sınırın üstünde ({numberOfSections}); dosya bozuk veya kasıtlı hatalı: {filePath}",
                        null, nameof(FileThreatAnalyzerService));
                    return;
                }
                uint timeDateStamp = reader.ReadUInt32();
                reader.ReadUInt32(); // PointerToSymbolTable
                reader.ReadUInt32(); // NumberOfSymbols
                ushort sizeOfOptionalHeader = reader.ReadUInt16();
                ushort characteristics = reader.ReadUInt16();

                bool is64Bit = machine == 0x8664 || machine == 0xAA64;
                string archName = machine switch
                {
                    0x8664 => "x64 (AMD64)",
                    0x014C => "x86 (i386)",
                    0xAA64 => "ARM64",
                    0x01C0 => "ARM",
                    _ => $"0x{machine:X4}"
                };

                string compileTimeStr;
                try
                {
                    compileTimeStr = DateTimeOffset.FromUnixTimeSeconds(timeDateStamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                }
                catch
                {
                    compileTimeStr = "Geçersiz Zaman Damgası";
                }

                var peHeader = new PeHeaderInfo
                {
                    IsPeFile = true,
                    MachineArchitecture = archName,
                    SectionCount = numberOfSections,
                    CompileTimeUtc = compileTimeStr,
                    Is64Bit = is64Bit
                };

                var mitigations = new ExploitMitigationMatrix();
                uint entryPointRva = 0;
                ulong imageBase = 0;
                ushort subsystem = 0;
                ushort dllCharacteristics = 0;
                uint importDirRva = 0;
                uint importDirSize = 0;
                uint resourceDirRva = 0;
                uint resourceDirSize = 0;
                uint certDirOffset = 0;
                uint certDirSize = 0;
                uint tlsDirRva = 0;
                uint tlsDirSize = 0;
                bool isDotNet = false;

                // Rich Header & Compiler Fingerprint (DOS Stub ile e_lfanew arasında)
                if (e_lfanew > 0x40 && e_lfanew <= stream.Length)
                {
                    long currentPos = stream.Position;
                    try
                    {
                        stream.Position = 0x40;
                        int stubLen = Math.Min(e_lfanew - 0x40, 2048);
                        byte[] stub = reader.ReadBytes(stubLen);
                        int richIdx = -1;
                        for (int j = 0; j <= stub.Length - 8; j++)
                        {
                            if (stub[j] == 0x52 && stub[j + 1] == 0x69 && stub[j + 2] == 0x63 && stub[j + 3] == 0x68) // "Rich"
                            {
                                richIdx = j;
                                break;
                            }
                        }

                        if (richIdx >= 0)
                        {
                            peHeader.HasRichHeader = true;
                            uint xorKey = BitConverter.ToUInt32(stub, richIdx + 4);
                            uint dansEncrypted = 0x536E6144 ^ xorKey; // "DanS" little-endian
                            int dansIdx = -1;
                            for (int j = richIdx - 4; j >= 0; j -= 4)
                            {
                                if (BitConverter.ToUInt32(stub, j) == dansEncrypted)
                                {
                                    dansIdx = j;
                                    break;
                                }
                            }

                            peHeader.CompilerFingerprint = dansIdx >= 0
                                ? "Microsoft Visual Studio (MSVC Rich Header Doğrulandı)"
                                : "MSVC (Rich Yapısı Mevcut)";
                        }
                    }
                    catch { }
                    finally
                    {
                        stream.Position = currentPos;
                    }
                }

                // 4. Optional Header
                if (sizeOfOptionalHeader > 0)
                {
                    long optionalHeaderStart = stream.Position;
                    ushort magic = reader.ReadUInt16(); // 0x10B = PE32, 0x20B = PE32+
                    peHeader.Is64Bit = magic == 0x20B;

                    reader.ReadByte(); // MajorLinkerVersion
                    reader.ReadByte(); // MinorLinkerVersion
                    reader.ReadUInt32(); // SizeOfCode
                    reader.ReadUInt32(); // SizeOfInitializedData
                    reader.ReadUInt32(); // SizeOfUninitializedData
                    entryPointRva = reader.ReadUInt32();
                    reader.ReadUInt32(); // BaseOfCode

                    if (magic == 0x10B) // PE32 (32-bit)
                    {
                        reader.ReadUInt32(); // BaseOfData
                        imageBase = reader.ReadUInt32();
                        reader.ReadUInt32(); // SectionAlignment
                        reader.ReadUInt32(); // FileAlignment
                        // HATA DÜZELTMESİ: Sürüm bloğu 12 BAYTTIR, 16 değil.
                        // MajorOS+MinorOS+MajorImage+MinorImage+MajorSubsystem+MinorSubsystem
                        // = 6 x ushort = 12 bayt. 16 atlamak Subsystem ve
                        // DllCharacteristics alanlarını 4 bayt kaydırıyor ve
                        // tüm exploit kalkanları (ASLR/DEP/CFG) yanlış okunuyordu —
                        // notepad.exe bile "0/5 kalkan" görünüyordu.
                        stream.Position += 12; // OS, Image, Subsystem versions (6 x ushort)
                        reader.ReadUInt32(); // Win32VersionValue
                        reader.ReadUInt32(); // SizeOfImage
                        reader.ReadUInt32(); // SizeOfHeaders
                        reader.ReadUInt32(); // CheckSum
                        subsystem = reader.ReadUInt16();
                        dllCharacteristics = reader.ReadUInt16();
                        stream.Position += 16; // SizeOfStack/Heap Reserve/Commit (32-bit: 4 * 4 = 16 bytes)
                        reader.ReadUInt32(); // LoaderFlags
                        uint numRva = reader.ReadUInt32();

                        // Data Directories
                        for (int d = 0; d < numRva && d < 16; d++)
                        {
                            uint va = reader.ReadUInt32();
                            uint sz = reader.ReadUInt32();
                            if (d == 1) { importDirRva = va; importDirSize = sz; }
                            if (d == 2) { resourceDirRva = va; resourceDirSize = sz; }
                            if (d == 4) { certDirOffset = va; certDirSize = sz; } // Certificate Table (raw file offset)
                            if (d == 9) { tlsDirRva = va; tlsDirSize = sz; }
                            if (d == 14 && va > 0) { isDotNet = true; } // CLR Runtime Header
                        }
                    }
                    else if (magic == 0x20B) // PE32+ (64-bit)
                    {
                        imageBase = reader.ReadUInt64();
                        reader.ReadUInt32(); // SectionAlignment
                        reader.ReadUInt32(); // FileAlignment
                        // HATA DÜZELTMESİ: Sürüm bloğu 12 BAYTTIR, 16 değil.
                        // MajorOS+MinorOS+MajorImage+MinorImage+MajorSubsystem+MinorSubsystem
                        // = 6 x ushort = 12 bayt. 16 atlamak Subsystem ve
                        // DllCharacteristics alanlarını 4 bayt kaydırıyor ve
                        // tüm exploit kalkanları (ASLR/DEP/CFG) yanlış okunuyordu —
                        // notepad.exe bile "0/5 kalkan" görünüyordu.
                        stream.Position += 12; // OS, Image, Subsystem versions (6 x ushort)
                        reader.ReadUInt32(); // Win32VersionValue
                        reader.ReadUInt32(); // SizeOfImage
                        reader.ReadUInt32(); // SizeOfHeaders
                        reader.ReadUInt32(); // CheckSum
                        subsystem = reader.ReadUInt16();
                        dllCharacteristics = reader.ReadUInt16();
                        stream.Position += 32; // SizeOfStack/Heap Reserve/Commit (64-bit: 4 * 8 = 32 bytes)
                        reader.ReadUInt32(); // LoaderFlags
                        uint numRva = reader.ReadUInt32();

                        // Data Directories
                        for (int d = 0; d < numRva && d < 16; d++)
                        {
                            uint va = reader.ReadUInt32();
                            uint sz = reader.ReadUInt32();
                            if (d == 1) { importDirRva = va; importDirSize = sz; }
                            if (d == 2) { resourceDirRva = va; resourceDirSize = sz; }
                            if (d == 4) { certDirOffset = va; certDirSize = sz; } // Certificate Table (raw file offset)
                            if (d == 9) { tlsDirRva = va; tlsDirSize = sz; }
                            if (d == 14 && va > 0) { isDotNet = true; } // CLR Runtime Header
                        }
                    }

                    stream.Position = optionalHeaderStart + sizeOfOptionalHeader;
                }

                if (!peHeader.HasRichHeader)
                {
                    peHeader.CompilerFingerprint = isDotNet
                        ? ".NET Derleyicisi (Roslyn/CLR)"
                        : "MinGW / GCC veya Özel Derleyici (Rich Header Yok)";
                }

                peHeader.EntryPointRva = entryPointRva;
                peHeader.ImageBase = imageBase;
                peHeader.Subsystem = subsystem switch
                {
                    1 => "Native Sürücü",
                    2 => "Windows GUI (Arayüz)",
                    3 => "Windows CUI (Konsol)",
                    7 => "POSIX CUI",
                    9 => "Windows CE GUI",
                    10 => "EFI Uygulaması",
                    _ => $"Diğer ({subsystem})"
                };

                // Parse Exploit Mitigations
                mitigations.HasHighEntropyVa = (dllCharacteristics & 0x0020) != 0;
                mitigations.HasAslr = (dllCharacteristics & 0x0040) != 0;
                mitigations.HasDep = (dllCharacteristics & 0x0100) != 0;
                mitigations.HasSafeSeh = (dllCharacteristics & 0x0400) != 0;
                mitigations.HasCfg = (dllCharacteristics & 0x4000) != 0;
                mitigations.IsDotNet = isDotNet;

                result.PeHeader = peHeader;
                result.Mitigations = mitigations;

                // Mitigation Risk Scoring
                // Eksik kalkan bir güvenlik açığı işaretidir, zararlılık kanıtı değil: imzalı dosyada bilgi.
                int mitigationImpact = result.IsSigned ? 0 : 5;
                if (!mitigations.HasAslr && !isDotNet)
                {
                    score += mitigationImpact;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "ASLR Kalkanı Devre Dışı",
                        Description = "Dosya bellek adresi rastgeleleştirme (ASLR) olmadan derlenmiş. Bellek taşması açıklarına karşı daha savunmasızdır; tek başına zararlılık işareti değildir.",
                        Severity = mitigationImpact > 0 ? ThreatSeverity.Warning : ThreatSeverity.Info,
                        ScoreImpact = mitigationImpact
                    });
                }

                if (!mitigations.HasDep && !isDotNet)
                {
                    score += mitigationImpact;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "DEP / NX Kalkanı Devre Dışı",
                        Description = "Veri alanında kod yürütme engeli (DEP) etkin değil. Eski derleyicilerle üretilen meşru yazılımlarda da görülür.",
                        Severity = mitigationImpact > 0 ? ThreatSeverity.Warning : ThreatSeverity.Info,
                        ScoreImpact = mitigationImpact
                    });
                }

                // 5. Section Headers & Section Entropies
                var rawSectionTuples = new List<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)>();
                var sectionsList = new List<PeSectionItem>();

                bool foundSuspiciousPacker = false;
                bool foundWxSection = false;

                for (int i = 0; i < numberOfSections; i++)
                {
                    // Bölüm başlığı 40 bayttır; dosya biterse sessizce dur
                    if (stream.Position + 40 > stream.Length) break;

                    byte[] nameBytes = reader.ReadBytes(8);
                    if (nameBytes.Length < 8) break;
                    string rawName = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0', ' ');

                    uint secVirtualSize = reader.ReadUInt32();
                    uint secVirtualAddress = reader.ReadUInt32();
                    uint secRawSize = reader.ReadUInt32();
                    uint secRawPointer = reader.ReadUInt32();

                    reader.ReadUInt32(); // PointerToRelocations
                    reader.ReadUInt32(); // PointerToLinenumbers
                    reader.ReadUInt16(); // NumberOfRelocations
                    reader.ReadUInt16(); // NumberOfLinenumbers
                    uint secCharacteristics = reader.ReadUInt32();

                    bool isExec = (secCharacteristics & 0x20000000) != 0;
                    bool isRead = (secCharacteristics & 0x40000000) != 0;
                    bool isWrite = (secCharacteristics & 0x80000000) != 0;

                    rawSectionTuples.Add((secVirtualAddress, secVirtualSize, secRawPointer, secRawSize));

                    // Calculate section entropy
                    double secEntropy = 0.0;
                    if (secRawPointer > 0 && secRawSize > 0 && secRawPointer + secRawSize <= stream.Length)
                    {
                        long savedPos = stream.Position;
                        try
                        {
                            stream.Position = secRawPointer;
                            int readLen = (int)Math.Min(secRawSize, 2 * 1024 * 1024);
                            byte[] secBuf = reader.ReadBytes(readLen);
                            secEntropy = CalculateBufferEntropy(secBuf);
                        }
                        catch { }
                        finally
                        {
                            stream.Position = savedPos;
                        }
                    }

                    // Packer check
                    string lowerSec = rawName.ToLowerInvariant();
                    bool isPackerName = lowerSec.Contains("upx") || lowerSec.Contains("themida") || lowerSec.Contains("vmp") ||
                                       lowerSec.Contains("aspack") || lowerSec.Contains("pecompact") || lowerSec.Contains("nspack");
                    bool isHighEntropyCode = isExec && secEntropy >= 7.3;

                    bool isSuspiciousSec = isPackerName || isHighEntropyCode;
                    if (isSuspiciousSec) foundSuspiciousPacker = true;

                    if (isExec && isWrite) foundWxSection = true;

                    sectionsList.Add(new PeSectionItem
                    {
                        Name = string.IsNullOrWhiteSpace(rawName) ? $"Section_{i}" : rawName,
                        VirtualAddress = secVirtualAddress,
                        VirtualSize = secVirtualSize,
                        RawSize = secRawSize,
                        Entropy = secEntropy,
                        IsExecutable = isExec,
                        IsWritable = isWrite,
                        IsSuspiciousPacker = isSuspiciousSec
                    });
                }

                result.Sections = sectionsList;

                if (foundSuspiciousPacker)
                {
                    score += 20;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "PE Bölüm Röntgeni: Şüpheli Paketleyici (Packer) İmzası",
                        Description = "Bölüm tablosunda bilinen packer isimleri (UPX/Themida/VMP) veya yüksek entropili (>7.3) şifreli kod bölümleri tespit edildi.",
                        Severity = ThreatSeverity.Warning,
                        ScoreImpact = 20
                    });
                }

                if (foundWxSection)
                {
                    score += 25;
                    result.Factors.Add(new ThreatFactor
                    {
                        Title = "W+X Bölümü (Hem Yazılabilir Hem Çalıştırılabilir Bellek)",
                        Description = "Binary'de aynı anda hem yazma hem çalıştırma yetkisine sahip bellek bölümü bulundu! Self-modifying kod veya bellek enjeksiyonu tekniğidir.",
                        Severity = ThreatSeverity.Critical,
                        ScoreImpact = 25
                    });
                }

                // 5b. PE Overlay Tespiti (Ek Veri / Dropper Gizleme Alanı)
                uint maxRawEnd = 0;
                foreach (var sec in rawSectionTuples)
                {
                    uint end = sec.RawPointer + sec.RawSize;
                    if (end > maxRawEnd) maxRawEnd = end;
                }

                if (maxRawEnd > 0 && stream.Length > maxRawEnd + 512)
                {
                    long overlaySize = stream.Length - maxRawEnd;
                    peHeader.HasOverlay = true;
                    peHeader.OverlaySizeBytes = overlaySize;

                    long savedPos = stream.Position;
                    try
                    {
                        stream.Position = maxRawEnd;
                        int readLen = (int)Math.Min(overlaySize, 2 * 1024 * 1024);
                        byte[] overlayBuf = reader.ReadBytes(readLen);
                        double overlayEntropy = CalculateBufferEntropy(overlayBuf);
                        peHeader.OverlayEntropy = Math.Round(overlayEntropy, 2);

                        if (CheckBufferForEmbeddedPe(overlayBuf))
                        {
                            peHeader.HasEmbeddedPe = true;
                            peHeader.EmbeddedPeDetails = "Overlay alanında ikincil bir PE/Sürücü başlığı tespit edildi.";
                            score += 30;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "PE Overlay İçinde Gömülü İkili / Sürücü (Embedded PE)",
                                Description = "Dosyanın sonundaki ek veri (Overlay) içinde başka bir çalıştırılabilir dosya ya da sürücü (.sys) başlığı bulundu. Klasik Dropper ve BYOVD tekniğidir.",
                                Severity = ThreatSeverity.Critical,
                                ScoreImpact = 30
                            });
                        }
                        else
                        {
                            // Authenticode Sertifika Bloğu Kontrolü:
                            // PE spesifikasyonunda Data Directory[4] (IMAGE_DIRECTORY_ENTRY_SECURITY) RVA değil,
                            // doğrudan ham dosya ofsetidir (file offset).
                            // Dijital imzalı Windows ikililerinde sertifika tablosu PE bölümlerinin bittiği ofsette saklanır.
                            // Sertifikalar ve imzalar yüksek entropilidir (>7.2). Bu durum meşru bir dijital imzadır, zararlı payload değildir.
                            bool isSignatureBlock = (certDirOffset > 0 && certDirOffset >= maxRawEnd) || result.IsSigned;

                            if (isSignatureBlock)
                            {
                                result.Factors.Add(new ThreatFactor
                                {
                                    Title = "Authenticode Sertifika Bloğu (Overlay)",
                                    Description = $"PE bölümlerinin bittiği ofsetten sonra {peHeader.OverlaySizeFormatted} boyutunda dijital imza sertifika tablosu yer alıyor. Dosya meşru şekilde imzalanmıştır.",
                                    Severity = ThreatSeverity.Clean,
                                    ScoreImpact = 0
                                });
                            }
                            else if (overlaySize > 4096)
                            {
                                int impact = overlayEntropy >= 7.2 ? 20 : 10;
                                score += impact;
                                result.Factors.Add(new ThreatFactor
                                {
                                    Title = overlayEntropy >= 7.2
                                        ? "Şüpheli Şifreli PE Overlay (Gizli Payload)"
                                        : "PE Overlay Tespit Edildi (Ek Veri)",
                                    Description = $"PE bölümlerinin bittiği ofsetten sonra {peHeader.OverlaySizeFormatted} boyutunda ek veri bulundu (Entropi: {overlayEntropy:F2} / 8.0). Dropper ve şifrelenmiş payload'lar sıklıkla bu alana gizlenir.",
                                    Severity = overlayEntropy >= 7.2 ? ThreatSeverity.Warning : ThreatSeverity.Info,
                                    ScoreImpact = impact
                                });
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        stream.Position = savedPos;
                    }
                }

                // 5c. Kaynaklar (.rsrc) Bölümünde Gömülü İkili / Sürücü Taraması
                if (!peHeader.HasEmbeddedPe && resourceDirRva > 0 && resourceDirSize > 0)
                {
                    uint resOffset = RvaToOffset(resourceDirRva, rawSectionTuples);
                    if (resOffset > 0 && resOffset < stream.Length)
                    {
                        long savedPos = stream.Position;
                        try
                        {
                            stream.Position = resOffset;
                            int readLen = (int)Math.Min(resourceDirSize, 4 * 1024 * 1024);
                            byte[] resBuf = reader.ReadBytes(readLen);
                            if (CheckBufferForEmbeddedPe(resBuf))
                            {
                                peHeader.HasEmbeddedPe = true;
                                peHeader.EmbeddedPeDetails = "Kaynak (.rsrc) bölümünde gömülü ikincil bir PE/Sürücü tespit edildi.";
                                score += 25;
                                result.Factors.Add(new ThreatFactor
                                {
                                    Title = "Kaynaklarda Gömülü İkili / Sürücü (Embedded PE in .rsrc)",
                                    Description = "Dosyanın kaynak bölümünde başka bir çalıştırılabilir dosya veya kernel sürücüsü (.sys) başlığı tespit edildi. Dropper veya BYOVD rootkit tekniğidir.",
                                    Severity = ThreatSeverity.Critical,
                                    ScoreImpact = 25
                                });
                            }
                        }
                        catch { }
                        finally
                        {
                            stream.Position = savedPos;
                        }
                    }
                }

                // 5d. TLS Callbacks Tespiti (Main Öncesi Gizli Kod Yürütme)
                if (tlsDirRva > 0 && tlsDirSize > 0)
                {
                    uint tlsOffset = RvaToOffset(tlsDirRva, rawSectionTuples);
                    if (tlsOffset > 0 && tlsOffset + (is64Bit ? 40 : 24) <= stream.Length)
                    {
                        long savedPos = stream.Position;
                        try
                        {
                            stream.Position = tlsOffset + (is64Bit ? 24 : 12);
                            ulong addressOfCallBacks = is64Bit
                                ? reader.ReadUInt64()
                                : reader.ReadUInt32();

                            if (addressOfCallBacks >= imageBase)
                            {
                                uint callbacksRva = (uint)(addressOfCallBacks - imageBase);
                                uint callbacksOffset = RvaToOffset(callbacksRva, rawSectionTuples);
                                if (callbacksOffset > 0 && callbacksOffset < stream.Length)
                                {
                                    stream.Position = callbacksOffset;
                                    int callbackCount = 0;
                                    while (callbackCount < 32 && stream.Position + (is64Bit ? 8 : 4) <= stream.Length)
                                    {
                                        ulong cb = is64Bit ? reader.ReadUInt64() : reader.ReadUInt32();
                                        if (cb == 0) break;
                                        callbackCount++;
                                    }

                                    if (callbackCount > 0)
                                    {
                                        peHeader.HasTlsCallbacks = true;
                                        peHeader.TlsCallbackCount = callbackCount;
                                        int tlsImpact = result.IsSigned ? 5 : 15;
                                        score += tlsImpact;
                                        result.Factors.Add(new ThreatFactor
                                        {
                                            Title = "TLS Callback Tespiti (Main Öncesi Kod Yürütme)",
                                            Description = $"Programda giriş noktası (EntryPoint) çalışmadan önce yürütülen {callbackCount} adet TLS Callback tespit edildi. Anti-debug veya gizli ön-başlatma rutinleri için sıkça kullanılır.",
                                            Severity = ThreatSeverity.Warning,
                                            ScoreImpact = tlsImpact
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            stream.Position = savedPos;
                        }
                    }
                }

                // 6. Import Directory & Suspicious Win32 APIs
                //
                // Çok büyük dosyalarda import yürüyüşü uzun sürebilir ve bu metot
                // analiz zincirini bekletir. 256 MB üstünde bu adım atlanır;
                // header, bölümler ve kalkanlar yine de raporlanır.
                const long MaxImportWalkBytes = 256L * 1024 * 1024;
                if (fileInfo.Length > MaxImportWalkBytes)
                {
                    AppLog.Info(
                        $"Dosya {FormatBytes(fileInfo.Length)} — import tablosu analizi atlandı: {filePath}",
                        nameof(FileThreatAnalyzerService));
                }
                else if (importDirRva > 0 && importDirSize > 0)
                {
                    uint importOffset = RvaToOffset(importDirRva, rawSectionTuples);
                    if (importOffset > 0 && importOffset < stream.Length)
                    {
                        stream.Position = importOffset;
                        var dllGroups = new List<ImportedDllGroup>();
                        var normalizedImports = new List<string>();

                        int dllCount = 0;
                        while (dllCount < 100 && stream.Position + 20 <= stream.Length)
                        {
                            uint origFirstThunk = reader.ReadUInt32();
                            uint timeStamp = reader.ReadUInt32();
                            uint forwarderChain = reader.ReadUInt32();
                            uint nameRva = reader.ReadUInt32();
                            uint firstThunk = reader.ReadUInt32();

                            // Terminating null descriptor
                            if (origFirstThunk == 0 && nameRva == 0 && firstThunk == 0) break;
                            dllCount++;

                            long nextDescPos = stream.Position;

                            uint dllNameOffset = RvaToOffset(nameRva, rawSectionTuples);
                            string dllName = ReadNullTerminatedString(stream, dllNameOffset);
                            if (string.IsNullOrWhiteSpace(dllName))
                            {
                                stream.Position = nextDescPos;
                                continue;
                            }

                            var dllGroup = new ImportedDllGroup
                            {
                                DllName = dllName
                            };

                            uint thunkRva = origFirstThunk != 0 ? origFirstThunk : firstThunk;
                            uint thunkOffset = RvaToOffset(thunkRva, rawSectionTuples);

                            if (thunkOffset > 0 && thunkOffset < stream.Length)
                            {
                                stream.Position = thunkOffset;
                                int funcCount = 0;

                                while (funcCount < 500 && stream.Position < stream.Length)
                                {
                                    funcCount++;
                                    string funcName;

                                    if (peHeader.Is64Bit)
                                    {
                                        if (stream.Position + 8 > stream.Length) break;
                                        ulong thunkVal = reader.ReadUInt64();
                                        if (thunkVal == 0) break;

                                        if ((thunkVal & 0x8000000000000000UL) != 0)
                                        {
                                            ushort ordinal = (ushort)(thunkVal & 0xFFFF);
                                            funcName = $"ord{ordinal}";
                                        }
                                        else
                                        {
                                            uint hintRva = (uint)(thunkVal & 0x7FFFFFFF);
                                            uint hintOffset = RvaToOffset(hintRva, rawSectionTuples);
                                            funcName = ReadNullTerminatedString(stream, hintOffset + 2);
                                        }
                                    }
                                    else
                                    {
                                        if (stream.Position + 4 > stream.Length) break;
                                        uint thunkVal = reader.ReadUInt32();
                                        if (thunkVal == 0) break;

                                        if ((thunkVal & 0x80000000U) != 0)
                                        {
                                            ushort ordinal = (ushort)(thunkVal & 0xFFFF);
                                            funcName = $"ord{ordinal}";
                                        }
                                        else
                                        {
                                            uint hintOffset = RvaToOffset(thunkVal, rawSectionTuples);
                                            funcName = ReadNullTerminatedString(stream, hintOffset + 2);
                                        }
                                    }

                                    if (!string.IsNullOrWhiteSpace(funcName))
                                    {
                                        bool isSuspicious = SuspiciousApiCatalog.TryGetValue(funcName, out var apiMeta);

                                        dllGroup.Functions.Add(new ImportedApiFunction
                                        {
                                            Name = funcName,
                                            IsSuspicious = isSuspicious,
                                            Category = isSuspicious ? apiMeta.Category : string.Empty,
                                            Description = isSuspicious ? apiMeta.Description : string.Empty
                                        });

                                        string cleanDll = Path.GetFileNameWithoutExtension(dllName).ToLowerInvariant();
                                        normalizedImports.Add($"{cleanDll}.{funcName.ToLowerInvariant()}");
                                    }
                                }
                            }

                            if (dllGroup.Functions.Count > 0)
                            {
                                dllGroups.Add(dllGroup);
                            }

                            stream.Position = nextDescPos;
                        }

                        result.ImportedDlls = dllGroups;

                        // Calculate ImpHash
                        if (normalizedImports.Count > 0)
                        {
                            try
                            {
                                string joined = string.Join(",", normalizedImports);
                                byte[] impBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.ASCII.GetBytes(joined));
                                result.ImpHash = Convert.ToHexString(impBytes).ToLowerInvariant();
                            }
                            catch { }
                        }

                        // Puanlama EvaluateImports içinde, imza bilgisiyle birlikte yapılır (ANL Z5).
                    }
                }
            }
            catch
            {
                // Silently fallback if PE is corrupt or malformed
            }
        }

        private static uint RvaToOffset(uint rva, List<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)> sections)
        {
            foreach (var sec in sections)
            {
                uint size = Math.Max(sec.VirtualSize, sec.RawSize);
                if (rva >= sec.VirtualAddress && rva < sec.VirtualAddress + size)
                {
                    return (rva - sec.VirtualAddress) + sec.RawPointer;
                }
            }
            return 0;
        }

        private static string ReadNullTerminatedString(Stream stream, uint offset, int maxLen = 128)
        {
            if (offset <= 0 || offset >= stream.Length) return string.Empty;
            long origPos = stream.Position;
            try
            {
                stream.Position = offset;
                var bytes = new List<byte>();
                for (int i = 0; i < maxLen && stream.Position < stream.Length; i++)
                {
                    int b = stream.ReadByte();
                    if (b <= 0) break;
                    if (b >= 32 && b <= 126) bytes.Add((byte)b);
                }
                return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                try { stream.Position = origPos; } catch { }
            }
        }

        private static double CalculateBufferEntropy(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return 0.0;
            int[] map = new int[256];
            for (int i = 0; i < buffer.Length; i++) map[buffer[i]]++;
            double entropy = 0.0;
            double len = buffer.Length;
            for (int i = 0; i < 256; i++)
            {
                if (map[i] > 0)
                {
                    double p = map[i] / len;
                    entropy -= p * Math.Log2(p);
                }
            }
            return Math.Round(entropy, 2);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblBytes = bytes;
            while (dblBytes >= 1024 && i < suffixes.Length - 1)
            {
                dblBytes /= 1024;
                i++;
            }
            return $"{dblBytes:0.##} {suffixes[i]}";
        }

        private static bool CheckBufferForEmbeddedPe(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 256) return false;
            for (int i = 0; i <= buffer.Length - 128; i++)
            {
                if (buffer[i] == 0x4D && buffer[i + 1] == 0x5A) // 'MZ'
                {
                    if (i + 0x3C + 4 <= buffer.Length)
                    {
                        int elfanew = BitConverter.ToInt32(buffer, i + 0x3C);
                        if (elfanew > 0 && elfanew < 1024 && i + elfanew + 4 <= buffer.Length)
                        {
                            if (buffer[i + elfanew] == 0x50 && buffer[i + elfanew + 1] == 0x45 &&
                                buffer[i + elfanew + 2] == 0 && buffer[i + elfanew + 3] == 0) // 'PE\0\0'
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
