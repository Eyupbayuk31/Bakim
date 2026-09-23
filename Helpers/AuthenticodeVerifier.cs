using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Bakım.Models;
using Bakım.Services;

namespace Bakım.Helpers
{
    /// <summary>Bir dosyanın Authenticode imza durumu ve yayıncı bilgisi.</summary>
    public sealed class SignatureVerdict
    {
        public SignatureStatus Status { get; init; } = SignatureStatus.Unsigned;
        public string Signer { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;

        public bool IsTrusted => Status == SignatureStatus.Verified;

        public string Describe() => Status switch
        {
            SignatureStatus.Verified => $"Geçerli dijital imza — yayıncı: {Signer}",
            SignatureStatus.InvalidOrTampered => $"GEÇERSİZ veya DEĞİŞTİRİLMİŞ imza ({Signer})",
            _ => "Dijital imza yok"
        };
    }

    /// <summary>
    /// WinVerifyTrust üzerinden Authenticode doğrulaması.
    /// Güncelleme paketini çalıştırmadan önce kaynağını denetlemek için kullanılır.
    /// </summary>
    public static class AuthenticodeVerifier
    {
        private static readonly Guid WinTrustActionGenericVerifyV2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        private const uint WtdUiNone = 2;
        private const uint WtdRevokeNone = 0;
        private const uint WtdChoiceFile = 1;
        private const uint WtdStateActionVerify = 1;
        private const uint TrustEProvidedUnknown = 0x800B0001;
        private const uint TrustENosignature = 0x800B0100;
        private const uint CertEUntrustedRoot = 0x800B0109;
        private const uint CertEChainUntrustedRoot = 0x800B010A;
        private const uint CertERevocationFailure = 0x800B010E;
        private const string ExpectedSignerThumbprint = "06B7CFC6453D55E1C7FCC8053D1928DA8F29AF46";

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
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
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WintrustData pWVTData);

        public static SignatureVerdict Verify(string filePath)
        {
            string sha256 = ComputeSha256(filePath);

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new SignatureVerdict { Status = SignatureStatus.Unsigned, Sha256 = sha256 };

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
                    pFile = pFile,
                    dwStateAction = WtdStateActionVerify
                };

                uint result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                // Durum tanıtıcısını her koşulda serbest bırak
                data.dwStateAction = WtdStateActionClose;
                WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);

                bool isUntrustedRootOrRevocationOffline = result == CertEUntrustedRoot ||
                                                         result == CertEChainUntrustedRoot ||
                                                         result == CertERevocationFailure;

                if (result == 0 || (isUntrustedRootOrRevocationOffline && IsProjectCertificate(filePath)))
                {
                    return new SignatureVerdict
                    {
                        Status = SignatureStatus.Verified,
                        Signer = ReadSigner(filePath),
                        Sha256 = sha256
                    };
                }

                if (result == TrustENosignature || result == TrustEProvidedUnknown)
                {
                    return new SignatureVerdict { Status = SignatureStatus.Unsigned, Sha256 = sha256 };
                }

                return new SignatureVerdict
                {
                    Status = SignatureStatus.InvalidOrTampered,
                    Signer = ReadSigner(filePath),
                    Sha256 = sha256
                };
            }
            catch (Exception ex)
            {
                AppLog.Warning($"İmza doğrulaması yapılamadı: {filePath}", ex, nameof(AuthenticodeVerifier));
                return new SignatureVerdict { Status = SignatureStatus.Unsigned, Sha256 = sha256 };
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

        private static string ReadSigner(string filePath)
        {
            try
            {
                // Authenticode imzasını PE dosyasından çıkarmanın desteklenen tek yolu budur;
                // X509CertificateLoader imzalı yürütülebilir dosyaları okuyamaz.
#pragma warning disable SYSLIB0057
                var cert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057

                if (cert == null) return string.Empty;

                string subject = cert.Subject;
                int cnIndex = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
                if (cnIndex < 0) return subject;

                int commaIndex = subject.IndexOf(',', cnIndex);
                return commaIndex > 0
                    ? subject.Substring(cnIndex + 3, commaIndex - (cnIndex + 3))
                    : subject.Substring(cnIndex + 3);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsProjectCertificate(string filePath)
        {
            try
            {
#pragma warning disable SYSLIB0057
                using var cert = new X509Certificate2(filePath);
#pragma warning restore SYSLIB0057
                if (cert == null) return false;

                if (string.Equals(cert.Thumbprint, ExpectedSignerThumbprint, StringComparison.OrdinalIgnoreCase))
                    return true;

                string subject = cert.Subject;
                if (subject.Contains("CN=Bakım", StringComparison.OrdinalIgnoreCase) ||
                    subject.Contains("Bakım Open Source Project", StringComparison.OrdinalIgnoreCase) ||
                    subject.Contains("Eyupbayuk31", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static string ComputeSha256(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return string.Empty;

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                AppLog.Warning($"SHA-256 hesaplanamadı: {filePath}", ex, nameof(AuthenticodeVerifier));
                return string.Empty;
            }
        }
    }
}
