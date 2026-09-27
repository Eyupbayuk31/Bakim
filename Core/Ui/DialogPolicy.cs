using System.Globalization;
using System.Text.RegularExpressions;

namespace Bakım.Core.Ui
{
    /// <summary>
    /// Onay pencerelerinde birincil düğmenin kırmızı (yıkıcı) olup olmayacağına metinden karar verir (ANL Faz 5).
    /// Eski <c>MessageBox</c> çağrıları bu bilgiyi taşımıyor; 148 çağrıyı tek tek işaretlemek yerine silme,
    /// kaldırma, sonlandırma, sıfırlama ve geri alınamaz işlemleri dilden tanınır.
    /// </summary>
    public static class DialogPolicy
    {
        private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        private static readonly Regex Destructive = new(
            @"(^|[^\p{L}])(sil|kaldır|sonlandır|boşalt|sıfırla|zorla|temizle)|geri alınamaz|kalıcı olarak",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsDestructive(string? title, string? message) =>
            Destructive.IsMatch(((title ?? string.Empty) + "\n" + (message ?? string.Empty)).ToLower(Tr));
    }
}
