using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Storage
{
    public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
    {
        public double Area => Width * Height;
    }

    /// <summary>
    /// Squarified treemap yerleşimi (Bruls, Huizing, van Wijk 2000). Dikdörtgenlerin en/boy oranını
    /// 1'e yakın tutar; küçük klasörler de tıklanabilir kalır. Saf mantık: Linux'ta test edilir.
    /// </summary>
    public static class Treemap
    {
        /// <summary>
        /// Büyükten küçüğe sıralı değerleri <paramref name="bounds"/> içine yerleştirir.
        /// Sıfır ve negatif değerler atlanır (dönen listede Width=Height=0).
        /// </summary>
        public static IReadOnlyList<TreemapRect> Layout(IReadOnlyList<double> values, TreemapRect bounds)
        {
            var result = new TreemapRect[values.Count];
            double total = values.Where(v => v > 0).Sum();
            if (total <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return result;

            // Değerleri alana ölçekle.
            double scale = bounds.Area / total;
            var indexed = values.Select((v, i) => (Index: i, Area: v > 0 ? v * scale : 0))
                                .Where(p => p.Area > 0)
                                .OrderByDescending(p => p.Area)
                                .ToList();

            var remaining = bounds;
            var row = new List<(int Index, double Area)>();
            int k = 0;
            while (k < indexed.Count)
            {
                var item = indexed[k];
                double side = Math.Min(remaining.Width, remaining.Height);
                if (row.Count == 0 || Worst(row, side) >= Worst(row.Append(item).ToList(), side))
                {
                    row.Add(item);
                    k++;
                    continue;
                }
                remaining = PlaceRow(row, remaining, result);
                row.Clear();
            }
            if (row.Count > 0) PlaceRow(row, remaining, result);
            return result;
        }

        /// <summary>Satırdaki en kötü en/boy oranı (küçük daha iyi).</summary>
        private static double Worst(IReadOnlyList<(int Index, double Area)> row, double side)
        {
            double sum = row.Sum(r => r.Area);
            double max = row.Max(r => r.Area), min = row.Min(r => r.Area);
            double s2 = side * side, sum2 = sum * sum;
            return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
        }

        /// <summary>Satırı kısa kenar boyunca yerleştirir, kalan alanı döndürür.</summary>
        private static TreemapRect PlaceRow(IReadOnlyList<(int Index, double Area)> row, TreemapRect r, TreemapRect[] result)
        {
            double sum = row.Sum(x => x.Area);
            if (r.Width >= r.Height)
            {
                // Dikey şerit (solda)
                double stripWidth = sum / r.Height;
                double y = r.Y;
                foreach (var (index, area) in row)
                {
                    double h = area / stripWidth;
                    result[index] = new TreemapRect(r.X, y, stripWidth, h);
                    y += h;
                }
                return new TreemapRect(r.X + stripWidth, r.Y, Math.Max(0, r.Width - stripWidth), r.Height);
            }
            else
            {
                // Yatay şerit (üstte)
                double stripHeight = sum / r.Width;
                double x = r.X;
                foreach (var (index, area) in row)
                {
                    double w = area / stripHeight;
                    result[index] = new TreemapRect(x, r.Y, w, stripHeight);
                    x += w;
                }
                return new TreemapRect(r.X, r.Y + stripHeight, r.Width, Math.Max(0, r.Height - stripHeight));
            }
        }
    }

    /// <summary>Disk haritasında bir düğüm (klasör ya da "diğer" toplamı).</summary>
    public sealed class FolderNode
    {
        public FolderNode(string name, string fullPath, bool isAggregate = false)
        {
            Name = name;
            FullPath = fullPath;
            IsAggregate = isAggregate;
        }

        public string Name { get; }
        public string FullPath { get; }
        /// <summary>"Diğer N öğe" gibi birleştirilmiş düğüm: içine girilemez.</summary>
        public bool IsAggregate { get; }
        public long SizeBytes { get; set; }
        public long FileCount { get; set; }
        public FolderNode? Parent { get; set; }
        public List<FolderNode> Children { get; } = new();

        /// <summary>
        /// Çocukları en büyük <paramref name="keep"/> tanesiyle sınırlar; gerisi (ve klasörün doğrudan
        /// dosyaları) tek bir "diğer" düğümünde toplanır. Bellek, klasör sayısından bağımsız kalır.
        /// </summary>
        public void Compact(int keep, long directFilesBytes, long directFileCount)
        {
            var ordered = Children.OrderByDescending(c => c.SizeBytes).ToList();
            var kept = ordered.Take(keep).ToList();
            var rest = ordered.Skip(keep).ToList();
            Children.Clear();
            Children.AddRange(kept);

            long otherBytes = rest.Sum(r => r.SizeBytes) + directFilesBytes;
            long otherFiles = rest.Sum(r => r.FileCount) + directFileCount;
            if (otherBytes > 0)
            {
                string label = rest.Count > 0
                    ? $"Diğer {rest.Count} klasör ve dosyalar"
                    : "Bu klasördeki dosyalar";
                Children.Add(new FolderNode(label, FullPath, isAggregate: true)
                {
                    SizeBytes = otherBytes,
                    FileCount = otherFiles,
                    Parent = this
                });
            }
        }
    }
}
