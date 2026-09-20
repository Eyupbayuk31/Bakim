using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.Helpers
{
    /// <summary>Bir dosyanın imza durumunun tam tablosu.</summary>
    public sealed class SignatureReport
    {
        public SignatureStatus Status { get; init; } = SignatureStatus.Unsigned;

        /// <summary>İmzalayan kuruluşun ortak adı (CN).</summary>
        public string Signer { get; init; } = string.Empty;

        /// <summary>İmza gömülü değil, bir Windows kataloğundan (.cat) geliyorsa true.</summary>
        public bool IsCatalogSigned { get; init; }

        /// <summary>İmzayı sağlayan katalog dosyasının yolu (varsa).</summary>
        public string CatalogPath { get; init; } = string.Empty;

        /// <summary>Sertifikanın geçerlilik bitiş tarihi.</summary>
        public DateTime? CertificateExpiry { get; init; }

        /// <summary>Sertifika süresi dolmuş mu? (Zaman damgalı imzalarda dosya yine geçerlidir.)</summary>
        public bool IsCertificateExpired { get; init; }

        /// <summary>Zincir doğrulamasında saptanan sorunlar.</summary>
        public IReadOnlyList<string> ChainIssues { get; init; } = Array.Empty<string>();

        public bool IsTrusted => Status == SignatureStatus.Verified;

        /// <summary>Kullanıcıya gösterilecek özet.</summary>
        public string Describe()
        {
            if (Status == SignatureStatus.InvalidOrTampered)
                return string.IsNullOrWhiteSpace(Signer) ? "Bozulmuş / Geçersiz İmza" : $"{Signer} (GEÇERSİZ)";

            if (Status == SignatureStatus.Unsigned)
                return "İmzasız";

            string suffix = IsCatalogSigned ? " — Windows kataloğu" : string.Empty;
            string expiry = IsCertificateExpired ? " (sertifika süresi dolmuş)" : string.Empty;
            return $"Geçerli ({Signer}){suffix}{expiry}";
        }
    }

    /// <summary>
    /// Authenticode imza denetleyicisi — hem GÖMÜLÜ hem KATALOG imzalarını görür.
    ///
    /// Neden katalog desteği şart: Windows sistem ikililerinin büyük kısmı
    /// (svchost.exe, birçok sürücü ve DLL) gömülü imza taşımaz; imzaları
    /// %SystemRoot%\System32\CatRoot altındaki .cat dosyalarındadır.
    /// Yalnızca WTD_CHOICE_FILE ile bakan bir denetleyici bu dosyaları
    /// "İmzasız" sanır ve meşru Windows bileşenlerini tehdit gibi gösterir.
    /// </summary>
    public static class SignatureInspector
    {
        private static readonly Guid WinTrustActionGenericVerifyV2 =
            new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdChoiceCatalog = 2;
        private const uint WtdStateActionVerify = 1;
        private const uint WtdStateActionClose = 2;
        private const uint WtdSaferFlag = 0x100;
        private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

        private const uint TrustENosignature = 0x800B0100;
        private const uint TrustESubjectFormUnknown = 0x800B0003;
        private const uint TrustEProviderUnknown = 0x800B0001;

        #region P/Invoke

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustCatalogInfo
        {
            public uint cbStruct;
            public uint dwCatalogVersion;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszCatalogFilePath;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberTag;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszMemberFilePath;
            public IntPtr hMemberFile;
            public IntPtr pbCalculatedFileHash;
            public uint cbCalculatedFileHash;
            public IntPtr pcCatalogContext;
            public IntPtr hCatAdmin;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pUnion;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CatalogInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszCatalogFile;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WintrustData pWVTData);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATAdminAcquireContext2(out IntPtr phCatAdmin,
            IntPtr pgSubsystem, [MarshalAs(UnmanagedType.LPWStr)] string? pwszHashAlgorithm,
            IntPtr pStrongHashPolicy, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr hCatAdmin,
            IntPtr hFile, ref uint pcbHash, byte[]? pbHash, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr hCatAdmin,
            byte[] pbHash, uint cbHash, uint dwFlags, ref IntPtr phPrevCatInfo);

        [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptCATCatalogInfoFromContext(IntPtr hCatInfo,
            ref CatalogInfo psCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr hCatAdmin,
            IntPtr hCatInfo, uint dwFlags);

        [DllImport("wintrust.dll", SetLastError = true)]
        private static extern bool CryptCATAdminReleaseContext(IntPtr hCatAdmin, uint dwFlags);

        #endregion

        /// <summary>Dosyayı önce gömülü, bulunamazsa katalog imzası için denetler.</summary>
        public static SignatureReport Inspect(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new SignatureReport { Status = SignatureStatus.Unsigned };

            try
            {
                // 1) Gömülü imza
                uint embedded = VerifyEmbedded(filePath);

                if (embedded == 0)
                {
                    return BuildReport(filePath, SignatureStatus.Verified,
                        isCatalog: false, catalogPath: string.Empty);
                }

                // 2) Gömülü imza yoksa katalog denetimi.
                //    Yalnızca "imza yok" ailesindeki kodlarda denenir; bozulmuş
                //    imza (başka bir hata kodu) katalogla kurtarılmaya çalışılmaz.
                if (embedded is TrustENosignature or TrustESubjectFormUnknown or TrustEProviderUnknown)
                {
                    var (catalogOk, catalogPath) = VerifyViaCatalog(filePath);
                    if (catalogOk)
                    {
                        return BuildReport(filePath, SignatureStatus.Verified,
                            isCatalog: true, catalogPath: catalogPath);
                    }

                    return new SignatureReport { Status = SignatureStatus.Unsigned };
                }

                // 3) İmza var ama doğrulanamadı → değiştirilmiş / güvenilmeyen
                return BuildReport(filePath, SignatureStatus.InvalidOrTampered,
                    isCatalog: false, catalogPath: string.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Warning($"İmza denetimi başarısız: {filePath}", ex, nameof(SignatureInspector));
                return new SignatureReport { Status = SignatureStatus.Unsigned };
            }
        }

        private static uint VerifyEmbedded(string filePath)
        {
            IntPtr pFile = IntPtr.Zero;
            try
            {
                var fileInfo = new WintrustFileInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                };

                pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, pFile, false);

                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceFile,
                    pUnion = pFile,
                    dwStateAction = WtdStateActionVerify,
                    dwProvFlags = WtdSaferFlag | WtdCacheOnlyUrlRetrieval
                };

                uint result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                data.dwStateAction = WtdStateActionClose;
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                return result;
            }
            finally
            {
                if (pFile != IntPtr.Zero)
                {
                    try
                    {
                        Marshal.DestroyStructure<WintrustFileInfo>(pFile);
                        Marshal.FreeCoTaskMem(pFile);
                    }
                    catch { /* serbest bırakma hatası yutulur */ }
                }
            }
        }

        /// <summary>Dosyanın hash'ini içeren Windows kataloğunu bulup doğrular.</summary>
        private static (bool Ok, string CatalogPath) VerifyViaCatalog(string filePath)
        {
            IntPtr hCatAdmin = IntPtr.Zero;
            IntPtr hCatInfo = IntPtr.Zero;
            IntPtr prevCat = IntPtr.Zero;
            FileStream? stream = null;

            try
            {
                // SHA-256 katalog bağlamı (Windows 8+). Başarısız olursa katalog yok sayılır.
                if (!CryptCATAdminAcquireContext2(out hCatAdmin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0))
                    return (false, string.Empty);

                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                IntPtr handle = stream.SafeFileHandle.DangerousGetHandle();

                // Önce gerekli hash uzunluğunu öğren
                uint hashSize = 0;
                CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, handle, ref hashSize, null, 0);
                if (hashSize == 0) return (false, string.Empty);

                var hash = new byte[hashSize];
                if (!CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, handle, ref hashSize, hash, 0))
                    return (false, string.Empty);

                hCatInfo = CryptCATAdminEnumCatalogFromHash(hCatAdmin, hash, hashSize, 0, ref prevCat);
                if (hCatInfo == IntPtr.Zero)
                    return (false, string.Empty);

                var catInfo = new CatalogInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<CatalogInfo>(),
                    wszCatalogFile = string.Empty
                };

                if (!CryptCATCatalogInfoFromContext(hCatInfo, ref catInfo, 0))
                    return (false, string.Empty);

                string catalogPath = catInfo.wszCatalogFile ?? string.Empty;
                if (string.IsNullOrWhiteSpace(catalogPath))
                    return (false, string.Empty);

                // Katalog üyeliği doğrulaması: üye etiketi hash'in hex karşılığıdır
                string memberTag = Convert.ToHexString(hash);
                bool verified = VerifyCatalogMembership(filePath, catalogPath, memberTag, hash, hCatAdmin);

                return (verified, verified ? catalogPath : string.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Debug($"Katalog imza denetimi atlandı: {ex.Message}", nameof(SignatureInspector));
                return (false, string.Empty);
            }
            finally
            {
                stream?.Dispose();

                if (hCatInfo != IntPtr.Zero && hCatAdmin != IntPtr.Zero)
                {
                    try { CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0); } catch { }
                }
                if (hCatAdmin != IntPtr.Zero)
                {
                    try { CryptCATAdminReleaseContext(hCatAdmin, 0); } catch { }
                }
            }
        }

        private static bool VerifyCatalogMembership(string filePath, string catalogPath,
            string memberTag, byte[] hash, IntPtr hCatAdmin)
        {
            IntPtr pCatalog = IntPtr.Zero;
            IntPtr pHash = IntPtr.Zero;

            try
            {
                pHash = Marshal.AllocHGlobal(hash.Length);
                Marshal.Copy(hash, 0, pHash, hash.Length);

                var catalogInfo = new WintrustCatalogInfo
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustCatalogInfo>(),
                    dwCatalogVersion = 0,
                    pcwszCatalogFilePath = catalogPath,
                    pcwszMemberTag = memberTag,
                    pcwszMemberFilePath = filePath,
                    hMemberFile = IntPtr.Zero,
                    pbCalculatedFileHash = pHash,
                    cbCalculatedFileHash = (uint)hash.Length,
                    pcCatalogContext = IntPtr.Zero,
                    hCatAdmin = hCatAdmin
                };

                pCatalog = Marshal.AllocCoTaskMem(Marshal.SizeOf<WintrustCatalogInfo>());
                Marshal.StructureToPtr(catalogInfo, pCatalog, false);

                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceCatalog,
                    pUnion = pCatalog,
                    dwStateAction = WtdStateActionVerify,
                    dwProvFlags = WtdSaferFlag | WtdCacheOnlyUrlRetrieval
                };

                uint result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                data.dwStateAction = WtdStateActionClose;
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                return result == 0;
            }
            finally
            {
                if (pCatalog != IntPtr.Zero)
                {
                    try
                    {
                        Marshal.DestroyStructure<WintrustCatalogInfo>(pCatalog);
                        Marshal.FreeCoTaskMem(pCatalog);
                    }
                    catch { }
                }
                if (pHash != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(pHash); } catch { }
                }
            }
        }

        /// <summary>İmzalayan bilgisini ve sertifika sağlığını toplar.</summary>
        private static SignatureReport BuildReport(string filePath, SignatureStatus status,
            bool isCatalog, string catalogPath)
        {
            string signer = string.Empty;
            DateTime? expiry = null;
            bool expired = false;
            var issues = new List<string>();

            // Katalog imzalı dosyada sertifika, dosyanın kendisinde değil katalogdadır.
            string certSource = isCatalog && !string.IsNullOrWhiteSpace(catalogPath)
                ? catalogPath
                : filePath;

            try
            {
#pragma warning disable SYSLIB0057
                var raw = X509Certificate.CreateFromSignedFile(certSource);
#pragma warning restore SYSLIB0057

                if (raw != null)
                {
                    using var cert = new X509Certificate2(raw);
                    signer = ExtractCommonName(cert.Subject);
                    expiry = cert.NotAfter;
                    expired = DateTime.Now > cert.NotAfter;

                    if (expired)
                    {
                        // Zaman damgalı imzalarda bu ölümcül değildir: dosya,
                        // sertifika geçerliyken imzalanmış olabilir.
                        issues.Add("Sertifika geçerlilik süresi dolmuş");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"Sertifika okunamadı ({certSource}): {ex.Message}", nameof(SignatureInspector));
            }

            return new SignatureReport
            {
                Status = status,
                Signer = signer,
                IsCatalogSigned = isCatalog,
                CatalogPath = catalogPath,
                CertificateExpiry = expiry,
                IsCertificateExpired = expired,
                ChainIssues = issues
            };
        }

        private static string ExtractCommonName(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return string.Empty;

            int cn = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
            if (cn < 0) return subject;

            int start = cn + 3;
            // Tırnaklı CN değerleri virgül içerebilir
            if (start < subject.Length && subject[start] == '"')
            {
                int close = subject.IndexOf('"', start + 1);
                return close > start ? subject.Substring(start + 1, close - start - 1) : subject[start..];
            }

            int comma = subject.IndexOf(',', start);
            return comma > 0 ? subject[start..comma].Trim() : subject[start..].Trim();
        }
    }
}
