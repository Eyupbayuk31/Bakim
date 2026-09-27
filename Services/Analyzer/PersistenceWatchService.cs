using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Bakım.Core.Analyzer;
using Bakım.Models;
using Bakım.Services.Activity;
using WatchGroup = Bakım.Services.AutorunsScannerEngine.WatchGroup;

namespace Bakım.Services.Analyzer
{
    public interface IPersistenceWatchService : IDisposable
    {
        bool IsRunning { get; }
        void Start();
        void Stop();

        /// <summary>Yeni kalıcılık girdisi eklendi (değerlendirilmiş, zenginleştirilmiş).</summary>
        event Action<PersistenceItem>? PersistenceAdded;
    }

    /// <summary>
    /// Canlı kalıcılık izleme (ANL Faz 4). Run/RunOnce ve hizmet anahtarları kayıt defteri değişim
    /// bildirimiyle, başlangıç klasörleri dosya izleyicisiyle, görevler ve WMI 5 dakikalık yoklamayla
    /// izlenir. Değişiklikler 3 sn birleştirilip yalnızca ilgili konum yeniden taranır; yeni girdi
    /// değerlendirilir, Etkinlik Merkezi'ne yazılır ve riskliyse bildirilir.
    /// </summary>
    public sealed class PersistenceWatchService : IPersistenceWatchService
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _startupDelay;

        private readonly AutorunsScannerEngine _engine;
        private readonly ILogService _log;
        private readonly ConcurrentDictionary<WatchGroup, byte> _pending = new();
        private readonly Dictionary<WatchGroup, HashSet<string>> _baseline = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly SemaphoreSlim _signal = new(0, 1);
        /// <summary>Kayıt defteri bildirimleri kuruldu (taban "hazır" ancak bundan sonra sayılır).</summary>
        private readonly ManualResetEventSlim _armed = new(false);

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private Thread? _registryThread;

        public event Action<PersistenceItem>? PersistenceAdded;

        /// <param name="startupDelay">Taban alınmadan önce bekleme (varsayılan 45 sn: açılışı yavaşlatmamak için).</param>
        public PersistenceWatchService(IAutorunsScannerEngine engine, ILogService log, TimeSpan? startupDelay = null)
        {
            _startupDelay = startupDelay ?? TimeSpan.FromSeconds(45);
            _engine = engine as AutorunsScannerEngine ?? new AutorunsScannerEngine(App.TryGetService<IVirusTotalCheckService>() ?? new VirusTotalCheckService());
            _log = log;
        }

        public bool IsRunning => _loop is { IsCompleted: false };

