using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakım.Core.Uninstall
{
    /// <summary>
    /// Windows Installer (MSI) GUID ve Sıkıştırılmış / Ters Çevrilmiş (Packed GUID / SFree)
    /// dönüşüm yardımcısı.
    ///
    /// Windows Installer, kayıt defterinde ürün ve bileşen kodlarını ("{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}")
    /// şu şekilde sıkıştırarak saklar:
    ///   - 1. grup (8 hane): tamamen ters çevrilir (5C6F8543 -> 3458F6C5)
    ///   - 2. grup (4 hane): tamamen ters çevrilir (7EA8 -> 8AE7)
    ///   - 3. grup (4 hane): tamamen ters çevrilir (40B9 -> 9B04)
    ///   - 4. grup (4 hane / 2 bayt): bayt bazında nibble ters çevrilir (B7 -> 7B, 8F -> F8 -> 7BF8)
    ///   - 5. grup (12 hane / 6 bayt): bayt bazında nibble ters çevrilir (04C78300DE7F -> 407C3800EDF7)
    /// Sonuç: 3458F6C58AE79B047BF8407C3800EDF7
    /// </summary>
    public static class MsiGuidHelper
    {
        private static readonly Regex GuidRegex = new(
            @"\{([0-9A-Fa-f]{8})-([0-9A-Fa-f]{4})-([0-9A-Fa-f]{4})-([0-9A-Fa-f]{4})-([0-9A-Fa-f]{12})\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Standart GUID'i Windows Installer'ın kullandığı 32 karakterlik Packed GUID biçimine dönüştürür.
        /// </summary>
        public static string? ToPackedGuid(string? guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;

            string clean = guid.Trim().Trim('{', '}').Replace("-", string.Empty);
            if (clean.Length != 32) return null;

            for (int i = 0; i < 32; i++)
            {
                char c = clean[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return null;
            }

            var sb = new StringBuilder(32);

            // 1. grup: 8 karakter ters
            for (int i = 7; i >= 0; i--) sb.Append(char.ToUpperInvariant(clean[i]));

            // 2. grup: 4 karakter ters
            for (int i = 11; i >= 8; i--) sb.Append(char.ToUpperInvariant(clean[i]));

            // 3. grup: 4 karakter ters
            for (int i = 15; i >= 12; i--) sb.Append(char.ToUpperInvariant(clean[i]));

            // 4. grup: 2 bayt, bayt içi ters (16..19)
            for (int i = 16; i < 20; i += 2)
            {
                sb.Append(char.ToUpperInvariant(clean[i + 1]));
                sb.Append(char.ToUpperInvariant(clean[i]));
            }

            // 5. grup: 6 bayt, bayt içi ters (20..31)
            for (int i = 20; i < 32; i += 2)
            {
                sb.Append(char.ToUpperInvariant(clean[i + 1]));
                sb.Append(char.ToUpperInvariant(clean[i]));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 32 karakterlik Packed GUID'i standart "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}" biçimine dönüştürür.
        /// </summary>
        public static string? FromPackedGuid(string? packedGuid)
        {
            if (string.IsNullOrWhiteSpace(packedGuid)) return null;
            string clean = packedGuid.Trim();
            if (clean.Length != 32) return null;

            var sb = new StringBuilder(38);
            sb.Append('{');

            // 1. grup
            for (int i = 7; i >= 0; i--) sb.Append(char.ToUpperInvariant(clean[i]));
            sb.Append('-');

            // 2. grup
            for (int i = 11; i >= 8; i--) sb.Append(char.ToUpperInvariant(clean[i]));
            sb.Append('-');

            // 3. grup
            for (int i = 15; i >= 12; i--) sb.Append(char.ToUpperInvariant(clean[i]));
            sb.Append('-');

            // 4. grup
            for (int i = 16; i < 20; i += 2)
            {
                sb.Append(char.ToUpperInvariant(clean[i + 1]));
                sb.Append(char.ToUpperInvariant(clean[i]));
            }
            sb.Append('-');

            // 5. grup
            for (int i = 20; i < 32; i += 2)
            {
                sb.Append(char.ToUpperInvariant(clean[i + 1]));
                sb.Append(char.ToUpperInvariant(clean[i]));
            }
            sb.Append('}');

            return sb.ToString();
        }

        /// <summary>
        /// UninstallString veya RegistryKeyPath içerisinden standart MSI ProductCode GUID'ini ayıklar.
        /// </summary>
        public static string? ExtractProductCode(string? uninstallString, string? registryKeyPath)
        {
            if (!string.IsNullOrWhiteSpace(uninstallString))
            {
                var match = GuidRegex.Match(uninstallString);
                if (match.Success) return match.Value.ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(registryKeyPath))
            {
                var match = GuidRegex.Match(registryKeyPath);
                if (match.Success) return match.Value.ToUpperInvariant();
            }

            return null;
        }
    }
}
