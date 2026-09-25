using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Bakım.Core.Safety;
using Bakım.Helpers;

namespace Bakım.Services.Safety
{
    public sealed record ProcessCandidate(int ProcessId, string Name, string ImagePath, DateTime? StartTimeUtc);

    public interface ISafeProcessService
    {
        /// <summary>
        /// Klasör altında çalışan, sonlandırılabilir süreçleri listeler. Klasör güvenli
        /// kapsamda değilse (Windows, Program Files kökü, İndirilenler …) boş liste döner.
        /// </summary>
        IReadOnlyList<ProcessCandidate> FindProcessesUnder(string? directory, out string? refusalReason);

        /// <summary>Önce pencereyi kapatmayı dener (3 sn), sonra süreç ağacını sonlandırır.</summary>
        Task<IReadOnlyList<OperationResult>> TerminateAsync(IEnumerable<ProcessCandidate> processes);

        /// <summary>Tek süreç için korumalı mı? (Optimizer, avcı modu vb. için)</summary>
        bool IsProtected(int processId, string? processName, string? imagePath);

        /// <summary>
        /// Tek bir süreci (PID) koruma kontrolüyle sonlandırır: kritik sistem süreçleri,
        /// Windows klasöründeki ikililer ve Bakım'ın kendisi reddedilir.
        /// </summary>
        Task<OperationResult> TerminateProcessAsync(int processId);
    }

    /// <summary>
    /// Süreç sonlandırmanın TEK yolu. Eski kaldırıcı InstallLocation önekiyle
    /// eşleşen her süreci soru sormadan öldürüyordu; InstallLocation "C:\Windows"
    /// çözümlendiğinde (sağ tık → explorer.exe) sistem süreçleri hedef oluyordu.
    /// </summary>
    public sealed class SafeProcessService : ISafeProcessService
    {
        private readonly PathSafetyGuard _guard;
        private readonly ILogService _log;
        private readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        public SafeProcessService(ILogService log) : this(PathSafetyGuard.Default, log) { }

        public SafeProcessService(PathSafetyGuard guard, ILogService log)
        {
            _guard = guard;
            _log = log;
        }

        public bool IsProtected(int processId, string? processName, string? imagePath) =>
            CriticalProcessPolicy.IsProtected(processId, processName, imagePath, _windowsDir, Environment.ProcessId);

        public IReadOnlyList<ProcessCandidate> FindProcessesUnder(string? directory, out string? refusalReason)
        {
            var result = new List<ProcessCandidate>();
            var scope = _guard.CheckKillScope(directory);
            if (!scope.IsAllowed)
            {
                refusalReason = scope.Reason;
                return result;
            }

            refusalReason = null;
            string root = scope.NormalizedPath;

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string? image = NativeProcess.TryGetImagePath(p.Id);
                    if (image == null) continue;

                    string? normalized = WindowsPath.Normalize(image);
                    if (normalized == null || !WindowsPath.IsStrictlyUnder(normalized, root)) continue;
                    if (IsProtected(p.Id, p.ProcessName, image)) continue;

                    result.Add(new ProcessCandidate(p.Id, p.ProcessName, image, NativeProcess.TryGetStartTimeUtc(p.Id)));
                }
                catch (InvalidOperationException)
                {
                    // Süreç listeleme sırasında çıktı.
                }
                finally
                {
                    p.Dispose();
                }
            }

            return result;
        }

        public async Task<OperationResult> TerminateProcessAsync(int processId)
        {
            ProcessCandidate candidate;
            try
            {
                using var p = Process.GetProcessById(processId);
                candidate = new ProcessCandidate(processId, p.ProcessName,
                    NativeProcess.TryGetImagePath(processId) ?? string.Empty,
                    NativeProcess.TryGetStartTimeUtc(processId));
            }
            catch (ArgumentException)
            {
                return new OperationResult($"PID {processId}", DeleteOutcome.NotFound, "Süreç zaten kapanmış.", 0);
            }

            var results = await TerminateAsync(new[] { candidate });
            return results[0];
        }

        public async Task<IReadOnlyList<OperationResult>> TerminateAsync(IEnumerable<ProcessCandidate> processes)
        {
            var results = new List<OperationResult>();

            foreach (var candidate in processes)
            {
                string label = $"{candidate.Name} (PID {candidate.ProcessId})";

                if (IsProtected(candidate.ProcessId, candidate.Name, candidate.ImagePath))
                {
                    results.Add(new OperationResult(label, DeleteOutcome.Blocked, "Korumalı sistem süreci.", 0));
                    continue;
                }

                Process? p = null;
                try
                {
                    p = Process.GetProcessById(candidate.ProcessId);

                    // PID yeniden kullanıldıysa (farklı süreç) dokunma.
                    var start = NativeProcess.TryGetStartTimeUtc(candidate.ProcessId);
                    if (candidate.StartTimeUtc.HasValue && start.HasValue && start.Value != candidate.StartTimeUtc.Value)
                    {
                        results.Add(new OperationResult(label, DeleteOutcome.NotFound, "Süreç zaten kapanmış.", 0));
                        continue;
                    }

                    if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow())
                    {
                        var closed = p.WaitForExitAsync();
                        if (await Task.WhenAny(closed, Task.Delay(3000)) == closed)
                        {
                            results.Add(new OperationResult(label, DeleteOutcome.Deleted, "Kapatıldı.", 0));
                            continue;
                        }
                    }

                    p.Kill(entireProcessTree: true);
                    var killed = p.WaitForExitAsync();
                    bool exited = await Task.WhenAny(killed, Task.Delay(5000)) == killed;
                    results.Add(new OperationResult(label, exited ? DeleteOutcome.Deleted : DeleteOutcome.Failed,
                        exited ? "Sonlandırıldı." : "Sonlandırma zaman aşımına uğradı.", 0));
                }
                catch (ArgumentException)
                {
                    results.Add(new OperationResult(label, DeleteOutcome.NotFound, "Süreç zaten kapanmış.", 0));
                }
                catch (Win32Exception ex)
                {
                    _log.Warning($"Süreç sonlandırılamadı: {label}", ex, nameof(SafeProcessService));
                    results.Add(new OperationResult(label, DeleteOutcome.AccessDenied, "Erişim reddedildi (yönetici gerekebilir).", 0));
                }
                catch (InvalidOperationException)
                {
                    results.Add(new OperationResult(label, DeleteOutcome.NotFound, "Süreç zaten kapanmış.", 0));
                }
                finally
                {
                    p?.Dispose();
                }
            }

            return results;
        }
    }
}