        /// <summary>Taban alındı ve izleyiciler kuruldu.</summary>
        public bool IsBaselineReady { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => RunAsync(ct), ct);
            _log.Info("Canlı kalıcılık izleme başlatıldı.", nameof(PersistenceWatchService));
        }

        public void Stop()
        {
            _cts?.Cancel();
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _registryThread = null;
            _log.Info("Canlı kalıcılık izleme durduruldu.", nameof(PersistenceWatchService));
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private static string KeyOf(PersistenceItem i) => PersistenceWatchRules.Key(i.Category.ToString(), i.LocationSource, i.Name, i.RawCommand);

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                // Açılışı yavaşlatmamak için taban, uygulama yerleştikten sonra alınır.
                await Task.Delay(_startupDelay, ct);
                foreach (WatchGroup group in Enum.GetValues<WatchGroup>())
                    _baseline[group] = (await _engine.ScanGroupsAsync(new[] { group })).Select(KeyOf).ToHashSet();
                _log.Info($"Kalıcılık tabanı alındı: {_baseline.Sum(b => b.Value.Count)} girdi.", nameof(PersistenceWatchService));

                StartRegistryWatch(ct);
                StartFolderWatch();
                // Bildirimler kurulmadan "hazır" denmez: aradaki değişiklik kaçardı (yarış durumu).
                _armed.Wait(TimeSpan.FromSeconds(10), ct);
                IsBaselineReady = true;
                var lastPoll = DateTime.UtcNow;

                while (!ct.IsCancellationRequested)
                {
                    await _signal.WaitAsync(PollInterval, ct);
                    if (DateTime.UtcNow - lastPoll >= PollInterval)
                    {
                        lastPoll = DateTime.UtcNow;
                        // Görevler ve WMI yalnızca yoklamayla; Run ve başlangıç klasörleri için de güvenlik ağı.
                        foreach (WatchGroup g in Enum.GetValues<WatchGroup>()) _pending.TryAdd(g, 0);
                    }
                    if (_pending.IsEmpty) continue;

                    // Birleştirme: art arda gelen değişiklikler tek taramada ele alınır.
                    await Task.Delay(Debounce, ct);
                    var groups = _pending.Keys.ToList();
                    foreach (var g in groups) _pending.TryRemove(g, out _);
                    await ProcessAsync(groups);
                }
            }
            catch (OperationCanceledException)
            {
                // Durduruldu.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.Error("Canlı kalıcılık izleme durdu.", ex, nameof(PersistenceWatchService));
            }
        }

        private void Signal(WatchGroup group)
        {
            _pending.TryAdd(group, 0);
            if (_signal.CurrentCount == 0)
            {
                try
                {
                    _signal.Release();
                }
                catch (SemaphoreFullException ex)
                {
                    _log.Debug($"İzleme sinyali zaten bekliyor: {ex.Message}", nameof(PersistenceWatchService));
                }
            }
        }

        private async Task ProcessAsync(IReadOnlyCollection<WatchGroup> groups)
        {
            foreach (var group in groups)
            {
                var items = await _engine.ScanGroupsAsync(new[] { group });
                var before = _baseline.TryGetValue(group, out var b) ? b : new HashSet<string>();
                var added = PersistenceWatchRules.Added(before, items, KeyOf);
                _baseline[group] = items.Select(KeyOf).ToHashSet();

                foreach (var item in added)
                {
                    ApplyProvenance(item);
                    Report(item);
                    PersistenceAdded?.Invoke(item);
                }
            }
        }

        /// <summary>Etkin ya da az önce biten Nöbetçi oturumu bu girdiyi eklediyse kaynağı yazılır.</summary>
        private static void ApplyProvenance(PersistenceItem item)
        {
            var sentinel = App.TryGetService<ISetupSentinelService>();
            if (sentinel == null) return;
            if (sentinel.ActiveSession is { IsActive: true } session)
            {
                item.Provenance = $"{session.AppName} kurulumu sırasında eklendi";
                return;
            }
            var index = ProvenanceIndex.Build(sentinel.RecentReports.Select(ProvenanceFromReports.ToTrail));
            if (index.Find(ProvenanceIndex.CandidateKeys(item.RegistryKeyPath, item.RegistryValueName, item.ServiceName, item.TaskPath)) is { } hit)
                item.Provenance = ProvenanceFromReports.Describe(hit);
        }

        private void Report(PersistenceItem item)
        {
            bool gameMode = App.TryGetService<IGameModeService>()?.IsGameModeActive == true;
            bool notify = PersistenceWatchRules.ShouldNotify(item.RiskScore, item.IsMicrosoft, gameMode);
            _log.Info($"Yeni kalıcılık girdisi: {item.Name} ({item.CategoryDisplayName}, {item.RiskLabel} {item.RiskScore}) — {item.RawCommand}", nameof(PersistenceWatchService));
            if (item.IsMicrosoft) return;

            var activity = App.TryGetService<IActivityService>();
            activity?.RecordSimple(Core.Activity.ActivityKind.StartupChange, "Analizör",
                $"Yeni kalıcılık girdisi: {item.Name}",
                $"{item.CategoryDisplayName} · Risk: {item.RiskLabel}" + (item.Provenance != null ? $" · {item.Provenance}" : string.Empty),
                Core.Activity.ActivityOutcome.Succeeded,
                new[] { new Core.Activity.ActivityItem(string.IsNullOrEmpty(item.FilePath) ? item.RawCommand : item.FilePath, item.CategoryDisplayName, "Eklendi") },
                deepLink: "Analyzer");

            if (notify)
            {
                App.Current?.Dispatcher.BeginInvoke(() => App.TryGetService<ITrayIconService>()?.ShowBalloon(
                    $"Yeni başlangıç girdisi: {item.Name}",
                    $"{item.CategoryDisplayName} · Risk: {item.RiskLabel}. Ayrıntılar Analizör'de."));
            }
        }

        #region Kayıt defteri değişim bildirimi

        private const uint RegNotifyChangeName = 0x1;
        private const uint RegNotifyChangeLastSet = 0x4;

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle hKey, bool bWatchSubtree, uint dwNotifyFilter, SafeWaitHandle hEvent, bool fAsynchronous);

        private static readonly (RegistryHive Hive, string Path, bool Subtree, WatchGroup Group)[] WatchedKeys =
        {
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", false, WatchGroup.RunKeys),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", false, WatchGroup.RunKeys),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", false, WatchGroup.RunKeys),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", false, WatchGroup.RunKeys),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", false, WatchGroup.RunKeys),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", false, WatchGroup.RunKeys),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true, WatchGroup.RunKeys),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true, WatchGroup.RunKeys),
            // Yalnızca alt anahtar adları: hizmet ekleme/silme; hizmetlerin çalışma anı yazmaları tetiklemez.
            (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services", false, WatchGroup.Services),
        };

        private void StartRegistryWatch(CancellationToken ct)
        {
            _registryThread = new Thread(() => RegistryLoop(ct)) { IsBackground = true, Name = "Bakım kalıcılık izleyici" };
            _registryThread.Start();
        }

        /// <summary>İzlenen tek bir kayıt anahtarı. Hedef henüz yoksa var olan en yakın üst anahtar izlenir.</summary>
        private sealed class RegistryWatch : IDisposable
        {
            public required RegistryHive Hive { get; init; }
            public required string TargetPath { get; init; }
            public required bool Subtree { get; init; }
            public required WatchGroup Group { get; init; }
            public AutoResetEvent Event { get; } = new(false);
            public RegistryKey? Key { get; set; }
            /// <summary>Hedef anahtar yoktu; üst anahtar izleniyor (anahtar oluşunca hedefe geçilir).</summary>
            public bool WatchingAncestor { get; set; }

            public void Dispose()
            {
                Key?.Dispose();
                Event.Dispose();
            }
        }

        private void RegistryLoop(CancellationToken ct)
        {
            var watches = new List<RegistryWatch>();
            try
            {
                foreach (var w in WatchedKeys)
                {
                    var watch = new RegistryWatch { Hive = w.Hive, TargetPath = w.Path, Subtree = w.Subtree, Group = w.Group };
                    if (Open(watch) && Arm(watch)) watches.Add(watch);
                    else watch.Dispose();
                }
                _armed.Set();
                if (watches.Count == 0) return;

                var handles = watches.Select(k => (WaitHandle)k.Event).Append(ct.WaitHandle).ToArray();
                while (!ct.IsCancellationRequested)
                {
                    int index = WaitHandle.WaitAny(handles);
                    if (index == handles.Length - 1) break;
                    var watch = watches[index];
                    Signal(watch.Group);
                    // Üst anahtar izleniyorsa hedef oluşmuş olabilir: hedefe geç.
                    if (watch.WatchingAncestor)
                    {
                        watch.Key?.Dispose();
                        watch.Key = null;
                        Open(watch);
                    }
                    Arm(watch); // bildirim tek seferlik: yeniden kurulur
                }
            }
            finally
            {
                foreach (var watch in watches) watch.Dispose();
            }
        }

        /// <summary>
        /// Hedef anahtarı açar; yoksa var olan en yakın üst anahtarı açar. Temiz bir kullanıcı hesabında
        /// HKCU\…\Run bulunmayabilir: ilk program kendini eklediğinde anahtar yeni oluşur ve yalnız hedefi
        /// izleyen bir izleyici bunu hiç görmezdi.
        /// </summary>
        private bool Open(RegistryWatch watch)
        {
            try
            {
                var root = RegistryKey.OpenBaseKey(watch.Hive, RegistryView.Registry64);
                string path = watch.TargetPath;
                while (true)
                {
                    var key = path.Length == 0 ? root : root.OpenSubKey(path);
                    if (key != null)
                    {
                        if (!ReferenceEquals(key, root)) root.Dispose();
                        watch.Key = key;
                        watch.WatchingAncestor = !string.Equals(path, watch.TargetPath, StringComparison.OrdinalIgnoreCase);
                        return true;
                    }
                    int slash = path.LastIndexOf('\\');
                    path = slash > 0 ? path[..slash] : string.Empty;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Debug($"Kayıt anahtarı izlenemiyor: {watch.TargetPath} — {ex.Message}", nameof(PersistenceWatchService));
                return false;
            }
        }

        private bool Arm(RegistryWatch watch)
        {
            if (watch.Key == null) return false;
            // Üst anahtar izlenirken alt anahtar oluşumu (ad) alt ağaçta aranır.
            bool subtree = watch.Subtree || watch.WatchingAncestor;
            uint filter = watch.Group == WatchGroup.Services && !watch.WatchingAncestor ? RegNotifyChangeName : RegNotifyChangeName | RegNotifyChangeLastSet;
            int rc = RegNotifyChangeKeyValue(watch.Key.Handle, subtree, filter, watch.Event.SafeWaitHandle, true);
            if (rc != 0) _log.Debug($"Kayıt değişim bildirimi kurulamadı ({watch.Key.Name}): {rc}", nameof(PersistenceWatchService));
            return rc == 0;
        }

        #endregion

        private void StartFolderWatch()
        {
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var fsw = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
                    fsw.Created += (_, _) => Signal(WatchGroup.StartupFolders);
                    fsw.Renamed += (_, _) => Signal(WatchGroup.StartupFolders);
                    fsw.Changed += (_, _) => Signal(WatchGroup.StartupFolders);
                    _watchers.Add(fsw);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    _log.Debug($"Başlangıç klasörü izlenemiyor: {folder} — {ex.Message}", nameof(PersistenceWatchService));
                }
            }
        }
    }

    /// <summary>Nöbetçi raporlarını kaynak dizinine çevirir.</summary>
    public static class ProvenanceFromReports
    {
        public static SetupTrail ToTrail(SetupDeltaReport report)
        {
            var keys = new List<string>(report.AddedStartupEntries);
            keys.AddRange(report.AddedServices);
            keys.AddRange(report.SystemChanges.Where(c => c.Area == Core.Sentinel.SystemArea.ScheduledTask && c.Kind == Core.Sentinel.ChangeKind.Added).Select(c => c.Key));
            return new SetupTrail(report.AppName, report.InstallTime, keys);
        }

        public static string Describe((string App, DateTime When) hit) =>
            $"{hit.App} kurulumu ekledi ({hit.When.ToLocalTime():d MMMM yyyy})";
    }
}
