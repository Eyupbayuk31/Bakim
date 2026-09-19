using System;
using System.Threading.Tasks;

namespace Bakım.Services
{
    /// <summary>
    /// GPU telemetri verisi modeli (64-bit VRAM, Sürücü ve Donanım kimlikleri).
    /// </summary>
    public class GpuTelemetrySnapshot
    {
        public string ModelName { get; set; } = string.Empty;
        public double DedicatedVramGB { get; set; }
        public string FormattedVram { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public bool IsDiscrete { get; set; } = true;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// DirectX DXGI 64-bit API ve Registry QWORD motorunu kullanarak 
    /// ekran kartı durumunu, bellek kapasitesini ve sürücü bilgilerini 
    /// WMI 32-bit taşması (4 GB) olmaksızın izleyen motor.
    /// </summary>
    public class GpuMonitoringEngine
    {
        private static readonly Lazy<GpuMonitoringEngine> _instance = new(() => new GpuMonitoringEngine());
        public static GpuMonitoringEngine Instance => _instance.Value;

        private GpuTelemetrySnapshot? _cachedSnapshot;
        private readonly object _lock = new();

        /// <summary>
        /// Anlık veya önbelleğe alınmış GPU telemetri özetini döndürür.
        /// </summary>
        public GpuTelemetrySnapshot GetSnapshot(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (_cachedSnapshot != null && !forceRefresh)
                {
                    return _cachedSnapshot;
                }

                var (name, formattedVram, vramGb, driver) = GpuInfoProvider.GetCompleteGpuDetails();

                string vendor = "Bilinmeyen";
                if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    vendor = "AMD";
                else if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || name.Contains("RTX", StringComparison.OrdinalIgnoreCase))
                    vendor = "NVIDIA";
                else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                    vendor = "Intel";

                bool isDiscrete = vramGb > 0 && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase);

                _cachedSnapshot = new GpuTelemetrySnapshot
                {
                    ModelName = !string.IsNullOrWhiteSpace(name) ? name : "Harici Ekran Kartı",
                    DedicatedVramGB = vramGb,
                    FormattedVram = formattedVram,
                    DriverVersion = driver,
                    VendorName = vendor,
                    IsDiscrete = isDiscrete,
                    Timestamp = DateTime.Now
                };

                return _cachedSnapshot;
            }
        }

        /// <summary>
        /// Asenkron olarak GPU telemetri verisini çeker.
        /// </summary>
        public Task<GpuTelemetrySnapshot> GetSnapshotAsync(bool forceRefresh = false)
        {
            return Task.Run(() => GetSnapshot(forceRefresh));
        }
    }
}
