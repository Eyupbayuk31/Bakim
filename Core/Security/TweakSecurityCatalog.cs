using System;
using System.Collections.Generic;

namespace Bakım.Core.Security
{
    public enum SecurityImpact
    {
        None,
        /// <summary>Korumayı bir miktar zayıflatır; bilinçli kullanılmalı.</summary>
        Low,
        /// <summary>Bir güvenlik katmanını kapatır; ayrı onay ister, asla "önerilen" değildir.</summary>
        High
    }

    public readonly record struct SecurityNote(SecurityImpact Impact, string Warning);

    /// <summary>
    /// Güvenliği azaltan ince ayarların tek listesi (S-16). Eskiden SmartScreen'i ya da
    /// Windows Update'i kapatmak, masaüstü ikonunu gizlemekle aynı görünüyordu.
    /// </summary>
    public static class TweakSecurityCatalog
    {
        private static readonly Dictionary<string, SecurityNote> Notes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["disable_smartscreen"] = new(SecurityImpact.High,
                "SmartScreen, bilinmeyen ve kötü amaçlı programları çalıştırmadan önce uyarır. Kapatıldığında indirilen zararlı bir dosya uyarısız açılır."),
            ["disable_zone_identifier"] = new(SecurityImpact.High,
                "İndirilen dosyalar \"internetten geldi\" işaretini (MOTW) kaybeder: SmartScreen ve Office Korumalı Görünüm devre dışı kalır, Kurulum Nöbetçisi dosyanın indirildiği siteyi gösteremez."),
            ["disable_windows_update"] = new(SecurityImpact.High,
                "Güvenlik yamaları gelmez; bilinen açıklar kapanmadan kalır. Yalnızca kısa süreli ve bilinçli kullanın."),
            ["disable_mrt_install"] = new(SecurityImpact.High,
                "Windows Kötü Amaçlı Yazılım Temizleme Aracı'nın aylık güncellemeleri gelmez."),
            ["edge_disable_updates"] = new(SecurityImpact.High,
                "Edge güvenlik güncellemeleri gelmez; tarayıcı açıkları kapanmaz."),
            ["disable_driver_updates"] = new(SecurityImpact.Low,
                "Sürücü güvenlik düzeltmeleri Windows Update ile gelmez; sürücüleri üreticiden güncel tutmanız gerekir."),
            ["disable_auto_maintenance"] = new(SecurityImpact.Low,
                "Otomatik bakım, Defender taramaları ve güncelleme temizliği gibi görevleri de çalıştırır."),
            ["enable_autologon_checkbox"] = new(SecurityImpact.Low,
                "Parolasız otomatik oturum açmayı kolaylaştırır; bilgisayara fiziksel erişen herkes oturum açabilir."),
            ["context_take_ownership"] = new(SecurityImpact.Low,
                "Sistem dosyalarının sahipliğini tek tıkla almak, yanlışlıkla Windows bileşenlerinin izinlerini bozabilir."),
        };

        public static SecurityNote For(string? tweakId) =>
            tweakId != null && Notes.TryGetValue(tweakId, out var note) ? note : new SecurityNote(SecurityImpact.None, string.Empty);

        public static IReadOnlyCollection<string> AllIds => Notes.Keys;
    }
}
