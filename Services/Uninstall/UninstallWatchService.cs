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
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

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
            IsBaselineReady = false;
            _ = Task.Run(() => LoopAsync(cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// İzleme döngüsü. Başlangıç, taban ve her kayıt değişikliği Bilgi düzeyinde günlüğe yazılır: eskiden yalnızca
        /// ayrıntılı günlükte görünüyordu ve izlemenin çalışıp çalışmadığı sahada anlaşılamıyordu. Tek bir turdaki hata
        /// döngüyü bitirmez; taban alınamazsa bir süre sonra yeniden denenir.
        /// </summary>
        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                while (!IsBaselineReady)
                {
                    try
                    {
                        _fingerprint = ReadFingerprint();
                        _snapshot = await ReadSnapshotAsync();
                        IsBaselineReady = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warning("Kaldırma izleme tabanı alınamadı; yeniden denenecek.", ex, nameof(UninstallWatchService));
                        await Task.Delay(RetryDelay, ct);
                    }
                }
                _log.Info($"Kaldırma izleme başladı: {_snapshot.Count} program kaydı izleniyor.", nameof(UninstallWatchService));

                using var timer = new PeriodicTimer(PollInterval);
                while (await timer.WaitForNextTickAsync(ct))
                {
                    try
                    {
                        await PollOnceAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warning("Kaldırma izleme turu başarısız; izleme sürüyor.", ex, nameof(UninstallWatchService));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _log.Info("Kaldırma izleme durduruldu.", nameof(UninstallWatchService));
            }
            catch (Exception ex)
            {
                _log.Warning("Kaldırma izleme beklenmedik şekilde durdu.", ex, nameof(UninstallWatchService));
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            string fingerprint = ReadFingerprint();
            if (fingerprint == _fingerprint) return;

            // Birden çok anahtarı sırayla silen kaldırıcılar ve MSI işlemleri için kısa bekleme.
            await Task.Delay(SettleDelay, ct);
            _fingerprint = ReadFingerprint();
            var current = await ReadSnapshotAsync();
            int added = current.Keys.Count(k => !_snapshot.ContainsKey(k));
            int gone = _snapshot.Keys.Count(k => !current.ContainsKey(k));
            _log.Info($"Program kayıtları değişti: +{added} / -{gone}.", nameof(UninstallWatchService));
            Compare(_snapshot, current);
            _snapshot = current;
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
