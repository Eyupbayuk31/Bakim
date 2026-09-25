using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Storage;

namespace Bakım.Services
{
    public sealed record DiskMapProgress(string CurrentFolder, long FilesScanned, long BytesCounted);

    /// <summary>
    /// Disk Haritası (MASTER_PLAN §5.3): klasör boyutlarını ağaç olarak çıkarır.
    ///   • Bağlantı noktalarına (junction/symlink) inilmez: aynı veri iki kez sayılmaz, döngü olmaz.
    ///   • OneDrive yer tutucuları (yalnızca çevrimiçi) diskte yer kaplamaz; boyutları sayılmaz ve
    ///     okunmadıkları için indirme tetiklenmez.
    ///   • Her klasörde en büyük 40 alt klasör tutulur, gerisi "Diğer" düğümüne katlanır (bellek sabit).
    /// </summary>
    public interface IDiskMapService
    {
        Task<FolderNode> ScanAsync(string root, IProgress<DiskMapProgress>? progress, CancellationToken ct);
    }

    public sealed class DiskMapService : IDiskMapService
    {
        private const int KeepChildren = 40;
        private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

        public Task<FolderNode> ScanAsync(string root, IProgress<DiskMapProgress>? progress, CancellationToken ct) =>
            Task.Run(() =>
            {
                string full = Path.GetFullPath(root);
                var state = new ScanState(progress);
                var node = ScanDirectory(new DirectoryInfo(full), parent: null, state, ct, depth: 0);
                state.Report(full, force: true);
                return node;
            }, ct);

        private sealed class ScanState
        {
            private readonly IProgress<DiskMapProgress>? _progress;
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private long _lastReportMs = -1000;
            public long Files;
            public long Bytes;

            public ScanState(IProgress<DiskMapProgress>? progress) => _progress = progress;

            public void Report(string folder, bool force = false)
            {
                if (_progress == null) return;
                long now = _clock.ElapsedMilliseconds;
                if (!force && now - Interlocked.Read(ref _lastReportMs) < 200) return;
                Interlocked.Exchange(ref _lastReportMs, now);
                _progress.Report(new DiskMapProgress(folder, Interlocked.Read(ref Files), Interlocked.Read(ref Bytes)));
            }
        }

        private static readonly EnumerationOptions Options = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        private static FolderNode ScanDirectory(DirectoryInfo dir, FolderNode? parent, ScanState state, CancellationToken ct, int depth)
        {
            ct.ThrowIfCancellationRequested();
            string name = parent == null ? dir.FullName : dir.Name;
            var node = new FolderNode(name, dir.FullName) { Parent = parent };

            long directBytes = 0, directFiles = 0;
            var subdirs = new List<DirectoryInfo>();
            try
            {
                foreach (var entry in dir.EnumerateFileSystemInfos("*", Options))
                {
                    if (entry is FileInfo file)
                    {
                        var attrs = file.Attributes;
                        if ((attrs & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0) continue;
                        long len;
                        try { len = file.Length; }
                        catch (IOException) { continue; }
                        directBytes += len;
                        directFiles++;
                    }
                    else if (entry is DirectoryInfo sub)
                    {
                        subdirs.Add(sub);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Erişilemeyen klasör boyutu 0 görünür; tarama durmaz.
            }

            Interlocked.Add(ref state.Files, directFiles);
            Interlocked.Add(ref state.Bytes, directBytes);
            state.Report(dir.FullName);

            // Üst düzeyde paralel: sürücü kökündeki büyük klasörler (Windows, Users …) aynı anda taranır.
            if (depth == 0 && subdirs.Count > 1)
            {
                var children = new FolderNode[subdirs.Count];
                Parallel.For(0, subdirs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
                    i => children[i] = ScanDirectory(subdirs[i], node, state, ct, depth + 1));
                node.Children.AddRange(children);
            }
            else
            {
                foreach (var sub in subdirs) node.Children.Add(ScanDirectory(sub, node, state, ct, depth + 1));
            }

            node.SizeBytes = directBytes + node.Children.Sum(c => c.SizeBytes);
            node.FileCount = directFiles + node.Children.Sum(c => c.FileCount);
            node.Compact(KeepChildren, directBytes, directFiles);
            return node;
        }
    }
}
