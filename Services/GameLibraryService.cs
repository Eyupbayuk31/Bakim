using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.GameMode;
using Microsoft.Win32;

namespace Bakım.Services
{
    /// <summary>Seçicide gösterilen çalışan uygulama.</summary>
    public sealed record RunningApp(string ProcessName, string Description, long WorkingSetBytes, bool HasWindow);

    /// <summary>Oyun Modu için bilinen arka plan uygulaması (öneri listesi).</summary>
    public sealed record KnownBackgroundApp(string Process, string Name);

    public interface IGameLibraryService
    {
        /// <summary>Steam ve Epic kütüphanelerindeki kurulu oyunlar. Sonuç önbelleğe alınır.</summary>
        Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(bool refresh = false, CancellationToken ct = default);

        /// <summary>Askıya alınabilecek, kullanıcı oturumunda çalışan uygulamalar (kritik süreçler hariç).</summary>
        IReadOnlyList<RunningApp> GetRunningApps();

        /// <summary>Oyun sırasında genellikle gereksiz olan arka plan uygulamaları (Assets/gamemode/background-apps.json).</summary>
        IReadOnlyList<KnownBackgroundApp> KnownBackgroundApps { get; }
    }

    /// <summary>
    /// Oyun kütüphanesi bulucu (MASTER_PLAN §5.5). Yalnızca okur: Steam (libraryfolders.vdf +
    /// appmanifest_*.acf) ve Epic Games Launcher (*.item) bildirimleri. Ayrıştırma kuralları
    /// Core/GameMode/GameLibraryParser'dadır ve test edilir. Xbox / Microsoft Store oyunları
    /// paket koruması nedeniyle güvenilir biçimde okunamadığından listelenmez.
    /// </summary>
    public sealed class GameLibraryService : IGameLibraryService
    {
        private const int MaxExecutablesPerGame = 200;
        private readonly ILogService _log;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IReadOnlyList<DetectedGame>? _cache;
        private IReadOnlyList<KnownBackgroundApp>? _known;

        public GameLibraryService(ILogService log)
        {
            _log = log ?? NullLogService.Instance;
        }

        public IReadOnlyList<KnownBackgroundApp> KnownBackgroundApps => _known ??= LoadKnownApps();

        public async Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(bool refresh = false, CancellationToken ct = default)
        {
            if (!refresh && _cache != null) return _cache;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!refresh && _cache != null) return _cache;
                var games = await Task.Run(() => ScanSteam(ct).Concat(ScanEpic(ct)).ToList(), ct).ConfigureAwait(false);
                _cache = games
                    .GroupBy(g => g.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _log.Info($"Oyun kütüphanesi tarandı: {_cache.Count} oyun.", nameof(GameLibraryService));
                return _cache;
            }
            finally
            {
                _gate.Release();
            }
        }

