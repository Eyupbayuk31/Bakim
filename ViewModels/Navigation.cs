using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace Bakım.ViewModels
{
    /// <summary>Kenar çubuğundaki bir gezinme öğesi.</summary>
    public sealed partial class NavItem : ObservableObject
    {
        public NavItem(string key, AppModule module, string label, SymbolRegular icon, string tooltip)
        {
            Key = key;
            Module = module;
            Label = label;
            Icon = icon;
            Tooltip = tooltip;
        }

        /// <summary>NavigateCommand'a verilen anahtar.</summary>
        public string Key { get; }
        public AppModule Module { get; }
        public string Label { get; }
        public string Tooltip { get; }
        /// <summary>Seçiliyken dolu (Filled) varyantı çizilir — §3.4.</summary>
        public SymbolRegular Icon { get; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>Sayaç rozeti (ör. okunmamış etkinlik). 0 → gizli.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBadge), nameof(BadgeText))]
        private int _badge;

        public bool HasBadge => Badge > 0;
        public string BadgeText => Badge > 99 ? "99+" : Badge.ToString();
    }

    public sealed class NavGroup
    {
        public NavGroup(string title, IReadOnlyList<NavItem> items, bool isFirst = false)
        {
            Title = title;
            Items = items;
            IsFirst = isFirst;
        }

        public string Title { get; }
        public IReadOnlyList<NavItem> Items { get; }
        /// <summary>İlk grubun üstünde ayırıcı çizgi çizilmez.</summary>
        public bool IsFirst { get; }
    }

    /// <summary>
    /// Bilgi mimarisi v2 (MASTER_PLAN §2.1): kenar çubuğu grupları tek yerde tanımlanır.
    /// Her anahtar <see cref="AppModuleRegistry"/>'de çözülür (test edilir).
    /// </summary>
    public static class NavCatalog
    {
        public static IReadOnlyList<NavGroup> Build() => new List<NavGroup>
        {
            new("Genel bakış", new[]
            {
                Item("Dashboard", "Kontrol Paneli", SymbolRegular.Board24, "Sistem durumu ve hızlı eylemler"),
                Item("Activity", "Etkinlik Merkezi", SymbolRegular.History24, "Yapılan değişiklikler ve geri alma"),
            }, isFirst: true),
            new("Temizlik", new[]
            {
                Item("Cleaner", "Temizleyici", SymbolRegular.Broom24, "Gereksiz dosyaları güvenle temizler"),
                Item("Storage", "Depolama", SymbolRegular.HardDrive20, "Büyük dosyalar, yinelenenler ve boş klasörler"),
                Item("Uninstaller", "Kaldırıcı", SymbolRegular.AppsList24, "Programları kalıntılarıyla kaldırır"),
            }),
            new("Performans", new[]
            {
                Item("Optimizer", "Süreçler", SymbolRegular.TopSpeed24, "Çalışan süreçler ve kaynak kullanımı"),
                Item("Startup", "Başlangıç", SymbolRegular.Rocket24, "Windows ile açılan programlar"),
                Item("ServiceManager", "Hizmetler ve sürücüler", SymbolRegular.DeveloperBoard24, "Windows hizmetleri ve sürücüler"),
                Item("GameMode", "Oyun Modu", SymbolRegular.Games24, "Oyun oturumu için sistem ayarları"),
            }),
            new("Güvenlik", new[]
            {
                Item("Analyzer", "Analizör", SymbolRegular.ShieldTask24, "Kalıcılık taraması, dosya analizi ve geçmiş"),
                Item("Sentinel", "Kurulum Nöbetçisi", SymbolRegular.ShieldCheckmark24, "Kurulumların yaptığı değişiklikler"),
                Item("Network", "Ağ İzleyici", SymbolRegular.NetworkCheck24, "Bağlantılar, dinleyen portlar, güvenlik duvarı"),
            }),
            new("Sistem", new[]
            {
                Item("SystemInfo", "Sistem Bilgisi", SymbolRegular.Info24, "Donanım ve Windows bilgileri"),
                Item("CrashAnalyzer", "Olaylar ve çökmeler", SymbolRegular.Warning24, "Olay günlüğü ve çökme analizi"),
                Item("Tweaker", "Windows Ayarları", SymbolRegular.Wrench24, "İnce ayarlar ve gizlilik"),
                Item("WindowsTools", "Windows Araçları", SymbolRegular.Toolbox24, "Klasik yönetim araçları ve konsollar"),
                Item("Store", "Mağaza", SymbolRegular.ArrowDownload24, "Yazılım ve çalışma zamanı paketleri"),
            }),
        };

        /// <summary>Kenar çubuğunun en altındaki öğe (grup dışı).</summary>
        public static NavItem BuildSettingsItem() =>
            Item("Settings", "Ayarlar", SymbolRegular.Settings24, "Uygulama ayarları");

        public static IEnumerable<NavItem> AllItems(IEnumerable<NavGroup> groups) => groups.SelectMany(g => g.Items);

        private static NavItem Item(string key, string label, SymbolRegular icon, string tooltip)
        {
            AppModuleRegistry.TryResolve(key, out var module);
            return new NavItem(key, module, label, icon, tooltip);
        }
    }
}
