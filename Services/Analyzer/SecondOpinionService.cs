using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Analyzer;

namespace Bakım.Services.Analyzer
{
    /// <summary>Antivirüs ikinci görüşü: Windows Defender dosya taraması ve AMSI içerik taraması (ANL Faz 3).</summary>
    public interface ISecondOpinionService
    {
        /// <summary>Defender etkin mi? null: durum okunamadı (tarama yine denenir).</summary>
        bool? IsDefenderActive();

        Task<DefenderScanResult> ScanFileWithDefenderAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Metni sistemdeki AMSI sağlayıcısına (Defender ya da uyumlu antivirüs) taratır. true: zararlı
        /// bulundu; false: bulunmadı; null: AMSI kullanılamıyor. "Bulunmadı" temizlik kanıtı sayılmaz
        /// (sağlayıcı yoksa da bu sonuç döner).
        /// </summary>
        bool? ScanContentWithAmsi(string content, string contentName);
    }

    public sealed class SecondOpinionService : ISecondOpinionService
    {
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(2);
        private bool? _defenderActive;
        private DateTime _statusReadUtc = DateTime.MinValue;

        public bool? IsDefenderActive()
        {
            if (DateTime.UtcNow - _statusReadUtc < TimeSpan.FromMinutes(5)) return _defenderActive;
            _statusReadUtc = DateTime.UtcNow;
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled, AMServiceEnabled FROM MSFT_MpComputerStatus");
                foreach (ManagementObject status in searcher.Get())
                {
                    using (status)
                    {
                        _defenderActive = status["AntivirusEnabled"] is true && status["AMServiceEnabled"] is true;
                        return _defenderActive;
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Defender durumu okunamadı: {ex.Message}", nameof(SecondOpinionService));
            }
            _defenderActive = null;
            return null;
        }

        /// <summary>En güncel platform sürümündeki MpCmdRun.exe (yoksa Program Files'taki).</summary>
        private static string? FindMpCmdRun()
        {
            string platform = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
            try
            {
                if (Directory.Exists(platform))
                {
                    string? latest = Directory.GetDirectories(platform)
                        .Select(d => Path.Combine(d, "MpCmdRun.exe"))
                        .Where(File.Exists)
                        .OrderByDescending(p => Version.TryParse(Path.GetFileName(Path.GetDirectoryName(p))!.Split('-')[0], out var v) ? v : new Version(0, 0))
                        .FirstOrDefault();
                    if (latest != null) return latest;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Defender platform klasörü okunamadı: {ex.Message}", nameof(SecondOpinionService));
            }
            string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            return File.Exists(classic) ? classic : null;
        }

        public async Task<DefenderScanResult> ScanFileWithDefenderAsync(string path, CancellationToken ct = default)
        {
            if (!File.Exists(path)) return DefenderScanResult.Failed("Dosya bulunamadı.");
            if (IsDefenderActive() == false)
                return DefenderScanResult.Unavailable("Windows Defender kapalı ya da başka bir antivirüs etkin; ikinci görüş alınamadı.");
            string? exe = FindMpCmdRun();
            if (exe == null) return DefenderScanResult.Unavailable("Windows Defender komut satırı aracı bulunamadı.");

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string arg in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) psi.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return DefenderScanResult.Failed("Windows Defender başlatılamadı.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ScanTimeout);
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { AppLog.Debug("Defender taraması zaten bitmişti.", nameof(SecondOpinionService)); }
                    return ct.IsCancellationRequested ? DefenderScanResult.Failed("Tarama iptal edildi.") : DefenderScanResult.Failed("Windows Defender taraması zaman aşımına uğradı.");
                }
                var result = DefenderOutput.Parse(await output, process.ExitCode);
                AppLog.Info($"Defender ikinci görüşü: {Path.GetFileName(path)} → {result.State} {string.Join(", ", result.Threats)}", nameof(SecondOpinionService));
                return result;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                AppLog.Warning("Windows Defender taraması çalıştırılamadı.", ex, nameof(SecondOpinionService));
                return DefenderScanResult.Failed($"Windows Defender çalıştırılamadı: {ex.Message}");
            }
        }

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        private static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

        [DllImport("amsi.dll")]
        private static extern void AmsiUninitialize(IntPtr amsiContext);

        [DllImport("amsi.dll")]
        private static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr session);

        [DllImport("amsi.dll")]
        private static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr session);

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        private static extern int AmsiScanString(IntPtr amsiContext, string content, string contentName, IntPtr session, out int result);

        public bool? ScanContentWithAmsi(string content, string contentName)
        {
            if (string.IsNullOrEmpty(content)) return false;
            try
            {
                if (AmsiInitialize("Bakım", out IntPtr context) != 0) return null;
                try
                {
                    if (AmsiOpenSession(context, out IntPtr session) != 0) return null;
                    try
                    {
                        if (AmsiScanString(context, content, contentName, session, out int result) != 0) return null;
                        return AmsiResult.IsMalware(result);
                    }
                    finally
                    {
                        AmsiCloseSession(context, session);
                    }
                }
                finally
                {
                    AmsiUninitialize(context);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
            {
                AppLog.Debug($"AMSI kullanılamıyor: {ex.Message}", nameof(SecondOpinionService));
                return null;
            }
        }
    }
}
