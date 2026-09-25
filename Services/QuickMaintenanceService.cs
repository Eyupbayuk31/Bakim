using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Core.Activity;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services.Activity;

namespace Bakım.Services
{
    /// <param name="Stage">Kullanıcıya gösterilen aşama metni.</param>
    /// <param name="Percent">Gerçek ilerleme: tamamlanan adım / toplam adım.</param>
    public sealed record QuickMaintenanceProgress(string Stage, int Percent);

    public sealed record QuickMaintenanceResult(int FilesDeleted, int FilesSkipped, long BytesFreed, bool DnsFlushed,
        IReadOnlyList<string> Details, bool Cancelled)
    {
        public string Summary =>
            $"{Core.Text.ByteFormatter.Format(BytesFreed)} boşaltıldı · {FilesDeleted:N0} dosya" +
            (FilesSkipped > 0 ? $" · {FilesSkipped:N0} atlandı (kullanımda/korumalı)" : "") +
            (DnsFlushed ? " · DNS önbelleği temizlendi" : "");
    }

    /// <summary>
    /// "Hızlı Bakım" (MASTER_PLAN §5.1): ölçülebilir, güvenli işler — yaş filtreli geçici dosyalar,
    /// çökme dökümleri, gölgelendirici ve tarayıcı önbellekleri, DNS önbelleği. RAM kırpma YOKTUR:
    /// etkisi geçicidir ve "hızlandırma" gibi sunulmamalıdır.
    /// </summary>
    public enum QuickMaintenanceOrigin { Manual, Scheduled }

    public interface IQuickMaintenanceService
    {
        Task<QuickMaintenanceResult> RunAsync(IProgress<QuickMaintenanceProgress>? progress, CancellationToken ct,
            QuickMaintenanceOrigin origin = QuickMaintenanceOrigin.Manual);
    }

    public sealed class QuickMaintenanceService : IQuickMaintenanceService
    {
        /// <summary>Hızlı Bakım'ın dokunduğu kategoriler (Temizleyici'deki kurallarla aynı kapsam ve yaş filtresi).</summary>
        public static readonly string[] CategoryIds =
        {
            "user_temp", "windows_temp", "crash_dumps", "directx_shader",
            "edge_cache", "chrome_cache", "brave_cache", "firefox_cache"
        };

        private readonly ISystemCleanService _clean;
        private readonly IActivityService _activity;

        public QuickMaintenanceService(ISystemCleanService clean, IActivityService activity)
        {
            _clean = clean;
            _activity = activity;
        }

        public async Task<QuickMaintenanceResult> RunAsync(IProgress<QuickMaintenanceProgress>? progress, CancellationToken ct,
            QuickMaintenanceOrigin origin = QuickMaintenanceOrigin.Manual)
        {
            bool admin = UacHelper.IsAdministrator();
            var categories = _clean.GetDefaultCategories()
                .Where(c => CategoryIds.Contains(c.Id) && (!c.RequiresAdmin || admin))
                .ToList();

            int steps = categories.Count * 2 + 1;
            int done = 0;
            void Report(string stage) => progress?.Report(new QuickMaintenanceProgress(stage, (int)Math.Round(100.0 * done / steps)));

            var details = new List<string>();
            var items = new List<ActivityItem>();
            int deleted = 0, skipped = 0;
            long freed = 0;
            bool cancelled = false;

            try
            {
                foreach (var category in categories)
                {
                    ct.ThrowIfCancellationRequested();
                    Report($"{category.Name} taranıyor…");
                    var (found, _) = await _clean.ScanCategoryAsync(category, new Progress<string>(_ => { }), ct).ConfigureAwait(false);
                    done++;

                    if (found.Count == 0)
                    {
                        done++;
                        continue;
                    }

                    Report($"{category.Name} temizleniyor…");
                    var result = await _clean.CleanItemsAsync(found, new Progress<(string, int)>(_ => { }), ct).ConfigureAwait(false);
                    done++;

                    deleted += result.TotalFilesDeleted;
                    skipped += result.TotalFilesSkipped;
                    freed += result.TotalBytesFreed;
                    details.Add($"{category.Name}: {result.TotalFilesDeleted:N0} dosya · {result.FormattedBytesFreed}" +
                                (result.TotalFilesSkipped > 0 ? $" · {result.TotalFilesSkipped:N0} atlandı" : ""));
                    items.Add(new ActivityItem(category.Name, "Temizle", $"{result.TotalFilesDeleted} dosya · {result.FormattedBytesFreed}",
                        result.TotalFilesSkipped > 0 ? $"{result.TotalFilesSkipped} dosya kullanımda/korumalı" : null));
                }

                if (!admin)
                    details.Add("Windows geçici dosyaları atlandı (yönetici izni gerekir).");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            bool dns = false;
            if (!cancelled)
            {
                Report("DNS önbelleği temizleniyor…");
                var run = await ProcessRunner.RunAsync("ipconfig.exe", new[] { "/flushdns" }, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                dns = run.Succeeded;
                details.Add(dns ? "DNS önbelleği temizlendi." : $"DNS önbelleği temizlenemedi: {run.Describe()}");
                items.Add(new ActivityItem("DNS önbelleği", "ipconfig /flushdns", dns ? "Tamam" : "Başarısız"));
                done++;
            }
            Report(cancelled ? "İptal edildi" : "Tamamlandı");

            var outcome = cancelled ? ActivityOutcome.Cancelled
                : deleted == 0 && skipped > 0 && !dns ? ActivityOutcome.Failed
                : ActivityOutcome.Succeeded;
            var final = new QuickMaintenanceResult(deleted, skipped, freed, dns, details, cancelled);
            if (origin == QuickMaintenanceOrigin.Scheduled)
                _activity.RecordSimple(ActivityKind.Clean, "Zamanlanmış bakım", "Haftalık güvenli temizlik", final.Summary, outcome, items, deepLink: "Settings");
            else
                _activity.RecordSimple(ActivityKind.Clean, "Kontrol Paneli", "Hızlı Bakım", final.Summary, outcome, items, deepLink: "Dashboard");
            return final;
        }
    }
}
