using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Bakım.Helpers
{
    /// <summary>
    /// Fluent 2 onay ve bilgi pencereleri (ANL Faz 5). Sistem <c>MessageBox</c>'ı tasarım dilinin dışında
    /// kalıyor, Mica/koyu tema almıyor ve klavye odağı zayıf. Kural:
    ///   • onay gerektiren eylem (silme, süreç sonlandırma, yükleme) → <see cref="ConfirmAsync"/>,
    ///   • hata ya da kullanıcının görmesi gereken sonuç → <see cref="InfoAsync"/>,
    ///   • "kopyalandı / tamamlandı" gibi bilgiler → pencere açılmaz, durum satırına yazılır.
    /// </summary>
    public static class FluentDialog
    {
        private static Window? Owner() =>
            Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? (Application.Current?.MainWindow is { IsVisible: true } main ? main : null);

        private static Wpf.Ui.Controls.MessageBox Create(string title, string message)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                // Uzun metin (ör. zaman çizelgesi) pencereyi ekrandan taşırmasın.
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new System.Windows.Controls.TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 480
                    }
                },
                MinWidth = 380
            };
            if (Owner() is { } owner)
            {
                box.Owner = owner;
                box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            return box;
        }

        /// <summary>Onay: birincil düğme seçildiyse true. <paramref name="destructive"/> birincil düğmeyi kırmızı yapar.</summary>
        public static async Task<bool> ConfirmAsync(string title, string message, string primaryText = "Devam et", string closeText = "Vazgeç", bool destructive = false)
        {
            var box = Create(title, message);
            box.PrimaryButtonText = primaryText;
            box.CloseButtonText = closeText;
            box.PrimaryButtonAppearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary;
            try
            {
                return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        /// <summary>Bilgi ya da hata: tek "Tamam" düğmesi.</summary>
        public static async Task InfoAsync(string title, string message)
        {
            var box = Create(title, message);
            box.CloseButtonText = "Tamam";
            box.IsPrimaryButtonEnabled = false;
            try
            {
                await box.ShowDialogAsync();
            }
            catch (TaskCanceledException ex)
            {
                Bakım.Services.AppLog.Debug($"Bilgi penceresi kapatıldı: {ex.Message}", nameof(FluentDialog));
            }
        }
    }
}
