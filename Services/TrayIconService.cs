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
    }

    /// <summary>
    /// Ayarlardaki "Simge Durumunda Tepsiye Küçült" tercihini gerçekten uygulayan servis.
    /// Bu tercih v3.0.3'e kadar arayüzde görünüyor ve kaydediliyor fakat hiçbir etkisi yoktu.
    ///
    /// WPF-UI 4.x tray desteği içermediği için System.Windows.Forms.NotifyIcon kullanılır;
    /// hiçbir WinForms penceresi açılmaz, yalnızca bildirim alanı simgesi oluşturulur.
    /// </summary>
    public sealed class TrayIconService : ITrayIconService, IDisposable
    {
        private readonly IAppSettingsService _settings;
        private readonly ISystemCleanService _cleanService;
        private readonly ILogService _log;

        private Forms.NotifyIcon? _notifyIcon;
        private Window? _window;
        private bool _balloonShown;
        private bool _disposed;

        public TrayIconService(IAppSettingsService settings, ISystemCleanService cleanService, ILogService log)
        {
            _settings = settings;
            _cleanService = cleanService;
            _log = log ?? NullLogService.Instance;
        }

        public void Attach(Window window)
        {
            if (_disposed || window == null) return;

            Detach();

            _window = window;
            _window.StateChanged += OnWindowStateChanged;
            _window.Closed += OnWindowClosed;

            try
            {
                _notifyIcon = new Forms.NotifyIcon
                {
                    Icon = LoadAppIcon(),
                    Text = "Bakım - Sistem Yönetim Paneli",
                    Visible = true,
                    ContextMenuStrip = BuildMenu()
                };

                _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

                _log.Info("Sistem tepsisi simgesi oluşturuldu.", nameof(TrayIconService));
            }
            catch (Exception ex)
            {
                _log.Error("Sistem tepsisi simgesi oluşturulamadı.", ex, nameof(TrayIconService));
                _notifyIcon = null;
            }
        }

        public void Detach()
        {
            if (_window != null)
            {
                _window.StateChanged -= OnWindowStateChanged;
                _window.Closed -= OnWindowClosed;
                _window = null;
            }

            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.ContextMenuStrip?.Dispose();
                    _notifyIcon.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Warning("Tepsi simgesi kaldırılırken hata.", ex, nameof(TrayIconService));
                }

                _notifyIcon = null;
            }
        }

        public void RestoreWindow()
        {
            var window = _window;
            if (window == null) return;

            try
            {
                window.Show();
                window.WindowState = WindowState.Normal;
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

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window == null) return;
            if (_window.WindowState != WindowState.Minimized) return;

            // Tercih kapalıysa normal simge durumunda küçültme davranışı korunur.
            if (!_settings.Current.MinimizeToTray) return;

            _window.Hide();

            if (!_balloonShown)
            {
                _balloonShown = true;
                ShowBalloon("Bakım arka planda çalışıyor",
                    "Pencereyi geri getirmek için tepsi simgesine çift tıklayın.");
            }

            _log.Debug("Pencere sistem tepsisine küçültüldü.", nameof(TrayIconService));
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Detach();

        private void ShowBalloon(string title, string message)
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

        private Forms.ContextMenuStrip BuildMenu()
        {
            var menu = new Forms.ContextMenuStrip();

            var showItem = new Forms.ToolStripMenuItem("Bakım'ı Göster");
            showItem.Click += (_, _) => RestoreWindow();

            var boostItem = new Forms.ToolStripMenuItem("Hızlı RAM Temizle");
            boostItem.Click += async (_, _) => await QuickBoostAsync();

            var exitItem = new Forms.ToolStripMenuItem("Çıkış");
            exitItem.Click += (_, _) => ShutdownApplication();

            menu.Items.Add(showItem);
            menu.Items.Add(boostItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private async System.Threading.Tasks.Task QuickBoostAsync()
        {
            try
            {
                long freed = await _cleanService.AutoTrimWorkingSetsAsync();

                ShowBalloon("RAM Temizlendi",
                    $"{Models.CleanCategory.FormatBytes(freed)} bellek geri kazanıldı.");

                _log.Info($"Tepsiden hızlı RAM temizliği: {Models.CleanCategory.FormatBytes(freed)}", nameof(TrayIconService));
            }
            catch (Exception ex)
            {
                _log.Error("Tepsiden RAM temizliği başarısız.", ex, nameof(TrayIconService));
            }
        }

        private void ShutdownApplication()
        {
            try
            {
                Detach();
                Application.Current?.Shutdown();
            }
            catch (Exception ex)
            {
                _log.Error("Tepsiden çıkış başarısız.", ex, nameof(TrayIconService));
            }
        }

        private static System.Drawing.Icon LoadAppIcon()
        {
            try
            {
                var resource = Application.GetResourceStream(
                    new Uri("pack://application:,,,/Bakim;component/Assets/app.ico", UriKind.Absolute));

                if (resource?.Stream != null)
                {
                    using var stream = resource.Stream;
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
            Detach();
        }
    }
}
