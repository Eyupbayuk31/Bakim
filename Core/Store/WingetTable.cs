using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Store
{
    public sealed record WingetUpgrade(string Name, string Id, string Version, string Available, string Source);

    /// <summary>
    /// "winget upgrade" tablo çıktısını ayrıştırır (§5.12 Güncellemeler). Sütunlar başlıktaki
    /// kelimelerin konumundan belirlenir; başlıklar yerelleştirilir ("Name Id Version Available Source" /
    /// "Ad Kimlik Sürüm Kullanılabilir Kaynak"), bu yüzden adlara değil sıraya bakılır.
    /// </summary>
    public static class WingetTable
    {
        public static IReadOnlyList<WingetUpgrade> ParseUpgrades(string? output)
        {
            var result = new List<WingetUpgrade>();
            if (string.IsNullOrWhiteSpace(output)) return result;

            // İlerleme göstergesi \r ile aynı satırın üzerine yazar: son parça geçerlidir.
            var lines = output.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
                .ToList();

            int sep = lines.FindIndex(l => l.Trim().Length > 10 && l.Trim().All(c => c == '-'));
            if (sep <= 0) return result;
            string header = lines[sep - 1];

            var starts = new List<int>();
            for (int i = 0; i < header.Length; i++)
            {
                if (!char.IsWhiteSpace(header[i]) && (i == 0 || char.IsWhiteSpace(header[i - 1]))) starts.Add(i);
            }
            if (starts.Count < 4) return result;

            for (int i = sep + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) break;              // tablo bitti ("N yükseltme var." öncesi boş satır)
                if (line.Length < starts[1]) continue;

                string Col(int c) => c + 1 < starts.Count
                    ? Slice(line, starts[c], starts[c + 1])
                    : Slice(line, starts[c], line.Length);

                string name = Col(0), id = Col(1), version = Col(2), available = Col(3);
                string source = starts.Count > 4 ? Col(4) : string.Empty;
                if (id.Length == 0 || id.Contains(' ') || available.Length == 0) continue;
                result.Add(new WingetUpgrade(name, id, version, available, source));
            }
            return result;
        }

        private static string Slice(string line, int from, int to)
        {
            if (from >= line.Length) return string.Empty;
            to = Math.Min(to, line.Length);
            return line[from..to].Trim();
        }
    }
}
