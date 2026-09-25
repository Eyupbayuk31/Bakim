using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Bakım.Helpers
{
    /// <summary>
    /// Sağ tık "Bakım ile Kaldır" için MSI ve URL kısayol çözümlemesi (KAL S-6).
    ///   • MSI "advertised" kısayolları (Office, bazı kurumsal uygulamalar): hedef yolu yoktur,
    ///     ürün kodu MsiGetShortcutTarget ile okunur.
    ///   • .msi paketleri: ProductCode, paketin Property tablosundan okunur.
    ///   • Steam .url kısayolları: steam://rungameid/{id} → "Steam App {id}" Uninstall anahtarı.
    /// </summary>
    public static class MsiInterop
    {
        private const uint ErrorSuccess = 0;
        private const int GuidChars = 39;

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetShortcutTargetW(string szShortcutTarget, StringBuilder szProductCode, StringBuilder szFeatureId, StringBuilder szComponentCode);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiOpenDatabaseW(string szDatabasePath, IntPtr szPersist, out IntPtr phDatabase);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiDatabaseOpenViewW(IntPtr hDatabase, string szQuery, out IntPtr phView);

        [DllImport("msi.dll")]
        private static extern uint MsiViewExecute(IntPtr hView, IntPtr hRecord);

        [DllImport("msi.dll")]
        private static extern uint MsiViewFetch(IntPtr hView, out IntPtr phRecord);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiRecordGetStringW(IntPtr hRecord, uint iField, StringBuilder szValueBuf, ref uint pcchValueBuf);

        [DllImport("msi.dll")]
        private static extern uint MsiCloseHandle(IntPtr hAny);

        private static readonly Regex SteamRunGame = new(@"^steam://rungameid/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>MSI "advertised" kısayolun ürün kodu ({GUID}); değilse null.</summary>
        public static string? TryGetShortcutProductCode(string lnkPath)
        {
            try
            {
                var product = new StringBuilder(GuidChars);
                var feature = new StringBuilder(GuidChars);
                var component = new StringBuilder(GuidChars);
                if (MsiGetShortcutTargetW(lnkPath, product, feature, component) != ErrorSuccess) return null;
                string code = product.ToString();
                return code.StartsWith('{') ? code : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
            {
                return null;
            }
        }

        /// <summary>.msi paketinin ProductCode değeri; okunamazsa null.</summary>
        public static string? TryGetPackageProductCode(string msiPath)
        {
            IntPtr db = IntPtr.Zero, view = IntPtr.Zero, record = IntPtr.Zero;
            try
            {
                if (MsiOpenDatabaseW(msiPath, IntPtr.Zero /* MSIDBOPEN_READONLY */, out db) != ErrorSuccess) return null;
                if (MsiDatabaseOpenViewW(db, "SELECT `Value` FROM `Property` WHERE `Property`='ProductCode'", out view) != ErrorSuccess) return null;
                if (MsiViewExecute(view, IntPtr.Zero) != ErrorSuccess) return null;
                if (MsiViewFetch(view, out record) != ErrorSuccess) return null;

                uint size = GuidChars;
                var buffer = new StringBuilder((int)size);
                if (MsiRecordGetStringW(record, 1, buffer, ref size) != ErrorSuccess) return null;
                string code = buffer.ToString();
                return code.StartsWith('{') ? code : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
            {
                return null;
            }
            finally
            {
                if (record != IntPtr.Zero) MsiCloseHandle(record);
                if (view != IntPtr.Zero) MsiCloseHandle(view);
                if (db != IntPtr.Zero) MsiCloseHandle(db);
            }
        }

        /// <summary>.url kısayolunun URL= satırı.</summary>
        public static string? TryReadUrlShortcut(string urlPath)
        {
            try
            {
                foreach (var line in File.ReadLines(urlPath))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line[4..].Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        /// <summary>steam://rungameid/730 → "Steam App 730" (Steam'in Uninstall anahtar adı).</summary>
        public static string? SteamUninstallKeyName(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = SteamRunGame.Match(url);
            return m.Success ? $"Steam App {m.Groups[1].Value}" : null;
        }
    }
}
