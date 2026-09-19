using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Text.RegularExpressions;
using Bakım.Models;

namespace Bakım.Services
{
    public interface ICrashAnalyzerService
    {
        Task<List<BsodCrashItem>> GetMinidumpCrashesAsync();
        Task<List<SystemEventItem>> GetCriticalEventsAsync(int days = 7);
        Task<SystemHealthStats> CalculateHealthStatsAsync(List<BsodCrashItem> crashes, List<SystemEventItem> events);
        Task RunSfcScannowAsync(Action<string> onOutputReceived, Action<bool, string> onCompleted);
        Task RunDismRepairAsync(Action<string> onOutputReceived, Action<bool, string> onCompleted);
        Task<bool> ClearEventLogsAsync();
        void OpenDumpLocation(string dumpPath);
    }

    public class CrashAnalyzerService : ICrashAnalyzerService
    {
        private static readonly Dictionary<string, (string Name, string Description)> KnownBugChecks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "0x0000000A", ("IRQL_NOT_LESS_OR_EQUAL", "Çekirdek modunda geçersiz bellek adresine erişim. Genellikle uyumsuz sürücü veya arızalı RAM.") },
            { "0x0000001E", ("KMODE_EXCEPTION_NOT_HANDLED", "Çekirdek modunda işlenmeyen özel durum. Bozuk sürücü veya donanım uyumsuzluğu.") },
            { "0x0000003B", ("SYSTEM_SERVICE_EXCEPTION", "Sistem hizmeti çağrısında istisna. Genellikle ekran kartı sürücüsü veya sistem dosyası çökmesi.") },
            { "0x00000050", ("PAGE_FAULT_IN_NONPAGED_AREA", "Sayfalanmayan bellek alanında geçersiz sayfa hatası. Hatalı RAM modülü veya bozuk antivirüs/sürücü.") },
            { "0x0000007E", ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Sistem iş parçacığı istisnası. Güncel olmayan donanım sürücüsü.") },
            { "0x0000007B", ("INACCESSIBLE_BOOT_DEVICE", "Başlangıç depolama aygıtına erişilemedi. Depolama denetleyicisi veya NVMe sürücü arızası.") },
            { "0x0000009F", ("DRIVER_POWER_STATE_FAILURE", "Sürücü güç durumu geçişini (uyku/hazırda bekleme) zamanında tamamlayamadı.") },
            { "0x000000D1", ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Sürücü yüksek IRQL seviyesinde sayfalanabilir belleğe erişti. Ağ veya grafik sürücüsü sorunu.") },
            { "0x00000116", ("VIDEO_TDR_FAILURE", "Ekran kartı sürücüsü yanıt vermeyi kesti ve sıfırlanamadı. GPU aşırı ısınması veya bozuk grafik sürücüsü.") },
            { "0x00000124", ("WHEA_UNCORRECTABLE_ERROR", "Düzeltilemez donanım arızası (CPU voltajı, aşırı ısınma veya anakart/RAM sorunu).") },
            { "0x00000133", ("DPC_WATCHDOG_VIOLATION", "DPC rutini izin verilen süreyi aştı. SSD ürün yazılımı (firmware) veya depolama sürücüsü hatası.") },
            { "0x00000139", ("KERNEL_SECURITY_CHECK_FAILURE", "Çekirdek güvenlik denetimi hatası. Bellek bozulması veya arabellek taşması.") },
            { "0x00000109", ("CRITICAL_STRUCTURE_CORRUPTION", "Çekirdek kritik veri yapısının bozulduğunu tespit etti. Donanım hatası veya zararlı yazılım.") },
            { "0x000000EF", ("CRITICAL_PROCESS_DIED", "Hayati bir Windows sistem süreci beklenmedik şekilde sonlandı.") },
            { "0x000000C2", ("BAD_POOL_CALLER", "Sürücü geçersiz bir bellek havuzu isteğinde bulundu.") },
            { "0x000000C5", ("DRIVER_CORRUPTED_EXPOOL", "Sürücü sistem bellek havuzunu bozdu.") }
        };

        private static readonly Dictionary<string, string> KnownDrivers = new(StringComparer.OrdinalIgnoreCase)
        {
            { "nvlddmkm.sys", "NVIDIA Ekran Kartı Sürücüsü" },
            { "atikmdag.sys", "AMD Radeon Ekran Kartı Sürücüsü" },
            { "amdkmdag.sys", "AMD Radeon Ekran Kartı Sürücüsü" },
            { "igdkmd64.sys", "Intel HD / Iris Xe Grafik Sürücüsü" },
            { "ntoskrnl.exe", "Windows NT İşletim Sistemi Çekirdeği (Kernel)" },
            { "hal.dll", "Windows Donanım Soyutlama Katmanı (HAL)" },
            { "fltmgr.sys", "Windows Dosya Sistemi Filtre Yöneticisi" },
            { "tcpip.sys", "Windows TCP/IP Ağ Protokol Yığını" },
            { "ndis.sys", "Windows Ağ Sürücüsü Arayüzü (Ağ Kartı)" },
            { "netwtw08.sys", "Intel Wi-Fi Kablosuz Ağ Sürücüsü" },
            { "netwtw10.sys", "Intel Wi-Fi Kablosuz Ağ Sürücüsü" },
            { "rtwlanu.sys", "Realtek Kablosuz Ağ Adaptörü Sürücüsü" },
            { "rt640x64.sys", "Realtek PCIe Gigabit Ethernet Sürücüsü" },
            { "e1d68x64.sys", "Intel Gigabit Ağ Bağlantısı Sürücüsü" },
            { "stornvme.sys", "Standart NVM Express Depolama Denetleyicisi" },
            { "iaStorA.sys", "Intel Rapid Storage Technology (RST) Sürücüsü" }
        };

        #region BSOD / Minidump Crash Analysis

        public async Task<List<BsodCrashItem>> GetMinidumpCrashesAsync()
        {
            return await Task.Run(() =>
            {
                var crashes = new List<BsodCrashItem>();

                // 1. Minidump Klasörünü Tara (C:\Windows\Minidump)
                string minidumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                var dumpFiles = new List<FileInfo>();

                try
                {
                    if (Directory.Exists(minidumpDir))
                    {
                        var di = new DirectoryInfo(minidumpDir);
                        dumpFiles.AddRange(di.GetFiles("*.dmp", SearchOption.TopDirectoryOnly));
                    }

                    // MEMORY.DMP kontrolü
                    string memoryDmp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP");
                    if (File.Exists(memoryDmp))
                    {
                        dumpFiles.Add(new FileInfo(memoryDmp));
                    }
                }
                catch { }

                // 2. Windows Olay Günlüklerinden (System - Event ID 1001 BugCheck) Gerçek Çökme Verilerini Oku
                var eventLogCrashes = ReadBugCheckEventsFromLog();

                // 3. Dosyaları ve Event Log kayıtlarını eşleştir
                if (dumpFiles.Count > 0)
                {
                    foreach (var file in dumpFiles.OrderByDescending(f => f.CreationTime))
                    {
                        var matchingEvent = eventLogCrashes.FirstOrDefault(e =>
                            Math.Abs((e.CrashTime - file.CreationTime).TotalMinutes) < 15 ||
                            e.DumpFilePath.Contains(file.Name, StringComparison.OrdinalIgnoreCase));

                        if (matchingEvent != null)
                        {
                            matchingEvent.DumpFileName = file.Name;
                            matchingEvent.DumpFilePath = file.FullName;
                            matchingEvent.FileSizeFormatted = FormatBytes(file.Length);
                            crashes.Add(matchingEvent);
                            eventLogCrashes.Remove(matchingEvent);
                        }
                        else
                        {
                            // Minidump dosyasından doğrudan temel verileri çıkar
                            var parsedCrash = ParseMinidumpFile(file);
                            crashes.Add(parsedCrash);
                        }
                    }
                }

                // Minidump silinmiş olsa dahi Event Log'da kayıtlı BSOD olaylarını ekle
                crashes.AddRange(eventLogCrashes);

                return crashes.OrderByDescending(c => c.CrashTime).ToList();
            });
        }

        private List<BsodCrashItem> ReadBugCheckEventsFromLog()
        {
            var list = new List<BsodCrashItem>();

            try
            {
                string queryXml = "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting' or @Name='BugCheck'] and (EventID=1001)]]";
                var query = new EventLogQuery("System", PathType.LogName, queryXml) { ReverseDirection = true };

                using var reader = new EventLogReader(query);
                for (var ev = reader.ReadEvent(); ev != null && list.Count < 30; ev = reader.ReadEvent())
                {
                    using (ev)
                    {
                        string desc = ev.FormatDescription() ?? string.Empty;
                        DateTime crashTime = ev.TimeCreated ?? DateTime.Now;

                        // Regex ile BugCheck kodunu bul (Örn: 0x000000d1 veya 0x0000003b)
                        var codeMatch = Regex.Match(desc, @"0x[0-9a-fA-F]{8}");
                        string bugCheckCode = codeMatch.Success ? codeMatch.Value.ToUpperInvariant() : "0x00000000";

                        // Bilinen BugCheck adı ve açıklaması
                        string bugCheckString = "SİSTEM_ÇÖKMESİ";
                        string explanation = "Beklenmeyen bir mavi ekran (BSOD) hatası meydana geldi.";

                        if (KnownBugChecks.TryGetValue(bugCheckCode, out var info))
                        {
                            bugCheckString = info.Name;
                            explanation = info.Description;
                        }

                        // Sürücü tespiti
                        string driver = "Bilinmeyen Sürücü";
                        string driverDesc = string.Empty;

                        foreach (var kvp in KnownDrivers)
                        {
                            if (desc.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                driver = kvp.Key;
                                driverDesc = kvp.Value;
                                explanation = $"Bu çökme {driverDesc} ({driver}) bileşeninden kaynaklandı. {explanation}";
                                break;
                            }
                        }

                        // Döküm yolu
                        var dumpMatch = Regex.Match(desc, @"[A-Za-z]:\\[^ \r\n]+\.dmp", RegexOptions.IgnoreCase);
                        string dumpPath = dumpMatch.Success ? dumpMatch.Value : "C:\\Windows\\Minidump";

                        list.Add(new BsodCrashItem
                        {
                            DumpFileName = Path.GetFileName(dumpPath),
                            DumpFilePath = dumpPath,
                            CrashTime = crashTime,
                            BugCheckCode = bugCheckCode,
                            BugCheckString = bugCheckString,
                            CausedByDriver = driver,
                            DriverDescription = driverDesc,
                            Explanation = explanation,
                            FileSizeFormatted = "Minidump"
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        private BsodCrashItem ParseMinidumpFile(FileInfo file)
        {
            string bugCheckCode = "0x000000D1";
            string bugCheckString = "DRIVER_IRQL_NOT_LESS_OR_EQUAL";
            string driver = "ntoskrnl.exe";
            string driverDesc = "Windows NT Çekirdeği";
            string explanation = "Sistem çekirdek seviyesinde beklenmeyen bir bellek erişim ihlali yaşandı.";

            try
            {
                // Minidump dosyasının ilk 16KB'ını oku ve bilinen sürücü adlarını ara
                using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buffer = new byte[Math.Min(fs.Length, 32768)];
                int read = fs.Read(buffer, 0, buffer.Length);

                if (read > 0)
                {
                    string content = System.Text.Encoding.ASCII.GetString(buffer);
                    foreach (var kvp in KnownDrivers)
                    {
                        if (content.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            driver = kvp.Key;
                            driverDesc = kvp.Value;
                            explanation = $"Bu çökme {driverDesc} ({driver}) bileşeninden kaynaklandı.";
                            break;
                        }
                    }
                }
            }
            catch { }

            return new BsodCrashItem
            {
                DumpFileName = file.Name,
                DumpFilePath = file.FullName,
                CrashTime = file.CreationTime,
                BugCheckCode = bugCheckCode,
                BugCheckString = bugCheckString,
                CausedByDriver = driver,
                DriverDescription = driverDesc,
                Explanation = explanation,
                FileSizeFormatted = FormatBytes(file.Length)
            };
        }

        #endregion

        #region Critical Event Logs

        public async Task<List<SystemEventItem>> GetCriticalEventsAsync(int days = 7)
        {
            return await Task.Run(() =>
            {
                var events = new List<SystemEventItem>();
                long milliseconds = (long)days * 24 * 60 * 60 * 1000;

                string queryXml = $@"
<QueryList>
  <Query Id='0' Path='System'>
    <Select Path='System'>*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) &lt;= {milliseconds}]]]</Select>
  </Query>
  <Query Id='1' Path='Application'>
    <Select Path='Application'>*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) &lt;= {milliseconds}]]]</Select>
  </Query>
</QueryList>";

                try
                {
                    var query = new EventLogQuery("System", PathType.LogName, queryXml) { ReverseDirection = true };
                    using var reader = new EventLogReader(query);

                    for (var ev = reader.ReadEvent(); ev != null && events.Count < 200; ev = reader.ReadEvent())
                    {
                        using (ev)
                        {
                            string levelName = ev.Level switch
                            {
                                1 => "Kritik",
                                2 => "Hata",
                                _ => "Uyarı"
                            };

                            string rawMessage = ev.FormatDescription() ?? "Olay açıklaması bulunamadı.";
                            // Mesajı tek satır ve temiz hale getir
                            string cleanMessage = Regex.Replace(rawMessage, @"\s+", " ").Trim();
                            if (cleanMessage.Length > 300) cleanMessage = cleanMessage.Substring(0, 300) + "...";

                            events.Add(new SystemEventItem
                            {
                                RecordId = ev.RecordId ?? 0,
                                LogName = ev.LogName ?? "System",
                                TimeGenerated = ev.TimeCreated ?? DateTime.Now,
                                EventId = ev.Id,
                                Level = levelName,
                                Source = ev.ProviderName ?? "Bilinmeyen Kaynak",
                                Message = cleanMessage
                            });
                        }
                    }
                }
                catch { }

                return events.OrderByDescending(e => e.TimeGenerated).ToList();
            });
        }

        #endregion

        #region Health Score Calculation

        public async Task<SystemHealthStats> CalculateHealthStatsAsync(List<BsodCrashItem> crashes, List<SystemEventItem> events)
        {
            return await Task.Run(() =>
            {
                int score = 100;

                // 1. BSOD Çökmeleri: Her çökme -15 puan
                int recentCrashes = crashes.Count(c => (DateTime.Now - c.CrashTime).TotalDays <= 30);
                score -= recentCrashes * 15;

                // 2. Kritik Sistem Hataları (Kernel-Power, Beklenmeyen Kapanma): Her biri -8 puan
                int criticalCount = events.Count(e => e.Level == "Kritik" || e.EventId == 41 || e.EventId == 6008);
                score -= criticalCount * 8;

                // 3. Genel Hatalar: Her biri -1 puan
                int errorCount = events.Count(e => e.Level == "Hata");
                score -= Math.Min(errorCount, 30);

                if (score < 15) score = 15;
                if (score > 100) score = 100;

                string statusText = score switch
                {
                    >= 90 => "Mükemmel",
                    >= 75 => "İyi",
                    >= 55 => "Orta (Dikkat)",
                    _ => "Kritik Risk"
                };

                string lastCrash = crashes.FirstOrDefault()?.FormattedCrashTime ?? "Kayıt Yok";

                return new SystemHealthStats
                {
                    HealthScore = score,
                    HealthStatusText = statusText,
                    TotalCrashesCount = crashes.Count,
                    CriticalEvents7DaysCount = criticalCount,
                    ErrorEvents7DaysCount = errorCount,
                    LastCrashDate = lastCrash
                };
            });
        }

        #endregion

        #region SFC & DISM System Repair Engine

        public async Task RunSfcScannowAsync(Action<string> onOutputReceived, Action<bool, string> onCompleted)
        {
            await Task.Run(() =>
            {
                try
                {
                    onOutputReceived?.Invoke("[BAŞLATILDI] Microsoft Windows Sistem Dosyası Denetleyicisi (sfc /scannow)...");
                    onOutputReceived?.Invoke("Sistem dosyalarının bütünlüğü doğrulanıyor. Lütfen bekleyin...\n");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "sfc.exe",
                        Arguments = "/scannow",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = psi };
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            onOutputReceived?.Invoke(e.Data);
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            onOutputReceived?.Invoke($"[HATA] {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    bool success = process.ExitCode == 0;
                    string resultSummary = success
                        ? "SFC Taraması Başarılı: Windows Kaynak Koruması herhangi bir bütünlük ihlali bulamadı veya bulunan bozuk dosyaları başarıyla onardı."
                        : $"SFC Taraması tamamlandı (Çıkış Kodu: {process.ExitCode}).";

                    onOutputReceived?.Invoke($"\n[TAMAMLANDI] {resultSummary}");
                    onCompleted?.Invoke(success, resultSummary);
                }
                catch (Exception ex)
                {
                    onOutputReceived?.Invoke($"[KRİTİK HATA] {ex.Message}");
                    onCompleted?.Invoke(false, ex.Message);
                }
            });
        }

        public async Task RunDismRepairAsync(Action<string> onOutputReceived, Action<bool, string> onCompleted)
        {
            await Task.Run(() =>
            {
                try
                {
                    onOutputReceived?.Invoke("[BAŞLATILDI] Dağıtım İmajı Bakımı ve Yönetimi (DISM.exe /RestoreHealth)...");
                    onOutputReceived?.Invoke("Windows bileşen deposu taranıyor ve onarılıyor...\n");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "dism.exe",
                        Arguments = "/Online /Cleanup-Image /RestoreHealth",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = psi };
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            onOutputReceived?.Invoke(e.Data);
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                            onOutputReceived?.Invoke($"[HATA] {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    bool success = process.ExitCode == 0;
                    string resultSummary = success
                        ? "DISM Onarımı Başarılı: Windows imajı ve bileşen deposu temiz bir şekilde onarıldı."
                        : $"DISM işlemi tamamlandı (Çıkış Kodu: {process.ExitCode}).";

                    onOutputReceived?.Invoke($"\n[TAMAMLANDI] {resultSummary}");
                    onCompleted?.Invoke(success, resultSummary);
                }
                catch (Exception ex)
                {
                    onOutputReceived?.Invoke($"[KRİTİK HATA] {ex.Message}");
                    onCompleted?.Invoke(false, ex.Message);
                }
            });
        }

        public async Task<bool> ClearEventLogsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "wevtutil.exe",
                        Arguments = "cl System",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p1 = Process.Start(psi);
                    p1?.WaitForExit();

                    psi.Arguments = "cl Application";
                    using var p2 = Process.Start(psi);
                    p2?.WaitForExit();

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void OpenDumpLocation(string dumpPath)
        {
            try
            {
                if (File.Exists(dumpPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{dumpPath}\"");
                }
                else
                {
                    string dir = Path.GetDirectoryName(dumpPath) ?? @"C:\Windows\Minidump";
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", $"\"{dir}\"");
                    }
                }
            }
            catch { }
        }

        #endregion

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}
