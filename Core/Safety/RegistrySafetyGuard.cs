using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Bakım.Core.Safety
{
    /// <summary>
    /// Bir kayıt defteri anahtarının silinmesine izin verilip verilmediğine karar verir.
    /// </summary>
    public static class RegistrySafetyGuard
    {
        /// <summary>Kendisi asla silinemeyen anahtarlar (içindeki alt anahtar/değerler silinebilir).</summary>
        private static readonly string[] ProtectedKeys =
        {
            "Software",
            @"Software\Classes",
            @"Software\Microsoft",
            @"Software\WOW6432Node",
            @"Software\WOW6432Node\Microsoft",
            @"Software\Policies",
            @"Software\Microsoft\Windows",
            @"Software\Microsoft\Windows\CurrentVersion",
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\Microsoft\Windows\CurrentVersion\App Paths",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer",
            @"Software\Microsoft\Windows NT",
            @"Software\Microsoft\Windows NT\CurrentVersion",
            @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon",
            @"Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
            @"Software\Classes\CLSID",
            @"Software\Classes\*",
            @"Software\Classes\Directory",
            @"Software\Classes\Folder",
            "SYSTEM",
            @"SYSTEM\CurrentControlSet",
            @"SYSTEM\CurrentControlSet\Services",
            @"SYSTEM\CurrentControlSet\Control",
        };

        /// <summary>Altında hiçbir şeyin silinemediği ağaçlar.</summary>
        private static readonly string[] ProtectedTrees =
        {
            @"SYSTEM\CurrentControlSet\Control",
            @"SYSTEM\Setup",
            @"SECURITY",
            @"SAM",
            @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon",
            @"Software\Microsoft\Cryptography",
            @"Software\Microsoft\SystemCertificates",
            @"Software\Policies\Microsoft\Windows Defender",
            @"Software\Microsoft\Windows Defender",
        };

        public static PathCheck CheckKeyDeletion(RegistryPath key, bool allowVendorRoot = false)
        {
            string display = key.ToDisplay();
            string sub = key.SubKey.Trim('\\');

            if (key.Hive is RegistryHive.Users or RegistryHive.CurrentConfig)
                return new PathCheck(PathVerdict.ProtectedTree, display, "Bu hive altında silme yapılmaz.");

            if (string.IsNullOrEmpty(sub))
                return new PathCheck(PathVerdict.ProtectedExact, display, "Hive kökü silinemez.");

            // HKCR, HKLM\Software\Classes ile HKCU\Software\Classes'ın birleşimidir.
            string normalized = key.Hive == RegistryHive.ClassesRoot ? @"Software\Classes\" + sub : sub;

            foreach (string tree in ProtectedTrees)
            {
                if (normalized.Equals(tree, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(tree + "\\", StringComparison.OrdinalIgnoreCase))
                    return new PathCheck(PathVerdict.ProtectedTree, display, $"Korunan sistem anahtarı: {tree}");
            }

            if (ProtectedKeys.Any(p => p.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                return new PathCheck(PathVerdict.ProtectedExact, display, "Bu anahtarın kendisi silinemez.");

            // SYSTEM altında yalnızca bir hizmetin kendi anahtarı silinebilir.
            if (normalized.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = normalized.Split('\\');
                bool isServiceKey = parts.Length >= 4 &&
                                    parts[1].Equals("CurrentControlSet", StringComparison.OrdinalIgnoreCase) &&
                                    parts[2].Equals("Services", StringComparison.OrdinalIgnoreCase);
                return isServiceKey
                    ? new PathCheck(PathVerdict.Allowed, display, string.Empty)
                    : new PathCheck(PathVerdict.ProtectedTree, display, "SYSTEM altında yalnızca hizmet anahtarları silinebilir.");
            }

            // Dosya uzantısı anahtarları (ör. Classes\.txt) paylaşılır: anahtarın kendisi
            // silinmez, yalnızca uygulamaya ait değerler (OpenWithProgids) silinebilir.
            if (IsExtensionKey(normalized))
                return new PathCheck(PathVerdict.ProtectedExact, display, "Dosya uzantısı anahtarı paylaşılır; yalnızca değerleri silinebilir.");

            // Software\<Yayıncı> (1 seviye) yalnızca açık izinle silinir: altında
            // aynı yayıncının başka ürünleri olabilir.
            if (IsVendorRoot(normalized) && !allowVendorRoot)
                return new PathCheck(PathVerdict.TooShallow, display, "Yayıncı kök anahtarı başka ürünleri de içerebilir.");

            return new PathCheck(PathVerdict.Allowed, display, string.Empty);
        }

        /// <summary>
        /// Bir anahtar altındaki tek bir değerin silinmesine izin verilir mi?
        /// Değer silme anahtar silmekten dar kapsamlıdır; yalnızca ağaç koruması uygulanır.
        /// </summary>
        public static PathCheck CheckValueDeletion(RegistryPath keyWithValue)
        {
            string display = keyWithValue.ToString();
            if (string.IsNullOrEmpty(keyWithValue.ValueName))
                return new PathCheck(PathVerdict.Invalid, display, "Değer adı belirtilmedi (varsayılan değer silinmez).");

            string sub = keyWithValue.SubKey.Trim('\\');
            string normalized = keyWithValue.Hive == RegistryHive.ClassesRoot ? @"Software\Classes\" + sub : sub;

            foreach (string tree in ProtectedTrees)
            {
                if (normalized.Equals(tree, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(tree + "\\", StringComparison.OrdinalIgnoreCase))
                    return new PathCheck(PathVerdict.ProtectedTree, display, $"Korunan sistem anahtarı: {tree}");
            }

            return new PathCheck(PathVerdict.Allowed, display, string.Empty);
        }

        private static bool IsExtensionKey(string sub)
        {
            string[] parts = sub.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 3 &&
                   parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase) &&
                   parts[1].Equals("Classes", StringComparison.OrdinalIgnoreCase) &&
                   parts[2].StartsWith('.');
        }

        private static bool IsVendorRoot(string sub)
        {
            string[] parts = sub.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase)) return true;
            if (parts.Length == 3 && parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase) &&
                parts[1].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Test ve tanılama için korunan anahtar listesi.</summary>
        public static IReadOnlyList<string> ProtectedKeyList => ProtectedKeys;
    }
}
