using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakım.Models;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// Kurulum, kaldırma ve güncelleme süreçlerini saf, test edilebilir
    /// puanlama ve kural tablosu ile sınıflandıran motor.
    /// </summary>
    public static class InstallerClassifier
    {
        public static readonly string[] InstallerKeywords = new[]
        {
            "setup", "install", "installer", "kurulum", "kurucu", "msiexec", "vcredist", "dxsetup"
        };

        public static readonly string[] UninstallerKeywords = new[]
        {
            "unins", "uninstall", "uninst", "kaldir"
        };

        public static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "idle", "system", "explorer", "svchost", "taskmgr", "devenv", "code",
            "jusched", "jucheck", "javaupdate", "googleupdate", "microsoftedgeupdate",
            "onedrive", "onedrivestandaloneupdater", "discord", "spotify", "steam",
            "epicgameslauncher", "riotclientservices", "bakim"
        };

        /// <summary>
        /// Bir sürecin kurulum, kaldırma veya güncelleme olup olmadığını puanlayarak belirler.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex MsiPackageArgument =
            new("(?<msi>\"[^\"]+\\.msi\"|[^\\s\"]+\\.msi)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static bool ClassifyProcess(
            string processName,
            string? executablePath,
            string? windowTitle,
            string? fileDescription,
            string? productName,
            string? commandLine,
            out SessionKind kind,
            out string detectedAppName,
            out int confidenceScore,
            int extraScore = 0)
        {
            kind = SessionKind.Unknown;
            detectedAppName = string.Empty;
            confidenceScore = 0;

            string rawProc = processName ?? string.Empty;
            string pName = Path.GetFileNameWithoutExtension(rawProc).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(pName)) pName = rawProc.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(pName)) return false;

            // 1. Bilinen arka plan güncelleyicileri ve sistem süreçleri
            if (ExcludedProcessNames.Contains(pName) || ExcludedProcessNames.Contains(rawProc.ToLowerInvariant()) || pName.StartsWith("service", StringComparison.OrdinalIgnoreCase))
            {
                kind = SessionKind.Update;
                return false;
            }

            string exePath = executablePath ?? string.Empty;
            string fileName = Path.GetFileName(exePath).ToLowerInvariant();
            string dir = Path.GetDirectoryName(exePath)?.ToLowerInvariant() ?? string.Empty;
            string title = (windowTitle ?? string.Empty).ToLowerInvariant();
            string desc = (fileDescription ?? string.Empty).ToLowerInvariant();
            string prod = (productName ?? string.Empty).ToLowerInvariant();
            string cmd = (commandLine ?? string.Empty).ToLowerInvariant();

            // 2. Kaldırıcı Kontrolü (P0-9)
            if (UninstallerKeywords.Any(k => pName.Contains(k) || fileName.Contains(k)) ||
                cmd.Contains("/x") || cmd.Contains("-uninstall") || cmd.Contains("/uninstall"))
            {
                kind = SessionKind.Uninstall;
                string fallbackName = (!string.IsNullOrWhiteSpace(windowTitle) && windowTitle.Length > 2)
                    ? windowTitle!
                    : (!string.IsNullOrWhiteSpace(exePath) ? (Path.GetFileNameWithoutExtension(exePath) ?? processName ?? "Unknown") : (processName ?? "Unknown"));
                detectedAppName = (!string.IsNullOrWhiteSpace(productName) && productName.Length > 2)
                    ? productName!
                    : fallbackName;
                if (string.IsNullOrWhiteSpace(detectedAppName)) detectedAppName = processName ?? "Unknown";
                confidenceScore = 90;
                return true;
            }

            // 3. Kurulu Dizin Denetimi (Program Files & Windows Koruması)
            // msiexec.exe hariç, bu dizinlerden çalışan yazılımlar zaten kuruludur.
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

                bool isAlreadyInstalledDir =
                    (!string.IsNullOrEmpty(programFiles) && exePath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(programFilesX86) && exePath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(windowsDir) && exePath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase));

                if (isAlreadyInstalledDir && !string.Equals(pName, "msiexec", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 4. Puanlama Matrisi (Faz 1.2 Puanlı Sınıflandırıcı)
            int score = 0;

            // msiexec özel kontrolü: yalnızca komut satırında bir .msi kurulumu (/i, /package) varsa.
            // Windows, Windows Update / onarım / uygulama güncellemeleri için arka planda sürekli
            // "msiexec /V" (sunucu) ve "msiexec -Embedding" (özel eylem) başlatır; komut satırı
            // okunamadığında bunlar eskiden "Windows Installer - Unicode kuruldu" diye raporlanıyordu.
            if (pName == "msiexec")
            {
                var msiArg = MsiPackageArgument.Match(commandLine ?? string.Empty);
                bool isInstallCommand = msiArg.Success &&
                                        (cmd.Contains(" /i") || cmd.Contains(" -i") || cmd.Contains("/package") || cmd.Contains("-package"));
                if (!isInstallCommand) return false;

                kind = SessionKind.Install;
                detectedAppName = Path.GetFileNameWithoutExtension(msiArg.Groups["msi"].Value.Trim('"'));
                if (!string.IsNullOrWhiteSpace(windowTitle) && windowTitle.Length > 2 &&
                    !windowTitle.Contains("Windows Installer", StringComparison.OrdinalIgnoreCase))
                {
                    detectedAppName = windowTitle!;
                }
                confidenceScore = 90;
                return true;
            }

            // Dosya adı veya süreç adı anahtar kelime eşleşmesi
            if (InstallerKeywords.Any(k => pName.Contains(k))) score += 30;
            if (!string.IsNullOrWhiteSpace(fileName) && InstallerKeywords.Any(k => fileName.Contains(k))) score += 30;

            // Pencere başlığı eşleşmesi
            if (!string.IsNullOrWhiteSpace(title))
            {
                if (title.Contains("setup") || title.Contains("kurulum") || title.Contains("wizard") || title.Contains("sihirbaz") || title.Contains("install"))
                {
                    score += 20;
                }
            }

            // FileVersionInfo eşleşmesi
            if (!string.IsNullOrWhiteSpace(desc) && InstallerKeywords.Any(k => desc.Contains(k))) score += 20;
            if (!string.IsNullOrWhiteSpace(prod) && InstallerKeywords.Any(k => prod.Contains(k))) score += 20;

            // Konum avantajı (İndirilenler, Temp, Masaüstü)
            if (dir.Contains("temp") || dir.Contains("downloads") || dir.Contains("indirilenler") || dir.Contains("desktop") || dir.Contains("masaüstü"))
            {
                score += 15;
            }

            // Dış sinyaller (NÖB 1.2): kurulum çatısı izi (+50), İnternet'ten indirilmiş (+10).
            score += extraScore;
            confidenceScore = Math.Min(score, 100);

            if (confidenceScore >= 50)
            {
                kind = SessionKind.Install;
                string fallbackName = (!string.IsNullOrWhiteSpace(windowTitle) && windowTitle.Length > 2)
                    ? windowTitle!
                    : (!string.IsNullOrWhiteSpace(exePath) ? (Path.GetFileNameWithoutExtension(exePath) ?? processName ?? "Unknown") : (processName ?? "Unknown"));
                detectedAppName = (!string.IsNullOrWhiteSpace(productName) && productName.Length > 2)
                    ? productName!
                    : fallbackName;
                if (string.IsNullOrWhiteSpace(detectedAppName)) detectedAppName = processName ?? "Unknown";

                return true;
            }

            return false;
        }
    }
}
