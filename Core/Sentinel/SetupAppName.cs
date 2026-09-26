using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakım.Core.Text;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// Kurulum oturumunun uygulama adı (NÖB v3 A9). Kurulum dosyalarının sürüm bilgisinde çoğu zaman
    /// uygulamanın değil kurulum çatısının adı yazar ("7-Zip SFX", "Setup/Uninstall",
    /// "Windows Installer - Unicode"); bu adlar atlanır. Kurulum bitince oluşan Uninstall kaydının
    /// DisplayName değeri en güçlü kanıttır.
    /// </summary>
    public static class SetupAppName
    {
        private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "setup", "install", "installer", "installation", "kurulum", "kurucu", "uninstall", "uninstaller",
            "setup/uninstall", "setup launcher", "setup bootstrapper", "bootstrapper", "self-extractor",
            "self-extracting archive", "sfx", "7-zip sfx", "7z sfx", "7z setup sfx", "7-zip setup sfx",
            "7z setup sfx (dialogs)", "winrar sfx", "winrar self-extracting archive", "windows installer",
            "windows installer - unicode", "windows® installer", "inno setup", "nsis", "nullsoft install system",
            "nullsoft installer", "installshield", "installshield(r)", "installshield (r)",
            "installshield setup launcher", "wix toolset", "wix burn", "squirrel", "squirrel setup",
            "advanced installer", "update", "updater", "unknown", "bilinmiyor",
        };

        /// <summary>Uygulamayı değil kurulum aracını anlatan ad mı?</summary>
        public static bool IsGeneric(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string trimmed = name.Trim();
            if (trimmed.Length <= 2 || GenericNames.Contains(trimmed)) return true;
            foreach (InstallerFramework framework in Enum.GetValues<InstallerFramework>())
            {
                if (framework != InstallerFramework.Unknown &&
                    trimmed.Equals(InstallerFingerprint.DisplayName(framework), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            string lower = trimmed.ToLowerInvariant();
            return lower.Contains("self-extract") || lower.EndsWith(" sfx", StringComparison.Ordinal);
        }

        /// <summary>
        /// Tespit anındaki ad: ürün adı → dosya açıklaması → pencere başlığı → dosya adı → süreç adı.
        /// Genel çatı adları atlanır.
        /// </summary>
        public static string Resolve(string? productName, string? fileDescription, string? windowTitle, string? executablePath, string? processName)
        {
            foreach (string? candidate in new[] { productName, fileDescription, windowTitle })
            {
                if (!IsGeneric(candidate)) return candidate!.Trim();
            }

            string fileName = FromFileName(executablePath);
            if (!IsGeneric(fileName)) return fileName;
            string process = FromFileName(processName);
            if (!IsGeneric(process)) return process;
            return fileName.Length > 0 ? fileName : process.Length > 0 ? process : "Adı bilinmeyen kurulum";
        }

        /// <summary>
        /// Oturumda oluşan Uninstall kayıtlarından adı seçer. Tek kayıt varsa odur; birden çoksa
        /// (paket kurulum) mevcut adla ya da kurulum dosyasının adıyla ortak kelimesi en çok olan
        /// seçilir. Ortak kelime yoksa null: mevcut ad korunur.
        /// </summary>
        public static string? FromNewPrograms(IReadOnlyList<string> newPrograms, string? currentName, string? installerPath)
        {
            var programs = newPrograms.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (programs.Count == 0) return null;
            if (programs.Count == 1) return programs[0];

            var known = Meaningful(currentName).Concat(Meaningful(FromFileName(installerPath))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (known.Count == 0) return null;

            var best = programs
                .Select(p => (Name: p, Score: Meaningful(p).Count(known.Contains)))
                .OrderByDescending(p => p.Score)
                .First();
            return best.Score > 0 ? best.Name : null;
        }

        private static IEnumerable<string> Meaningful(string? text) =>
            NameMatcher.Tokenize(text).Where(t => t.Length >= 3 && !NameMatcher.IsStopWord(t) && !t.All(char.IsDigit));

        private static string FromFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string name = Path.GetFileNameWithoutExtension(path.Trim());
            return name.Replace('_', ' ').Trim();
        }
    }
}
