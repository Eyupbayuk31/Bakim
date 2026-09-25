using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bakım.Helpers
{
    /// <summary>
    /// Zamanlayıcı olayları için yeniden girme korumalı async işleyici (P-2). DispatcherTimer
    /// önceki "async void" turun bitmesini beklemez; WMI/süreç okuması aralıktan uzun sürerse
    /// turlar üst üste biner ve kaynak tüketimi katlanır. Önceki tur sürüyorsa yeni tur atlanır.
    /// </summary>
    public static class AsyncTick
    {
        public static EventHandler Guarded(Func<Task> work, string category)
        {
            int running = 0;
            return async (_, _) =>
            {
                if (Interlocked.Exchange(ref running, 1) == 1) return;
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    Services.AppLog.Debug($"Zamanlayıcı turu başarısız: {ex.Message}", category);
                }
                finally
                {
                    Volatile.Write(ref running, 0);
                }
            };
        }
    }
}
