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

        [DllImport("ntdll.dll")]
        private static extern uint NtSetSystemInformation(int infoClass, IntPtr info, int length);

        [DllImport("ntdll.dll")]
        private static extern uint NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern uint NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const int SystemMemoryListInformation = 80;
        private const int MemoryFlushModifiedList = 1;
        private const int MemoryPurgeStandbyList = 2;

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

        public DriveInfoItem GetSystemDriveInfo()
        {
            try
            {
                string sysPath = Environment.SystemDirectory;
                string? driveRoot = Path.GetPathRoot(sysPath);
                if (string.IsNullOrEmpty(driveRoot)) driveRoot = "C:\\";

                var dInfo = new DriveInfo(driveRoot);
                if (dInfo.IsReady)
                {
                    double totalGb = Math.Round(dInfo.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                    double freeGb = Math.Round(dInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                    double usedGb = Math.Round(totalGb - freeGb, 1);
                    int usagePct = totalGb > 0 ? (int)((usedGb / totalGb) * 100) : 0;

                    return new DriveInfoItem
                    {
                        Name = dInfo.Name.TrimEnd('\\'),
                        VolumeLabel = string.IsNullOrWhiteSpace(dInfo.VolumeLabel) ? "Yerel Disk" : dInfo.VolumeLabel,
                        DriveFormat = dInfo.DriveFormat,
                        TotalGb = totalGb,
                        FreeGb = freeGb,
                        UsedGb = usedGb,
                        UsagePercentage = usagePct
                    };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AppLog.Warning("Sistem sürücüsü bilgisi okunamadı.", ex, nameof(SystemCleanService));
            }

            // Okunamadıysa bunu açıkça söyle. (Eskiden sabit "256 GB / %60 dolu"
            // değerleri gerçekmiş gibi gösteriliyordu.)
            return new DriveInfoItem
            {
                Name = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:",
                VolumeLabel = "Disk bilgisi okunamadı",
                DriveFormat = "—",
                IsAvailable = false
            };
        }

        public List<CleanCategory> GetDefaultCategories()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingApp = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return new List<CleanCategory>
            {
                // ==================== GRUP 1: WINDOWS & SİSTEM ====================
                new CleanCategory
                {
                    Id = "user_temp",
                    Name = "Kullanıcı Geçici Dosyaları (%TEMP%)",
                    Description = "Uygulamaların geride bıraktığı geçici çalışma ve oturum kalıntıları.",
                    TargetPath = Path.GetTempPath(),
                    GroupName = "Windows & Sistem",
                    IconSymbol = "Folder24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "windows_temp",
                    Name = "Windows Sistem Temp",
                    Description = "Windows sistem hizmetlerinin oluşturduğu genel geçici dosyalar.",
                    TargetPath = Path.Combine(winDir, "Temp"),
                    GroupName = "Windows & Sistem",
                    IconSymbol = "FolderZip24",
                    RequiresAdmin = true,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "software_distribution",
                    Name = "Windows Update İndirme Deposu",
                    Description = "Daha önce yüklenmiş Windows güncellemelerinden kalan kurulum paketleri.",
                    TargetPath = Path.Combine(winDir, "SoftwareDistribution", "Download"),
                    GroupName = "Windows & Sistem",
                    IconSymbol = "ArrowDownload24",
                    RequiresAdmin = true,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "delivery_optimization",
                    Name = "Windows Teslim İyileştirme Önbelleği",
                    Description = "Ağ üzerinden paylaşılan güncelleme parçacıkları ve önbellekleri.",
                    TargetPath = Path.Combine(winDir, "SoftwareDistribution", "DeliveryOptimization", "Cache"),
                    AdditionalPaths = new List<string> { Path.Combine(localApp, "Microsoft", "Windows", "DeliveryOptimization") },
                    GroupName = "Windows & Sistem",
                    IconSymbol = "ArrowDownload24",
                    RequiresAdmin = true,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "prefetch",
                    Name = "Windows Prefetch Önbelleği",
                    Description = "Eski ve artık sistemde bulunmayan uygulama açılış izleri.",
                    TargetPath = Path.Combine(winDir, "Prefetch"),
                    GroupName = "Windows & Sistem",
                    IconSymbol = "Flash24",
                    RequiresAdmin = true,
                    IsDeepClean = true,
                    IsSelected = false
                },
                new CleanCategory
                {
                    Id = "crash_dumps",
                    Name = "Bellek ve Çökme Dökümleri (Crash Dumps)",
                    Description = "Çöken uygulamaların ve sistemin diske bıraktığı bellek dökümleri.",
                    TargetPath = Path.Combine(localApp, "CrashDumps"),
                    AdditionalPaths = new List<string> { Path.Combine(winDir, "Minidump") },
                    GroupName = "Windows & Sistem",
                    IconSymbol = "DocumentError24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "wer_reports",
                    Name = "Windows Hata Raporlama Kalıntıları (WER)",
                    Description = "İşletim sistemi hata bildirim kuyrukları ve arşiv kalıntıları.",
                    TargetPath = Path.Combine(localApp, "Microsoft", "Windows", "WER"),
                    AdditionalPaths = new List<string> { Path.Combine(progData, "Microsoft", "Windows", "WER") },
                    GroupName = "Windows & Sistem",
                    IconSymbol = "ShieldError24",
                    RequiresAdmin = false,
                    IsSelected = false
                },
                new CleanCategory
                {
                    Id = "thumb_cache",
                    Name = "Küçük Resim (Thumbnail) Önbelleği",
                    Description = "Dosya Gezgini'nin resim ve videolar için oluşturduğu veritabanı kalıntıları.",
                    TargetPath = Path.Combine(localApp, "Microsoft", "Windows", "Explorer"),
                    FilePattern = "thumbcache_*.db",
                    GroupName = "Windows & Sistem",
                    IconSymbol = "Image24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "system_logs",
                    Name = "Eski Windows & CBS Günlükleri",
                    Description = "Windows ve bileşen yükleyicisi tarafından üretilmiş eski log dosyaları.",
                    TargetPath = Path.Combine(winDir, "Logs"),
                    AdditionalPaths = new List<string> { Path.Combine(winDir, "Panther") },
                    GroupName = "Windows & Sistem",
                    IconSymbol = "DocumentBulletList24",
                    RequiresAdmin = true,
                    IsDeepClean = true,
                    IsSelected = false
                },
                new CleanCategory
                {
                    Id = "directx_shader",
                    Name = "DirectX Shader & GPU Önbelleği",
                    Description = "Ekran kartı ve DirectX API'sinin derlenmiş gölgelendirici önbellekleri.",
                    TargetPath = Path.Combine(localApp, "D3DSCache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(localApp, "NVIDIA", "DXCache"),
                        Path.Combine(localApp, "AMD", "DxCache")
                    },
                    GroupName = "Windows & Sistem",
                    IconSymbol = "Sparkle24",
                    RequiresAdmin = false,
                    IsSelected = true
                },

                // ==================== GRUP 2: WEB TARAYICILARI & İLETİŞİM ====================
                new CleanCategory
                {
                    Id = "edge_cache",
                    Name = "Microsoft Edge Web Önbelleği",
                    Description = "Edge web tarayıcısının geçici internet verileri ve GPU önbellekleri.",
                    TargetPath = Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(localApp, "Microsoft", "Edge", "User Data", "Default", "Code Cache")
                    },
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Globe24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "chrome_cache",
                    Name = "Google Chrome Web Önbelleği",
                    Description = "Chrome tarayıcısının diskte biriktirdiği geçici web ve medya verileri.",
                    TargetPath = Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "Cache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(localApp, "Google", "Chrome", "User Data", "Default", "Code Cache")
                    },
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Globe24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "firefox_cache",
                    Name = "Mozilla Firefox Önbelleği",
                    Description = "Firefox profil dizinindeki HTTP ve medya önbellek kalıntıları.",
                    TargetPath = Path.Combine(localApp, "Mozilla", "Firefox", "Profiles"),
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Globe24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "brave_cache",
                    Name = "Brave Tarayıcı Önbelleği",
                    Description = "Brave Browser web ve render önbellek dosyaları.",
                    TargetPath = Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Globe24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "discord_cache",
                    Name = "Discord Medya & Kod Önbelleği",
                    Description = "Discord istemcisinin önbelleğe aldığı görseller, sesler ve kod blokları.",
                    TargetPath = Path.Combine(roamingApp, "discord", "Cache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(roamingApp, "discord", "Code Cache")
                    },
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Chat24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "telegram_cache",
                    Name = "Telegram Masaüstü Medya Önbelleği",
                    Description = "Telegram Desktop üzerinden indirilen geçici medya ve çıkartma önbellekleri.",
                    TargetPath = Path.Combine(roamingApp, "Telegram Desktop", "tdata", "user_data", "cache"),
                    GroupName = "Web Tarayıcıları",
                    IconSymbol = "Send24",
                    RequiresAdmin = false,
                    IsSelected = true
                },

                // ==================== GRUP 3: OYUNLAR & MEDYA / GELİŞTİRİCİ ====================
                new CleanCategory
                {
                    Id = "steam_cache",
                    Name = "Steam İstemci & Web Önbelleği",
                    Description = "Steam istemcisinin mağaza ve topluluk sayfaları için tuttuğu web önbellekleri.",
                    TargetPath = Path.Combine(progFilesX86, "Steam", "appcache", "httpcache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(localApp, "Steam", "htmlcache")
                    },
                    GroupName = "Oyunlar & Medya",
                    IconSymbol = "Games24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "spotify_cache",
                    Name = "Spotify Akış & Şarkı Deposu",
                    Description = "Spotify masaüstü uygulamasının diske indirdiği çevrimdışı ve akış önbellekleri.",
                    TargetPath = Path.Combine(localApp, "Spotify", "Storage"),
                    GroupName = "Oyunlar & Medya",
                    IconSymbol = "MusicNote224",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "epic_cache",
                    Name = "Epic Games Başlatıcı Önbelleği",
                    Description = "Epic Games Launcher web ve arayüz önbellek kalıntıları.",
                    TargetPath = Path.Combine(localApp, "EpicGamesLauncher", "Saved", "webcache"),
                    GroupName = "Oyunlar & Medya",
                    IconSymbol = "Games24",
                    RequiresAdmin = false,
                    IsSelected = true
                },
                new CleanCategory
                {
                    Id = "nuget_npm_cache",
                    Name = "Geliştirici Paket Önbellekleri (NuGet/npm/pip)",
                    Description = "Yazılım geliştirme ortamlarının paket depolarından arta kalan geçici indirmeler.",
                    TargetPath = Path.Combine(userProfile, ".nuget", "packages", ".cache"),
                    AdditionalPaths = new List<string>
                    {
                        Path.Combine(roamingApp, "npm-cache"),
                        Path.Combine(localApp, "pip", "cache")
                    },
                    GroupName = "Oyunlar & Medya",
                    IconSymbol = "Code24",
                    RequiresAdmin = false,
                    IsDeepClean = true,
                    IsSelected = false
                }
            };
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

                var pathsToScan = new List<string>();
                if (!string.IsNullOrWhiteSpace(category.TargetPath))
                    pathsToScan.Add(category.TargetPath);

                if (category.AdditionalPaths != null && category.AdditionalPaths.Count > 0)
                {
                    foreach (var p in category.AdditionalPaths)
                    {
                        if (!string.IsNullOrWhiteSpace(p) && !pathsToScan.Contains(p, StringComparer.OrdinalIgnoreCase))
                            pathsToScan.Add(p);
                    }
                }

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    ReturnSpecialDirectories = false
                };

                string pattern = string.IsNullOrWhiteSpace(category.FilePattern) ? "*" : category.FilePattern;

                foreach (var path in pathsToScan)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!Directory.Exists(path))
                        continue;

                    try
                    {
                        var dirInfo = new DirectoryInfo(path);
                        foreach (var file in dirInfo.EnumerateFiles(pattern, enumOptions))
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
                                    DirectoryPath = file.DirectoryName ?? string.Empty,
                                    Extension = file.Extension.ToLowerInvariant(),
                                    LastModified = file.LastWriteTime,
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
                    catch (UnauthorizedAccessException) { }
                    catch (Exception) { }
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

                    if (processed % 10 == 0)
                    {
                        Thread.Sleep(3);
                    }

                    // 0. Kullanıcı Tarafından Hariç Tutulan Dosyalar
                    if (item.IsExcluded)
                    {
                        item.Status = "Muaf Tutuldu (Atlandı)";
                        result.TotalFilesSkipped++;
                        continue;
                    }

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

                // Ölçülen gerçek fark döner (0 olabilir). Eskiden fark yoksa sabit
                // "50 MB" döndürülüyor ve kullanıcıya boşaltılmış gibi gösteriliyordu.
                return Math.Max(0, (long)afterMem.ullAvailPhys - (long)beforeMem.ullAvailPhys);
            });
        }

        public Task<long> ClearStandbyListAsync() => RunMemoryListCommandAsync(MemoryPurgeStandbyList, "Bekleme listesi");

        public Task<long> FlushModifiedPagesAsync() => RunMemoryListCommandAsync(MemoryFlushModifiedList, "Değiştirilmiş sayfa listesi");

        /// <summary>
        /// NtSetSystemInformation(SystemMemoryListInformation) komutunu çalıştırır.
        /// SeProfileSingleProcessPrivilege etkinleştirilir (yalnızca yöneticilerde
        /// mümkündür) ve NTSTATUS kontrol edilir; başarısızsa 0 döner.
        /// </summary>
        private async Task<long> RunMemoryListCommandAsync(int command, string label)
        {
            return await Task.Run(() =>
            {
                if (!Bakım.Helpers.TokenPrivilege.TryEnable(Bakım.Helpers.TokenPrivilege.ProfileSingleProcess))
                {
                    AppLog.Info($"{label} boşaltılamadı: yönetici yetkisi gerekiyor.", nameof(SystemCleanService));
                    return 0L;
                }

                var beforeMem = new MEMORYSTATUSEX();
                GlobalMemoryStatusEx(beforeMem);

                uint status;
                IntPtr pCommand = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    Marshal.WriteInt32(pCommand, command);
                    status = NtSetSystemInformation(SystemMemoryListInformation, pCommand, sizeof(int));
                }
                finally
                {
                    Marshal.FreeHGlobal(pCommand);
                }

                if (status != 0)
                {
                    AppLog.Warning($"{label} boşaltılamadı (NTSTATUS 0x{status:X8}).", null, nameof(SystemCleanService));
                    return 0L;
                }

                var afterMem = new MEMORYSTATUSEX();
                GlobalMemoryStatusEx(afterMem);
                return Math.Max(0, (long)afterMem.ullAvailPhys - (long)beforeMem.ullAvailPhys);
            });
        }

        public async Task<long> PurgeAllMemoryAsync()
        {
            long t1 = await AutoTrimWorkingSetsAsync();
            long t2 = await FlushModifiedPagesAsync();
            long t3 = await ClearStandbyListAsync();
            return t1 + t2 + t3; // Gerçek ölçüm; eskiden en az "150 MB" gösteriliyordu.
        }

        public async Task<bool> SuspendProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system", "smss", "csrss", "wininit", "services", "lsass", "svchost", "dwm", "explorer"
                };

                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(processId);
                    if (protectedNames.Contains(proc.ProcessName)) return false;

                    IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            uint status = NtSuspendProcess(handle);
                            return status == 0;
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
                catch { }

                return false;
            });
        }

        public async Task<bool> ResumeProcessAsync(int processId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            uint status = NtResumeProcess(handle);
                            return status == 0;
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                }
                catch { }

                return false;
            });
        }

        public async Task<DetailedMemoryComposition> GetDetailedMemoryCompositionAsync()
        {
            return await Task.Run(() =>
            {
                var composition = new DetailedMemoryComposition();
                var memStatus = new MEMORYSTATUSEX();

                if (GlobalMemoryStatusEx(memStatus))
                {
                    double totalGb = Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    double freeGb = Math.Round(memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
                    double usedGb = Math.Max(0, totalGb - freeGb);

                    double inUseGb = Math.Round(usedGb * 0.72, 1);
                    double standbyGb = Math.Round(usedGb * 0.22, 1);
                    double modifiedGb = Math.Round(Math.Max(0.1, usedGb - inUseGb - standbyGb), 1);

                    composition.TotalGb = totalGb;
                    composition.FreeGb = freeGb;
                    composition.InUseGb = inUseGb;
                    composition.StandbyGb = standbyGb;
                    composition.ModifiedGb = modifiedGb;

                    double commitLimit = Math.Round(memStatus.ullTotalPageFile / (1024.0 * 1024.0 * 1024.0), 1);
                    double commitAvail = Math.Round(memStatus.ullAvailPageFile / (1024.0 * 1024.0 * 1024.0), 1);
                    composition.CommitLimitGb = commitLimit;
                    composition.CommitTotalGb = Math.Max(0, Math.Round(commitLimit - commitAvail, 1));

                    composition.PagedPoolMb = Math.Round(inUseGb * 0.08 * 1024, 0);
                    composition.NonPagedPoolMb = Math.Round(inUseGb * 0.04 * 1024, 0);
                }

                return composition;
            });
        }

        /// <summary>
        /// Süreci ortak güvenlik kuralıyla sonlandırır (H-7). Eski yerel liste yalnızca 9 adı
        /// koruyordu; Windows klasöründeki diğer ikililer (winlogon, fontdrvhost…) ve
        /// Bakım'ın kendisi sonlandırılabiliyordu. Başarısızlıkta nedeni istisna olarak taşır.
        /// </summary>
        public async Task<bool> KillProcessAsync(int processId)
        {
            var safeProcess = App.TryGetService<Bakım.Services.Safety.ISafeProcessService>()
                              ?? new Bakım.Services.Safety.SafeProcessService(AppLog.Current);
            var result = await safeProcess.TerminateProcessAsync(processId);
            if (result.Succeeded || result.Outcome == Bakım.Services.Safety.DeleteOutcome.NotFound) return true;
            throw new InvalidOperationException(result.Outcome == Bakım.Services.Safety.DeleteOutcome.Blocked
                ? "Bu bir Windows sistem sürecidir; sistem kararlılığı için sonlandırılamaz."
                : result.Message);
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

            // Sadece bilinen güvenli önbellek/temp/dump/log dizinleri altındaki dosyalar silinebilir
            bool isUnderTemp = normalized.Contains(@"\temp\") || normalized.Contains(@"\tmp\");
            bool isUnderSoftwareDist = normalized.Contains(@"\softwaredistribution\download\") || normalized.Contains(@"\deliveryoptimization\");
            bool isUnderPrefetch = normalized.Contains(@"\windows\prefetch\");
            bool isUnderCache = normalized.Contains(@"\cache\") || normalized.Contains(@"\code cache\") || normalized.Contains(@"\appcache\") || normalized.Contains(@"\webcache\") || normalized.Contains(@"\htmlcache\");
            bool isUnderCrashDumps = normalized.Contains(@"\crashdumps\") || normalized.Contains(@"\minidump\");
            bool isUnderWer = normalized.Contains(@"\microsoft\windows\wer\");
            bool isUnderExplorer = normalized.Contains(@"\microsoft\windows\explorer\") && normalized.Contains("thumbcache_");
            bool isUnderLogs = normalized.Contains(@"\windows\logs\") || normalized.Contains(@"\windows\panther\");
            bool isUnderShader = normalized.Contains(@"\d3dscache\") || normalized.Contains(@"\dxcache\");
            bool isUnderAppStorage = normalized.Contains(@"\spotify\storage\") || normalized.Contains(@"\telegram desktop\") || normalized.Contains(@"\npm-cache\") || normalized.Contains(@"\pip\cache\") || normalized.Contains(@"\.nuget\packages\.cache\");

            return isUnderTemp || isUnderSoftwareDist || isUnderPrefetch || isUnderCache || isUnderCrashDumps || isUnderWer || isUnderExplorer || isUnderLogs || isUnderShader || isUnderAppStorage;
        }
    }
}
