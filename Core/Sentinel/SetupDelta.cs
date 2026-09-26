using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    public enum FileChangeKind
    {
        Created,
        Changed,
        Deleted,
        Renamed
    }

    /// <summary>Kurulum oturumunda yakalanan tek dosya sistemi olayı. <see cref="Sequence"/> yakalanma sırasıdır.</summary>
    public readonly record struct FileChangeEvent(long Sequence, FileChangeKind Kind, string Path, string? OldPath = null);

    public enum PathState
    {
        Missing,
        File,
        Directory
    }

    /// <summary>Oturumun dosya sistemi farkı.</summary>
    public sealed class SetupFileDelta
    {
        /// <summary>Oturumda oluşan ve hâlâ duran dosyalar. Geri almanın tek hedefi.</summary>
        public List<string> CreatedFiles { get; } = new();
        /// <summary>Önceden var olan ve oturumda değişen dosyalar (asla silinmez).</summary>
        public List<string> ModifiedFiles { get; } = new();
        /// <summary>Önceden var olan ve oturumda silinen öğeler.</summary>
        public List<string> DeletedFiles { get; } = new();
        /// <summary>Önceden var olan öğelerin taşınması: "eski → yeni".</summary>
        public List<string> RenamedFiles { get; } = new();
        public List<string> CreatedFolders { get; } = new();
        /// <summary>Oturumda oluşup yine oturumda silinen öğeler (kurulumun geçici dosyaları).</summary>
        public int TempItemCount { get; set; }
    }

    /// <summary>
    /// Olay listesinden dosya farkını çıkaran saf hesap (NÖB v3 A5).
    ///
    /// Kurallar:
    ///   • Oluşturulup silinen öğe "silinen" değil, geçicidir.
    ///   • Silinip yeniden oluşturulan öğe önceden vardı: "değişen" sayılır.
    ///   • Geçici adla yazılıp yeniden adlandırılan öğe yeni adıyla "oluşturulan" olur; klasör
    ///     yeniden adlandırılırsa içinde oluşan öğeler de yeni yola taşınır.
    ///   • Önceden var olan bir dosyanın üstüne taşınan öğe (<paramref name="existedBeforeSession"/>)
    ///     "değişen" sayılır: geri alma onu silmez.
    ///   • Son karar diskin o anki durumuna göre verilir (<paramref name="probe"/>).
    /// </summary>
    public static class SetupDeltaBuilder
    {
        public static SetupFileDelta Build(
            IEnumerable<FileChangeEvent> events,
            Func<string, PathState> probe,
            Func<string, bool>? existedBeforeSession = null)
        {
            var cmp = StringComparer.OrdinalIgnoreCase;
            var created = new HashSet<string>(cmp);
            var modified = new HashSet<string>(cmp);
            var deleted = new HashSet<string>(cmp);
            var renamed = new Dictionary<string, string>(cmp); // yeni yol → ilk yol
            var renameTargets = new HashSet<string>(cmp);
            int temp = 0;

            foreach (var ev in events.OrderBy(e => e.Sequence))
            {
                if (string.IsNullOrWhiteSpace(ev.Path)) continue;
                switch (ev.Kind)
                {
                    case FileChangeKind.Created:
                        OnCreated(ev.Path);
                        break;

                    case FileChangeKind.Changed:
                        if (!created.Contains(ev.Path) && !deleted.Contains(ev.Path) && !renamed.ContainsKey(ev.Path))
                            modified.Add(ev.Path);
                        break;

                    case FileChangeKind.Deleted:
                        if (created.Remove(ev.Path))
                        {
                            temp++;
                            renameTargets.Remove(ev.Path);
                        }
                        else if (renamed.Remove(ev.Path, out string? original))
                        {
                            deleted.Add(original);
                        }
                        else
                        {
                            modified.Remove(ev.Path);
                            deleted.Add(ev.Path);
                        }
                        break;

                    case FileChangeKind.Renamed:
                        if (string.IsNullOrWhiteSpace(ev.OldPath) || cmp.Equals(ev.OldPath, ev.Path))
                        {
                            OnCreated(ev.Path);
                            break;
                        }
                        OnRenamed(ev.OldPath, ev.Path);
                        break;
                }
            }

            var result = new SetupFileDelta { TempItemCount = temp };
            foreach (string path in created.OrderBy(p => p, cmp))
            {
                switch (probe(path))
                {
                    case PathState.File:
                        if (renameTargets.Contains(path) && existedBeforeSession?.Invoke(path) == true)
                            result.ModifiedFiles.Add(path);
                        else
                            result.CreatedFiles.Add(path);
                        break;
                    case PathState.Directory:
                        result.CreatedFolders.Add(path);
                        break;
                }
            }
            foreach (string path in modified.OrderBy(p => p, cmp))
            {
                if (probe(path) == PathState.File && !result.ModifiedFiles.Contains(path, cmp)) result.ModifiedFiles.Add(path);
            }
            result.ModifiedFiles.Sort(cmp);
            foreach (string path in deleted.OrderBy(p => p, cmp))
            {
                if (probe(path) == PathState.Missing) result.DeletedFiles.Add(path);
            }
            foreach (var (now, before) in renamed.OrderBy(r => r.Key, cmp))
            {
                if (probe(now) != PathState.Missing) result.RenamedFiles.Add($"{before} → {now}");
            }
            return result;

            void OnCreated(string path)
            {
                if (deleted.Remove(path)) modified.Add(path);
                else if (!modified.Contains(path)) created.Add(path);
            }

            void OnRenamed(string oldPath, string newPath)
            {
                bool targetExisted = deleted.Remove(newPath) | modified.Remove(newPath) | renamed.Remove(newPath);
                if (created.Remove(oldPath))
                {
                    if (targetExisted) modified.Add(newPath);
                    else
                    {
                        created.Add(newPath);
                        renameTargets.Add(newPath);
                    }
                    renameTargets.Remove(oldPath);
                }
                else
                {
                    // Önceden var olan (ya da oluşumu kaçırılan) öğe taşındı: silinmez, yalnızca bildirilir.
                    string original = renamed.Remove(oldPath, out string? first) ? first : oldPath;
                    modified.Remove(oldPath);
                    created.Remove(newPath);
                    renameTargets.Remove(newPath);
                    if (!cmp.Equals(original, newPath)) renamed[newPath] = original;
                }
                MoveDescendants(created, oldPath, newPath);
                MoveDescendants(modified, oldPath, newPath);
                MoveDescendants(renameTargets, oldPath, newPath);
            }
        }

        private static void MoveDescendants(HashSet<string> set, string oldDir, string newDir)
        {
            string prefix = oldDir.TrimEnd('\\') + "\\";
            var moved = set.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (string path in moved)
            {
                set.Remove(path);
                set.Add(newDir.TrimEnd('\\') + "\\" + path[prefix.Length..]);
            }
        }
    }
}
