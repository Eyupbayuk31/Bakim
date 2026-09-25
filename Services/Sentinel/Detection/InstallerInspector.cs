using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Bakım.Core.Sentinel;
using Bakım.Helpers;
using Bakım.Models;

namespace Bakım.Services.Sentinel.Detection
{
    /// <summary>
    /// Kurulum dosyasının parmak izi (NÖB 1.3, 1.4): çatı, Mark-of-the-Web kaynağı, SHA-256 ve imza.
    /// Tespit sırasında yalnızca ucuz kısım (çatı + MOTW) okunur ve önbelleğe alınır; özet ve
    /// imza oturum başladıktan sonra arka planda hesaplanır.
    /// </summary>
    public static class InstallerInspector
    {
        private const long MaxHashBytes = 200L * 1024 * 1024;
        private static readonly ConcurrentDictionary<string, (long Length, DateTime Write, InstallerFramework Framework, MarkOfTheWeb? Motw)> QuickCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sınıflandırıcı için ek puan: kurulum çatısı izi +50, İnternet'ten indirilmiş +10.</summary>
        public static int ExtraScore(string? path)
        {
            var (framework, motw) = Quick(path);
            int score = 0;
            if (framework != InstallerFramework.Unknown) score += 50;
            if (motw?.IsFromInternet == true) score += 10;
            return score;
        }

        /// <summary>Çatı ve MOTW; (yol, boyut, yazma zamanı) ile önbelleklenir.</summary>
        public static (InstallerFramework Framework, MarkOfTheWeb? Motw) Quick(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return (InstallerFramework.Unknown, null);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return (InstallerFramework.Unknown, null);
                if (QuickCache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Write == info.LastWriteTimeUtc)
                    return (cached.Framework, cached.Motw);

                var framework = ReadFramework(path, info.Length);
                var motw = ReadMotw(path);
                if (QuickCache.Count > 512) QuickCache.Clear();
                QuickCache[path] = (info.Length, info.LastWriteTimeUtc, framework, motw);
                return (framework, motw);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppLog.Debug($"Kurulum dosyası okunamadı: {path} — {ex.Message}", nameof(InstallerInspector));
                return (InstallerFramework.Unknown, null);
            }
        }

        /// <summary>Tam inceleme (oturum başında arka planda): çatı, kaynak, SHA-256, imza.</summary>
        public static InstallerInfo Inspect(string? path)
        {
            var result = new InstallerInfo();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;

            var (framework, motw) = Quick(path);
            result.Framework = framework;
            if (motw != null)
            {
                result.HostUrl = motw.HostUrl;
                result.ReferrerUrl = motw.ReferrerUrl;
                result.SourceSite = motw.SiteName;
                result.FromInternet = motw.IsFromInternet;
            }

            try
            {
                var length = new FileInfo(path).Length;
                if (length <= MaxHashBytes)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    result.Sha256 = Convert.ToHexString(SHA256.HashData(stream));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug($"Kurulum dosyasının özeti alınamadı: {ex.Message}", nameof(InstallerInspector));
            }

            try
            {
                var signature = SignatureInspector.Inspect(path);
                result.Signed = signature.Status == SignatureStatus.Verified;
                result.Signer = string.IsNullOrWhiteSpace(signature.Signer) ? null : signature.Signer;
                result.SignatureText = signature.Describe();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                AppLog.Debug($"Kurulum dosyasının imzası denetlenemedi: {ex.Message}", nameof(InstallerInspector));
            }
            return result;
        }

        /// <summary>Dosya geçerli imzalı mı? Dosya yoksa ya da denetlenemezse null.</summary>
        public static bool? IsSigned(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                return SignatureInspector.Inspect(path).Status == SignatureStatus.Verified;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                return null;
            }
        }

        /// <summary>Uzantısı yürütülebilir olmayan bir dosya aslında PE mi? (ilk iki bayt "MZ")</summary>
        public static bool LooksLikeHiddenPe(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Span<byte> head = stackalloc byte[2];
                return stream.Read(head) == 2 && InstallerFingerprint.IsPortableExecutable(head);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static InstallerFramework ReadFramework(string path, long length)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int headLength = (int)Math.Min(length, InstallerFingerprint.HeadBytes);
            byte[] head = new byte[headLength];
            stream.ReadExactly(head);

            byte[] tail = Array.Empty<byte>();
            long tailStart = Math.Max(headLength, length - InstallerFingerprint.TailBytes);
            if (tailStart < length)
            {
                tail = new byte[length - tailStart];
                stream.Seek(tailStart, SeekOrigin.Begin);
                stream.ReadExactly(tail);
            }
            return InstallerFingerprint.Detect(head, tail, Path.GetFileName(path));
        }

        private static MarkOfTheWeb? ReadMotw(string path)
        {
            try
            {
                // NTFS alternatif veri akışı; indirilen dosyalarda tarayıcı yazar.
                string ads = path + ":Zone.Identifier";
                return File.Exists(ads) ? MarkOfTheWeb.Parse(File.ReadAllText(ads)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }
    }
}
