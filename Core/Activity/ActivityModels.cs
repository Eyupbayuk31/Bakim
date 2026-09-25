using System;
using System.Collections.Generic;

namespace Bakım.Core.Activity
{
    /// <summary>Etkinlik Merkezi'ndeki kaydın türü (MASTER_PLAN §7.2).</summary>
    public enum ActivityKind
    {
        Clean, Uninstall, SetupSession, Tweak, StartupChange, ServiceChange, FirewallRule,
        Quarantine, Restore, GameModeSession, StoreInstall, StoreUpdate, Analysis, PersistenceScan,
        ScheduledJob, AppUpdate, Other
    }

    public enum ActivityOutcome { Succeeded, PartiallySucceeded, Failed, Cancelled }

    public enum UndoState { NotUndoable, Undoable, Undone, UndoFailed, Expired }

    /// <summary>Tek bir hedef üzerinde yapılan iş (dosya, anahtar, hizmet…).</summary>
    public sealed record ActivityItem(string Target, string Action, string Result, string? Detail = null);

    /// <summary>
    /// Bakım'ın sistemde yaptığı bir değişikliğin kaydı. Kayıtlar değişmez: geri alma durumu
    /// değiştiğinde aynı <see cref="Id"/> ile yeni bir satır yazılır, okurken son satır geçerlidir.
    /// </summary>
    public sealed record ActivityEntry
    {
        public const int MaxInlineItems = 200;

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public DateTime AtUtc { get; init; } = DateTime.UtcNow;
        public ActivityKind Kind { get; init; }
        public string Module { get; init; } = "";
        public string Title { get; init; } = "";
        public string Summary { get; init; } = "";
        public ActivityOutcome Outcome { get; init; }
        public UndoState Undo { get; init; }
        /// <summary>Geri alma işleyicisinin anahtarı ("registry-values", "registry-reg-import" …).</summary>
        public string? UndoHandler { get; init; }
        /// <summary>Geri alma verisinin (yedekler, eski değerler) bulunduğu klasör.</summary>
        public string? PayloadPath { get; init; }
        /// <summary>İlgili modüldeki ayrıntı ekranına gezinme anahtarı ("Analyzer", "Uninstaller" …).</summary>
        public string? DeepLink { get; init; }
        /// <summary>Bu kayıt bir geri alma ise, geri alınan kaydın kimliği.</summary>
        public string? RelatedId { get; init; }
        /// <summary>Son geri alma denemesinin sonucu (dürüst rapor).</summary>
        public string? UndoMessage { get; init; }
        public DateTime? UndoneAtUtc { get; init; }
        public IReadOnlyList<ActivityItem> Items { get; init; } = Array.Empty<ActivityItem>();

        public bool CanUndo => Undo == UndoState.Undoable && !string.IsNullOrEmpty(UndoHandler);
    }

    /// <summary>Bir geri alma işleyicisinin sonucu.</summary>
    /// <param name="Manual">Otomatik geri alma yok, kullanıcı yönlendirildi (ör. Geri Dönüşüm Kutusu açıldı);
    /// kaydın durumu değişmez.</param>
    public sealed record UndoResult(bool Success, int Restored, int Failed, string Message, IReadOnlyList<ActivityItem>? Items = null, bool Manual = false)
    {
        public static UndoResult Fail(string message) => new(false, 0, 0, message);

        public ActivityOutcome Outcome => Success
            ? (Failed > 0 ? ActivityOutcome.PartiallySucceeded : ActivityOutcome.Succeeded)
            : (Restored > 0 ? ActivityOutcome.PartiallySucceeded : ActivityOutcome.Failed);
    }

    /// <summary>Geri alma işleyici anahtarları (MASTER_PLAN §7.2 tablosu).</summary>
    public static class UndoHandlers
    {
        public const string RegistryRegImport = "registry-reg-import";
        public const string RegistryValues = "registry-values";
        public const string ServiceConfig = "service-config";
        public const string ScheduledTaskXml = "scheduled-task-xml";
        public const string StartupApproved = "startup-approved";
        public const string FirewallRule = "firewall-rule";
        public const string QuarantineRestore = "quarantine-restore";
        public const string RecycleBin = "recycle-bin";
    }
}
