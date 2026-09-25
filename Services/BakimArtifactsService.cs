using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Bakım.Core.Uninstall;
using Bakım.Helpers;

namespace Bakım.Services
{
    public enum BakimArtifactKind { ScheduledTask, ContextMenu, FirewallRule }

    /// <summary>Bakım'ın sistemde oluşturduğu bir öğe (§5.18 yönetim listeleri).</summary>
    public sealed record BakimArtifact(BakimArtifactKind Kind, string Name, string Detail)
    {
        public string KindText => Kind switch
        {
            BakimArtifactKind.ScheduledTask => "Zamanlanmış görev",
            BakimArtifactKind.ContextMenu => "Sağ tık kaydı",
            _ => "Güvenlik duvarı kuralı"
        };
    }

    /// <summary>
    /// Bakım'ın oluşturduğu zamanlanmış görevleri, sağ tık kayıtlarını ve güvenlik duvarı kurallarını
    /// listeler ve kaldırır. Kullanıcı Bakım'ı bıraksa bile arkasında ne kaldığını görebilir.
    /// </summary>
    public static class BakimArtifactsService
    {
        private static readonly string[] TaskPrefixes = { "Bakım_", "Bakim_" };
        private const string FirewallPrefix = "Bakim_Block_";
        private const string ContextMenuKey = "BakimUninstall";
        /// <summary>ShellContextMenuService'in yazdığı yerler (HKCU\Software\Classes\{hedef}\BakimUninstall).</summary>
        private static readonly string[] ContextMenuTargets = { @"lnkfile\shell", @"exefile\shell", @"Msi.Package\shell", @"InternetShortcut\shell", @"Directory\shell" };

        public static Task<List<BakimArtifact>> ListAsync() => Task.Run(() =>
        {
            var list = new List<BakimArtifact>();

            foreach (var task in TaskSchedulerReader.ReadAll())
            {
                if (TaskPrefixes.Any(p => task.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new BakimArtifact(BakimArtifactKind.ScheduledTask, task.Path, string.Join(" | ", task.ExecActions)));
            }

            foreach (string target in ContextMenuTargets)
            {
                string key = $@"Software\Classes\{target}\{ContextMenuKey}";
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(key);
                    if (k != null) list.Add(new BakimArtifact(BakimArtifactKind.ContextMenu, $@"HKCU\{key}", k.GetValue(null) as string ?? "Bakım ile Kaldır"));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
                {
                    AppLog.Debug($"Sağ tık kaydı okunamadı: {key}", nameof(BakimArtifactsService));
                }
            }

            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var rules = root.OpenSubKey(Uninstall.FootprintCollector.FirewallRulesKey);
                foreach (string id in rules?.GetValueNames() ?? Array.Empty<string>())
                {
                    var fields = FootprintText.ParseFirewallRule(rules!.GetValue(id) as string);
                    if (fields.TryGetValue("Name", out string? name) && name.StartsWith(FirewallPrefix, StringComparison.OrdinalIgnoreCase))
                        list.Add(new BakimArtifact(BakimArtifactKind.FirewallRule, name, fields.TryGetValue("App", out string? app) ? app : string.Empty));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                AppLog.Debug("Güvenlik duvarı kuralları okunamadı.", nameof(BakimArtifactsService));
            }
            return list;
        });

        /// <summary>Öğeyi kaldırır; sonucu kullanıcıya gösterilecek metinle döndürür.</summary>
        public static async Task<(bool Success, string Message)> RemoveAsync(BakimArtifact artifact)
        {
            switch (artifact.Kind)
            {
                case BakimArtifactKind.ScheduledTask:
                {
                    var run = await ElevatedPowerShell.RunAsync(
                        $"& schtasks.exe /Delete /TN {ElevatedPowerShell.Quote(artifact.Name)} /F | Out-Null; exit $LASTEXITCODE", TimeSpan.FromSeconds(30));
                    return run.Succeeded
                        ? (true, $"{artifact.Name} görevi silindi.")
                        : (false, run.Cancelled ? "Yönetici izni verilmedi." : $"Silinemedi: {run.Message}");
                }
                case BakimArtifactKind.ContextMenu:
                {
                    string sub = artifact.Name.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase) ? artifact.Name[5..] : artifact.Name;
                    bool ok = VerifiedRegistry.DeleteKeyTree(Registry.CurrentUser, sub);
                    return ok ? (true, "Sağ tık kaydı kaldırıldı.") : (false, "Sağ tık kaydı kaldırılamadı.");
                }
                default:
                {
                    var result = await Activity.FirewallRules.RemoveAsync(artifact.Name);
                    return result.Succeeded
                        ? (true, $"{artifact.Name} kuralı kaldırıldı.")
                        : (false, result.Cancelled ? "Yönetici izni verilmedi." : $"Kaldırılamadı: {result.Message}");
                }
            }
        }
    }
}
