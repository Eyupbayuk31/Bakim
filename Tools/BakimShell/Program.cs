using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BakimShell
{
    public static class Program
    {
        private const string MutexName = @"Local\Bakim.SingleInstance.v1";

        public static int Main(string[] args)
        {
            try
            {
                var (verb, target) = Bakım.Core.Shell.ShellTargetParser.ParseRequest(args);
                return RouteRequest(verb, target);
            }
            catch
            {
                return 1;
            }
        }

        private static int RouteRequest(Bakım.Core.Shell.ShellVerb verb, string? target)
        {
            string baseDir = AppContext.BaseDirectory;
            string bakimExe = Path.Combine(baseDir, "Bakim.exe");

            if (IsPrimaryRunning())
            {
                if (SendIpcRequest(verb, target))
                {
                    return 0;
                }
            }

            // Bakım çalışmıyorsa veya IPC teslim edilemediyse Bakim.exe'yi başlat
            if (File.Exists(bakimExe))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = bakimExe,
                    Arguments = string.IsNullOrWhiteSpace(target) ? string.Empty
                        : verb == Bakım.Core.Shell.ShellVerb.Analyze ? $"--analyze-target \"{target}\"" : $"--uninstall-target \"{target}\"",
                    UseShellExecute = true,
                    WorkingDirectory = baseDir
                };
                Process.Start(psi);
            }

            return 0;
        }

        private static bool IsPrimaryRunning()
        {
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var existing))
                {
                    existing.Dispose();
                    return true;
                }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Yönetici olarak çalışan örneğin mutex'ine erişim reddedildi ama çalışıyor
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SendIpcRequest(Bakım.Core.Shell.ShellVerb verb, string? target)
        {
            try
            {
                string inbox = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bakim", "ipc");
                Directory.CreateDirectory(inbox);

                // IpcRequestKind ile aynı sayılar: 0 Activate, 1 UninstallTarget, 2 AnalyzeTarget.
                int kind = string.IsNullOrWhiteSpace(target) ? 0 : verb == Bakım.Core.Shell.ShellVerb.Analyze ? 2 : 1;
                var payload = new
                {
                    Kind = kind,
                    Target = target,
                    CreatedUtc = DateTime.UtcNow
                };

                string file = Path.Combine(inbox, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(payload));
                File.Move(tmp, file);

                // Ana örneğin dosyayı okuyup silmesini bekle (maks 3 sn)
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 3000)
                {
                    if (!File.Exists(file)) return true;
                    Thread.Sleep(50);
                }

                // Zaman aşımı durumunda dosyayı temizle
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
