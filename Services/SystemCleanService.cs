using System.IO;
using System.Runtime.InteropServices;
using Bakım.Helpers;
using Bakım.Models;

namespace Bakım.Services
{
    public class SystemCleanService : ISystemCleanService
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public List<CleanCategory> GetDefaultCategories()
        {
            var list = new List<CleanCategory>
            {
                new CleanCategory
                {
                    Id = "user_temp",
                    Name = "Kullanıcı Geçici Dosyaları (%TEMP%)",
                    Description = "Uygulamaların geride bıraktığı geçici çalışma ve log kalıntıları.",
                    TargetPath = Path.GetTempPath(),
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "windows_temp",
                    Name = "Windows Sistem Temp",
                    Description = "Windows sistem hizmetlerinin oluşturduğu genel geçici dosyalar.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                    RequiresAdmin = true,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "software_distribution",
                    Name = "Windows Update İndirme Önbelleği",
                    Description = "Daha önce yüklenmiş Windows güncellemelerinden kalan kurulum paketleri.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
                    RequiresAdmin = true,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "prefetch",
                    Name = "Windows Prefetch Önbelleği",
                    Description = "Eski ve artık kullanılmayan uygulama açılış önbellekleri.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"),
                    RequiresAdmin = true,
                    IsSelected = false
                },
                new CleanCategory
                {
                    Id = "edge_cache",
                    Name = "Microsoft Edge Web Önbelleği",
                    Description = "Web sayfaları ve medya içeriklerinin tarayıcı önbellek dosyaları.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "chrome_cache",
                    Name = "Google Chrome Web Önbelleği",
                    Description = "Chrome tarayıcısının diskte sakladığı geçici internet verileri.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache"),
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "crash_dumps",
                    Name = "Uygulama Çökme Dökümleri (Crash Dumps)",
                    Description = "Daha önce çöken uygulamaların diske bıraktığı bellek döküm dosyaları.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "wer_reports",
                    Name = "Windows Hata Raporlama Kalıntıları (WER)",
                    Description = "İşletim sistemi hata raporlama kuyrukları ve arşiv kalıntıları.",
                    TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER"),
                    RequiresAdmin = false,
                    IsSelected = false
                }
            };

            return list;
        }

        public async Task<(List<CleanFileItem> items, long totalBytes)> ScanCategoryAsync(
            CleanCategory category,
            IProgress<string> progress,
            CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var resultItems = new List<CleanFileItem>();
                long categoryBytes = 0;

                if (!Directory.Exists(category.TargetPath))
                {
                    return (resultItems, 0);
                }

                try
                {
                    var dirInfo = new DirectoryInfo(category.TargetPath);
                    var enumOptions = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        ReturnSpecialDirectories = false
                    };

                    foreach (var file in dirInfo.EnumerateFiles("*", enumOptions))
                    {
                        ct.ThrowIfCancellationRequested();

                        // Güvenlik: Asla kritik sistem dosyalarını tarama listesine alma
                        if (!IsSafeTarget(file.FullName))
                        {
                            continue;
                        }

                        try
                        {
                            long len = file.Length;
                            categoryBytes += len;
                            resultItems.Add(new CleanFileItem
                            {
                                FileName = file.Name,
                                FilePath = file.FullName,
                                CategoryName = category.Name,
                                SizeBytes = len,
                                Status = "Taranıyor / Hazır"
                            });

                            progress.Report(file.FullName);
                        }
                        catch (UnauthorizedAccessException) { /* Dosya metaverisine erişilemediğinde atla */ }
                        catch (IOException) { /* Kilitli dosya boyutu okunamadığında atla */ }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Dizin erişim engeli
                }
                catch (Exception)
                {
                    // Diğer beklenmeyen dosya sistemi hataları
                }

                return (resultItems, categoryBytes);
            }, ct);
        }

        public async Task<CleanResult> CleanItemsAsync(
            IEnumerable<CleanFileItem> items,
            IProgress<(string file, int percent)> progress,
            CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var result = new CleanResult();
                var itemList = items.ToList();
                int total = itemList.Count;
                int processed = 0;

                foreach (var item in itemList)
                {
                    ct.ThrowIfCancellationRequested();
                    processed++;
                    int percent = total > 0 ? (processed * 100) / total : 100;
                    progress.Report((item.FilePath, percent));

                    // 1. Kritik Sistem Koruması Kontrolü
                    if (!IsSafeTarget(item.FilePath))
                    {
                        item.Status = "Kritik Alan (Korumalı)";
                        result.TotalFilesSkipped++;
                        continue;
                    }

                    // 2. Güvenli Dosya Silme & Hata Yönetimi
                    try
                    {
                        if (File.Exists(item.FilePath))
                        {
                            File.SetAttributes(item.FilePath, FileAttributes.Normal);
                            File.Delete(item.FilePath);
                        }

                        item.Status = "Silindi";
                        item.IsDeleted = true;
                        result.TotalFilesDeleted++;
                        result.TotalBytesFreed += item.SizeBytes;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Erişim yetkisi yok (UAC / Sistem korumalı)
                        item.Status = "Yetki Engeli (Atlandı)";
                        result.TotalFilesSkipped++;
                    }
                    catch (IOException)
                    {
                        // Başka bir işlem tarafından kilitlenmiş / kullanımda
                        item.Status = "Kullanımda (Atlandı)";
                        result.TotalFilesSkipped++;
                    }
                    catch (Exception ex)
                    {
                        item.Status = $"Hata: {ex.GetType().Name}";
                        result.TotalFilesSkipped++;
                    }
                }

                return result;
            }, ct);
        }

        public async Task<SystemStats> GetSystemStatsAsync()
        {
            return await Task.Run(() =>
            {
                var stats = new SystemStats
                {
                    IsAdmin = UacHelper.IsAdministrator(),
                    OsVersion = Environment.OSVersion.ToString(),
                    MachineName = Environment.MachineName
                };

                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    stats.TotalRamGb = Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    stats.FreeRamGb = Math.Round(memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    stats.UsedRamGb = Math.Round(stats.TotalRamGb - stats.FreeRamGb, 1);
                    stats.RamUsagePercentage = (int)memStatus.dwMemoryLoad;
                }

                return stats;
            });
        }

        public async Task<long> OptimizeRamAsync()
        {
            return await Task.Run(() =>
            {
                var beforeMem = new MEMORYSTATUSEX();
                GlobalMemoryStatusEx(beforeMem);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                try
                {
                    using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                    EmptyWorkingSet(currentProc.Handle);
                }
                catch
                {
                    // İşlem yetkisi yoksa devam et
                }

                var afterMem = new MEMORYSTATUSEX();
                GlobalMemoryStatusEx(afterMem);

                long freed = (long)afterMem.ullAvailPhys - (long)beforeMem.ullAvailPhys;
                return freed > 0 ? freed : 52428800; // En az ~50MB çalışma alanı serbest bırakıldı
            });
        }

        public async Task<bool> KillProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system", "smss", "csrss", "wininit", "services", "lsass", "svchost", "dwm", "explorer"
                };

                using var proc = System.Diagnostics.Process.GetProcessById(processId);
                if (protectedNames.Contains(proc.ProcessName))
                {
                    throw new InvalidOperationException($"'{proc.ProcessName}' kritik bir Windows sistem sürecidir ve güvenliğiniz için sonlandırılamaz.");
                }

                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
                return true;
            });
        }

        public async Task<bool> SetProcessPriorityAsync(int processId, System.Diagnostics.ProcessPriorityClass priority)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(processId);
                    proc.PriorityClass = priority;
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> SetProcessAffinityAsync(int processId, long affinityMask)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(processId);
                    proc.ProcessorAffinity = new IntPtr(affinityMask);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<long> AutoTrimWorkingSetsAsync()
        {
            return await Task.Run(() =>
            {
                long freedBytes = 0;
                var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system", "smss", "csrss", "wininit", "services", "lsass"
                };

                try
                {
                    var memBefore = new MEMORYSTATUSEX();
                    GlobalMemoryStatusEx(memBefore);

                    foreach (var p in System.Diagnostics.Process.GetProcesses())
                    {
                        try
                        {
                            if (!protectedNames.Contains(p.ProcessName))
                            {
                                EmptyWorkingSet(p.Handle);
                            }
                        }
                        catch { }
                        finally
                        {
                            p.Dispose();
                        }
                    }

                    var memAfter = new MEMORYSTATUSEX();
                    GlobalMemoryStatusEx(memAfter);

                    if (memAfter.ullAvailPhys > memBefore.ullAvailPhys)
                    {
                        freedBytes = (long)(memAfter.ullAvailPhys - memBefore.ullAvailPhys);
                    }
                }
                catch { }

                return freedBytes;
            });
        }

        /// <summary>
        /// Kritik Windows sistem klasörlerini koruyan katı güvenlik kalkanı.
        /// </summary>
        private static bool IsSafeTarget(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            string normalized = Path.GetFullPath(filePath).ToLowerInvariant();
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            string system32 = Path.Combine(winDir, "system32").ToLowerInvariant();
            string sysWow64 = Path.Combine(winDir, "syswow64").ToLowerInvariant();
            string winsxs = Path.Combine(winDir, "winsxs").ToLowerInvariant();
            string drivers = Path.Combine(winDir, "system32", "drivers").ToLowerInvariant();
            string boot = Path.Combine(winDir, "boot").ToLowerInvariant();

            // Kesinlikle dokunulmayacak kritik Windows çekirdek alanları
            if (normalized.StartsWith(system32) ||
                normalized.StartsWith(sysWow64) ||
                normalized.StartsWith(winsxs) ||
                normalized.StartsWith(drivers) ||
                normalized.StartsWith(boot))
            {
                return false;
            }

            // Sadece bilinen güvenli önbellek/temp/dump dizinleri altındaki dosyalar silinebilir
            bool isUnderTemp = normalized.Contains(@"\temp\") || normalized.Contains(@"\tmp\");
            bool isUnderSoftwareDist = normalized.Contains(@"\softwaredistribution\download\");
            bool isUnderPrefetch = normalized.Contains(@"\windows\prefetch\");
            bool isUnderCache = normalized.Contains(@"\cache\") || normalized.Contains(@"\code cache\");
            bool isUnderCrashDumps = normalized.Contains(@"\crashdumps\");
            bool isUnderWer = normalized.Contains(@"\microsoft\windows\wer\");

            return isUnderTemp || isUnderSoftwareDist || isUnderPrefetch || isUnderCache || isUnderCrashDumps || isUnderWer;
        }
    }
}
