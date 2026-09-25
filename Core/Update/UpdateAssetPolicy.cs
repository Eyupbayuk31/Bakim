using System;
using System.Text.RegularExpressions;

namespace Bakım.Core.Update
{
    /// <summary>
    /// Güncelleyicinin indirip çalıştıracağı paketin kuralları (S-14).
    ///
    /// Ürün kararı (G-1): paket için imza/sertifika kontrolü YAPILMAZ. Bunun yerine:
    ///   • Yalnızca bu deponun sürüm eki: https://github.com/Eyupbayuk31/Bakim/releases/download/…
    ///   • Yalnızca kurulum paketi adı: "Bakim-v3.21.0-Setup.exe" biçimi (eskiden ilk .exe alınıyordu).
    ///   • Yönlendirme yalnızca GitHub'ın indirme sunucularına.
    ///   • İndirilen boyut, GitHub'ın bildirdiği boyutla aynı olmalı.
    /// </summary>
    public static class UpdateAssetPolicy
    {
        public const string Owner = "Eyupbayuk31";
        public const string Repository = "Bakim";

        private static readonly Regex InstallerName =
            new(@"^Bakim-v?\d+\.\d+\.\d+(\.\d+)?-Setup\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] RedirectHosts =
        {
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com"
        };

        public static bool IsInstallerAssetName(string? name) =>
            !string.IsNullOrEmpty(name) && InstallerName.IsMatch(name);

        /// <summary>Başlangıç adresi: HTTPS, github.com, bu deponun releases/download yolu ve kurulum paketi adı.</summary>
        public static bool IsOfficialDownloadUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
            if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;

            string prefix = $"/{Owner}/{Repository}/releases/download/";
            string path = uri.AbsolutePath;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            // releases/download/{etiket}/{dosya} — tam iki bölüm, ".." yok.
            string[] rest = path.Substring(prefix.Length).Split('/');
            if (rest.Length != 2 || rest[0].Length == 0 || rest[0] == ".." || rest[0] == ".") return false;
            return IsInstallerAssetName(Uri.UnescapeDataString(rest[1]));
        }

        /// <summary>Yönlendirme sonrası son adresin sunucusu izinli mi?</summary>
        public static bool IsAllowedFinalHost(Uri? finalUri)
        {
            if (finalUri == null || finalUri.Scheme != Uri.UriSchemeHttps) return false;
            foreach (var host in RedirectHosts)
                if (string.Equals(finalUri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>İndirilen boyut beklenenle uyumlu mu? Beklenen bilinmiyorsa (≤0) yalnızca boş olmaması aranır.</summary>
        public static bool IsSizeAcceptable(long downloadedBytes, long expectedBytes) =>
            downloadedBytes > 0 && (expectedBytes <= 0 || downloadedBytes == expectedBytes);
    }
}
