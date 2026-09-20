using System;
using System.Security.Cryptography;
using System.Text;
using Bakım.Services;

namespace Bakım.Helpers
{
    /// <summary>
    /// Windows DPAPI (CurrentUser kapsamı) üzerine ince sarmalayıcı.
    /// Şifrelenen veri yalnızca aynı Windows kullanıcı hesabında çözülebilir;
    /// dosya başka bir makineye veya kullanıcıya kopyalansa bile okunamaz.
    /// </summary>
    public static class DataProtection
    {
        /// <summary>Uygulamaya özel ek entropi: başka bir uygulamanın çözmesini zorlaştırır.</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Bakim.App.v3.DataProtection");

        /// <summary>Düz metni şifreleyip Base64 döndürür. Boş girdi boş çıktı verir.</summary>
        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                AppLog.Error("Veri şifrelenemedi (DPAPI).", ex, nameof(DataProtection));
                return string.Empty;
            }
        }

        /// <summary>Base64 şifreli metni çözer. Çözülemezse boş dize döndürür.</summary>
        public static string Unprotect(string? protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return string.Empty;

            try
            {
                byte[] encrypted = Convert.FromBase64String(protectedBase64);
                byte[] raw = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                // Farklı kullanıcı profili veya bozuk veri: sır kaybı kabul edilir,
                // kullanıcı anahtarı yeniden girer.
                AppLog.Warning("Şifreli veri çözülemedi (DPAPI).", ex, nameof(DataProtection));
                return string.Empty;
            }
        }
    }
}
