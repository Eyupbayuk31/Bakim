using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Bakım.Helpers
{
    /// <summary>Bir yardımcı komutun (bcdedit, sc, net, powershell…) sonucu.</summary>
    public sealed record ProcessRunResult(bool Started, int ExitCode, bool TimedOut, string StdOut, string StdErr, string? StartError = null)
    {
        public bool Succeeded => Started && !TimedOut && ExitCode == 0;

        /// <summary>Kısa açıklama: "çıkış kodu 5: Erişim engellendi." gibi.</summary>
        public string Describe()
        {
            if (!Started) return "başlatılamadı" + (string.IsNullOrWhiteSpace(StartError) ? "" : $": {StartError}");
            if (TimedOut) return "zaman aşımına uğradı";
            if (ExitCode == 0) return "başarılı";
            string detail = FirstLine(StdErr) ?? FirstLine(StdOut) ?? string.Empty;
            return string.IsNullOrEmpty(detail) ? $"çıkış kodu {ExitCode}" : $"çıkış kodu {ExitCode}: {detail}";
        }

        private static string? FirstLine(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed.Length > 160 ? trimmed[..160] + "…" : trimmed;
            }
            return null;
        }
    }

    /// <summary>
    /// Yardımcı komutları gizli pencerede çalıştırır. Çıktıyı asenkron okur (tampon
    /// dolup kilitlenme olmaz), zaman aşımında süreci sonlandırır ve çıkış kodunu döndürür.
    ///
    /// Eski kod çoğu yerde <c>Process.Start(...)?.WaitForExit(3000)</c> ile yetinip çıkış
    /// kodunu hiç okumuyordu; komut başarısız olsa da işlem "başarılı" sayılıyordu (D-5).
    /// Argümanlar <see cref="ProcessStartInfo.ArgumentList"/> ile verilir; tırnaklama hatası
    /// ve komut enjeksiyonu riski yoktur.
    /// </summary>
    public static class ProcessRunner
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public static ProcessRunResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null) =>
            RunAsync(fileName, arguments, timeout, CancellationToken.None).GetAwaiter().GetResult();

        public static async Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
                // Kodlama bilerek verilmiyor: konsol araçları OEM kod sayfasıyla yazar,
                // .NET de varsayılan olarak konsol kod sayfasını kullanır.
            };
            foreach (var arg in arguments) psi.ArgumentList.Add(arg);

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                return new ProcessRunResult(false, -1, false, string.Empty, string.Empty, ex.Message);
            }
            if (process == null) return new ProcessRunResult(false, -1, false, string.Empty, string.Empty);

            using (process)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout ?? DefaultTimeout);
                bool timedOut = false;
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                string outText = await SafeRead(stdout).ConfigureAwait(false);
                string errText = await SafeRead(stderr).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                int exitCode = timedOut ? -1 : process.ExitCode;
                return new ProcessRunResult(true, exitCode, timedOut, outText, errText);
            }
        }

        /// <summary>
        /// Komutu çalıştırır; başarısızsa etkin <see cref="WriteScope"/>'a "<paramref name="label"/>: neden"
        /// olarak bildirir.
        /// </summary>
        public static bool RunReported(string label, string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
        {
            var result = Run(fileName, arguments, timeout);
            if (!result.Succeeded) WriteScope.Report($"{label}: {result.Describe()}");
            return result.Succeeded;
        }

        private static async Task<string> SafeRead(Task<string> task)
        {
            try
            {
                var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                return done == task ? await task.ConfigureAwait(false) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}

namespace Bakım.Helpers
{
    /// <summary>
    /// Yönetici gerektiren bir PowerShell komutunu çalıştırır: uygulama zaten yönetici ise
    /// doğrudan (çıktı ve çıkış koduyla), değilse tek bir UAC onayıyla. UAC reddi ayrı bildirilir.
    /// </summary>
    public static class ElevatedPowerShell
    {
        public sealed record Result(bool Succeeded, bool Cancelled, string Message, int ExitCode = 0);

        public static async Task<Result> RunAsync(string script, TimeSpan timeout, CancellationToken ct = default)
        {
            string fullScript = "$ErrorActionPreference = 'Stop'; " + script;

            if (UacHelper.IsAdministrator())
            {
                var run = await ProcessRunner.RunAsync("powershell.exe",
                    new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", fullScript },
                    timeout, ct).ConfigureAwait(false);
                return new Result(run.Succeeded, false, run.Describe(), run.ExitCode);
            }

            // -EncodedCommand: tırnak/kaçış sorunları olmadan ShellExecute argümanı.
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(fullScript));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new Result(false, true, "Yönetici izni verilmedi.");
            }
            catch (Exception ex)
            {
                return new Result(false, false, ex.Message);
            }

            if (process == null) return new Result(false, false, "başlatılamadı");
            using (process)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    ct.ThrowIfCancellationRequested();
                    return new Result(false, false, "zaman aşımına uğradı");
                }
                return process.ExitCode == 0
                    ? new Result(true, false, "başarılı")
                    : new Result(false, false, $"çıkış kodu {process.ExitCode}", process.ExitCode);
            }
        }

        /// <summary>PowerShell tek tırnaklı dize değişmezi: ' → ''.</summary>
        public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    }
}
