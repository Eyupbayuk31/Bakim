using System;
using System.IO;
using System.Text.Json;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IAppSettingsService
    {
        /// <summary>Bellekteki güncel ayar anlık görüntüsü. Asla null dönmez.</summary>
        AppSettingsData Current { get; }

        string SettingsFilePath { get; }

        /// <summary>Ayarlar diskten yeniden okunur ve <see cref="Current"/> tazelenir.</summary>
        AppSettingsData Load();

        /// <summary>Ayarları atomik olarak diske yazar ve <see cref="SettingsChanged"/> yayınlar.</summary>
        void Save(AppSettingsData data);

        /// <summary>Tek bir alanı değiştirip kaydetmek için kısa yol.</summary>
        void Update(Action<AppSettingsData> mutate);

        /// <summary>Ayarlar her kaydedildiğinde tetiklenir. Abonelerin UI thread'ine kendileri geçmesi gerekir.</summary>
        event Action<AppSettingsData>? SettingsChanged;
    }

    /// <summary>
    /// appsettings.json için tek yazma/okuma kapısı.
    /// Daha önce SettingsViewModel, VirusTotalCheckService ve UninstallerViewModel aynı dosyayı
    /// birbirinden habersiz okuyup yazıyordu; bu servis o yarış durumunu ortadan kaldırır.
    /// </summary>
    public sealed class AppSettingsService : IAppSettingsService
    {
        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly object _gate = new();
        private readonly ILogService _log;
        private AppSettingsData _current = new();

        public AppSettingsService() : this(NullLogService.Instance) { }

        public AppSettingsService(ILogService log)
        {
            _log = log ?? NullLogService.Instance;

            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bakim");

            SettingsFilePath = Path.Combine(appFolder, "appsettings.json");

            try
            {
                Directory.CreateDirectory(appFolder);
            }
            catch (Exception ex)
            {
                _log.Error("Uygulama veri klasörü oluşturulamadı.", ex, nameof(AppSettingsService));
            }

            Load();
        }

        public string SettingsFilePath { get; }

        public AppSettingsData Current
        {
            get { lock (_gate) { return _current; } }
        }

        public event Action<AppSettingsData>? SettingsChanged;

        public AppSettingsData Load()
        {
            AppSettingsData loaded;

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    loaded = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
                    _log.Debug("Ayarlar diskten yüklendi.", nameof(AppSettingsService));
                }
                else
                {
                    loaded = new AppSettingsData();
                    _log.Info("Ayar dosyası yok, varsayılanlar kullanılıyor.", nameof(AppSettingsService));
                }
            }
            catch (Exception ex)
            {
                // Bozuk JSON: kullanıcıyı kilitlemek yerine varsayılana dön ve bozuk dosyayı sakla.
                _log.Error("Ayar dosyası okunamadı, varsayılanlara dönülüyor.", ex, nameof(AppSettingsService));
                TryQuarantineCorruptFile();
                loaded = new AppSettingsData();
            }

            Normalize(loaded);

            lock (_gate) { _current = loaded; }
            return loaded;
        }

        public void Save(AppSettingsData data)
        {
            if (data == null) return;

            Normalize(data);

            AppSettingsData snapshot = data.Clone();

            try
            {
                lock (_gate)
                {
                    string? dir = Path.GetDirectoryName(SettingsFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // Atomik yazma: önce geçici dosya, sonra yer değiştirme.
                    // Yazma sırasında elektrik kesilse bile ayar dosyası bozulmaz.
                    string tempPath = SettingsFilePath + ".tmp";
                    File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, WriteOptions));

                    if (File.Exists(SettingsFilePath))
                        File.Replace(tempPath, SettingsFilePath, null, ignoreMetadataErrors: true);
                    else
                        File.Move(tempPath, SettingsFilePath);

                    _current = snapshot;
                }

                _log.Debug("Ayarlar kaydedildi.", nameof(AppSettingsService));
            }
            catch (Exception ex)
            {
                _log.Error("Ayarlar kaydedilemedi.", ex, nameof(AppSettingsService));
                return;
            }

            try
            {
                SettingsChanged?.Invoke(snapshot);
            }
            catch (Exception ex)
            {
                _log.Error("Ayar değişikliği aboneleri hata verdi.", ex, nameof(AppSettingsService));
            }
        }

        public void Update(Action<AppSettingsData> mutate)
        {
            if (mutate == null) return;

            AppSettingsData copy;
            lock (_gate) { copy = _current.Clone(); }

            try
            {
                mutate(copy);
            }
            catch (Exception ex)
            {
                _log.Error("Ayar güncelleme temsilcisi hata verdi.", ex, nameof(AppSettingsService));
                return;
            }

            Save(copy);
        }

        /// <summary>Dışarıdan gelen (içe aktarma / elle düzenleme) değerleri güvenli aralığa çeker.</summary>
        private static void Normalize(AppSettingsData data)
        {
            if (data.RefreshIntervalSeconds < 1) data.RefreshIntervalSeconds = 1;
            if (data.RefreshIntervalSeconds > 10) data.RefreshIntervalSeconds = 10;

            if (data.AutoRamCleanIntervalMinutes < 0) data.AutoRamCleanIntervalMinutes = 0;
            if (data.AutoRamCleanIntervalMinutes > 1440) data.AutoRamCleanIntervalMinutes = 1440;

            data.Theme ??= "MicaDark";
            data.VirusTotalApiKey ??= string.Empty;
            data.VirusTotalApiKeyProtected ??= string.Empty;

            if (data.SchemaVersion < 1) data.SchemaVersion = 1;
        }

        private void TryQuarantineCorruptFile()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                string backup = SettingsFilePath + ".corrupt";
                File.Copy(SettingsFilePath, backup, overwrite: true);
                _log.Warning($"Bozuk ayar dosyası yedeklendi: {backup}", null, nameof(AppSettingsService));
            }
            catch (Exception ex)
            {
                _log.Warning("Bozuk ayar dosyası yedeklenemedi.", ex, nameof(AppSettingsService));
            }
        }
    }
}
