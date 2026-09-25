using System;
using Microsoft.Win32;

namespace Bakım.Helpers
{
    /// <summary>
    /// Yazıp geri okuyarak doğrulayan kayıt defteri işlemleri. Hata fırlatmaz; başarısızlığı
    /// hem dönüş değeriyle hem de etkin <see cref="WriteScope"/>'a bildirir.
    /// Silme işlemleri idempotenttir: zaten olmayan değer/anahtar başarı sayılır.
    /// </summary>
    public static class VerifiedRegistry
    {
        public static bool SetDword(RegistryKey root, string subKey, string valueName, int value) =>
            Set(root, subKey, valueName, value, RegistryValueKind.DWord,
                read => read is int i && i == value);

        public static bool SetString(RegistryKey root, string subKey, string valueName, string value) =>
            Set(root, subKey, valueName, value, RegistryValueKind.String,
                read => read is string s && s == value);

        public static bool SetExpandString(RegistryKey root, string subKey, string valueName, string value) =>
            Set(root, subKey, valueName, value, RegistryValueKind.ExpandString,
                read => read is string s && s == value,
                RegistryValueOptions.DoNotExpandEnvironmentNames);

        public static bool SetBinary(RegistryKey root, string subKey, string valueName, byte[] value) =>
            Set(root, subKey, valueName, value, RegistryValueKind.Binary,
                read => read is byte[] b && b.AsSpan().SequenceEqual(value));

        public static bool DeleteValue(RegistryKey root, string subKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, true);
                if (key == null) return true;
                key.DeleteValue(valueName, false);
                if (key.GetValue(valueName) == null) return true;
                return Fail(root, subKey, valueName, "değer silinmedi");
            }
            catch (Exception ex)
            {
                return Fail(root, subKey, valueName, Reason(ex));
            }
        }

        public static bool DeleteKeyTree(RegistryKey root, string subKey)
        {
            try
            {
                root.DeleteSubKeyTree(subKey, false);
                using var check = root.OpenSubKey(subKey, false);
                if (check == null) return true;
                return Fail(root, subKey, null, "anahtar silinmedi");
            }
            catch (Exception ex)
            {
                return Fail(root, subKey, null, Reason(ex));
            }
        }

        private static bool Set(RegistryKey root, string subKey, string valueName, object value,
            RegistryValueKind kind, Func<object?, bool> matches,
            RegistryValueOptions readOptions = RegistryValueOptions.None)
        {
            try
            {
                using var key = root.CreateSubKey(subKey, true);
                if (key == null) return Fail(root, subKey, valueName, "anahtar açılamadı");

                key.SetValue(valueName, value, kind);
                if (matches(key.GetValue(valueName, null, readOptions))) return true;
                return Fail(root, subKey, valueName, "yazılan değer geri okunamadı");
            }
            catch (Exception ex)
            {
                return Fail(root, subKey, valueName, Reason(ex));
            }
        }

        private static string Reason(Exception ex) => ex switch
        {
            UnauthorizedAccessException => "erişim reddedildi (yönetici izni gerekebilir)",
            System.Security.SecurityException => "erişim reddedildi (yönetici izni gerekebilir)",
            _ => ex.Message
        };

        private static bool Fail(RegistryKey root, string subKey, string? valueName, string reason)
        {
            string target = $@"{root.Name}\{subKey}";
            if (!string.IsNullOrEmpty(valueName)) target += $" → {valueName}";
            WriteScope.Report($"{target}: {reason}");
            return false;
        }
    }
}
