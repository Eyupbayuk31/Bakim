using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IDuplicateFinderService
    {
        Task<List<DuplicateFileGroup>> ScanDuplicatesAsync(DuplicateScanOptions options, IProgress<DuplicateScanProgress>? progress, CancellationToken ct);
        Task<List<EmptyFolderItem>> ScanEmptyFoldersAsync(string targetPath, IProgress<string>? progress, CancellationToken ct);
        Task<(int SuccessCount, long FreedBytes)> DeleteDuplicatesAsync(List<DuplicateFileItem> files, bool moveToRecycleBin);
        Task<int> DeleteEmptyFoldersAsync(List<EmptyFolderItem> folders);
    }

    public class DuplicateFinderService : IDuplicateFinderService
    {
        #region Windows Shell File Deletion to Recycle Bin (P/Invoke)

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.U4)]
            public int wFunc;
            public string pFrom;
            public string pTo;
            public short fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const int FO_DELETE = 0x0003;
        private const short FOF_ALLOWUNDO = 0x0040;
        private const short FOF_NOCONFIRMATION = 0x0010;
        private const short FOF_SILENT = 0x0004;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        private static bool SendToRecycleBin(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) && !Directory.Exists(filePath)) return false;

                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = filePath + '\0' + '\0',
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
                };

                int result = SHFileOperation(ref shf);
                return result == 0 && !shf.fAnyOperationsAborted;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Korumalı Sistem Klasörleri

        private static readonly string[] ProtectedDirectoryKeywords = new[]
        {
            @"\windows\",
            @"\$recycle.bin",
            @"\system volume information",
            @"\recovery\",
            @"\programdata\microsoft",
            @"\appdata\local\microsoft\windows",
            @"\program files\windowsapps"
        };

        // Bulut yer tutucuları (OneDrive "yalnızca çevrimiçi"): içeriği okumak dosyayı İNDİRİR (H-10).
        private const int FileAttributeRecallOnOpen = 0x0004_0000;
        private const int FileAttributeRecallOnDataAccess = 0x0040_0000;

        private static bool IsCloudPlaceholder(FileSystemInfo info) =>
            ((int)info.Attributes & ((int)FileAttributes.Offline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess)) != 0;

        /// <summary>Bağlantı noktalarına (junction/symlink) inilmez: "Application Data" gibi döngüler ve hedef dışına taşma.</summary>
        private static bool IsReparsePoint(string dirPath)
        {
            try { return File.GetAttributes(dirPath).HasFlag(FileAttributes.ReparsePoint); }
            catch { return true; }
        }

        private static bool IsDirectoryProtected(string dirPath)
        {
            string lower = dirPath.ToLowerInvariant();
            if (!lower.EndsWith(@"\")) lower += @"\";

            foreach (var kw in ProtectedDirectoryKeywords)
            {
                if (lower.Contains(kw)) return true;
            }
            return false;
        }

        #endregion

        #region Dosya Uzantı Kategori Eşleme

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".svg", ".ico", ".raw", ".cr2", ".nef"
        };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts"
        };

        private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".txt", ".rtf", ".csv", ".md"
        };

        private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".vhd"
        };

        private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a"
        };

        private static string ClassifyCategory(string extension)
        {
            if (ImageExts.Contains(extension)) return "Resim";
            if (VideoExts.Contains(extension)) return "Video";
            if (DocExts.Contains(extension)) return "Belge";
            if (ArchiveExts.Contains(extension)) return "Arşiv";
            if (AudioExts.Contains(extension)) return "Ses";
            return "Diğer";
        }

        private static bool MatchesTypeFilter(string extension, string filter)
        {
            if (string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(filter, "Images", StringComparison.OrdinalIgnoreCase)) return ImageExts.Contains(extension);
            if (string.Equals(filter, "Videos", StringComparison.OrdinalIgnoreCase)) return VideoExts.Contains(extension);
            if (string.Equals(filter, "Documents", StringComparison.OrdinalIgnoreCase)) return DocExts.Contains(extension);
            if (string.Equals(filter, "Archives", StringComparison.OrdinalIgnoreCase)) return ArchiveExts.Contains(extension);
            if (string.Equals(filter, "Audio", StringComparison.OrdinalIgnoreCase)) return AudioExts.Contains(extension);
            return true;
        }

        #endregion

        /// <summary>
        /// 3 Aşamalı Ultra Hızlı Tarama: Boyut Eşleme -> İlk 4 KB Başlık Hash'i -> Tam SHA-256
        /// </summary>
        public async Task<List<DuplicateFileGroup>> ScanDuplicatesAsync(DuplicateScanOptions options, IProgress<DuplicateScanProgress>? progress, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var progressState = new DuplicateScanProgress
                {
                    CurrentStage = "1/3: Dosyalar taranıyor ve boyutlarına göre gruplanıyor..."
                };

                // AŞAMA 1: Dosya Ağacını Tara & Boyuta Göre Grupla
                var filesBySize = new Dictionary<long, List<FileInfo>>();
                int scannedCount = 0;

                try
                {
                    var stack = new Stack<string>();
                    if (Directory.Exists(options.TargetPath))
                    {
                        stack.Push(options.TargetPath);
                    }

                    while (stack.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        string currentDir = stack.Pop();

                        if (options.ExcludeSystemDirs && IsDirectoryProtected(currentDir))
                            continue;

                        // Alt klasörleri yığına ekle
                        try
                        {
                            foreach (var subDir in Directory.GetDirectories(currentDir))
                            {
                                if (!IsReparsePoint(subDir)) stack.Push(subDir);
                            }
                        }
                        catch { }

                        // Klasördeki dosyaları oku
                        try
                        {
                            var dirInfo = new DirectoryInfo(currentDir);
                            foreach (var file in dirInfo.GetFiles())
                            {
                                ct.ThrowIfCancellationRequested();
                                scannedCount++;

                                if (scannedCount % 250 == 0)
                                {
                                    progressState.ScannedFiles = scannedCount;
                                    progressState.CurrentFilePath = file.FullName;
                                    progress?.Report(progressState);
                                }

                                if (file.Length < options.MinSizeBytes)
                                    continue;

                                if (IsCloudPlaceholder(file))
                                {
                                    progressState.SkippedCloudFiles++;
                                    continue;
                                }

                                string ext = file.Extension;
                                if (!MatchesTypeFilter(ext, options.FileTypeFilter))
                                    continue;

                                if (!filesBySize.TryGetValue(file.Length, out var list))
                                {
                                    list = new List<FileInfo>();
                                    filesBySize[file.Length] = list;
                                }
                                list.Add(file);
                            }
                        }
                        catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }

                // Yalnızca boyutu aynı olan birden fazla dosya içeren grupları al (Tekilleri %90 ele)
                // Sabit bağlantılar (hardlink) aynı dosyadır: diskte bir kez yer kaplar, silmek yer açmaz.
                // Aynı dosya kimliğine sahip yollar tek aday sayılır (§5.3).
                var sizeCandidates = filesBySize.Values
                    .Where(g => g.Count > 1)
                    .Select(g => CollapseHardlinks(g, progressState))
                    .Where(g => g.Count > 1)
                    .ToList();
                progressState.ScannedFiles = scannedCount;
                progressState.CandidateGroups = sizeCandidates.Count;
                progressState.CurrentStage = "2/3: Aday dosyaların ilk 4 KB başlıkları doğrulanıyor...";
                progress?.Report(progressState);

                // AŞAMA 2: İlk 4 KB Başlık Hash'i Al
                var filesByHeader = new Dictionary<string, List<FileInfo>>();
                int headerEvaluated = 0;
                int totalCandidateFiles = sizeCandidates.Sum(g => g.Count);

                foreach (var group in sizeCandidates)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var file in group)
                    {
                        ct.ThrowIfCancellationRequested();
                        headerEvaluated++;

                        if (headerEvaluated % 50 == 0)
                        {
                            progressState.CurrentFilePath = file.FullName;
                            progressState.ProgressPercentage = totalCandidateFiles > 0 ? (int)((headerEvaluated / (double)totalCandidateFiles) * 50) : 0;
                            progress?.Report(progressState);
                        }

                        string headerHash = ComputeFastHeaderHash(file.FullName);
                        if (string.IsNullOrEmpty(headerHash)) continue;

                        string key = $"{file.Length}_{headerHash}";
                        if (!filesByHeader.TryGetValue(key, out var list))
                        {
                            list = new List<FileInfo>();
                            filesByHeader[key] = list;
                        }
                        list.Add(file);
                    }
                }

                // Yalnızca başlığı da aynı olan grupları al
                var headerCandidates = filesByHeader.Values.Where(g => g.Count > 1).ToList();
                progressState.CurrentStage = "3/3: Birebir eşleşen kopyalar için tam SHA-256 hesaplanıyor...";
                progress?.Report(progressState);

                // AŞAMA 3: Tam SHA-256 Hash Hesaplama ve Gruplama
                var confirmedGroups = new Dictionary<string, List<DuplicateFileItem>>();
                int sha256Evaluated = 0;
                int totalShaCandidates = headerCandidates.Sum(g => g.Count);
                int confirmedDuplicateCount = 0;

                foreach (var group in headerCandidates)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var file in group)
                    {
                        ct.ThrowIfCancellationRequested();
                        sha256Evaluated++;

                        if (sha256Evaluated % 10 == 0)
                        {
                            progressState.CurrentFilePath = file.FullName;
                            progressState.ProgressPercentage = 50 + (totalShaCandidates > 0 ? (int)((sha256Evaluated / (double)totalShaCandidates) * 50) : 0);
                            progressState.ConfirmedDuplicates = confirmedDuplicateCount;
                            progress?.Report(progressState);
                        }

                        string fullHash = ComputeFullSha256(file.FullName, ct);
                        if (string.IsNullOrEmpty(fullHash)) continue;

                        if (!confirmedGroups.TryGetValue(fullHash, out var list))
                        {
                            list = new List<DuplicateFileItem>();
                            confirmedGroups[fullHash] = list;
                        }

                        var item = new DuplicateFileItem
                        {
                            FilePath = file.FullName,
                            FileName = file.Name,
                            DirectoryPath = file.DirectoryName ?? string.Empty,
                            SizeBytes = file.Length,
                            FormattedSize = CleanCategory.FormatBytes(file.Length),
                            CreationTime = file.CreationTime,
                            LastWriteTime = file.LastWriteTime,
                            Sha256Hash = fullHash,
                            Extension = file.Extension.ToLowerInvariant(),
                            Category = ClassifyCategory(file.Extension)
                        };

                        list.Add(item);
                        if (list.Count > 1) confirmedDuplicateCount++;
                    }
                }

                // Final Grupları Hazırla
                var resultGroups = new List<DuplicateFileGroup>();
                int groupId = 1;

                foreach (var kvp in confirmedGroups)
                {
                    if (kvp.Value.Count <= 1) continue;

                    // Dosyaları oluşturulma tarihine göre sırala (en eski = orijinal)
                    var sorted = kvp.Value.OrderBy(f => f.CreationTime).ToList();

                    // İlk dosyayı orijinal yap, diğerlerini kopya ve seçili yap
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        if (i == 0)
                        {
                            sorted[i].IsOriginal = true;
                            sorted[i].IsSelected = false; // Orijinal varsayılan korunur
                        }
                        else
                        {
                            sorted[i].IsOriginal = false;
                            sorted[i].IsSelected = true; // Kopyalar varsayılan seçilidir
                        }
                    }

                    var groupItem = new DuplicateFileGroup
                    {
                        GroupId = groupId++,
                        Hash = kvp.Key,
                        SingleFileSizeBytes = sorted[0].SizeBytes,
                        FileSizeFormatted = sorted[0].FormattedSize
                    };

                    foreach (var f in sorted)
                    {
                        groupItem.Files.Add(f);
                    }

                    resultGroups.Add(groupItem);
                }

                progressState.ProgressPercentage = 100;
                progressState.ConfirmedDuplicates = resultGroups.Sum(g => g.GroupCount - 1);
                var notes = new List<string>();
                if (progressState.SkippedCloudFiles > 0)
                    notes.Add($"{progressState.SkippedCloudFiles:N0} bulut dosyası (yalnızca çevrimiçi) indirilmemek için atlandı");
                if (progressState.SkippedHardlinks > 0)
                    notes.Add($"{progressState.SkippedHardlinks:N0} sabit bağlantı aynı dosya olduğu için kopya sayılmadı");
                progressState.CurrentStage = notes.Count > 0
                    ? "Tarama tamamlandı. " + string.Join("; ", notes) + "."
                    : "Tarama tamamlandı!";
                progress?.Report(progressState);

                return resultGroups.OrderByDescending(g => g.TotalWastedBytes).ToList();
            }, ct);
        }

        /// <summary>
        /// Sahipsiz boş klasörleri tarar (içinde hiçbir dosya ve alt klasör bulunmayan).
        /// </summary>
        public async Task<List<EmptyFolderItem>> ScanEmptyFoldersAsync(string targetPath, IProgress<string>? progress, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                var emptyFolders = new List<EmptyFolderItem>();
                if (!Directory.Exists(targetPath)) return emptyFolders;

                // Hedef AppData'nın içinde değilse AppData taranmaz: uygulamalar boş klasörlerini
                // (önbellek, ayar, eklenti yerleri) bilerek tutar ve silinince bozulabilir (H-9).
                bool targetInsideAppData = targetPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)
                                           || targetPath.EndsWith(@"\AppData", StringComparison.OrdinalIgnoreCase);
                string rootFull = Path.GetFullPath(targetPath).TrimEnd('\\');

                try
                {
                    var stack = new Stack<string>();
                    stack.Push(targetPath);

                    while (stack.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        string currentDir = stack.Pop();

                        if (IsDirectoryProtected(currentDir)) continue;
                        if (!string.Equals(currentDir.TrimEnd('\\'), rootFull, StringComparison.OrdinalIgnoreCase) && IsReparsePoint(currentDir)) continue;
                        if (!targetInsideAppData && Path.GetFileName(currentDir).Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;

                        progress?.Report(currentDir);

                        string[] subDirs;
                        try
                        {
                            subDirs = Directory.GetDirectories(currentDir);
                        }
                        catch
                        {
                            continue;
                        }

                        // Alt klasörleri yığına ekle
                        foreach (var sub in subDirs)
                        {
                            stack.Push(sub);
                        }

                        // Eğer klasörde dosya ve alt klasör yoksa boş klasördür
                        try
                        {
                            // Taranan kökün kendisi asla listelenmez (eskiden boşsa kök silinebiliyordu).
                            bool isRoot = string.Equals(currentDir.TrimEnd('\\'), rootFull, StringComparison.OrdinalIgnoreCase);
                            if (!isRoot && subDirs.Length == 0 && Directory.GetFiles(currentDir).Length == 0)
                            {
                                var dirInfo = new DirectoryInfo(currentDir);
                                emptyFolders.Add(new EmptyFolderItem
                                {
                                    FolderPath = currentDir,
                                    FolderName = dirInfo.Name,
                                    ParentPath = dirInfo.Parent?.FullName ?? string.Empty,
                                    CreationTime = dirInfo.CreationTime,
                                    // Varsayılan seçili değil: kullanıcı neyi sileceğini kendisi seçer (H-9).
                                    IsSelected = false
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }

                return emptyFolders;
            }, ct);
        }

        /// <summary>
        /// Kopyaları siler. Her dosya PathSafetyGuard'dan geçer (Windows ve korumalı klasörler
        /// reddedilir); kalıcı silme yalnızca kullanıcı açıkça seçtiyse.
        /// </summary>
        public async Task<(int SuccessCount, long FreedBytes)> DeleteDuplicatesAsync(List<DuplicateFileItem> files, bool moveToRecycleBin)
        {
            var safeDelete = App.TryGetService<Safety.ISafeDeleteService>() ?? new Safety.SafeDeleteService(AppLog.Current);
            var policy = new Safety.DeletePolicy(Permanent: !moveToRecycleBin, AllowOutsideKnownRoots: true);
            int success = 0;
            long freed = 0;

            foreach (var file in files)
            {
                var result = await safeDelete.DeletePathAsync(file.FilePath, isDirectory: false, policy);
                if (result.Succeeded)
                {
                    success++;
                    freed += file.SizeBytes;
                }
            }
            return (success, freed);
        }

        public async Task<int> DeleteEmptyFoldersAsync(List<EmptyFolderItem> folders)
        {
            return await Task.Run(() =>
            {
                var guard = Bakım.Core.Safety.PathSafetyGuard.Default;
                int success = 0;
                foreach (var folder in folders)
                {
                    try
                    {
                        if (!Directory.Exists(folder.FolderPath) || IsDirectoryProtected(folder.FolderPath) || IsReparsePoint(folder.FolderPath))
                            continue;
                        if (!guard.CheckDeletion(folder.FolderPath, isDirectory: true, allowOutsideKnownRoots: true).IsAllowed)
                            continue;

                        // Güvenlik: gerçekten boş mu teyit et (tarama ile silme arasında dolmuş olabilir).
                        if (Directory.GetFileSystemEntries(folder.FolderPath).Length == 0)
                        {
                            Directory.Delete(folder.FolderPath, false);
                            success++;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return success;
            });
        }

        #region Hashing Yardımcıları

        #region Sabit bağlantı (hardlink) tanıma

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION info);

        /// <summary>Birden fazla bağlantısı olan dosyanın kimliği (birim + dosya dizini); tek bağlantılıysa null.</summary>
        private static (uint Volume, ulong Index)? HardlinkIdentity(string path)
        {
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!GetFileInformationByHandle(handle, out var info) || info.NumberOfLinks <= 1) return null;
                return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static List<FileInfo> CollapseHardlinks(List<FileInfo> group, DuplicateScanProgress progress)
        {
            var seen = new HashSet<(uint, ulong)>();
            var unique = new List<FileInfo>(group.Count);
            foreach (var file in group)
            {
                var id = HardlinkIdentity(file.FullName);
                if (id is { } key && !seen.Add(key))
                {
                    progress.SkippedHardlinks++;
                    continue;
                }
                unique.Add(file);
            }
            return unique;
        }

        #endregion

        private static string ComputeFastHeaderHash(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) return string.Empty;

                using var md5 = MD5.Create();
                byte[] hash = md5.ComputeHash(buffer, 0, bytesRead);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ComputeFullSha256(string filePath, CancellationToken ct)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);

                byte[] buffer = new byte[64 * 1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha256.TransformFinalBlock(buffer, 0, 0);
                return Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion
    }
}
