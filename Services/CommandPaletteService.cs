using Bakım.Models;

namespace Bakım.Services
{
    public interface ICommandPaletteService
    {
        List<CommandPaletteItem> GetAllCommands();
        List<CommandPaletteItem> Search(string query);
    }

    public class CommandPaletteService : ICommandPaletteService
    {
        private readonly List<CommandPaletteItem> _commands;

        public CommandPaletteService()
        {
            _commands = BuildCommandCatalog();
        }

        public List<CommandPaletteItem> GetAllCommands()
        {
            return _commands;
        }

        public List<CommandPaletteItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _commands.Take(12).ToList();

            string cleanQuery = query.Trim().ToLowerInvariant();

            return _commands
                .Where(c => c.Title.ToLowerInvariant().Contains(cleanQuery) ||
                            c.Description.ToLowerInvariant().Contains(cleanQuery) ||
                            c.Category.ToLowerInvariant().Contains(cleanQuery))
                .OrderByDescending(c => c.Title.ToLowerInvariant().StartsWith(cleanQuery))
                .ThenByDescending(c => c.Category.ToLowerInvariant().Contains(cleanQuery))
                .Take(15)
                .ToList();
        }

        private static List<CommandPaletteItem> BuildCommandCatalog()
        {
            return new List<CommandPaletteItem>
            {
                // Quick System Actions
                new()
                {
                    Title = "Tek Tıkla Optimize Et",
                    Category = "Hızlı Eylem",
                    Description = "RAM çalışma kümesini temizler ve geçici önbelleği boşaltır.",
                    IconName = "TopSpeed24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "QuickBoost",
                    KeyboardShortcut = "Booster"
                },
                new()
                {
                    Title = "Gezgini Yeniden Başlat (Restart Explorer)",
                    Category = "Hızlı Eylem",
                    Description = "Windows Gezgini sürecini yeniden başlatarak arayüzü tazeler.",
                    IconName = "ArrowClockwise24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "RestartExplorer",
                    KeyboardShortcut = "Action"
                },
                new()
                {
                    Title = "Sistem Geri Yükleme Noktası Oluştur",
                    Category = "Güvenlik",
                    Description = "Windows Sistem Koruması ile anlık geri yükleme noktası alır.",
                    IconName = "History24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "CreateRestorePoint",
                    KeyboardShortcut = "Backup"
                },
                new()
                {
                    Title = "Yönetici Olarak Yeniden Başlat (UAC)",
                    Category = "Güvenlik",
                    Description = "Uygulamayı Administrator yetkileriyle yeniden başlatır.",
                    IconName = "ShieldKeyhole24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "ElevateAdmin",
                    KeyboardShortcut = "UAC"
                },
                new()
                {
                    Title = "Tema Değiştir (Açık / Koyu)",
                    Category = "Görünüm",
                    Description = "Pencere temasını Açık ve Koyu mod arasında değiştirir.",
                    IconName = "DarkTheme24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "ToggleTheme",
                    KeyboardShortcut = "Theme"
                },
                new()
                {
                    Title = "AMOLED Pure Black Tema",
                    Category = "Görünüm",
                    Description = "OLED ekranlar için tam siyah ve yüksek kontrast teması.",
                    IconName = "DarkTheme24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "Theme:Amoled",
                    KeyboardShortcut = "OLED"
                },
                new()
                {
                    Title = "Cyberpunk Neon Mor Tema",
                    Category = "Görünüm",
                    Description = "Mor neon ve fütüristik cam temasına geçiş yapar.",
                    IconName = "Color24",
                    ActionKind = CommandActionKind.QuickAction,
                    TargetParameter = "Theme:Cyberpunk",
                    KeyboardShortcut = "Neon"
                },

                // Module Navigation
                new()
                {
                    Title = "Genel Bakış (Dashboard)",
                    Category = "Sayfa Navigasyonu",
                    Description = "Sistem sağlık skoru, canlı telemetri ve donanım özeti.",
                    IconName = "Home24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "Dashboard",
                    KeyboardShortcut = "Ctrl+1"
                },
                new()
                {
                    Title = "Sistem Temizliği",
                    Category = "Sayfa Navigasyonu",
                    Description = "Geçici dosyalar, web önbelleği ve log kalıntılarını temizler.",
                    IconName = "Delete24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "Cleaner",
                    KeyboardShortcut = "Ctrl+2"
                },
                new()
                {
                    Title = "Bellek ve Süreç Yöneticisi (RAM)",
                    Category = "Sayfa Navigasyonu",
                    Description = "RAM boşaltma, süreç önceliği ve kaynak canavarlarını izleme.",
                    IconName = "TopSpeed24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "Optimizer",
                    KeyboardShortcut = "Ctrl+3"
                },
                new()
                {
                    Title = "Başlangıç Programları",
                    Category = "Sayfa Navigasyonu",
                    Description = "Windows ile otomatik başlayan programları yönetin ve geciktirin.",
                    IconName = "Apps24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "Startup",
                    KeyboardShortcut = "Ctrl+4"
                },
                new()
                {
                    Title = "Donanım & Disk Bilgisi",
                    Category = "Sayfa Navigasyonu",
                    Description = "Sistem özellikleri, disk S.M.A.R.T sağlığı ve büyük dosya tarayıcı.",
                    IconName = "Desktop24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "SystemInfo",
                    KeyboardShortcut = "Ctrl+5"
                },
                new()
                {
                    Title = "Ağ ve Port İzleyici",
                    Category = "Sayfa Navigasyonu",
                    Description = "Aktif bağlantılar, dinlenen portlar ve güvenlik duvarı bloklama.",
                    IconName = "Globe24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "NetworkMonitor",
                    KeyboardShortcut = "Ctrl+6"
                },
                new()
                {
                    Title = "Windows Servisleri",
                    Category = "Sayfa Navigasyonu",
                    Description = "Gereksiz servisleri devre dışı bırakma ve başlangıç türü yönetimi.",
                    IconName = "DeveloperBoard24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "ServiceManager",
                    KeyboardShortcut = "Ctrl+7"
                },
                new()
                {
                    Title = "Gizlilik & Debloat",
                    Category = "Sayfa Navigasyonu",
                    Description = "Telemetri kapatma, Windows Defender ve Cortana ayarları.",
                    IconName = "ShieldKeyhole24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "PrivacyDebloat",
                    KeyboardShortcut = "Ctrl+8"
                },
                new()
                {
                    Title = "Mavi Ekran & Çökme Analizi",
                    Category = "Sayfa Navigasyonu",
                    Description = "Minidump analizi, BSOD hata kodları ve sürücü teşhisi.",
                    IconName = "HeartPulse24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "CrashAnalyzer",
                    KeyboardShortcut = "Ctrl+9"
                },
                new()
                {
                    Title = "Derin Program Kaldırıcı (Uninstaller)",
                    Category = "Sayfa Navigasyonu",
                    Description = "Revo stili otomatik kalıntı temizleme ve sessiz toplu kaldırma.",
                    IconName = "Apps24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "Uninstaller",
                    KeyboardShortcut = "Ctrl+0"
                },
                new()
                {
                    Title = "Windows Tweaker (Tüm Ayarlar)",
                    Category = "Sayfa Navigasyonu",
                    Description = "150+ Windows ince ayarı, görünüm ve davranış özelleştirme.",
                    IconName = "Wrench24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker",
                    KeyboardShortcut = "Tweaker"
                },

                // Tweaker Subcategory Shortcuts
                new()
                {
                    Title = "Tweaker: Windows 11 Ayarları",
                    Category = "Windows Tweaker",
                    Description = "Klasik görev çubuğu, bağlam menüsü ve widget ayarları.",
                    IconName = "AppGeneric24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:Windows11"
                },
                new()
                {
                    Title = "Tweaker: Görünüm & Tema",
                    Category = "Windows Tweaker",
                    Description = "Aero Lite teması, koyu mod ve özel vurgu renkleri.",
                    IconName = "Color24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:Appearance"
                },
                new()
                {
                    Title = "Tweaker: Masaüstü & Görev Çubuğu",
                    Category = "Windows Tweaker",
                    Description = "Görev çubuğu saatinde saniyeleri göster, ses karıştırıcısı.",
                    IconName = "Desktop24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:DesktopTaskbar"
                },
                new()
                {
                    Title = "Tweaker: Sağ Tık Menüsü & Kısayollar",
                    Category = "Windows Tweaker",
                    Description = "Sahiplik Al menüsü, Komut İstemi ve kısayol okları.",
                    IconName = "CursorClick24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:ContextMenu"
                },
                new()
                {
                    Title = "Tweaker: Dosya Gezgini",
                    Category = "Windows Tweaker",
                    Description = "Dosya uzantılarını göster, sürücü harflerini başa al.",
                    IconName = "Folder24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:FileExplorer"
                },
                new()
                {
                    Title = "Tweaker: Microsoft Edge",
                    Category = "Windows Tweaker",
                    Description = "Edge kenar çubuğu ve rahatsız edici reklamları devre dışı bırak.",
                    IconName = "Globe24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:Edge"
                },
                new()
                {
                    Title = "Tweaker: Sistem Araçları",
                    Category = "Windows Tweaker",
                    Description = "God Mode klasörü, Hosts dosyası düzenleyici ve OEM bilgisi.",
                    IconName = "DeveloperBoard24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:Tools"
                },
                new()
                {
                    Title = "Tweaker: Klasik Uygulamalar",
                    Category = "Windows Tweaker",
                    Description = "Klasik Windows Fotoğraf Görüntüleyici ve Hesap Makinesi.",
                    IconName = "Apps24",
                    ActionKind = CommandActionKind.Navigate,
                    TargetParameter = "WindowsTweaker:ClassicApps"
                }
            };
        }
    }
}
