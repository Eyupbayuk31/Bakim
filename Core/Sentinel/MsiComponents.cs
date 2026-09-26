using System;

namespace Bakım.Core.Sentinel
{
    /// <summary>Bir MSI bileşeninin anahtar yolu: dosya, klasör ya da kayıt defteri anahtarı.</summary>
    public readonly record struct MsiKeyPath(MsiKeyPathKind Kind, string Path);

    public enum MsiKeyPathKind
    {
        File,
        Folder,
        Registry
    }

    /// <summary>
    /// Windows Installer'ın bileşen kayıtlarını okumak için saf yardımcılar (NÖB v3 D3).
    /// Kurulu her bileşen <c>Installer\UserData\&lt;SID&gt;\Components\&lt;bileşen&gt;</c> altında,
    /// değer adı sıkıştırılmış ürün kodu, değeri anahtar yolu olacak şekilde kayıtlıdır. Böylece bir
    /// MSI ürününün yazdığı dosyalar, kurulum anında izlenmemiş olsa bile kesin olarak bulunur.
    /// </summary>
    public static class MsiComponents
    {
        /// <summary>
        /// Ürün kodunu Windows Installer'ın kayıt defterinde kullandığı sıkıştırılmış biçime çevirir:
        /// ilk üç grup ters çevrilir, son 16 karakterde her bayt çiftinin yarıları yer değiştirir.
        /// "{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}" → "3458F6C58AE79B047BF8407C3800EDF7".
        /// </summary>
        public static string? PackGuid(string? productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode) || !Guid.TryParse(productCode, out var guid)) return null;
            string hex = guid.ToString("N").ToUpperInvariant();
            var chars = new char[32];
            Reverse(hex, 0, 8, chars);
            Reverse(hex, 8, 4, chars);
            Reverse(hex, 12, 4, chars);
            for (int i = 16; i < 32; i += 2)
            {
                chars[i] = hex[i + 1];
                chars[i + 1] = hex[i];
            }
            return new string(chars);
        }

        private static void Reverse(string source, int start, int length, char[] target)
        {
            for (int i = 0; i < length; i++) target[start + i] = source[start + length - 1 - i];
        }

        /// <summary>
        /// Bileşen değerini çözümler. "C:\…\a.dll" dosya, "\" ile biten yol klasör,
        /// "NN:\…" kayıt defteridir (00 HKCR, 01 HKCU, 02 HKLM, 03 HKU; 2x aynı kovanın 64 bit hâli).
        /// "C?\…" gibi yerel olarak kurulmamış (kaynaktan çalışan) bileşenler ve boş değerler atlanır.
        /// </summary>
        public static bool TryParseKeyPath(string? raw, out MsiKeyPath keyPath)
        {
            keyPath = default;
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 4) return false;
            string value = raw.Trim();

            if (char.IsDigit(value[0]) && char.IsDigit(value[1]) && value[2] == ':')
            {
                string? hive = (value[1]) switch
                {
                    '0' => "HKCR",
                    '1' => "HKCU",
                    '2' => "HKLM",
                    '3' => "HKU",
                    _ => null
                };
                if (hive == null || (value[0] != '0' && value[0] != '2')) return false;
                string subKey = value[3..].Trim('\\');
                if (subKey.Length == 0) return false;
                keyPath = new MsiKeyPath(MsiKeyPathKind.Registry, $@"{hive}\{subKey}");
                return true;
            }

            if (!char.IsLetter(value[0]) || value[1] != ':' || value[2] != '\\') return false;
            keyPath = value.EndsWith('\\')
                ? new MsiKeyPath(MsiKeyPathKind.Folder, value.TrimEnd('\\'))
                : new MsiKeyPath(MsiKeyPathKind.File, value);
            return true;
        }
    }
}
