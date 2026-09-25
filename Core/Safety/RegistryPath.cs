using System;
using Microsoft.Win32;

namespace Bakım.Core.Safety
{
    /// <summary>
    /// Kayıt defteri yolunu (hive + görünüm + alt anahtar + isteğe bağlı değer adı)
    /// tek biçimde temsil eder. Kodda üç farklı yazım vardı ("LocalMachine\...",
    /// "HKLM\...", "HKEY_LOCAL_MACHINE\...") ve "HKCU" gibi kısaltmalar yanlışlıkla
    /// HKLM olarak çözülüyordu. Kalıcı biçim <see cref="ToDisplay"/> çıktısıdır.
    /// </summary>
    public readonly record struct RegistryPath(RegistryHive Hive, RegistryView View, string SubKey, string? ValueName = null)
    {
        private const string Suffix32 = " [32]";
        private const string Suffix64 = " [64]";

        public static bool TryParse(string? text, RegistryView defaultView, out RegistryPath result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            RegistryView view = defaultView;
            if (s.EndsWith(Suffix32, StringComparison.Ordinal)) { view = RegistryView.Registry32; s = s[..^Suffix32.Length]; }
            else if (s.EndsWith(Suffix64, StringComparison.Ordinal)) { view = RegistryView.Registry64; s = s[..^Suffix64.Length]; }

            int slash = s.IndexOf('\\');
            string hivePart = slash < 0 ? s : s[..slash];
            string sub = slash < 0 ? string.Empty : s[(slash + 1)..].Trim('\\');

            RegistryHive? hive = hivePart.ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" or "LOCALMACHINE" => RegistryHive.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" or "CURRENTUSER" => RegistryHive.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" or "CLASSESROOT" => RegistryHive.ClassesRoot,
                "HKU" or "HKEY_USERS" or "USERS" => RegistryHive.Users,
                "HKCC" or "HKEY_CURRENT_CONFIG" or "CURRENTCONFIG" => RegistryHive.CurrentConfig,
                _ => null
            };
            if (hive == null) return false;

            result = new RegistryPath(hive.Value, view, sub);
            return true;
        }

        public static RegistryPath Parse(string text, RegistryView defaultView = RegistryView.Registry64) =>
            TryParse(text, defaultView, out var r) ? r : throw new FormatException($"Geçersiz kayıt defteri yolu: {text}");

        public string HiveShortName => Hive switch
        {
            RegistryHive.LocalMachine => "HKLM",
            RegistryHive.CurrentUser => "HKCU",
            RegistryHive.ClassesRoot => "HKCR",
            RegistryHive.Users => "HKU",
            RegistryHive.CurrentConfig => "HKCC",
            _ => Hive.ToString()
        };

        public string HiveLongName => Hive switch
        {
            RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
            RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
            RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
            RegistryHive.Users => "HKEY_USERS",
            RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
            _ => Hive.ToString()
        };

        /// <summary>reg.exe ve .reg dosyası biçimi: "HKEY_LOCAL_MACHINE\Software\Foo".</summary>
        public string ToRegExe() => string.IsNullOrEmpty(SubKey) ? HiveLongName : $"{HiveLongName}\\{SubKey}";

        /// <summary>reg.exe için görünüm anahtarı: "/reg:32" ya da "/reg:64".</summary>
        public string RegExeViewSwitch => View == RegistryView.Registry32 ? "/reg:32" : "/reg:64";

        /// <summary>UI ve JSON'da kalıcı biçim: "HKLM\Software\Foo [32]".</summary>
        public string ToDisplay()
        {
            string baseText = string.IsNullOrEmpty(SubKey) ? HiveShortName : $"{HiveShortName}\\{SubKey}";
            return View == RegistryView.Registry32 ? baseText + Suffix32 : baseText;
        }

        public RegistryPath WithValue(string? valueName) => this with { ValueName = valueName };

        /// <summary>Alt anahtarın bölüm sayısı ("Software\Foo\Bar" → 3).</summary>
        public int SubKeyDepth => string.IsNullOrEmpty(SubKey) ? 0 : SubKey.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;

        public override string ToString() => ValueName == null ? ToDisplay() : $"{ToDisplay()} → {ValueName}";
    }
}
