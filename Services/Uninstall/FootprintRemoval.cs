using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services.Activity;
using Bakım.Services.Safety;

namespace Bakım.Services.Uninstall
{
    /// <summary>
    /// Hizmet, zamanlanmış görev ve güvenlik duvarı kuralı kalıntılarını yedekleyerek kaldırır.
    ///
    /// Her öğe silinmeden önce geri alma günlüğüne yazılır (hizmet → .reg, görev → XML,
    /// kural → ham kural verisi); yedeği alınamayan öğe silinmez. Silme tek UAC onayıyla yapılır
    /// ve sonuç komut çıktısına değil gerçek duruma bakılarak doğrulanır.
    /// </summary>
    public static class FootprintRemoval
    {
        public static bool Handles(LeftoverType type) =>
            type is LeftoverType.Service or LeftoverType.ScheduledTask or LeftoverType.FirewallRule;

        public static async Task<List<OperationResult>> RemoveAsync(IReadOnlyList<LeftoverItem> items, string journalId,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            var results = new List<OperationResult>();
            if (items.Count == 0) return results;

            string journalDir = UndoJournal.GetDirectory(journalId);
            var backup = ActivityPayload.Read<FootprintBackup>(journalDir, ActivityPayload.FootprintFile) ?? new FootprintBackup();
            var script = new StringBuilder("$ErrorActionPreference = 'Continue'; ");
            var pending = new List<(LeftoverItem Item, Func<OperationResult> Verify)>();

            object? taskService = items.Any(i => i.ItemType == LeftoverType.ScheduledTask) ? FootprintCollector.CreateTaskService() : null;
            try
            {
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"Yedekleniyor: {item.Description}");
                    switch (item.ItemType)
                    {
                        case LeftoverType.Service:
                        {
                            string name = item.Path;
                            if (!FootprintCollector.IsPlainName(name))
                            {
                                results.Add(Fail(item, "Geçersiz hizmet adı."));
                                break;
                            }
                            string reg = UndoJournal.NextBackupPath(journalId, ".reg");
                            bool exported = await UndoJournal.RunRegAsync("export",
                                $@"HKEY_LOCAL_MACHINE\{FootprintCollector.ServicesKey}\{name}", reg, "/y", "/reg:64").ConfigureAwait(false);
                            if (!exported)
                            {
                                results.Add(Fail(item, "Hizmet kaydı yedeklenemedi; güvenlik için silinmedi."));
                                break;
                            }
                            backup.Services.Add(name);
                            string q = ElevatedPowerShell.Quote(name);
                            script.Append($"Stop-Service -Name {q} -Force -ErrorAction SilentlyContinue; & sc.exe delete {q} | Out-Null; ");
                            pending.Add((item, () => VerifyService(item, name)));
                            break;
                        }

                        case LeftoverType.ScheduledTask:
                        {
                            string? xml = taskService == null ? null : FootprintCollector.GetTaskXml(taskService, item.Path);
                            if (xml == null)
                            {
                                results.Add(taskService == null
                                    ? Fail(item, "Görev Zamanlayıcı'ya bağlanılamadı.")
                                    : new OperationResult(item.Path, DeleteOutcome.NotFound, "Görev zaten yok.", 0));
                                break;
                            }
                            string tasksDir = Path.Combine(journalDir, "tasks");
                            Directory.CreateDirectory(tasksDir);
                            string file = $"{backup.Tasks.Count + 1:D4}.xml";
                            // Görev XML'i "UTF-16" bildirir; schtasks /XML aynı kodlamayı bekler.
                            await File.WriteAllTextAsync(Path.Combine(tasksDir, file), xml, Encoding.Unicode, ct).ConfigureAwait(false);
                            backup.Tasks.Add(new TaskBackup(item.Path, file));
                            script.Append($"& schtasks.exe /Delete /TN {ElevatedPowerShell.Quote(item.Path)} /F 2>$null | Out-Null; ");
                            pending.Add((item, () => VerifyTask(item)));
                            break;
                        }

                        case LeftoverType.FirewallRule:
                        {
                            string? data = FootprintCollector.ReadFirewallRule(item.Path);
                            if (data == null)
                            {
                                results.Add(new OperationResult(item.Path, DeleteOutcome.NotFound, "Kural zaten yok.", 0));
                                break;
                            }
                            backup.FirewallRules.Add(new FirewallRuleBackup(item.Path, data));
                            // -Name joker karakter kabul eder; kural kimliği birebir eşleşsin diye kaçışlanır.
                            script.Append($"Remove-NetFirewallRule -Name ([WildcardPattern]::Escape({ElevatedPowerShell.Quote(item.Path)})) -ErrorAction SilentlyContinue; ");
                            pending.Add((item, () => FootprintCollector.ReadFirewallRule(item.Path) == null
                                ? new OperationResult(item.Path, DeleteOutcome.Deleted, "Kural kaldırıldı.", 0)
                                : Fail(item, "Kural kaldırılamadı.")));
                            break;
                        }

                        default:
                            results.Add(Fail(item, "Desteklenmeyen öğe türü."));
                            break;
                    }
                }
            }
            finally
            {
                if (taskService != null) Marshal.ReleaseComObject(taskService);
            }

            if (pending.Count == 0) return results;

            // Yedek, silmeden ÖNCE diske yazılır: silme yarıda kalsa bile geri alma verisi vardır.
            ActivityPayload.Write(journalDir, ActivityPayload.FootprintFile, backup);

            progress?.Report($"{pending.Count} sistem öğesi kaldırılıyor (yönetici onayı gerekebilir)...");
            var run = await ElevatedPowerShell.RunAsync(script.Append("exit 0").ToString(), TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);

            foreach (var (item, verify) in pending)
            {
                var result = verify();
                if (!result.Succeeded && run.Cancelled)
                    result = result with { Outcome = DeleteOutcome.AccessDenied, Message = "Yönetici izni verilmedi." };
                results.Add(result);
            }
            return results;
        }

        private static OperationResult VerifyService(LeftoverItem item, string name)
        {
            if (FootprintCollector.ServicePendingDelete(name))
                return new OperationResult(item.Path, DeleteOutcome.ScheduledForReboot, "Hizmet silinmek üzere işaretlendi; yeniden başlatınca tamamen kalkar.", 0);
            return FootprintCollector.ServiceIsRegistered(name)
                ? Fail(item, "Hizmet silinemedi.")
                : new OperationResult(item.Path, DeleteOutcome.Deleted, "Hizmet silindi.", 0);
        }

        private static OperationResult VerifyTask(LeftoverItem item)
        {
            object? service = FootprintCollector.CreateTaskService();
            if (service == null) return Fail(item, "Görevin silindiği doğrulanamadı.");
            try
            {
                return FootprintCollector.TaskExists(service, item.Path)
                    ? Fail(item, "Görev silinemedi.")
                    : new OperationResult(item.Path, DeleteOutcome.Deleted, "Görev silindi.", 0);
            }
            finally
            {
                Marshal.ReleaseComObject(service);
            }
        }

        private static OperationResult Fail(LeftoverItem item, string message) =>
            new(item.Path, DeleteOutcome.Failed, message, 0);
    }
}
