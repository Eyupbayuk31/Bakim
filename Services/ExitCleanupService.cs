using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IExitCleanupService
    {
        /// <summary>
        /// "Çıkışta otomatik temizle" tercihi açıkken uygulama kapanırken çağrılır.
        /// Katı bir zaman bütçesiyle çalışır: kapanışı asla kilitlemez.
        /// </summary>
        void RunExitCleanup();
    }

    /// <summary>
    /// Ayarlardaki "Çıkışta Otomatik Temizle" tercihini gerçekten uygulayan servis.
    /// Yalnızca yönetici yetkisi gerektirmeyen, düşük riskli geçici dosya kategorilerini
    /// temizler; kapanış anında UAC istemi veya uzun süren tarama yapmaz.
    /// </summary>
    public sealed class ExitCleanupService : IExitCleanupService
    {
        /// <summary>Kapanışın kullanıcıya takılmış gibi hissettirmemesi için toplam süre bütçesi.</summary>
        private static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(6);

        /// <summary>Kapanışta yalnızca bu düşük riskli kategoriler temizlenir.</summary>
        private static readonly HashSet<string> SafeCategoryIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "user_temp"
        };

        private readonly ISystemCleanService _cleanService;
        private readonly ILogService _log;

        public ExitCleanupService(ISystemCleanService cleanService, ILogService log)
        {
            _cleanService = cleanService;
            _log = log ?? NullLogService.Instance;
        }

        public void RunExitCleanup()
        {
            using var cts = new CancellationTokenSource(TimeBudget);

            try
            {
                var categories = _cleanService.GetDefaultCategories()
                    .Where(c => SafeCategoryIds.Contains(c.Id) && !c.RequiresAdmin)
                    .ToList();

                if (categories.Count == 0)
                {
                    _log.Info("Çıkış temizliği: uygun kategori yok.", nameof(ExitCleanupService));
                    return;
                }

                var noProgress = new Progress<string>(_ => { });
                var allItems = new List<CleanFileItem>();

                foreach (var category in categories)
                {
                    if (cts.IsCancellationRequested) break;

                    var (items, _) = _cleanService
                        .ScanCategoryAsync(category, noProgress, cts.Token)
                        .GetAwaiter().GetResult();

                    allItems.AddRange(items);
                }

                if (allItems.Count == 0)
                {
                    _log.Info("Çıkış temizliği: silinecek dosya bulunamadı.", nameof(ExitCleanupService));
                    return;
                }

                var cleanProgress = new Progress<(string file, int percent)>(_ => { });

                var result = _cleanService
                    .CleanItemsAsync(allItems, cleanProgress, cts.Token)
                    .GetAwaiter().GetResult();

                _log.Info(
                    $"Çıkış temizliği tamamlandı: {result.TotalFilesDeleted} dosya silindi, " +
                    $"{result.FormattedBytesFreed} kazanıldı ({result.TotalFilesSkipped} atlandı).",
                    nameof(ExitCleanupService));
            }
            catch (OperationCanceledException)
            {
                _log.Info("Çıkış temizliği zaman bütçesini aştı ve güvenle yarıda kesildi.", nameof(ExitCleanupService));
            }
            catch (Exception ex)
            {
                _log.Error("Çıkış temizliği başarısız oldu.", ex, nameof(ExitCleanupService));
            }
        }
    }
}
