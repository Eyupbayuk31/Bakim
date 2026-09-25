using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// Kurulum raporu ile Kaldırıcı listesi arasındaki bağ (NÖB 5.5): Uninstall kaydının kimliği.
    /// Nöbetçi "HKLM\Software\...\Uninstall\{ad}", Kaldırıcı "HKLM\Software\...\Uninstall\{ad} [32]"
    /// yazar; ikisi de "HKLM|{AD}" kimliğine indirgenir.
    /// </summary>
    public static class SetupTrace
    {
        private const string Marker = @"\Uninstall\";

        public static string? UninstallIdentity(string? keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath)) return null;
            string k = keyPath.Trim();
            if (k.EndsWith(" [32]", StringComparison.Ordinal)) k = k[..^5];

            int i = k.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string name = k[(i + Marker.Length)..];
            int slash = name.IndexOf('\\');
            if (slash >= 0) name = name[..slash];
            if (name.Length == 0) return null;

            bool user = k.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);
            return $"{(user ? "HKCU" : "HKLM")}|{name}".ToUpperInvariant();
        }

        /// <summary>
        /// Kurulumun oluşturduğu en üst klasörler: kendisi oluşturulmuş ama üst klasörü önceden var
        /// olan klasörler ("C:\Program Files\Foo" evet, "C:\Program Files\Foo\bin" hayır).
        /// </summary>
        public static IReadOnlyList<string> TopLevelCreatedFolders(IEnumerable<string> createdFolders)
        {
            var set = new HashSet<string>(createdFolders.Select(f => f.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            return set.Where(f =>
                {
                    int slash = f.LastIndexOf('\\');
                    return slash > 0 && !set.Contains(f[..slash]);
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
