using System;
using System.Collections.Generic;

namespace Bakım.Core.Safety
{
    /// <summary>
    /// Windows yollarını işletim sisteminden bağımsız, saf dize işlemleriyle
    /// normalleştirir. <see cref="System.IO.Path.GetFullPath(string)"/> Linux'ta
    /// "C:\..." yolunu göreli sayar; güvenlik kararları hem Windows'ta hem
    /// Linux'taki birim testlerinde aynı sonucu vermek zorunda olduğu için
    /// burada kendi normalleştirmemiz kullanılır.
    /// </summary>
    public static class WindowsPath
    {
        /// <summary>
        /// "C:/Foo//Bar/../Baz/" → "C:\Foo\Baz". Sürücü kökü "C:" olarak döner.
        /// Göreli, UNC (\\server) ve aygıt (\\?\) yolları için null döner.
        /// </summary>
        public static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string p = path.Trim().Trim('"').Replace('/', '\\');
            if (p.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            if (p.Length < 2 || !char.IsAsciiLetter(p[0]) || p[1] != ':') return null;
            if (p.Length > 2 && p[2] != '\\') return null; // "C:foo" sürücü-göreli yol

            string drive = char.ToUpperInvariant(p[0]) + ":";
            var segments = new List<string>();

            foreach (string raw in p.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == ".") continue;
                if (raw == "..")
                {
                    if (segments.Count == 0) return null; // kökün üstüne çıkılamaz
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                // Windows sondaki boşluk ve noktaları yok sayar ("Foo. " == "Foo").
                string seg = raw.TrimEnd(' ', '.');
                if (seg.Length == 0) return null;
                if (seg.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*', ':' }) >= 0) return null;
                segments.Add(seg);
            }

            return segments.Count == 0 ? drive : drive + "\\" + string.Join('\\', segments);
        }

        /// <summary>Sürücü kökünden sonraki bölüm sayısı. "C:" → 0, "C:\A\B" → 2.</summary>
        public static int Depth(string normalized)
        {
            if (normalized.Length <= 2) return 0;
            int count = 0;
            foreach (char c in normalized) if (c == '\\') count++;
            return count;
        }

        /// <summary>
        /// path, root'a eşit ya da onun altında mı? Önek hatasını önler:
        /// "C:\AppData" "C:\App" altında sayılmaz.
        /// </summary>
        public static bool IsUnderOrEqual(string normalizedPath, string normalizedRoot)
        {
            if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            return IsStrictlyUnder(normalizedPath, normalizedRoot);
        }

        /// <summary>path, root'un kesin olarak altında mı (eşit değil)?</summary>
        public static bool IsStrictlyUnder(string normalizedPath, string normalizedRoot)
        {
            string prefix = normalizedRoot.EndsWith('\\') ? normalizedRoot : normalizedRoot + "\\";
            return normalizedPath.Length > prefix.Length &&
                   normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sürücü kökünden sonraki ilk bölüm ("C:\Windows\X" → "Windows").</summary>
        public static string? FirstSegment(string normalized)
        {
            if (normalized.Length <= 3) return null;
            int next = normalized.IndexOf('\\', 3);
            return next < 0 ? normalized.Substring(3) : normalized.Substring(3, next - 3);
        }

        /// <summary>Son bölüm (dosya ya da klasör adı).</summary>
        public static string LastSegment(string normalized)
        {
            int idx = normalized.LastIndexOf('\\');
            return idx < 0 ? normalized : normalized.Substring(idx + 1);
        }

        /// <summary>Üst klasör; kökte null.</summary>
        public static string? Parent(string normalized)
        {
            int idx = normalized.LastIndexOf('\\');
            if (idx < 0) return null;
            return normalized.Substring(0, idx);
        }
    }
}
