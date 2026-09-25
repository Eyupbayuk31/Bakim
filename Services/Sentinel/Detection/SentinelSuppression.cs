using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// Bakım'ın kendi başlattığı kaldırıcıların (ve alt süreçlerinin) Kurulum Nöbetçisi'nce
    /// "yeni kurulum" sanılmasını önler (KAL D5). Kaldırıcılar çoğu zaman setup.exe ya da
    /// %TEMP%'teki bir kopya olarak çalışır ve sınıflandırıcı bunları kurulum sayabilir.
    ///
    /// Kök süreç kaydedilir; ebeveyni kayıtlı olan her süreç de kaydedilir (ebeveyn çıkmış
    /// olsa bile ebeveyn kimliği kayıtta kaldığı için torunlar yakalanır). Kayıtlar,
    /// kaldırma bittikten sonra kısa bir süre daha geçerli kalır.
    /// </summary>
    public static class SentinelSuppression
    {
        private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(3);

        /// <summary>Kök PID → (kayıt anı, geçerlilik sonu). Sonu DateTime.MaxValue: kaldırma sürüyor.</summary>
        private static readonly ConcurrentDictionary<int, (DateTime Since, DateTime Until)> Roots = new();
        /// <summary>Ağaçtaki her PID → kök PID.</summary>
        private static readonly ConcurrentDictionary<int, int> Members = new();

        /// <summary>Kök süreci ve ağacını bastırır; döndürülen nesne atılınca kısa bir süre sonra kalkar.</summary>
        public static IDisposable SuppressProcessTree(int rootPid)
        {
            Roots[rootPid] = (DateTime.UtcNow, DateTime.MaxValue);
            Members[rootPid] = rootPid;
            return new Releaser(rootPid);
        }

        /// <summary>
        /// Süreç bastırılmış bir ağaca mı ait? Ebeveyni bastırılmışsa süreç de ağaca eklenir.
        /// </summary>
        public static bool IsSuppressed(int pid, int? parentPid)
        {
            if (Roots.IsEmpty) return false;
            Prune();
            if (Members.ContainsKey(pid)) return true;
            if (parentPid is int parent && parent > 4 && Members.TryGetValue(parent, out int root))
            {
                Members[pid] = root;
                return true;
            }
            return false;
        }

        public static bool IsActive => !Roots.IsEmpty;

        private static void Release(int rootPid)
        {
            if (Roots.TryGetValue(rootPid, out var entry))
                Roots[rootPid] = (entry.Since, DateTime.UtcNow + Grace);
        }

        private static void Prune()
        {
            var now = DateTime.UtcNow;
            foreach (var (root, (since, until)) in Roots)
            {
                if (until > now && now - since < MaxLifetime) continue;
                Roots.TryRemove(root, out _);
                foreach (var member in Members.Where(m => m.Value == root).Select(m => m.Key).ToList())
                    Members.TryRemove(member, out _);
            }
        }

        private sealed class Releaser : IDisposable
        {
            private readonly int _rootPid;
            private bool _disposed;

            public Releaser(int rootPid) => _rootPid = rootPid;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Release(_rootPid);
            }
        }
    }
}
