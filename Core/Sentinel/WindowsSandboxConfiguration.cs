using System;
using System.IO;
using System.Security;
using System.Text;

namespace Bakım.Core.Sentinel
{
    /// <summary>
    /// Windows Sandbox (.wsb) XML yapılandırma oluşturucu (NÖB Faz 10 / Sandbox Önizleme).
    /// Şüpheli kurulum paketlerini ana sisteme dokunmadan izole bir ortamda güvenle çalıştırmayı sağlar.
    /// </summary>
    public static class WindowsSandboxConfiguration
    {
        public const string SandboxSharedFolder = @"C:\SandboxShared";

        public static string BuildWsbXml(
            string hostFolder,
            string? commandToRun = null,
            bool readOnly = true,
            bool enableNetworking = true,
            bool enableVGpu = true)
        {
            if (string.IsNullOrWhiteSpace(hostFolder))
                throw new ArgumentException("Ana makine klasörü belirtilmelidir.", nameof(hostFolder));

            string escapedHost = SecurityElement.Escape(Path.GetFullPath(hostFolder)) ?? hostFolder;
            string vGpuValue = enableVGpu ? "Default" : "Disable";
            string netValue = enableNetworking ? "Default" : "Disable";

            var sb = new StringBuilder();
            sb.AppendLine("<Configuration>");
            sb.AppendLine($"  <VGpu>{vGpuValue}</VGpu>");
            sb.AppendLine($"  <Networking>{netValue}</Networking>");
            sb.AppendLine("  <MappedFolders>");
            sb.AppendLine("    <MappedFolder>");
            sb.AppendLine($"      <HostFolder>{escapedHost}</HostFolder>");
            sb.AppendLine($"      <SandboxFolder>{SandboxSharedFolder}</SandboxFolder>");
            sb.AppendLine($"      <ReadOnly>{(readOnly ? "true" : "false")}</ReadOnly>");
            sb.AppendLine("    </MappedFolder>");
            sb.AppendLine("  </MappedFolders>");

            if (!string.IsNullOrWhiteSpace(commandToRun))
            {
                string escapedCmd = SecurityElement.Escape(commandToRun) ?? commandToRun;
                sb.AppendLine("  <LogonCommand>");
                sb.AppendLine($"    <Command>{escapedCmd}</Command>");
                sb.AppendLine("  </LogonCommand>");
            }

            sb.AppendLine("</Configuration>");
            return sb.ToString();
        }

        public static string GenerateInstallerLogonCommand(string installerFileName)
        {
            if (string.IsNullOrWhiteSpace(installerFileName)) return string.Empty;
            int lastBackslash = installerFileName.LastIndexOf('\\');
            int lastSlash = installerFileName.LastIndexOf('/');
            int lastSep = Math.Max(lastBackslash, lastSlash);
            string cleanName = lastSep >= 0 ? installerFileName[(lastSep + 1)..] : installerFileName;
            return $@"cmd.exe /c start """" ""{SandboxSharedFolder}\{cleanName}""";
        }
    }
}