        private IEnumerable<DetectedGame> ScanSteam(CancellationToken ct)
        {
            string? steam = ReadSteamPath();
            if (steam == null) yield break;

            var libraries = new List<string> { steam };
            string vdfPath = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
                libraries.AddRange(GameLibraryParser.ParseSteamLibraryFolders(SafeRead(vdfPath)));

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(apps)) continue;
                IEnumerable<string> manifests;
                try { manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                foreach (var file in manifests)
                {
                    ct.ThrowIfCancellationRequested();
                    var manifest = GameLibraryParser.ParseSteamAppManifest(SafeRead(file));
                    if (manifest == null || GameLibraryParser.IsSteamTool(manifest)) continue;
                    string dir = Path.Combine(apps, "common", manifest.InstallDir);
                    string? process = GameLibraryParser.PickGameProcess(ListExecutables(dir), manifest.Name, manifest.InstallDir);
                    if (process != null) yield return new DetectedGame(manifest.Name, process, "Steam");
                }
            }
        }

        private IEnumerable<DetectedGame> ScanEpic(CancellationToken ct)
        {
            string manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests)) yield break;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(manifests, "*.item").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var manifest = GameLibraryParser.ParseEpicManifest(SafeRead(file));
                if (manifest == null) continue;

                // Başlatılan exe çoğu zaman bir başlatıcıdır (ör. FortniteLauncher); asıl oyun süreci aranır.
                string launch = GameLibraryParser.FileStem(manifest.LaunchExecutable);
                string? process = !GameLibraryParser.IsNonGameExecutable(launch) &&
                                  !launch.Contains("launcher", StringComparison.OrdinalIgnoreCase)
                    ? launch
                    : GameLibraryParser.PickGameProcess(ListExecutables(manifest.InstallLocation), manifest.DisplayName,
                        Path.GetFileName(manifest.InstallLocation.TrimEnd('\\', '/')));
                if (process != null) yield return new DetectedGame(manifest.DisplayName, process, "Epic");
            }
        }

        private static IEnumerable<(string RelativePath, long Size)> ListExecutables(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Array.Empty<(string, long)>();
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };
                return Directory.EnumerateFiles(root, "*.exe", options)
                    .Take(MaxExecutablesPerGame)
                    .Select(f => (Path.GetRelativePath(root, f), SafeLength(f)))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<(string, long)>();
            }
        }

        public IReadOnlyList<RunningApp> GetRunningApps()
        {
            var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int session = Process.GetCurrentProcess().SessionId;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        string name = p.ProcessName;
                        // Hizmetler (oturum 0), kritik süreçler ve Bakım'ın kendisi seçilemez.
                        if (excluded.Contains(name) || p.Id == Environment.ProcessId || p.SessionId != session ||
                            Core.Safety.CriticalProcessPolicy.IsCriticalName(name))
                            continue;

                        bool hasWindow = p.MainWindowHandle != IntPtr.Zero;
                        long memory = p.WorkingSet64;
                        if (result.TryGetValue(name, out var existing))
                        {
                            result[name] = existing with
                            {
                                WorkingSetBytes = existing.WorkingSetBytes + memory,
                                HasWindow = existing.HasWindow || hasWindow
                            };
                            continue;
                        }

                        // Windows klasöründen çalışan ikililer askıya alınamaz (SuspendProcessAsync de reddeder).
                        var module = TryMainModule(p);
                        if (module?.FileName is { } image && image.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        {
                            excluded.Add(name);
                            continue;
                        }

                        string description = module?.FileVersionInfo.FileDescription?.Trim() is { Length: > 0 } d
                            ? d
                            : (string.IsNullOrWhiteSpace(p.MainWindowTitle) ? name : p.MainWindowTitle);
                        result[name] = new RunningApp(name, description, memory, hasWindow);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Süreç bu sırada kapandı ya da erişim yok: atla.
                    }
                }
            }

            return result.Values
                .OrderByDescending(a => a.HasWindow)
                .ThenByDescending(a => a.WorkingSetBytes)
                .ToList();
        }

        private static ProcessModule? TryMainModule(Process p)
        {
            try { return p.MainModule; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Yükseltilmiş ya da korumalı süreç: yalnızca adı gösterilir.
                return null;
            }
        }

        private IReadOnlyList<KnownBackgroundApp> LoadKnownApps()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("gamemode/background-apps.json");
                if (stream == null) return Array.Empty<KnownBackgroundApp>();
                var items = JsonSerializer.Deserialize<List<KnownBackgroundApp>>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return items?.Where(i => !string.IsNullOrWhiteSpace(i.Process)).ToList() ?? new List<KnownBackgroundApp>();
            }
            catch (JsonException ex)
            {
                _log.Warning("Oyun Modu öneri listesi okunamadı.", ex, nameof(GameLibraryService));
                return Array.Empty<KnownBackgroundApp>();
            }
        }

        private static string? ReadSteamPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path && !string.IsNullOrWhiteSpace(path))
                {
                    path = path.Replace('/', '\\');
                    return Directory.Exists(path) ? path : null;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                // Steam yok ya da okunamıyor.
            }
            return null;
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return string.Empty; }
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }
    }
}
