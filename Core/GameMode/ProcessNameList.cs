using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.GameMode
{
    /// <summary>
    /// Oyun Modu'nun süreç adı listeleri (askıya alınacak uygulamalar, otomatik tetikleyen oyunlar).
    /// Ayar dosyasında virgüllü metin olarak saklanır; arayüzde çip listesi olarak düzenlenir.
    /// </summary>
    public static class ProcessNameList
    {
        private static readonly char[] Separators = { ',', ';', '\n', '\r' };
        private static readonly char[] Invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>"OneDrive, Teams.exe" → {"OneDrive","Teams"}. Boş, yinelenen ve geçersiz adlar atılır.</summary>
        public static IReadOnlyList<string> Parse(string? text) =>
            (text ?? string.Empty)
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Normalize)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Tek bir adı sadeleştirir: boşluk ve ".exe" atılır; yol ya da joker içeren ad geçersizdir (boş döner).</summary>
        public static string Normalize(string? name)
        {
            var n = (name ?? string.Empty).Trim().Trim('"').Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4].TrimEnd();
            return n.IndexOfAny(Invalid) >= 0 ? string.Empty : n;
        }

        /// <summary>Listeyi ayar dosyasındaki biçime çevirir.</summary>
        public static string Format(IEnumerable<string> names) =>
            string.Join(", ", names.Select(Normalize).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Girilen metni (virgüllü olabilir) mevcut listeye ekler. Eklenen yeni adları döndürür;
        /// zaten olanlar (büyük/küçük harf duyarsız) tekrar eklenmez.
        /// </summary>
        public static IReadOnlyList<string> AddTo(IList<string> target, string? input)
        {
            var added = new List<string>();
            foreach (var name in Parse(input))
            {
                if (target.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))) continue;
                target.Add(name);
                added.Add(name);
            }
            return added;
        }
    }
}
