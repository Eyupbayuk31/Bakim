using System;
using System.Threading.Tasks;

namespace Bakım.Services
{
    /// <summary>
    /// Tüm sistem donanım bilgilerini (CPU, GPU, RAM, Diskler) 
    /// 64-bit doğrulukla ve WMI taşmaları engellenmiş olarak sağlayan merkezi servis.
    /// </summary>
    public class HardwareInfoService
    {
        private static readonly Lazy<HardwareInfoService> _instance = new(() => new HardwareInfoService());
        public static HardwareInfoService Instance => _instance.Value;

        private readonly GpuMonitoringEngine _gpuEngine;

        public HardwareInfoService()
        {
            _gpuEngine = GpuMonitoringEngine.Instance;
        }

        /// <summary>
        /// GPU bilgilerini 64-bit DXGI & Registry QWORD mimarisi ile döndürür.
        /// </summary>
        public (string Name, double VramGB) GetGpuVramDetails()
        {
            return GpuInfoProvider.GetPrimaryGpuDetails();
        }

        /// <summary>
        /// Ayrıntılı GPU telemetri özetini döndürür.
        /// </summary>
        public GpuTelemetrySnapshot GetGpuTelemetry(bool forceRefresh = false)
        {
            return _gpuEngine.GetSnapshot(forceRefresh);
        }

        /// <summary>
        /// Ayrıntılı GPU telemetri özetini asenkron olarak döndürür.
        /// </summary>
        public Task<GpuTelemetrySnapshot> GetGpuTelemetryAsync(bool forceRefresh = false)
        {
            return _gpuEngine.GetSnapshotAsync(forceRefresh);
        }
    }
}
