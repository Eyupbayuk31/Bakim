using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bakım.Core.Uninstall
{
    /// <summary>İkonun okunacağı dosya ve kaynak dizini (negatifse kaynak kimliği).</summary>
    public sealed record IconLocation(string Path, int Index);

    /// <summary>
    /// Kaldırıcı listesinde program ikonunun nereden okunacağı. Eskiden <c>DisplayIcon</c> okunup hiç
    /// kullanılmıyordu; her satırda varsayılan simge görünüyordu.
    /// Sıra: DisplayIcon → kurulum klasörünün ana exe'si (çağıran verir) → kaldırıcı exe'si (msiexec hariç).
    /// </summary>
    public static class AppIconSource
    {
        private static readonly string[] IconExtensions = { ".exe", ".ico", ".dll" };

        /// <summary>
        /// <c>"C:\x\app.exe",0</c>, <c>C:\x\app.exe,-101</c>, <c>%ProgramFiles%\x\app.ico</c> gibi değerleri çözer.
        /// Tanınmayan uzantı ya da boş değer için null.
        /// </summary>
        public static IconLocation? ParseDisplayIcon(string? raw, Func<string, string>? expand = null)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string value = raw.Trim();
            int index = 0;

            // Sondaki ",<sayı>" dizindir; yol içindeki virgüller (nadir) korunur.
            int comma = value.LastIndexOf(',');
            if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                index = parsed;
                value = value[..comma].Trim();
            }

            value = value.Trim('"', ' ');
            if (expand != null) value = expand(value);
            return IsIconFile(value) ? new IconLocation(value, index) : null;
        }

        public static bool IsIconFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            foreach (var ext in IconExtensions)
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>msiexec ve rundll32 gibi paylaşılan sistem dosyalarının ikonu programı temsil etmez.</summary>
        public static bool IsSharedHost(string path)
        {
            string name = FileName(path);
            return name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Denenecek adaylar, öncelik sırasıyla; var olmayanlar çağıranın <paramref name="fileExists"/> denetimiyle elenir.</summary>
        public static IReadOnlyList<IconLocation> Candidates(string? displayIcon, string? mainExecutable, string? uninstallExecutable,
            Func<string, bool> fileExists, Func<string, string>? expand = null)
        {
            var list = new List<IconLocation>();
            void Add(IconLocation? loc)
            {
                if (loc == null || IsSharedHost(loc.Path) || !fileExists(loc.Path)) return;
                if (list.Exists(l => l.Path.Equals(loc.Path, StringComparison.OrdinalIgnoreCase) && l.Index == loc.Index)) return;
                list.Add(loc);
            }

            Add(ParseDisplayIcon(displayIcon, expand));
            if (IsIconFile(mainExecutable)) Add(new IconLocation(mainExecutable!, 0));
            if (IsIconFile(uninstallExecutable)) Add(new IconLocation(uninstallExecutable!, 0));
            return list;
        }

        private static string FileName(string path)
        {
            int slash = path.LastIndexOfAny(new[] { '\\', '/' });
            return slash >= 0 ? path[(slash + 1)..] : path;
        }
    }
}
