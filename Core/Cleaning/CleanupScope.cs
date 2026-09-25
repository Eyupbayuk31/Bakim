using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.Safety;

namespace Bakım.Core.Cleaning
{
    /// <summary>
    /// Temizleyicinin silebileceği yerler: YALNIZCA kategorilerin açıkça tanımlı kök
    /// klasörlerinin altı (S-13).
    ///
    /// Eski kontrol yolda "\temp\", "\cache\", "\telegram desktop\" gibi parçalar arıyordu:
    /// "D:\Projeler\temp\tez.docx" ya da Telegram'ın oturum klasörü (tdata) silinebilir
    /// sayılıyordu. Kök klasörün kendisi hiçbir zaman silinebilir değildir.
    /// </summary>
    public sealed class CleanupScope
    {
        private readonly List<string> _roots;
        private readonly List<string> _forbiddenTrees;

        public CleanupScope(IEnumerable<string?> roots, IEnumerable<string?> forbiddenTrees)
        {
            _roots = roots.Select(WindowsPath.Normalize).OfType<string>()
                          .Where(r => WindowsPath.Depth(r) >= 2) // "C:" ya da "C:\Users" gibi geniş kökler asla
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _forbiddenTrees = forbiddenTrees.Select(WindowsPath.Normalize).OfType<string>().ToList();
        }

        public IReadOnlyList<string> Roots => _roots;

        /// <summary>Yolun ait olduğu kök (en derin olan); kapsam dışıysa null.</summary>
        public string? RootFor(string? path)
        {
            string? normalized = WindowsPath.Normalize(path);
            if (normalized == null) return null;
            if (_forbiddenTrees.Any(f => WindowsPath.IsUnderOrEqual(normalized, f))) return null;

            return _roots.Where(r => WindowsPath.IsStrictlyUnder(normalized, r))
                         .OrderByDescending(r => r.Length)
                         .FirstOrDefault();
        }

        public bool Contains(string? path) => RootFor(path) != null;

        /// <summary>
        /// Kökten dosyaya kadar denetlenmesi gereken ara klasörler (kök hariç, dosya hariç).
        /// Aralarından biri bağlantı noktasıysa (junction/symlink) silme kökün dışına taşabilir.
        /// </summary>
        public static IReadOnlyList<string> IntermediateDirectories(string normalizedFile, string normalizedRoot)
        {
            var list = new List<string>();
            string? current = WindowsPath.Parent(normalizedFile);
            while (current != null && WindowsPath.IsStrictlyUnder(current, normalizedRoot))
            {
                list.Add(current);
                current = WindowsPath.Parent(current);
            }
            return list;
        }

        /// <summary>Dosya en az <paramref name="minAge"/> kadar eski mi? (Açık kurulumların temp dosyalarını korur.)</summary>
        public static bool IsOldEnough(DateTime lastWriteUtc, DateTime nowUtc, TimeSpan minAge) =>
            minAge <= TimeSpan.Zero || nowUtc - lastWriteUtc >= minAge;
    }
}
