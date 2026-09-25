using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Bakım.Services
{
    public enum IpcRequestKind { Activate, UninstallTarget }

    public sealed record IpcRequest(IpcRequestKind Kind, string? Target, DateTime CreatedUtc);

    /// <summary>
    /// Tek örnek ve süreçler arası istek (KAL C2). Bakım açıkken yeniden başlatılırsa ya da sağ tık
    /// "Bakım ile Kaldır" seçilirse istek açık örneğe iletilir; ikinci bir nöbetçi, tepsi simgesi ya da
    /// pencere açılmaz.
    ///
    /// Adlandırılmış kanal yerine dosya kutusu kullanılır: ana örnek yönetici (yüksek bütünlük) olarak
    /// çalışırken standart kullanıcı süreci kanala yazamaz (UIPI / bütünlük etiketi), aynı kullanıcının
    /// %LocalAppData% klasörüne ise yazabilir.
    /// </summary>
    public sealed class SingleInstanceService : IDisposable
    {
        public const string MutexName = @"Local\Bakim.SingleInstance.v1";

        /// <summary>Bundan eski istekler yok sayılır (ör. kapalıyken birikenler).</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions Json = new();
        private readonly string _inbox;
        private Mutex? _mutex;
        private FileSystemWatcher? _watcher;
        private Action<IpcRequest>? _handler;

        public SingleInstanceService(string? inboxDirectory = null)
        {
            _inbox = inboxDirectory ?? DefaultInbox;
        }

        public static string DefaultInbox { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "ipc");

        /// <summary>Ana örnek olmayı dener. false: başka bir örnek zaten çalışıyor.</summary>
        public bool TryBecomePrimary()
        {
            try
            {
                var mutex = new Mutex(true, MutexName, out bool created);
                if (!created)
                {
                    mutex.Dispose();
                    return false;
                }
                _mutex = mutex;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Yönetici olarak çalışan örneğin nesnesi: var ama açılamıyor.
                return false;
            }
        }

        /// <summary>Ana örnek çalışıyor mu? (Nesneyi sahiplenmeden bakar.)</summary>
        public static bool IsPrimaryRunning()
        {
            try
            {
                if (!Mutex.TryOpenExisting(MutexName, out var existing)) return false;
                existing.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        /// <summary>
        /// İsteği ana örneğe bırakır ve alındığını (dosyanın silinmesini) bekler. Süre dolarsa
        /// istek geri çekilir ki ana örnek sonradan ikinci kez işlemesin.
        /// </summary>
        public bool Send(IpcRequest request, TimeSpan timeout)
        {
            string file;
            try
            {
                Directory.CreateDirectory(_inbox);
                file = Path.Combine(_inbox, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(request, Json));
                File.Move(tmp, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning("İstek açık Bakım örneğine iletilemedi.", ex, nameof(SingleInstanceService));
                return false;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (!File.Exists(file)) return true;
                Thread.Sleep(100);
            }

            try
            {
                if (!File.Exists(file)) return true;
                File.Delete(file);
                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Silinemiyorsa ana örnek şu an okuyor: iletilmiş sayılır.
                return true;
            }
        }

        /// <summary>Ana örnek: kutudaki istekleri dinler. İşleyici çağıran iş parçacığında değil, izleyicide çalışır.</summary>
        public void Listen(Action<IpcRequest> handler)
        {
            _handler = handler;
            try
            {
                Directory.CreateDirectory(_inbox);
                foreach (string stale in Directory.GetFiles(_inbox))
                    Consume(stale);

                _watcher = new FileSystemWatcher(_inbox, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (_, e) => Consume(e.FullPath);
                _watcher.Renamed += (_, e) => Consume(e.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                AppLog.Warning("Tek örnek istek kutusu dinlenemiyor; ikinci açılışlar yeni pencere açar.", ex, nameof(SingleInstanceService));
            }
        }

        /// <summary>İsteği okur, siler ve (tazeyse) işleyiciye verir.</summary>
        public IpcRequest? Consume(string path)
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // Yarım kalmış .tmp dosyaları: eskiyse temizlenir.
                TryDeleteIfOld(path);
                return null;
            }

            IpcRequest? request = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    request = JsonSerializer.Deserialize<IpcRequest>(File.ReadAllText(path), Json);
                    File.Delete(path);
                    break;
                }
                catch (FileNotFoundException)
                {
                    return null; // başka bir olay zaten işledi
                }
                catch (JsonException ex)
                {
                    AppLog.Warning($"Geçersiz istek dosyası silindi: {path}", ex, nameof(SingleInstanceService));
                    TryDelete(path);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(50); // yazan süreç dosyayı henüz bırakmadı
                }
            }

            if (request == null) return null;
            if (DateTime.UtcNow - request.CreatedUtc > MaxAge)
            {
                AppLog.Info($"Eski istek yok sayıldı: {request.Kind}", nameof(SingleInstanceService));
                return null;
            }

            try
            {
                _handler?.Invoke(request);
            }
            catch (Exception ex)
            {
                AppLog.Error("Tek örnek isteği işlenemedi.", ex, nameof(SingleInstanceService));
            }
            return request;
        }

        private static void TryDeleteIfOld(string path)
        {
            try
            {
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > MaxAge) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Eski istek dosyası silinemedi: {ex.Message}", nameof(SingleInstanceService));
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"İstek dosyası silinemedi: {ex.Message}", nameof(SingleInstanceService));
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _watcher = null;
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Başka bir iş parçacığından bırakılamaz; tanıtıcı kapanınca sistem bırakır.
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
