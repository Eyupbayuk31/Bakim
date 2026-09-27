using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Bakım.Core.GameMode;

namespace Bakım.Services.GameMode
{
    /// <summary>Oyun Modu oturumunun diske yazılan durumu: Bakım kapanır ya da çökerse açılışta geri yüklenir.</summary>
    /// <param name="PreviousWindowsGameMode">Oturum Windows Oyun Modu'nu açtıysa önceki değer (-1 = değer yoktu); açmadıysa null.</param>
    public sealed record GameModeState(string? PreviousScheme, DateTime ActivatedAtUtc, int? PreviousWindowsGameMode = null);

    /// <summary>
    /// Oyun Modu'nun sisteme dokunan her işlemi. Tek arayüzde toplanır ki testler sahte uygulamayla çalışsın:
    /// eskiden birim testleri gerçek powercfg'ı çağırıyor, geliştirme makinesinin güç planını değiştirip
    /// gerçek durum dosyasını siliyordu.
    /// </summary>
    public interface IGameModeSystem
    {
        string? GetActivePowerScheme();
        bool SetPowerScheme(string schemeGuid);

        /// <summary>HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled; değer yoksa -1.</summary>
        int GetWindowsGameMode();
        bool SetWindowsGameMode(int value);

        GameModeState? LoadState();
        void SaveState(GameModeState state);
        void DeleteState();

        /// <summary>Tüm mantıksal çekirdeklerin toplam meşgul süresi (çekirdek + kullanıcı − boşta).</summary>
        TimeSpan GetSystemBusyTime();
        int LogicalCores { get; }
        TimeSpan? GetProcessCpuTime(int pid);

        /// <summary>Fiziksel bellek yükü (0–100) ve kullanılabilir bayt.</summary>
        (uint Load, ulong AvailableBytes) GetMemoryStatus();

        IReadOnlyList<RunningProcess> GetRunningProcesses();
        int GetForegroundProcessId();
        int CurrentProcessId { get; }

        /// <summary>Önceliği okur; erişilemezse null.</summary>
        ProcessPriorityClass? GetPriority(int pid);
        bool SetPriority(int pid, ProcessPriorityClass priority);
        /// <summary>Sürecin başlama zamanı: öncelik geri yüklerken PID'in başka bir sürece geçmediğini doğrulamak için.</summary>
        DateTime? GetStartTime(int pid);
        bool TrimWorkingSet(int pid);
        bool IsProtected(int pid, string name);
    }

    public sealed class WindowsGameModeSystem : IGameModeSystem
    {
        private const string GameBarKey = @"Software\Microsoft\GameBar";
        private const string AutoGameModeValue = "AutoGameModeEnabled";

        private static readonly string StateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "gamemode-state.json");

        private readonly ILogService _log;

        public WindowsGameModeSystem(ILogService log) => _log = log;

        public string? GetActivePowerScheme()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                var m = System.Text.RegularExpressions.Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                return m.Success ? m.Value.ToLowerInvariant() : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Debug($"Etkin güç planı okunamadı: {ex.Message}", nameof(WindowsGameModeSystem));
                return null;
            }
        }

        public bool SetPowerScheme(string schemeGuid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"/setactive {schemeGuid}") { CreateNoWindow = true, UseShellExecute = false };
                using var p = Process.Start(psi);
                if (p == null || !p.WaitForExit(5000)) return false;
                return p.ExitCode == 0;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                _log.Debug($"Güç planı değiştirilemedi: {ex.Message}", nameof(WindowsGameModeSystem));
                return false;
            }
        }

        public int GetWindowsGameMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(GameBarKey);
                return key?.GetValue(AutoGameModeValue) is int v ? v : -1;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Debug($"Windows Oyun Modu ayarı okunamadı: {ex.Message}", nameof(WindowsGameModeSystem));
                return -1;
            }
        }

        public bool SetWindowsGameMode(int value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(GameBarKey);
                if (value < 0) key.DeleteValue(AutoGameModeValue, throwOnMissingValue: false);
                else key.SetValue(AutoGameModeValue, value, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.Warning("Windows Oyun Modu ayarı değiştirilemedi.", ex, nameof(WindowsGameModeSystem));
                return false;
            }
        }

        public GameModeState? LoadState()
        {
            try
            {
                return File.Exists(StateFile) ? JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(StateFile)) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _log.Warning("Oyun Modu durumu okunamadı.", ex, nameof(WindowsGameModeSystem));
                return null;
            }
        }

        public void SaveState(GameModeState state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning("Oyun Modu durumu kaydedilemedi.", ex, nameof(WindowsGameModeSystem));
            }
        }

        public void DeleteState()
        {
            try
            {
                if (File.Exists(StateFile)) File.Delete(StateFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Debug($"Oyun Modu durum dosyası silinemedi: {ex.Message}", nameof(WindowsGameModeSystem));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low; public uint High; public long Value => ((long)High << 32) | Low; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        public TimeSpan GetSystemBusyTime()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return TimeSpan.Zero;
            // Çekirdek süresi boşta süreyi içerir.
            return TimeSpan.FromTicks(Math.Max(0, kernel.Value + user.Value - idle.Value));
        }

        public int LogicalCores => Environment.ProcessorCount;

        public TimeSpan? GetProcessCpuTime(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.TotalProcessorTime;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        public (uint Load, ulong AvailableBytes) GetMemoryStatus()
        {
            var m = new MEMORYSTATUSEX();
            return GlobalMemoryStatusEx(m) ? (m.dwMemoryLoad, m.ullAvailPhys) : (0u, 0ul);
        }

        public IReadOnlyList<RunningProcess> GetRunningProcesses()
        {
            var list = new List<RunningProcess>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    list.Add(new RunningProcess(p.Id, p.ProcessName));
                }
                finally
                {
                    p.Dispose();
                }
            }
            return list;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public int GetForegroundProcessId()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }

        public int CurrentProcessId => Environment.ProcessId;

        public ProcessPriorityClass? GetPriority(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.PriorityClass;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        public bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = priority;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Debug($"Öncelik değiştirilemedi (PID {pid}): {ex.Message}", nameof(WindowsGameModeSystem));
                return false;
            }
        }

        public DateTime? GetStartTime(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.StartTime;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public bool TrimWorkingSet(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return EmptyWorkingSet(p.Handle);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _log.Debug($"Çalışma kümesi kırpılamadı (PID {pid}): {ex.Message}", nameof(WindowsGameModeSystem));
                return false;
            }
        }

        public bool IsProtected(int pid, string name)
        {
            string? image = null;
            try
            {
                using var p = Process.GetProcessById(pid);
                image = p.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Yol okunamadıysa ad kuralı yine uygulanır.
                _log.Debug($"Süreç yolu okunamadı (PID {pid}): {ex.Message}", nameof(WindowsGameModeSystem));
            }
            return Core.Safety.CriticalProcessPolicy.IsProtected(pid, name, image,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.ProcessId);
        }
    }
}
