using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.Activity;
using Bakım.Helpers;
using Microsoft.Win32;

namespace Bakım.Services.Activity
{
    /// <summary>
    /// Modüllerin Etkinlik Merkezi'ne kayıt yazması için kısayollar. Hiçbiri istisna fırlatmaz:
    /// kayıt yazılamaması asıl işlemi (temizlik, kaldırma …) asla bozmaz.
    /// </summary>
    public static class ActivityRecording
    {
        /// <summary>Geri alma verisi olmayan kayıt (temizlik, kurulum oturumu, analiz …).</summary>
        public static ActivityEntry? RecordSimple(this IActivityService activity, ActivityKind kind, string module, string title,
            string summary, ActivityOutcome outcome, IEnumerable<ActivityItem>? items = null, string? deepLink = null)
        {
            return Safe(() => activity.Record(new ActivityEntry
            {
                Kind = kind,
                Module = module,
                Title = title,
                Summary = summary,
                Outcome = outcome,
                Undo = UndoState.NotUndoable,
                DeepLink = deepLink,
                Items = items?.ToList() ?? new List<ActivityItem>()
            }));
        }

        /// <summary>
        /// Değer düzeyinde kayıt defteri değişikliği (ince ayar, başlangıç girdisi). Özgün değer
        /// yakalanmışsa kayıt tek tıkla geri alınabilir.
        /// </summary>
        public static ActivityEntry? RecordRegistryChange(this IActivityService activity, ActivityKind kind, string module, string title,
            string summary, ActivityOutcome outcome, IReadOnlyList<RegistryValueSnapshot> originals,
            string? deepLink = null, string handler = UndoHandlers.RegistryValues)
        {
            return Safe(() =>
            {
                var entry = new ActivityEntry
                {
                    Kind = kind,
                    Module = module,
                    Title = title,
                    Summary = summary,
                    Outcome = outcome,
                    DeepLink = deepLink,
                    Items = originals.Select(o => new ActivityItem(
                        $"{o.Root}\\{o.SubKey}" + (o.ValueName == null ? "" : $" → {(o.ValueName.Length == 0 ? "(Varsayılan)" : o.ValueName)}"),
                        o.ValueName == null ? "Anahtar" : "Değer",
                        o.Existed ? "Önceki değer yedeklendi" : "Önceden yoktu")).ToList()
                };

                // Hiç yazılmamış (başarısız) bir işlemin geri alınacak bir şeyi yoktur.
                if (originals.Count > 0 && outcome != ActivityOutcome.Failed)
                {
                    string dir = activity.CreateJournal(entry.Id);
                    ActivityPayload.Write(dir, ActivityPayload.RegistryValuesFile, originals.ToList());
                    entry = entry with { Undo = UndoState.Undoable, UndoHandler = handler, PayloadPath = dir };
                }
                return activity.Record(entry);
            });
        }

        /// <summary>Silinmeden önce .reg yedeği alınmış değişiklik (kaldırma kalıntıları, girdi silme).</summary>
        public static ActivityEntry? RecordWithRegBackup(this IActivityService activity, ActivityKind kind, string module, string title,
            string summary, ActivityOutcome outcome, string? backupDirectory, IEnumerable<ActivityItem>? items = null, string? deepLink = null)
        {
            return Safe(() =>
            {
                bool hasBackup = !string.IsNullOrEmpty(backupDirectory) && System.IO.Directory.Exists(backupDirectory) &&
                                 System.IO.Directory.EnumerateFiles(backupDirectory, "*.reg").Any();
                return activity.Record(new ActivityEntry
                {
                    Kind = kind,
                    Module = module,
                    Title = title,
                    Summary = summary,
                    Outcome = outcome,
                    Undo = hasBackup ? UndoState.Undoable : UndoState.NotUndoable,
                    UndoHandler = hasBackup ? UndoHandlers.RegistryRegImport : null,
                    PayloadPath = hasBackup ? backupDirectory : null,
                    DeepLink = deepLink,
                    Items = items?.ToList() ?? new List<ActivityItem>()
                });
            });
        }

