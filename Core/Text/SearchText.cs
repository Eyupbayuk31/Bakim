using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bakım.Core.Text
{
    /// <summary>
    /// Türkçe duyarsız arama (komut paleti, §5.19): "kaldirici" ↔ "Kaldırıcı", "gecmis" ↔ "Geçmiş".
    /// Sorgu kelimelere bölünür; her kelime alanlardan birinde geçmelidir.
    /// </summary>
    public static class SearchText
    {
        public static string Fold(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                sb.Append(ch switch
                {
                    'ı' or 'I' or 'İ' or 'i' => 'i',
                    'ş' or 'Ş' => 's',
                    'ğ' or 'Ğ' => 'g',
                    'ü' or 'Ü' => 'u',
                    'ö' or 'Ö' => 'o',
                    'ç' or 'Ç' => 'c',
                    'â' or 'Â' => 'a',
                    'î' or 'Î' => 'i',
                    'û' or 'Û' => 'u',
                    _ => char.ToLowerInvariant(ch)
                });
            }
            return sb.ToString();
        }

        /// <summary>
        /// Eşleşme puanı (0: eşleşmedi). Başlık başlangıcı &gt; başlıkta kelime başı &gt; başlıkta &gt; diğer alanlar.
        /// </summary>
        public static int Score(string query, string title, params string?[] otherFields)
        {
            var tokens = Fold(query).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return 1;
            string t = Fold(title);
            string others = string.Join(" ", otherFields.Select(Fold));

            int score = 0;
            foreach (string token in tokens)
            {
                if (t.StartsWith(token, StringComparison.Ordinal)) score += 40;
                else if (t.Contains(" " + token, StringComparison.Ordinal)) score += 25;
                else if (t.Contains(token, StringComparison.Ordinal)) score += 15;
                else if (others.Contains(token, StringComparison.Ordinal)) score += 5;
                else return 0;
            }
            return score;
        }
    }
}
