using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// Kernel ve WMI tabanlı gerçek zamanlı süreç izleme sensörü (NÖB Faz 8 / Tam Koruma Modu).
    /// Polling gecikmesini sıfıra indirir; kurulum süreçlerinin çocuklarını anında yakalar.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class KernelTraceSensor : IDisposable
    {
        private ManagementEventWatcher? _startWatcher;
        private ManagementEventWatcher? _stopWatcher;
        private bool _isRunning;

        public event Action<int, string, int>? ProcessStarted;
        public event Action<int, string>? ProcessStopped;

        public bool IsRunning => _isRunning;

        public bool Start()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                Stop();

                // Win32_ProcessStartTrace: Her yeni süreç başladığında kernel seviyesinden olay fırlatır
                var startQuery = new WqlEventQuery("Win32_ProcessStartTrace");
                _startWatcher = new ManagementEventWatcher(startQuery);
                _startWatcher.EventArrived += OnProcessStartArrived;
                _startWatcher.Start();

                var stopQuery = new WqlEventQuery("Win32_ProcessStopTrace");
                _stopWatcher = new ManagementEventWatcher(stopQuery);
                _stopWatcher.EventArrived += OnProcessStopArrived;
                _stopWatcher.Start();

                _isRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Debug($"Kernel süreç izleyici başlatılamadı (yetersiz yetki olabilir): {ex.Message}", nameof(KernelTraceSensor));
                Stop();
                return false;
            }
        }

        private void OnProcessStartArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                using var evt = e.NewEvent;
                if (evt == null) return;

                int pid = Convert.ToInt32(evt.Properties["ProcessID"]?.Value ?? 0);
                int parentPid = Convert.ToInt32(evt.Properties["ParentProcessID"]?.Value ?? 0);
                string name = evt.Properties["ProcessName"]?.Value?.ToString() ?? string.Empty;

                if (pid > 0)
                {
                    ProcessStarted?.Invoke(pid, name, parentPid);
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"StartTrace olayı işlenirken hata: {ex.Message}", nameof(KernelTraceSensor));
            }
        }

        private void OnProcessStopArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                using var evt = e.NewEvent;
                if (evt == null) return;

                int pid = Convert.ToInt32(evt.Properties["ProcessID"]?.Value ?? 0);
                string name = evt.Properties["ProcessName"]?.Value?.ToString() ?? string.Empty;

                if (pid > 0)
                {
                    ProcessStopped?.Invoke(pid, name);
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"StopTrace olayı işlenirken hata: {ex.Message}", nameof(KernelTraceSensor));
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_startWatcher != null)
                {
                    _startWatcher.Stop();
                    _startWatcher.Dispose();
                    _startWatcher = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"StartWatcher kapatılamadı: {ex.Message}", nameof(KernelTraceSensor));
            }

            try
            {
                if (_stopWatcher != null)
                {
                    _stopWatcher.Stop();
                    _stopWatcher.Dispose();
                    _stopWatcher = null;
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"StopWatcher kapatılamadı: {ex.Message}", nameof(KernelTraceSensor));
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
