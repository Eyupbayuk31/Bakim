using System;
using System.Collections.Generic;
using Bakım.Core.Safety;

namespace Bakım.Core.Uninstall
{
    /// <summary>Kaldırmadan ÖNCE uygulamaya ait olduğu kanıtlanan iz türleri (KAL B1).</summary>
    public enum FootprintKind { Service, ScheduledTask, StartupEntry, Shortcut, FirewallRule, AppPath }

    /// <param name="Target">
    /// Kaldırma hedefi: hizmet adı, görev yolu ("\Klasör\Görev"), kayıt değeri ("HKCU\...\Run → Ad"),
    /// kısayol dosyası, güvenlik duvarı kuralının program yolu ya da App Paths anahtarı.
    /// </param>
    /// <param name="Evidence">Neden bu uygulamaya ait sayıldığı (kullanıcıya gösterilir).</param>
    public sealed record FootprintItem(FootprintKind Kind, string Target, string Display, string Evidence);

    public sealed class UninstallFootprint
    {
        public UninstallFootprint(string? installDir) => InstallDir = installDir;

        /// <summary>Doğrulanmış kurulum klasörü (normalize). Yoksa iz toplanmaz.</summary>
        public string? InstallDir { get; }
        public List<FootprintItem> Items { get; } = new();
        /// <summary>Zaman bütçesi aşıldı: bazı kaynaklar taranamadı.</summary>
        public bool IsPartial { get; set; }

        public static UninstallFootprint Empty { get; } = new(null);
    }

    public static class FootprintMatch
    {
        /// <summary>
        /// Kurulum klasörü iz toplamaya uygun mu? Sürücü kökü, "Program Files" gibi paylaşılan kökler
        /// ve en az iki seviye derinlikte olmayan klasörler reddedilir: aksi halde başka programların
        /// hizmet/görev/kısayolları "bu uygulamanın" sanılırdı.
        /// </summary>
        public static bool IsUsableInstallDir(string? normalizedDir)
        {
            if (normalizedDir == null) return false;
            if (WindowsPath.Depth(normalizedDir) < 2) return false;
            // Paylaşılan klasörler hangi derinlikte olursa olsun reddedilir (…\AppData\Local\Programs birçok uygulamayı tutar).
            if (SharedRoots.Contains(WindowsPath.LastSegment(normalizedDir))) return false;
            // Kullanıcı profili kökü (C:\Users\ad) bir kurulum klasörü değildir.
            string? parent = WindowsPath.Parent(normalizedDir);
            if (parent != null && string.Equals(WindowsPath.LastSegment(parent), "Users", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>
        /// Uninstall kaydında InstallLocation yoksa (NSIS/Inno'da sık) kurulum klasörü kaldırıcının
        /// klasöründen çıkarılır: "C:\Program Files\Foo\unins000.exe" → "C:\Program Files\Foo".
        /// Yalnızca verilen kurulum köklerinin (Program Files, AppData\Local\Programs) ALTINDAKİ
        /// klasörler kabul edilir; MSI ve kabuk komutları (rundll32 …) hiçbir zaman klasör vermez.
        /// InstallLocation doluysa olduğu gibi (normalize) döner, uygunluğu çağıran denetler.
        /// </summary>
        public static string? InferInstallDir(string? installLocation, string? uninstallString,
            IReadOnlyList<string> normalizedInstallRoots, Func<string, bool> fileExists, Func<string, string>? expandEnvironment = null)
        {
            if (!string.IsNullOrWhiteSpace(installLocation)) return WindowsPath.Normalize(installLocation.Trim().Trim('"'));

            var parsed = UninstallCommandParser.Parse(uninstallString, fileExists, expandEnvironment);
            if (parsed == null || parsed.IsMsi || parsed.IsShellCommand) return null;
            string? file = WindowsPath.Normalize(parsed.FileName);
            string? dir = file == null ? null : WindowsPath.Parent(file);
            if (dir == null) return null;

            string last = WindowsPath.LastSegment(dir);
            if (last.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) dir = WindowsPath.Parent(dir);
            if (dir == null) return null;

            foreach (string root in normalizedInstallRoots)
            {
                if (WindowsPath.IsStrictlyUnder(dir, root)) return dir;
            }
            return null;
        }

        private static readonly HashSet<string> SharedRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            "Program Files", "Program Files (x86)", "ProgramData", "Users", "Windows", "AppData", "Local", "Roaming", "Programs"
        };

        /// <summary>
        /// Komut satırı ya da yol kurulum klasörünün İÇİNE mi işaret ediyor? Komut satırı
        /// UninstallCommandParser ile çalıştırılabilir + argümana ayrılır; yalnızca çalıştırılabilir
        /// dosya dikkate alınır (argümandaki bir yol eşleşme sayılmaz).
        /// </summary>
        public static bool PointsInto(string? commandOrPath, string? normalizedInstallDir, Func<string, bool> fileExists,
            Func<string, string>? expandEnvironment = null)
        {
            if (string.IsNullOrWhiteSpace(commandOrPath) || normalizedInstallDir == null) return false;
            var parsed = UninstallCommandParser.Parse(commandOrPath, fileExists, expandEnvironment);
            string? file = parsed?.FileName;
            if (string.IsNullOrWhiteSpace(file) || parsed!.IsMsi) return false;
            string? normalized = WindowsPath.Normalize(file);
            return normalized != null && WindowsPath.IsStrictlyUnder(normalized, normalizedInstallDir);
        }
    }

    /// <summary>Kayıt defterindeki ham iz verisini okunur hale getirir.</summary>
    public static class FootprintText
    {
        /// <summary>
        /// Güvenlik duvarı kuralı verisi: "v2.30|Action=Allow|Dir=In|App=C:\x.exe|Name=Foo|".
        /// İlk parça sürümdür; anahtarlar büyük/küçük harf duyarsızdır, tekrarlanan anahtarda ilki kalır.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ParseFirewallRule(string? data)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(data)) return fields;
            foreach (string part in data.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part[..eq].Trim();
                if (!fields.ContainsKey(key)) fields[key] = part[(eq + 1)..].Trim();
            }
            return fields;
        }

        /// <summary>
        /// Hizmet ImagePath'i dosya yoluna yaklaştırır: "\??\C:\x.sys" → "C:\x.sys",
        /// "\SystemRoot\x" → "%SystemRoot%\x". Diğer biçimler olduğu gibi döner.
        /// </summary>
        public static string NormalizeServiceImagePath(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return string.Empty;
            string s = imagePath.Trim();
            if (s.StartsWith(@"\??\", StringComparison.Ordinal)) s = s[4..];
            else if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) s = "%SystemRoot%" + s[11..];
            return s;
        }
    }
}
