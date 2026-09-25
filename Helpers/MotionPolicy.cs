using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Bakım.Helpers
{
    /// <summary>
    /// "Animasyonları azalt" (MASTER_PLAN §3.3). Kullanıcı ayarı ya da Windows'ta istemci alanı
    /// animasyonları kapalıysa, hareket token'ları açılışta sıfırlanır. Görünümler bu token'ları
    /// StaticResource ile okuduğu için pencereler yüklenmeden önce çağrılmalıdır.
    /// </summary>
    public static class MotionPolicy
    {
        public static bool IsReduced { get; private set; }

        private static readonly string[] DurationKeys =
        {
            "Motion.Fast", "Motion.Normal", "Motion.Slow",
            "Duration.Instant", "Duration.Fast", "Duration.Base", "Duration.Slow"
        };

        private static readonly string[] TimeKeys = { "Time.Fast", "Time.Base" };

        public static void Apply(bool userPrefersReduced)
        {
            bool systemReduced;
            try
            {
                systemReduced = !SystemParameters.ClientAreaAnimation;
            }
            catch
            {
                systemReduced = false;
            }

            IsReduced = userPrefersReduced || systemReduced;
            var app = Application.Current;
            if (!IsReduced || app == null) return;

            foreach (var key in DurationKeys) app.Resources[key] = new Duration(TimeSpan.Zero);
            foreach (var key in TimeKeys) app.Resources[key] = TimeSpan.Zero;

            // Sayfa geçişi: boş hikâye tahtası (anında görünür).
            if (app.Resources.Contains("FadeInSlideLeft")) app.Resources["FadeInSlideLeft"] = new Storyboard();
        }
    }
}
