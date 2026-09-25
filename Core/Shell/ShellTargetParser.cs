using System;
using System.IO;

namespace Bakım.Core.Shell
{
    /// <summary>
    /// Sağ tık bağlam menüsü ve kabuk komut satırı argümanlarını ayrıştıran saf mantık sınıfı (KAL C3).
    /// </summary>
    public static class ShellTargetParser
    {
        public static string? ParseTarget(string[]? args)
        {
            if (args == null || args.Length == 0) return null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim('\"');
                if (arg.Equals("--uninstall-target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return CleanPath(args[i + 1]);
                }

                if (arg.StartsWith("--uninstall-target=", StringComparison.OrdinalIgnoreCase))
                {
                    return CleanPath(arg["--uninstall-target=".Length..]);
                }
            }

            // Bayrak içermeyen ilk bağımsız yol argümanı
            string first = args[0].Trim('\"');
            if (!first.StartsWith("-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(first))
            {
                return CleanPath(first);
            }

            return null;
        }

        private static string CleanPath(string raw)
        {
            string clean = raw.Trim().Trim('\"');
            return clean;
        }
    }
}
