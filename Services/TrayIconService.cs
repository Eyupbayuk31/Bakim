using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Bakım.Services
{
    public interface ITrayIconService
    {
        /// <summary>Ana pencereyi tepsi davranışına bağlar. Birden çok kez çağrılabilir.</summary>
        void Attach(Window window);

        /// <summary>Tepsi simgesini kaldırır ve pencere bağlantısını keser.</summary>
        void Detach();

        /// <summary>Pencereyi tepsiden geri getirir ve öne alır.</summary>
        void RestoreWindow();

        /// <summary>Kullanıcıya sistem tepsisi balon bildirimi gösterir.</summary>
        void ShowBalloon(string title, string message);
    }

    /// <summary>
    /// Sistem Tepsisi (System Tray) ve Arka Planda Çalışma Motoru.
    /// Çarpıya (X) basıldığında uygulamanın kapanmayıp arka planda nöbet tutmasını sağlar.
    /// Ultra Oyun Modu ve Hızlı RAM temizliğini doğrudan tepsi menüsünden yönetir.
    /// </summary>
    public sealed class TrayIconService : ITrayIconService, IDisposable
    {
        private readonly IAppSettingsService _settings;
        private readonly ISystemCleanService _cleanService;
        private readonly IGameModeService _gameModeService;
        private readonly INavigationService _navigationService;
        private readonly ILogService _log;

        private Forms.NotifyIcon? _notifyIcon;
        private Window? _window;
        private bool _balloonShown;
        private bool _disposed;
        private Views.Windows.TrayFlyoutWindow? _flyoutWindow;

        public TrayIconService(
            IAppSettingsService settings,
            ISystemCleanService cleanService,
            IGameModeService gameModeService,
            INavigationService navigationService,
            ILogService log)
        {
            _settings = settings;
            _cleanService = cleanService;
            _gameModeService = gameModeService;
            _navigationService = navigationService;
            _log = log ?? NullLogService.Instance;

            _gameModeService.GameModeChanged += OnGameModeChanged;
        }

        public void Attach(Window window)
        {
            if (_disposed || window == null) return;

            Detach();

            _window = window;
            _window.StateChanged += OnWindowStateChanged;
            _window.Closing += OnWindowClosing;
            _window.Closed += OnWindowClosed;

            try
            {
                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = LoadAppIcon(),
                    Text = "Bakım - Sistem Yönetim Paneli",
                    Visible = true
                };

                _notifyIcon.MouseUp += OnNotifyIconMouseUp;

                _log.Info("Sistem tepsisi simgesi oluşturuldu ve pencereye bağlandı.", nameof(TrayIconService));
            }
            catch (Exception ex)
            {
                _log.Error("Sistem tepsisi simgesi oluşturulamadı.", ex, nameof(TrayIconService));
                _notifyIcon = null;
            }
        }

        private void OnNotifyIconMouseUp(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left || e.Button == Forms.MouseButtons.Right)
            {
                Application.Current?.Dispatcher.Invoke(() => ShowFlyoutWindow());
            }
        }

        private void ShowFlyoutWindow()
        {
            if (_flyoutWindow == null)
            {
                _flyoutWindow = new Views.Windows.TrayFlyoutWindow(this, _gameModeService, _cleanService, _navigationService);
            }

            _flyoutWindow.UpdateState();

            // Ekran çalışma alanını ve farenin pozisyonunu al
            var workArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var mousePos = System.Windows.Forms.Cursor.Position;

            // X ve Y pozisyonunu çalışma alanının sağ alt köşesine hizala
            // Margin vs payı bırakalım
            double x = workArea.Right - _flyoutWindow.Width - 10;
            double y = workArea.Bottom - _flyoutWindow.Height - 10;

            // Eğer taskbar üstte veya soldaysa diye fare pozisyonuna göre de şekillenebilir, ama standart sağ alt daha şıktır.
            // Fare X'i çok soldaysa sola açılabilir.
            if (mousePos.X < workArea.Right / 2)
                x = mousePos.X;

            _flyoutWindow.Left = x;
            _flyoutWindow.Top = y;

            _flyoutWindow.Show();
            _flyoutWindow.Activate();
        }

        public void Detach()
        {
            if (_window != null)
            {
                _window.StateChanged -= OnWindowStateChanged;
                _window.Closing -= OnWindowClosing;
                _window.Closed -= OnWindowClosed;
                _window = null;
            }

            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
                    _notifyIcon.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Warning("Tepsi simgesi kaldırılırken hata.", ex, nameof(TrayIconService));
                }

                _notifyIcon = null;
            }

            if (_flyoutWindow != null)
            {
                _flyoutWindow.Close();
                _flyoutWindow = null;
            }
        }

        public void RestoreWindow()
        {
            var window = _window;
            if (window == null) return;

            try
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
                window.Activate();
                window.Topmost = true;
                window.Topmost = false;
                window.Focus();
            }
            catch (Exception ex)
            {
                _log.Warning("Pencere tepsiden geri getirilemedi.", ex, nameof(TrayIconService));
            }
        }

        public void ShowBalloon(string title, string message)
        {
            try
            {
                _notifyIcon?.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _log.Warning("Tepsi bildirimi gösterilemedi.", ex, nameof(TrayIconService));
            }
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window == null) return;
            if (_window.WindowState != WindowState.Minimized) return;

            if (!_settings.Current.MinimizeToTray) return;

            _window.Hide();

            if (!_balloonShown)
            {
                _balloonShown = true;
                ShowBalloon("Bakım arka planda çalışıyor",
                    "Pencereyi geri getirmek için tepsi simgesine tıklayın.");
            }

            _log.Debug("Pencere sistem tepsisine küçültüldü.", nameof(TrayIconService));
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!MainWindow.IsExplicitExit)
            {
                e.Cancel = true;
                _window?.Hide();
                ShowBalloon("Bakım Arka Planda Çalışıyor",
                    "Uygulama arka planda nöbet tutmaya devam ediyor. Açmak için simgeye tıklayın.");
                _log.Info("Çarpı (X) butonuna basıldı: Pencere arka plana küçültüldü (Close-to-Tray).", nameof(TrayIconService));
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Detach();

        private void OnGameModeChanged(bool isActive)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_flyoutWindow != null)
                {
                    _flyoutWindow.UpdateState();
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Text = isActive 
                        ? "Bakım - Oyun Modu Aktif (Arka Plan Donduruldu)" 
                        : "Bakım - Sistem Yönetim Paneli";
                }
            });
        }

        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var resource = Application.Current.Resources;
                // GetResourceStream might fail if Application.Current is null
                var res = Application.GetResourceStream(
                    new Uri("pack://application:,,,/Bakim;component/Assets/app.ico", UriKind.Absolute));

                if (res?.Stream != null)
                {
                    using var stream = res.Stream;
                    return new System.Drawing.Icon(stream);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Uygulama simgesi yüklenemedi, sistem simgesi kullanılıyor.", ex, nameof(TrayIconService));
            }

            return System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gameModeService.GameModeChanged -= OnGameModeChanged;
            Detach();
        }
    }
}
