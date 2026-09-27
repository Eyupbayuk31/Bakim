using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace Bakım.Helpers
{
    /// <summary>
    /// Fluent 2 onay ve bilgi pencereleri (ANL Faz 5). Sistem <c>MessageBox</c>'ı tasarım dilinin dışında
    /// kalıyor, Mica/koyu tema almıyor ve klavye odağı zayıf. Kural:
    ///   • onay gerektiren eylem (silme, süreç sonlandırma, yükleme) → <see cref="ConfirmAsync"/>,
    ///   • hata ya da kullanıcının görmesi gereken sonuç → <see cref="InfoAsync"/>,
    ///   • "kopyalandı / tamamlandı" gibi bilgiler → pencere açılmaz, durum satırına yazılır.
    /// Eşzamanlı eski kod için <see cref="Show"/>, <c>MessageBox.Show</c> ile aynı imza ve modal davranıştadır.
    /// Tek istisna: çökme yakalayıcısı ve UAC yeniden başlatması sistem penceresinde kalır (arayüz bozukken
    /// Fluent pencere açmak ikinci bir hataya yol açabilir).
    /// </summary>
    public static class FluentDialog
    {
        private static Window? Owner() =>
            Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? (Application.Current?.MainWindow is { IsVisible: true } main ? main : null);

        private static Wpf.Ui.Controls.MessageBox Create(string title, string message, MessageBoxImage image = MessageBoxImage.None)
        {
            // Uzun metin (ör. zaman çizelgesi) pencereyi ekrandan taşırmasın.
            var text = new ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480
                }
            };

            object content = text;
            if (IconFor(image) is { } icon)
            {
                // Durum yalnızca renkle değil simgeyle de anlatılır (renk körlüğü, yüksek karşıtlık).
                var symbol = new SymbolIcon { Symbol = icon.Symbol, FontSize = 24, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                symbol.SetResourceReference(Control.ForegroundProperty, icon.Brush);
                System.Windows.Automation.AutomationProperties.SetName(symbol, icon.Label);
                var row = new DockPanel();
                DockPanel.SetDock(symbol, Dock.Left);
                row.Children.Add(symbol);
                row.Children.Add(text);
                content = row;
            }

            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = content,
                MinWidth = 380
            };
            if (Owner() is { } owner)
            {
                box.Owner = owner;
                box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            OpeningForTest?.Invoke(box);
            return box;
        }

        /// <summary>
        /// Yalnızca XAML duman testi içindir: pencere açılmadan önce çağrılır. Test pencereyi ekran dışına alır
        /// ve düğmeye otomasyonla basar; böylece kullanıcının masaüstünde hiçbir şey görünmez.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static Action<Wpf.Ui.Controls.MessageBox>? OpeningForTest { get; set; }

        private static (SymbolRegular Symbol, string Brush, string Label)? IconFor(MessageBoxImage image) => image switch
        {
            MessageBoxImage.Error => (SymbolRegular.ErrorCircle24, "Status.Critical.Text", "Hata"),
            MessageBoxImage.Warning => (SymbolRegular.Warning24, "Status.Caution.Text", "Uyarı"),
            MessageBoxImage.Question => (SymbolRegular.QuestionCircle24, "Status.Info.Text", "Soru"),
            MessageBoxImage.Information => (SymbolRegular.Info24, "Status.Info.Text", "Bilgi"),
            _ => null
        };

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

        /// <summary>
        /// <c>System.Windows.MessageBox.Show</c> yerine geçer: aynı parametreler, aynı dönüş değeri, aynı modal (çağıranı
        /// bekleten) davranış. Onay pencerelerinde silme/kaldırma gibi eylemlerin birincil düğmesi kırmızıdır
        /// (<see cref="Core.Ui.DialogPolicy"/>). Arka plan iş parçacığından çağrılabilir; pencere arayüz iş parçacığında açılır.
        /// </summary>
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption = "", MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None, System.Windows.MessageBoxResult defaultResult = System.Windows.MessageBoxResult.None)
        {
            var app = Application.Current;
            if (app == null) return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
            if (!app.Dispatcher.CheckAccess()) return app.Dispatcher.Invoke(() => Show(messageBoxText, caption, button, icon, defaultResult));

            try
            {
                var task = ShowCoreAsync(messageBoxText, caption, button, icon);
                if (!task.IsCompleted)
                {
                    // MessageBox.Show gibi çağıranı bekletir; bu sırada arayüz mesajları işlenmeye devam eder.
                    var frame = new DispatcherFrame();
                    task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                }
                return task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                // Dağıtıcı askıdayken (yerleşim geçişi vb.) iç döngü açılamaz; sistem penceresine düşülür.
                Bakım.Services.AppLog.Debug($"Fluent pencere açılamadı, sistem penceresi kullanılıyor: {ex.Message}", nameof(FluentDialog));
                return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
            }
        }

        private static async Task<System.Windows.MessageBoxResult> ShowCoreAsync(string message, string title, MessageBoxButton button, MessageBoxImage image)
        {
            var box = Create(string.IsNullOrWhiteSpace(title) ? "Bakım" : title, message, image);
            bool destructive = Core.Ui.DialogPolicy.IsDestructive(title, message);
            box.PrimaryButtonAppearance = destructive ? ControlAppearance.Danger : ControlAppearance.Primary;

            // Eşleme: birincil = Tamam/Evet, ikincil = Hayır (yalnızca üç düğmede), kapat (Esc) = Vazgeç/Hayır/Tamam.
            System.Windows.MessageBoxResult primary, secondary = System.Windows.MessageBoxResult.None, close;
            switch (button)
            {
                case MessageBoxButton.OKCancel:
                    box.PrimaryButtonText = "Tamam"; box.CloseButtonText = "Vazgeç";
                    primary = System.Windows.MessageBoxResult.OK; close = System.Windows.MessageBoxResult.Cancel;
                    box.IsSecondaryButtonEnabled = false;
                    break;
                case MessageBoxButton.YesNo:
                    box.PrimaryButtonText = "Evet"; box.CloseButtonText = "Hayır";
                    primary = System.Windows.MessageBoxResult.Yes; close = System.Windows.MessageBoxResult.No;
                    box.IsSecondaryButtonEnabled = false;
                    break;
                case MessageBoxButton.YesNoCancel:
                    box.PrimaryButtonText = "Evet"; box.SecondaryButtonText = "Hayır"; box.CloseButtonText = "Vazgeç";
                    primary = System.Windows.MessageBoxResult.Yes; secondary = System.Windows.MessageBoxResult.No; close = System.Windows.MessageBoxResult.Cancel;
                    break;
                default:
                    box.CloseButtonText = "Tamam";
                    box.IsPrimaryButtonEnabled = false;
                    box.IsSecondaryButtonEnabled = false;
                    primary = close = System.Windows.MessageBoxResult.OK;
                    break;
            }

            try
            {
                return await box.ShowDialogAsync() switch
                {
                    Wpf.Ui.Controls.MessageBoxResult.Primary => primary,
                    Wpf.Ui.Controls.MessageBoxResult.Secondary => secondary,
                    _ => close
                };
            }
            catch (TaskCanceledException)
            {
                return close;
            }
        }
    }
}
