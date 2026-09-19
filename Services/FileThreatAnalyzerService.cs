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

namespace Bakım.Services
{
    public interface IFileThreatAnalyzerService
    {
        Task<ThreatAnalysisResult> AnalyzeFileAsync(string filePath, string? commandArgs = null, PersistenceItem? autorunItem = null);
        Task<bool> KillProcessAsync(int processId);
        Task<bool> ForceDeleteFileAsync(string filePath);
    }

    public class FileThreatAnalyzerService : IFileThreatAnalyzerService
    {
        private readonly IVirusTotalCheckService _virusTotalService;

        private static readonly string[] CriticalSystemExes = new[]
        {
            "svchost.exe", "csrss.exe", "lsass.exe", "smss.exe", "services.exe",
            "winlogon.exe", "taskhostw.exe", "explorer.exe", "rundll32.exe",
            "dwm.exe", "spoolsv.exe", "conhost.exe", "sihost.exe"
        };

        private static readonly string[] KnownTrustedPublishers = new[]
        {
            "Microsoft", "Google", "NVIDIA", "Intel", "AMD", "Valve", "Apple",
            "Adobe", "Mozilla", "Discord", "Spotify", "Oracle", "GitHub", "Epic Games"
        };

        #region WinTrust P/Invoke for Authenticode

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_FILE_INFO : IDisposable
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            public IntPtr pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;

            public WINTRUST_FILE_INFO(string filePath)
            {
                pcwszFilePath = Marshal.StringToCoTaskMemUni(filePath);
            }

            public void Dispose()
            {
                if (pcwszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pcwszFilePath);
                    pcwszFilePath = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_DATA : IDisposable
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public uint dwUIChoice = 2; // WTD_UI_NONE
            public uint fdwRevocationChecks = 0;
            public uint dwUnionChoice = 1; // WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public uint dwProvFlags = 0x00000040 | 0x00000010;
            public uint dwUIContext = 0;
            public IntPtr pSignatureSettings = IntPtr.Zero;

            public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
            {
                pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFile, false);
            }

            public void Dispose()
            {
                if (pFile != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                    Marshal.FreeCoTaskMem(pFile);
                    pFile = IntPtr.Zero;
                }
            }
        }

        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
            WINTRUST_DATA pWVTData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

        #endregion

