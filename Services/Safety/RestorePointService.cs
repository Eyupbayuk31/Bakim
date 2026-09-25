using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Bakım.Helpers;

namespace Bakım.Services.Safety
{
    public enum RestorePointOutcome
    {
        Created,
        SkippedFrequencyLimit,
        Disabled,
        NotAdmin,
        Failed
    }

    public sealed record RestorePointResult(RestorePointOutcome Outcome, string Message, DateTime? LastRestorePointLocal = null)
    {
        public bool Created => Outcome == RestorePointOutcome.Created;
    }

    public interface IRestorePointService
    {
        Task<RestorePointResult> CreateAsync(string description, RestorePointKind kind = RestorePointKind.ModifySettings);
    }

    public enum RestorePointKind
    {
        ApplicationUninstall = 1,
        ModifySettings = 12
    }

    /// <summary>
    /// Windows Sistem Geri Yükleme noktası oluşturmanın TEK yolu.
    ///
    /// Eski kod üç ayrı yerde "powershell Checkpoint-Computer" komutunu runas ile
    /// çalıştırıyordu: zaten yöneticiyken bile UAC istemi açılıyor, pencere
    /// görünüyordu ve Windows'un 24 saat sınırı yüzünden sessizce başarısız
    /// olup "oluşturuldu" deniyordu. Burada WMI kullanılır ve sonuç dürüstçe döner.
    /// </summary>
    public sealed class RestorePointService : IRestorePointService
    {
        private readonly ILogService _log;

        public RestorePointService(ILogService log)
        {
            _log = log;
        }

        public Task<RestorePointResult> CreateAsync(string description, RestorePointKind kind = RestorePointKind.ModifySettings)
        {
            return Task.Run(() => CreateCore(description, kind));
        }

        private RestorePointResult CreateCore(string description, RestorePointKind kind)
        {
            if (!UacHelper.IsAdministrator())
                return new RestorePointResult(RestorePointOutcome.NotAdmin, "Geri yükleme noktası için yönetici yetkisi gerekir.");

            DateTime? last = TryGetLastRestorePoint();
            if (last.HasValue && DateTime.Now - last.Value < TimeSpan.FromHours(24))
            {
                return new RestorePointResult(RestorePointOutcome.SkippedFrequencyLimit,
                    $"Son 24 saatte zaten bir geri yükleme noktası var ({last.Value:dd.MM.yyyy HH:mm}). Windows yenisini oluşturmaz.",
                    last);
            }

            try
            {
                string safe = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray());
                if (safe.Length > 60) safe = safe[..60];
                if (string.IsNullOrWhiteSpace(safe)) safe = "Bakim";

                using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
                using var input = cls.GetMethodParameters("CreateRestorePoint");
                input["Description"] = "Bakim: " + safe;
                input["RestorePointType"] = (int)kind;
                input["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
                uint code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);

                if (code == 0)
                    return new RestorePointResult(RestorePointOutcome.Created, "Geri yükleme noktası oluşturuldu.", DateTime.Now);

                // 1058: hizmet devre dışı (Sistem Koruması kapalı)
                if (code == 1058)
                    return new RestorePointResult(RestorePointOutcome.Disabled, "Sistem Koruması bu sürücüde kapalı.");

                return new RestorePointResult(RestorePointOutcome.Failed, $"Geri yükleme noktası oluşturulamadı (kod {code}).");
            }
            catch (ManagementException ex)
            {
                _log.Warning("Geri yükleme noktası WMI hatası.", ex, nameof(RestorePointService));
                return new RestorePointResult(RestorePointOutcome.Disabled, "Sistem Geri Yükleme kullanılamıyor (kapalı olabilir).");
            }
            catch (Exception ex)
            {
                _log.Error("Geri yükleme noktası oluşturulamadı.", ex, nameof(RestorePointService));
                return new RestorePointResult(RestorePointOutcome.Failed, "Geri yükleme noktası oluşturulamadı: " + ex.Message);
            }
        }

        private DateTime? TryGetLastRestorePoint()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT CreationTime FROM SystemRestore");
                DateTime? latest = null;
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string? raw = mo["CreationTime"]?.ToString();
                        if (string.IsNullOrEmpty(raw)) continue;
                        DateTime t = ManagementDateTimeConverter.ToDateTime(raw);
                        if (latest == null || t > latest) latest = t;
                    }
                }
                return latest;
            }
            catch (ManagementException ex)
            {
                _log.Debug($"Son geri yükleme noktası okunamadı: {ex.Message}", nameof(RestorePointService));
                return null;
            }
        }
    }
}
