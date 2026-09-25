using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Activity;

namespace Bakım.Services.Activity
{
    /// <summary>Bir kaydı geri alan işleyici; <see cref="Key"/> ile kayda bağlanır (MASTER_PLAN §7.2).</summary>
    public interface IUndoHandler
    {
        string Key { get; }
        Task<UndoResult> UndoAsync(ActivityEntry entry, CancellationToken ct);
        /// <summary>Kaydın geri alma verisi hâlâ yerinde mi?</summary>
        bool HasPayload(ActivityEntry entry) => string.IsNullOrEmpty(entry.PayloadPath) || Directory.Exists(entry.PayloadPath);
    }

    /// <summary>
    /// Etkinlik Merkezi: Bakım'ın sistemde yaptığı her değişikliğin tek kaydı ve geri alma noktası.
    /// Modüllerin kendi geçmiş ekranları bu kayıtların filtrelenmiş görünümüdür.
    /// </summary>
    public interface IActivityService
    {
        /// <summary>Kayıtlar, yeniden eskiye.</summary>
        IReadOnlyList<ActivityEntry> Entries { get; }

        /// <summary>Kayıt eklendi ya da güncellendi. Arka plan iş parçacığından tetiklenebilir.</summary>
        event EventHandler? Changed;

        /// <summary>Kaydı ekler. Kayıt yazılamasa bile asıl işlemi bozmaz (yalnızca günlüğe yazar).</summary>
        ActivityEntry Record(ActivityEntry entry);

        /// <summary>Kayıt kimliği için geri alma verisi klasörü oluşturur.</summary>
        string CreateJournal(string entryId);

        bool IsUndoing(string entryId);

        /// <summary>Kaydı geri alır; sonucu özgün kayda işler ve geri almayı ayrı bir kayıt olarak yazar.</summary>
        Task<UndoResult> UndoAsync(string entryId, CancellationToken ct = default);

        void Clear();
    }

    public sealed class ActivityService : IActivityService
    {
        private readonly ActivityStore _store;
        private readonly Dictionary<string, IUndoHandler> _handlers;
        private readonly object _gate = new();
        private readonly HashSet<string> _undoing = new(StringComparer.Ordinal);
        private List<ActivityEntry>? _entries;

        public ActivityService(IEnumerable<IUndoHandler> handlers)
            : this(new ActivityStore(ActivityStore.DefaultDirectory()), handlers) { }

        public ActivityService(ActivityStore store, IEnumerable<IUndoHandler> handlers)
        {
            _store = store;
            _handlers = handlers.ToDictionary(h => h.Key, StringComparer.OrdinalIgnoreCase);
        }

        public event EventHandler? Changed;

        public IReadOnlyList<ActivityEntry> Entries
        {
            get
            {
                lock (_gate) return EnsureLoaded().ToList();
            }
        }

        private List<ActivityEntry> EnsureLoaded()
        {
            if (_entries != null) return _entries;
            try
            {
                _store.Maintain(DateTime.UtcNow);
                var loaded = _store.Load();
                if (loaded.CorruptLines > 0)
                    AppLog.Warning($"Etkinlik kaydında {loaded.CorruptLines} bozuk satır atlandı.", null, nameof(ActivityService));
                _entries = loaded.Entries.Select(WithPayloadState).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning("Etkinlik kaydı okunamadı.", ex, nameof(ActivityService));
                _entries = new List<ActivityEntry>();
            }
            return _entries;
        }

        /// <summary>Yedeği silinmiş (ya da işleyicisi olmayan) geri alınabilir kayıt → "Süresi doldu".</summary>
        private ActivityEntry WithPayloadState(ActivityEntry e)
        {
            if (e.Undo != UndoState.Undoable) return e;
            if (e.UndoHandler == null || !_handlers.TryGetValue(e.UndoHandler, out var handler) || !handler.HasPayload(e))
                return e with { Undo = UndoState.Expired };
            return e;
        }

        public ActivityEntry Record(ActivityEntry entry)
        {
            if (entry.CanUndo && !_handlers.ContainsKey(entry.UndoHandler!))
            {
                AppLog.Warning($"Bilinmeyen geri alma işleyicisi: {entry.UndoHandler}", null, nameof(ActivityService));
                entry = entry with { Undo = UndoState.NotUndoable, UndoHandler = null };
            }

            try
            {
                _store.Append(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning($"Etkinlik kaydı yazılamadı: {entry.Title}", ex, nameof(ActivityService));
            }

            lock (_gate)
            {
                var list = EnsureLoaded();
                int index = list.FindIndex(e => e.Id == entry.Id);
                if (index >= 0) list[index] = entry;
                else list.Insert(0, entry);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return entry;
        }

        public string CreateJournal(string entryId) => _store.CreateJournal(entryId);

        public bool IsUndoing(string entryId)
        {
            lock (_gate) return _undoing.Contains(entryId);
        }

        public async Task<UndoResult> UndoAsync(string entryId, CancellationToken ct = default)
        {
            ActivityEntry? entry;
            lock (_gate)
            {
                entry = EnsureLoaded().FirstOrDefault(e => e.Id == entryId);
                if (entry == null) return UndoResult.Fail("Kayıt bulunamadı.");
                if (!entry.CanUndo) return UndoResult.Fail(entry.Undo == UndoState.Expired
                    ? "Bu işlemin yedeği artık yok; geri alınamaz."
                    : "Bu işlem geri alınamaz.");
                if (!_undoing.Add(entryId)) return UndoResult.Fail("Geri alma zaten sürüyor.");
            }

            UndoResult result;
            try
            {
                var handler = _handlers[entry.UndoHandler!];
                result = await handler.UndoAsync(entry, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = UndoResult.Fail("Geri alma iptal edildi.");
            }
            catch (Exception ex)
            {
                AppLog.Error($"Geri alma başarısız: {entry.Title}", ex, nameof(ActivityService));
                result = UndoResult.Fail($"Geri alma hata verdi: {ex.Message}");
            }
            finally
            {
                lock (_gate) _undoing.Remove(entryId);
            }

            // Elle yapılması gereken geri alma (ör. Geri Dönüşüm Kutusu): durum değişmez.
            if (result.Manual) return result;

            var now = DateTime.UtcNow;
            Record(ActivityQuery.MarkUndone(entry, result, now));
            Record(ActivityQuery.RestoreEntry(entry, result, now));
            return result;
        }

        public void Clear()
        {
            try
            {
                _store.ClearAll();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning("Etkinlik kaydı temizlenemedi.", ex, nameof(ActivityService));
            }
            lock (_gate) _entries = new List<ActivityEntry>();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