        public FileThreatAnalyzerService(IVirusTotalCheckService? virusTotalService = null)
        {
            _virusTotalService = virusTotalService ?? new VirusTotalCheckService();
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
                var fileInfo = new FileInfo(filePath);
                result.FileSizeBytes = fileInfo.Length;
                result.FileSizeFormatted = FormatBytes(fileInfo.Length);

                // 0. Calculate SHA-256 Hash
                result.Sha256 = _virusTotalService.ComputeSha256(filePath);

                // Check Active Running Processes
                CheckActiveRunningProcess(filePath, result);

                // =========================================================================
                // VEKTÖR 1: DİJİTAL İMZA & YAYINCI DOĞRULAMASI (%25)
                // =========================================================================
                var (sigStatus, signerName) = VerifyFileSignature(filePath);
                result.SignerName = signerName;

                if (sigStatus == SignatureStatus.Verified)
                {
                    result.IsSigned = true;
                    result.DigitalSignatureText = $"Geçerli ({signerName})";

                    bool isTrustedVendor = KnownTrustedPublishers.Any(p => signerName.Contains(p, StringComparison.OrdinalIgnoreCase));
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
                            Title = "Geçerli Dijital Sertifika",
                            Description = $"Dosyanın Authenticode imzası geçerli. İmzacı: {signerName}",
                            Severity = ThreatSeverity.Clean,
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
                // VEKTÖR 2: ÇALIŞMA KONUMU & SAHTE SİSTEM DOSYASI TAKLİDİ (%25)
                // =========================================================================
                string lowerPath = filePath.ToLowerInvariant();
                string lowerFileName = result.FileName.ToLowerInvariant();
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
                string sys32Dir = Path.Combine(winDir, "system32").ToLowerInvariant();
                string sysWow64Dir = Path.Combine(winDir, "syswow64").ToLowerInvariant();

                // Fake System32 Mimicry Check (e.g. C:\Windows\svchost.exe instead of System32)
                if (CriticalSystemExes.Contains(lowerFileName))
                {
                    bool isInLegitSystemDir = lowerPath.StartsWith(sys32Dir) || lowerPath.StartsWith(sysWow64Dir);
                    if (!isInLegitSystemDir)
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

                // Temp Dirs
                if (lowerPath.Contains(@"\appdata\local\temp") || lowerPath.Contains(@"\windows\temp") || lowerPath.Contains(@"\temporary internet files\"))
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
                else if (lowerPath.Contains(@"\users\public"))
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
                else if (lowerPath.Contains(@"\appdata\roaming\") && !lowerPath.Replace(@"\appdata\roaming\", "").Contains(@"\"))
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
                string argsToCheck = (commandArgs ?? string.Empty).ToLowerInvariant();
                if (argsToCheck.Contains("-w hidden") || argsToCheck.Contains("-windowstyle hidden") ||
                    argsToCheck.Contains("-enc") || argsToCheck.Contains("-encodedcommand") ||
                    argsToCheck.Contains("/silent") || argsToCheck.Contains("/s") || argsToCheck.Contains("-nop"))
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

                        if (malicious >= 4)
                        {
                            score += 25;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "VirusTotal Antivirüs Tespiti",
                                Description = $"{total} antivirüs motorundan {malicious} tanesi bu dosyayı zararlı/tehdit olarak etiketledi.",
                                Severity = ThreatSeverity.Critical,
                                ScoreImpact = 25
                            });
                        }
                        else if (malicious >= 1)
                        {
                            score += 15;
                            result.Factors.Add(new ThreatFactor
                            {
                                Title = "VirusTotal Şüpheli Tespiti",
                                Description = $"{malicious} adet güvenlik motoru dosyayı şüpheli olarak işaretledi (Olası yanlış pozitif olabilir).",
                                Severity = ThreatSeverity.Warning,
                                ScoreImpact = 15
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
                        else if (total == 0 && (msg.Contains("VT Kaydı Yok") || msg.Contains("Bulunamadı")))
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

                // Final Score clamp
                result.RiskScore = Math.Clamp(score, 0, 100);

                // Build Recommendation
                if (result.RiskScore >= 61)
                {
                    result.Recommendation = "KRİTİK UYARI: Bu dosya çok sayıda belirgin zararlı yazılım ve gizlenme niteliği taşımaktadır. Sürecin sonlandırılması, başlangıçtan kaldırılması ve sistemden derhal kalıcı olarak silinmesi önemle tavsiye edilir.";
                }
                else if (result.RiskScore >= 26)
                {
                    result.Recommendation = "DİKKAT: Dosya bazı şüpheli nitelikler (imzasız kod, alışılmadık çalışma dizini veya parametreler) barındırmaktadır. Kaynağını bilmiyorsanız devre dışı bırakmanız ve incelemeniz önerilir.";
                }
                else
                {
                    result.Recommendation = "GÜVENLİ: Dosya temel güvenlik ve imza doğrulamalarından başarıyla geçmiştir. Meşru bir yazılım bileşeni olarak değerlendirilmektedir.";
                }

                return result;
            });
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

                foreach (var proc in processes)
                {
                    try
                    {
                        string? procPath = proc.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(procPath) && procPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            result.IsActiveProcess = true;
                            result.ActiveProcessId = proc.Id;
                            result.ActiveProcessMemory = FormatBytes(proc.WorkingSet64);
                            return;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public async Task<bool> KillProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ForceDeleteFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(filePath)) return true;

                try
                {
                    // 1. Remove ReadOnly / System attributes
                    File.SetAttributes(filePath, FileAttributes.Normal);

                    // 2. Kill any process locking the file
                    try
                    {
                        var procs = Process.GetProcesses();
                        foreach (var p in procs)
                        {
                            try
                            {
                                if (p.MainModule?.FileName.Equals(filePath, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    p.Kill(true);
                                    p.WaitForExit(2000);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // 3. Delete directly
                    File.Delete(filePath);
                    return !File.Exists(filePath);
                }
                catch
                {
                    // 4. Fallback: Schedule delete on reboot
                    try
                    {
                        return MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    }
                    catch
                    {
                        return false;
                    }
                }
            });
        }

        private static (SignatureStatus Status, string Signer) VerifyFileSignature(string filePath)
        {
            try
            {
                using var fileInfo = new WINTRUST_FILE_INFO(filePath);
                using var trustData = new WINTRUST_DATA(fileInfo);

                uint result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, trustData);

                string signer = string.Empty;
                try
                {
#pragma warning disable SYSLIB0057
                    var cert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                    if (cert != null)
                    {
                        string subject = cert.Subject;
                        int cnIdx = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                        if (cnIdx >= 0)
                        {
                            int commaIdx = subject.IndexOf(',', cnIdx);
                            signer = commaIdx > 0 ? subject.Substring(cnIdx + 3, commaIdx - (cnIdx + 3)) : subject.Substring(cnIdx + 3);
                        }
                        else
                        {
                            signer = subject;
                        }
                    }
                }
                catch { }

                if (result == 0)
                {
                    return (SignatureStatus.Verified, signer);
                }
                else if (result == 0x800B0100) // TRUST_E_NOSIGNATURE
                {
                    return (SignatureStatus.Unsigned, string.Empty);
                }
                else
                {
                    return (SignatureStatus.InvalidOrTampered, !string.IsNullOrWhiteSpace(signer) ? $"{signer} (Geçersiz)" : "Bozulmuş İmza");
                }
            }
            catch
            {
                return (SignatureStatus.Unsigned, string.Empty);
            }
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
    }
}
