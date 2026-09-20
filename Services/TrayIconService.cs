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
        private Forms.ToolStripMenuItem? _gameModeItem;
        private Window? _window;
        private bool _balloonShown;
        private bool _disposed;

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
                    Visible = true,
                    ContextMenuStrip = BuildMenu()
                };

                _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

                _log.Info("Sistem tepsisi simgesi oluşturuldu ve pencereye bağlandı.", nameof(TrayIconService));
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
                _window.Closing -= OnWindowClosing;
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
                _gameModeItem = null;
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

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!MainWindow.IsExplicitExit)
            {
                e.Cancel = true;
                _window?.Hide();
                ShowBalloon("Bakım Arka Planda Çalışıyor",
                    "Uygulama arka planda nöbet tutmaya devam ediyor. Açmak için çift tıklayın.");
                _log.Info("Çarpı (X) butonuna basıldı: Pencere arka plana küçültüldü (Close-to-Tray).", nameof(TrayIconService));
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Detach();

        private Forms.ContextMenuStrip BuildMenu()
        {
            var menu = new Forms.ContextMenuStrip();

            var showItem = new Forms.ToolStripMenuItem("🖥️ Bakım'ı Göster");
            showItem.Font = new System.Drawing.Font(showItem.Font, System.Drawing.FontStyle.Bold);
            showItem.Click += (_, _) => RestoreWindow();

            _gameModeItem = new Forms.ToolStripMenuItem(GetGameModeMenuText());
            _gameModeItem.Click += async (_, _) => await ToggleGameModeFromTrayAsync();

            var boostItem = new Forms.ToolStripMenuItem("⚡ Hızlı RAM Temizle");
            boostItem.Click += async (_, _) => await QuickBoostAsync();

            var settingsItem = new Forms.ToolStripMenuItem("⚙️ Ayarlar");
            settingsItem.Click += (_, _) =>
            {
                RestoreWindow();
                _navigationService.Navigate("Settings");
            };

            var exitItem = new Forms.ToolStripMenuItem("❌ Çıkış (Uygulamayı Kapat)");
            exitItem.Click += (_, _) => ShutdownApplication();

            menu.Items.Add(showItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_gameModeItem);
            menu.Items.Add(boostItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private string GetGameModeMenuText()
        {
            return _gameModeService.IsGameModeActive
                ? "🎮 Oyun Modu: [AÇIK] (Kapat)"
                : "🎮 Oyun Modu: [KAPALI] (Aç)";
        }

        private void OnGameModeChanged(bool isActive)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_gameModeItem != null)
                {
                    _gameModeItem.Text = GetGameModeMenuText();
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Text = isActive 
                        ? "Bakım - Oyun Modu Aktif (Arka Plan Donduruldu)" 
                        : "Bakım - Sistem Yönetim Paneli";
                }
            });
        }

        private async System.Threading.Tasks.Task ToggleGameModeFromTrayAsync()
        {
            try
            {
                long freed = await _gameModeService.ToggleGameModeAsync();
                bool isActive = _gameModeService.IsGameModeActive;

                if (isActive)
                {
                    ShowBalloon("🎮 Ultra Oyun Modu Aktif!",
                        $"Arka plan servisleri donduruldu. {Models.CleanCategory.FormatBytes(freed)} bellek serbest bırakıldı.");
                }
                else
                {
                    ShowBalloon("Oyun Modu Kapatıldı", "Arka plan servisleri normale döndü.");
                }
            }
            catch (Exception ex)
            {
                _log.Error("Tepsiden Oyun Modu değiştirilirken hata.", ex, nameof(TrayIconService));
            }
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
                MainWindow.IsExplicitExit = true;
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
            _gameModeService.GameModeChanged -= OnGameModeChanged;
            Detach();
        }
    }
}
