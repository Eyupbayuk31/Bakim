namespace Bakım.Core.Text
{
    /// <summary>
    /// Bellek işlemlerinin sonucunu kullanıcıya dürüstçe anlatır.
    ///
    /// Ölçülen değer, işlem öncesi ve sonrası "kullanılabilir fiziksel bellek"
    /// farkıdır: anlıktır ve Windows sayfaları geri yükledikçe azalır. 0 ya da
    /// negatifse uydurma bir sayı yerine bunu açıkça söyleriz.
    /// </summary>
    public static class MemoryResultText
    {
        public const long SignificantThresholdBytes = 16L * 1024 * 1024;

        public static bool IsSignificant(long freedBytes) => freedBytes >= SignificantThresholdBytes;

        /// <summary>Kısa sonuç: "≈420 MB anlık olarak boşaldı" ya da "önemli bir değişiklik olmadı".</summary>
        public static string Describe(long freedBytes) => IsSignificant(freedBytes)
            ? $"≈{ByteFormatter.Format(freedBytes)} bellek anlık olarak boşaldı."
            : "Kullanılabilir bellekte önemli bir değişiklik olmadı; Windows belleği zaten yönetiyor.";

        /// <summary>Rozet metni: "+420 MB" ya da "—".</summary>
        public static string Badge(long freedBytes) => IsSignificant(freedBytes) ? "+" + ByteFormatter.Format(freedBytes) : "—";
    }
}