        /// <summary>Geri Dönüşüm Kutusu'na gönderilen dosyalar: geri alma kutuyu açar.</summary>
        public static ActivityEntry? RecordRecycled(this IActivityService activity, ActivityKind kind, string module, string title,
            string summary, ActivityOutcome outcome, IEnumerable<ActivityItem> items, string? deepLink = null)
        {
            return Safe(() => activity.Record(new ActivityEntry
            {
                Kind = kind,
                Module = module,
                Title = title,
                Summary = summary,
                Outcome = outcome,
                Undo = outcome == ActivityOutcome.Failed ? UndoState.NotUndoable : UndoState.Undoable,
                UndoHandler = outcome == ActivityOutcome.Failed ? null : UndoHandlers.RecycleBin,
                DeepLink = deepLink,
                Items = items.ToList()
            }));
        }

        public static ActivityEntry? RecordServiceChange(this IActivityService activity, string title, string summary,
            ActivityOutcome outcome, ServiceUndoPayload? original)
        {
            return Safe(() =>
            {
                var entry = new ActivityEntry
                {
                    Kind = ActivityKind.ServiceChange,
                    Module = "Hizmetler",
                    Title = title,
                    Summary = summary,
                    Outcome = outcome,
                    DeepLink = "ServiceManager",
                    Items = original == null ? new List<ActivityItem>() : new List<ActivityItem> { new(original.ServiceName, "Hizmet", summary) }
                };
                if (original != null && outcome != ActivityOutcome.Failed)
                {
                    string dir = activity.CreateJournal(entry.Id);
                    ActivityPayload.Write(dir, ActivityPayload.ServiceFile, original);
                    entry = entry with { Undo = UndoState.Undoable, UndoHandler = UndoHandlers.ServiceConfig, PayloadPath = dir };
                }
                return activity.Record(entry);
            });
        }

        public static ActivityEntry? RecordFirewallChange(this IActivityService activity, string title, string summary,
            ActivityOutcome outcome, FirewallUndoPayload payload)
        {
            return Safe(() =>
            {
                var entry = new ActivityEntry
                {
                    Kind = ActivityKind.FirewallRule,
                    Module = "Ağ İzleyici",
                    Title = title,
                    Summary = summary,
                    Outcome = outcome,
                    DeepLink = "Network",
                    Items = new List<ActivityItem> { new(payload.ProgramPath, payload.Added ? "Kural eklendi" : "Kural kaldırıldı", payload.RuleName) }
                };
                if (outcome != ActivityOutcome.Failed)
                {
                    string dir = activity.CreateJournal(entry.Id);
                    ActivityPayload.Write(dir, ActivityPayload.FirewallFile, payload);
                    entry = entry with { Undo = UndoState.Undoable, UndoHandler = UndoHandlers.FirewallRule, PayloadPath = dir };
                }
                return activity.Record(entry);
            });
        }

        /// <summary>
        /// Hizmetin şu anki başlangıç türü (Services\{ad}\Start ve DelayedAutostart). Yönetici izni
        /// gerekmez. Okunamazsa null: kayıt yine yazılır ama geri alınamaz.
        /// </summary>
        public static ServiceUndoPayload? ReadServiceState(string serviceName, string displayName, bool? restoreRunning, bool includeStartType)
        {
            try
            {
                int? start = null;
                bool delayed = false;
                if (includeStartType)
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", false);
                    if (key?.GetValue("Start") is int s) start = s;
                    delayed = key?.GetValue("DelayedAutostart") is int d && d != 0;
                    if (start == null) return null;
                }
                return new ServiceUndoPayload(serviceName, displayName, start, delayed, restoreRunning);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                return null;
            }
        }

        private static ActivityEntry? Safe(Func<ActivityEntry> write)
        {
            try
            {
                return write();
            }
            catch (Exception ex)
            {
                AppLog.Warning("Etkinlik kaydı yazılamadı.", ex, nameof(ActivityRecording));
                return null;
            }
        }
    }
}
