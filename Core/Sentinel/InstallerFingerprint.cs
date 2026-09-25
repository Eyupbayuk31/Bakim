using System;
using System.Text;

namespace Bakım.Core.Sentinel
{
    /// <summary>Kurulum dosyasını üreten çatı (NÖB 1.3).</summary>
    public enum InstallerFramework
    {
        Unknown,
        InnoSetup,
        Nsis,
        WixBurn,
        InstallShield,
        AdvancedInstaller,
        SevenZipSfx,
        Squirrel,
        Msi
    }

    /// <summary>
    /// Kurulum çatısını dosyanın baş ve son baytlarındaki izlerden tanır. Tam dosya okunmaz:
    /// çağıran en fazla birkaç MB baş ve 1 MB son bölüm verir (overlay çoğunlukla sondadır).
    /// </summary>
    public static class InstallerFingerprint
    {
        /// <summary>Taranacak baş bölüm (PE başlığı, bölüm tablosu ve kaynaklar).</summary>
        public const int HeadBytes = 4 * 1024 * 1024;
        /// <summary>Taranacak son bölüm (NSIS/7z overlay).</summary>
        public const int TailBytes = 1024 * 1024;

        private static readonly (InstallerFramework Framework, byte[] Marker)[] Markers =
        {
            (InstallerFramework.InnoSetup, Encoding.ASCII.GetBytes("Inno Setup Setup Data")),
            (InstallerFramework.InnoSetup, Encoding.ASCII.GetBytes("Inno Setup Messages")),
            (InstallerFramework.Nsis, Encoding.ASCII.GetBytes("NullsoftInst")),
            (InstallerFramework.Nsis, Encoding.ASCII.GetBytes("Nullsoft.NSIS")),
            (InstallerFramework.WixBurn, Encoding.ASCII.GetBytes(".wixburn")),
            (InstallerFramework.AdvancedInstaller, Encoding.ASCII.GetBytes("Advanced Installer")),
            (InstallerFramework.InstallShield, Encoding.ASCII.GetBytes("InstallShield")),
            (InstallerFramework.Squirrel, Encoding.ASCII.GetBytes("SquirrelTemp")),
            (InstallerFramework.Squirrel, Encoding.Unicode.GetBytes("SquirrelTemp")),
            (InstallerFramework.SevenZipSfx, new byte[] { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C }),
        };

        private static readonly byte[] OleCompound = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        public static InstallerFramework Detect(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, string? fileName = null)
        {
            if (fileName != null && fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && head.StartsWith(OleCompound))
                return InstallerFramework.Msi;

            // PE olmayan dosyada (MZ yok) çatı aranmaz.
            if (head.Length < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z') return InstallerFramework.Unknown;

            foreach (var (framework, marker) in Markers)
            {
                if (head.IndexOf(marker) >= 0 || (!tail.IsEmpty && tail.IndexOf(marker) >= 0))
                    return framework;
            }
            return InstallerFramework.Unknown;
        }

        /// <summary>Uzantısı ne olursa olsun dosya bir PE (MZ) mi? (".dat"/".jpg" adıyla bırakılan exe'ler)</summary>
        public static bool IsPortableExecutable(ReadOnlySpan<byte> head) =>
            head.Length >= 2 && head[0] == (byte)'M' && head[1] == (byte)'Z';

        public static string DisplayName(InstallerFramework framework) => framework switch
        {
            InstallerFramework.InnoSetup => "Inno Setup",
            InstallerFramework.Nsis => "NSIS",
            InstallerFramework.WixBurn => "WiX Burn",
            InstallerFramework.InstallShield => "InstallShield",
            InstallerFramework.AdvancedInstaller => "Advanced Installer",
            InstallerFramework.SevenZipSfx => "7-Zip SFX",
            InstallerFramework.Squirrel => "Squirrel",
            InstallerFramework.Msi => "Windows Installer (MSI)",
            _ => "Bilinmiyor"
        };
    }

    /// <summary>İndirilen dosyanın Mark-of-the-Web bilgisi (Zone.Identifier akışı).</summary>
    public sealed record MarkOfTheWeb(int ZoneId, string? HostUrl, string? ReferrerUrl)
    {
        /// <summary>3 = İnternet, 4 = Güvenilmeyen siteler.</summary>
        public bool IsFromInternet => ZoneId >= 3;

        /// <summary>"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=…" içeriğini ayrıştırır; ZoneId yoksa null.</summary>
        public static MarkOfTheWeb? Parse(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            int? zone = null;
            string? host = null, referrer = null;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key.Equals("ZoneId", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int z)) zone = z;
                else if (key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase) && value.Length > 0) host = value;
                else if (key.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase) && value.Length > 0) referrer = value;
            }
            return zone == null ? null : new MarkOfTheWeb(zone.Value, host, referrer);
        }

        /// <summary>Kullanıcıya gösterilecek site: "https://example.com/a/b" → "example.com".</summary>
        public string? SiteName
        {
            get
            {
                string? url = HostUrl ?? ReferrerUrl;
                if (url == null) return null;
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : null;
            }
        }
    }
}
