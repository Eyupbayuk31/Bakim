using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Sentinel;

namespace Bakım.Services.Sentinel
{
    public sealed record SandboxLaunchResult(bool Succeeded, string Message, string? WsbFilePath = null);

    public interface IWindowsSandboxService
    {
        bool IsSandboxSupported { get; }
        bool IsSandboxInstalled { get; }
        Task<SandboxLaunchResult> LaunchInstallerPreviewAsync(string installerPath, bool enableNetworking = true, CancellationToken ct = default);
    }

    public sealed class WindowsSandboxService : IWindowsSandboxService
    {
        private readonly ILogService _log;

        public WindowsSandboxService(ILogService log)
        {
            _log = log;
        }

        public bool IsSandboxSupported
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
                // Windows 10 build 18362 (1903) ve üzeri 64-bit sürümler gereklidir
                return Environment.OSVersion.Version.Major >= 10 && Environment.Is64BitOperatingSystem;
            }
        }

        public bool IsSandboxInstalled
        {
            get
            {
                if (!IsSandboxSupported) return false;
                string sandboxExe = Path.Combine(Environment.SystemDirectory, "WindowsSandbox.exe");
                return File.Exists(sandboxExe);
            }
        }

        public async Task<SandboxLaunchResult> LaunchInstallerPreviewAsync(
            string installerPath,
            bool enableNetworking = true,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                if (!IsSandboxSupported)
                {
                    return new SandboxLaunchResult(false, "Windows Sandbox bu işletim sistemi sürümünde desteklenmiyor (Windows 10/11 Pro/Enterprise 64-bit gereklidir).");
                }

                if (!IsSandboxInstalled)
                {
                    return new SandboxLaunchResult(false, "Windows Sandbox özelliği sisteminizde yüklü değil. Windows Özellikleri'nden 'Windows Sandbox' bileşenini etkinleştirebilirsiniz.");
                }

                if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                {
                    return new SandboxLaunchResult(false, "Önizlenecek kurulum dosyası bulunamadı.");
                }

                try
                {
                    string hostFolder = Path.GetDirectoryName(Path.GetFullPath(installerPath))!;
                    string fileName = Path.GetFileName(installerPath);
                    string logonCmd = WindowsSandboxConfiguration.GenerateInstallerLogonCommand(fileName);

                    string wsbXml = WindowsSandboxConfiguration.BuildWsbXml(
                        hostFolder: hostFolder,
                        commandToRun: logonCmd,
                        readOnly: true,
                        enableNetworking: enableNetworking,
                        enableVGpu: true);

                    string sandboxDir = Path.Combine(Path.GetTempPath(), "Bakim", "Sandbox");
                    Directory.CreateDirectory(sandboxDir);

                    string wsbPath = Path.Combine(sandboxDir, $"SetupPreview_{Guid.NewGuid():N}.wsb");
                    File.WriteAllText(wsbPath, wsbXml);

                    string sandboxExe = Path.Combine(Environment.SystemDirectory, "WindowsSandbox.exe");
                    var psi = new ProcessStartInfo
                    {
                        FileName = sandboxExe,
                        Arguments = $"\"{wsbPath}\"",
                        UseShellExecute = true
                    };

                    _log.Info($"Windows Sandbox kurulum önizlemesi başlatılıyor: {installerPath} -> {wsbPath}", nameof(WindowsSandboxService));
                    Process.Start(psi);

                    return new SandboxLaunchResult(true, "Windows Sandbox yalıtılmış ortamında kurulum başlatıldı.", wsbPath);
                }
                catch (Exception ex)
                {
                    _log.Error($"Windows Sandbox başlatılamadı: {installerPath}", ex, nameof(WindowsSandboxService));
                    return new SandboxLaunchResult(false, $"Windows Sandbox başlatılırken hata oluştu: {ex.Message}");
                }
            }, ct);
        }
    }
}
