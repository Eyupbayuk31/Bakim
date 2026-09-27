using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Uninstall;
using Bakım.Models;

namespace Bakım.Services.Uninstall
{
    public interface IUninstallWatchService
    {
        void Start();
        void Stop();

        /// <summary>Bakım dışından kaldırılan program (kaldırma kaydı silinmiş, güncelleme değil). Arka plan iş parçacığında.</summary>
        event Action<InstalledAppItem>? AppUninstalledExternally;
    }

    /// <summary>
    /// Bakım dışından yapılan kaldırmaları yakalar: Windows Ayarlar, programın kendi kaldırıcısı, winget… Kaldırma
    /// kayıtlarının alt anahtar adları 15 sn'de bir ucuzca karşılaştırılır; değişince tam liste okunur ve
    /// <see cref="UninstallWatchRules.Removed"/> ile gerçek kaldırmalar ayrılır. Kaldırılan programın bilgileri
    /// (klasör, yayıncı, anahtar) önceki anlık görüntüden gelir; kayıt artık olmadığı için başka yerden okunamaz.
    /// </summary>
    public sealed class UninstallWatchService : IUninstallWatchService, IDisposable
    {
        private readonly TimeSpan PollInterval;
        /// <summary>Kaldırıcılar kaydı silip dosyaları sonra temizleyebilir; kalıntı taraması erken yapılmasın.</summary>
        private readonly TimeSpan SettleDelay;

        private static readonly ConcurrentDictionary<string, DateTime> BakimInitiated = new(StringComparer.OrdinalIgnoreCase);

        private readonly IUninstallerService _uninstaller;
        private readonly ILogService _log;
        private CancellationTokenSource? _cts;
        private Dictionary<string, InstalledAppItem> _snapshot = new(StringComparer.OrdinalIgnoreCase);
        private string _fingerprint = string.Empty;

        public event Action<InstalledAppItem>? AppUninstalledExternally;

        /// <summary>Yoklama 15 sn, yerleşme beklemesi 8 sn; testler kısaltabilir.</summary>
        public UninstallWatchService(IUninstallerService uninstaller, ILogService log, TimeSpan? pollInterval = null, TimeSpan? settleDelay = null)
        {
            _uninstaller = uninstaller;
            _log = log;
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
            SettleDelay = settleDelay ?? TimeSpan.FromSeconds(8);
        }

        /// <summary>İlk anlık görüntü alındı; bundan sonraki kaldırmalar yakalanır.</summary>
        public bool IsBaselineReady { get; private set; }

        /// <summary>Bakım bir kaldırma başlatırken çağrılır: kaldırma sihirbazı kalıntıları zaten gösterir, ikinci bildirim çıkmaz.</summary>
        public static void MarkInitiatedByBakim(string? registryKeyPath)
        {
            if (!string.IsNullOrWhiteSpace(registryKeyPath)) BakimInitiated[registryKeyPath] = DateTime.UtcNow;
        }

        public void Start()
        {
            if (_cts != null) return;
            var cts = _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        public void Dispose() => Stop();

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                _fingerprint = ReadFingerprint();
                _snapshot = await ReadSnapshotAsync();
                IsBaselineReady = true;
                _log.Debug($"Kaldırma izleme başladı ({_snapshot.Count} kayıt).", nameof(UninstallWatchService));

                using var timer = new PeriodicTimer(PollInterval);
                while (await timer.WaitForNextTickAsync(ct))
                {
                    string fingerprint = ReadFingerprint();
                    if (fingerprint == _fingerprint) continue;

                    // Birden çok anahtarı sırayla silen kaldırıcılar ve MSI işlemleri için kısa bekleme.
                    await Task.Delay(SettleDelay, ct);
                    _fingerprint = ReadFingerprint();
                    var current = await ReadSnapshotAsync();
                    Compare(_snapshot, current);
                    _snapshot = current;
                }
            }
            catch (OperationCanceledException)
            {
                // Durduruldu.
            }
            catch (Exception ex)
            {
                _log.Warning("Kaldırma izleme durdu.", ex, nameof(UninstallWatchService));
            }
        }

        private void Compare(Dictionary<string, InstalledAppItem> before, Dictionary<string, InstalledAppItem> after)
        {
            // Süresi geçmiş işaretler atılır.
            foreach (var old in BakimInitiated.Where(kv => DateTime.UtcNow - kv.Value > UninstallWatchRules.BakimInitiatedWindow).ToList())
                BakimInitiated.TryRemove(old.Key, out _);

            var removed = UninstallWatchRules.Removed(before.Values.Select(ToEntry), after.Values.Select(ToEntry),
                new Dictionary<string, DateTime>(BakimInitiated, StringComparer.OrdinalIgnoreCase));

            foreach (var entry in removed)
            {
                if (!before.TryGetValue(entry.KeyPath, out var app)) continue;
                _log.Info($"Bakım dışından kaldırma algılandı: {app.DisplayName}", nameof(UninstallWatchService));
                try
                {
                    AppUninstalledExternally?.Invoke(app);
                }
                catch (Exception ex)
                {
                    _log.Warning($"Kaldırma bildirimi işlenemedi: {app.DisplayName}", ex, nameof(UninstallWatchService));
                }
            }
        }

        private static UninstallSnapshotEntry ToEntry(InstalledAppItem a) => new(a.RegistryKeyPath, a.DisplayName, a.InstallLocation, a.IsSystemComponent);

        private async Task<Dictionary<string, InstalledAppItem>> ReadSnapshotAsync()
        {
            var map = new Dictionary<string, InstalledAppItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in await _uninstaller.GetInstalledAppsAsync())
                if (!string.IsNullOrWhiteSpace(app.RegistryKeyPath)) map[app.RegistryKeyPath] = app;
            return map;
        }

        private static readonly (RegistryHive Hive, RegistryView View)[] Roots =
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64)
        };

        private string ReadFingerprint()
        {
            var names = new List<string>();
            foreach (var (hive, view) in Roots)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key != null) names.AddRange(key.GetSubKeyNames().Select(n => $"{hive}{view}\\{n}"));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
                {
                    _log.Debug($"Kaldırma kayıtları okunamadı ({hive}/{view}): {ex.Message}", nameof(UninstallWatchService));
                }
            }
            return UninstallWatchRules.Fingerprint(names);
        }
    }
}
