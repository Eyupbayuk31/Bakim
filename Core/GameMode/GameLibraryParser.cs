using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakım.Core.GameMode
{
    /// <summary>Kütüphanede bulunan bir oyun. <see cref="ProcessName"/> otomatik tetiklemede izlenen addır.</summary>
    public sealed record DetectedGame(string DisplayName, string ProcessName, string Source);

    /// <summary>Steam kurulum bildirimi (appmanifest_*.acf).</summary>
    public sealed record SteamAppManifest(string AppId, string Name, string InstallDir);

    /// <summary>Epic Games Launcher kurulum bildirimi (*.item).</summary>
    public sealed record EpicManifest(string DisplayName, string InstallLocation, string LaunchExecutable);

    /// <summary>
    /// Oyun kütüphanesi dosyalarının saf ayrıştırıcıları (disk erişimi yok; test edilebilir).
    /// Disk taraması Services/GameLibraryService'tedir.
    /// </summary>
    public static class GameLibraryParser
    {
        private static readonly Regex VdfPath = new("\"path\"\\s+\"(?<p>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
        private static readonly Regex VdfKeyValue = new("\"(?<k>[^\"]+)\"\\s+\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"");

        /// <summary>libraryfolders.vdf → kütüphane kök klasörleri.</summary>
        public static IReadOnlyList<string> ParseSteamLibraryFolders(string? vdf) =>
            VdfPath.Matches(vdf ?? string.Empty)
                .Select(m => Unescape(m.Groups["p"].Value))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>appmanifest_*.acf → uygulama kimliği, ad ve kurulum klasörü adı. Eksikse null.</summary>
        public static SteamAppManifest? ParseSteamAppManifest(string? acf)
        {
            string? appId = null, name = null, installDir = null;
            foreach (Match m in VdfKeyValue.Matches(acf ?? string.Empty))
            {
                string key = m.Groups["k"].Value;
                string value = Unescape(m.Groups["v"].Value);
                if (appId == null && key.Equals("appid", StringComparison.OrdinalIgnoreCase)) appId = value;
                else if (name == null && key.Equals("name", StringComparison.OrdinalIgnoreCase)) name = value;
                else if (installDir == null && key.Equals("installdir", StringComparison.OrdinalIgnoreCase)) installDir = value;
            }
            return appId != null && name != null && installDir != null ? new SteamAppManifest(appId, name, installDir) : null;
        }

        /// <summary>
        /// Steam'in kendisi ve araçları (Proton, Steamworks ortak dağıtılabilirleri, SDK'lar) oyun değildir.
        /// </summary>
        public static bool IsSteamTool(SteamAppManifest manifest) =>
            manifest.AppId is "228980" or "1070560" or "1391110" or "1493710" ||
            manifest.Name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
            manifest.Name.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
            manifest.Name.Contains(" SDK", StringComparison.OrdinalIgnoreCase) ||
            manifest.Name.Contains("Dedicated Server", StringComparison.OrdinalIgnoreCase);

        /// <summary>Epic *.item (JSON) → görünen ad, kurulum yeri, başlatılan exe. Geçersizse null.</summary>
        public static EpicManifest? ParseEpicManifest(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? Get(string name) =>
                    root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                // Yalnızca oyunlar: eklentiler (DLC) ve uygulama olmayanlar atlanır.
                if (root.TryGetProperty("bIsApplication", out var isApp) && isApp.ValueKind == JsonValueKind.False) return null;
                if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;

                string? display = Get("DisplayName"), location = Get("InstallLocation"), launch = Get("LaunchExecutable");
                return string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(launch)
                    ? null
                    : new EpicManifest(display!, location!, launch!);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Oyun olmayan yürütülebilirler: kaldırıcılar, kurulumlar, hata bildiriciler, hile koruması başlatıcıları.</summary>
        private static readonly string[] NonGameContains =
        {
            "uninstall", "redist", "crashhandler", "crashreport", "crashpad", "easyanticheat", "battleye",
            "start_protected_game", "errorreporter", "webhelper", "prereq", "dxsetup", "launcherhelper"
        };

        /// <summary>Yalnızca ad başında anlamlı işaretler ("eac" → "ReachGame" elenmesin).</summary>
        private static readonly string[] NonGamePrefixes =
        {
            "unins", "setup", "install", "eac", "be_", "cef", "update", "patch", "config", "dotnet",
            "directx", "benchmark", "server", "helper", "crash", "report"
        };

        public static bool IsNonGameExecutable(string fileName)
        {
            string n = FileStem(fileName).ToLowerInvariant();
            return NonGameContains.Any(n.Contains) || NonGamePrefixes.Any(n.StartsWith);
        }

        /// <summary>
        /// Kurulum klasöründeki exe'ler arasından oyunun kendi sürecini seçer.
        /// Öncelik: Unreal "*-Win64-Shipping" ikilisi, oyun/klasör adına benzeyen ad, en büyük dosya.
        /// </summary>
        /// <param name="executables">Kurulum klasörüne göre göreli yol ve bayt boyutu.</param>
        /// <returns>Süreç adı (uzantısız) ya da uygun aday yoksa null.</returns>
        public static string? PickGameProcess(IEnumerable<(string RelativePath, long Size)> executables, string gameName, string installDir)
        {
            string game = Simplify(gameName), dir = Simplify(installDir);
            var best = executables
                .Where(e => e.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Where(e => !IsNonGameExecutable(e.RelativePath))
                .Select(e =>
                {
                    string name = FileStem(e.RelativePath);
                    string simple = Simplify(name);
                    int score = 0;
                    if (name.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) score += 120;
                    if (simple.Length > 0 && (simple == game || simple == dir)) score += 100;
                    else if (simple.Length >= 3 && (game.Contains(simple) || dir.Contains(simple) || simple.Contains(dir) || simple.Contains(game))) score += 50;
                    if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 40;
                    return (Name: name, Score: score, e.Size);
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Size)
                .FirstOrDefault();
            return best.Name;
        }

        /// <summary>"a\\b/c.exe" → "c". Ayırıcı olarak hem \\ hem / kabul edilir (platformdan bağımsız).</summary>
        public static string FileStem(string path)
        {
            string file = path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
            return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : Path.GetFileNameWithoutExtension(file);
        }

        private static string Simplify(string text) =>
            new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static string Unescape(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }
}
