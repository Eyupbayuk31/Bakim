using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bakım.Services
{
    /// <summary>
    /// Windows DPAPI (Data Protection API) kullanarak kullanıcı kimlik bilgilerini 
    /// geçerli Windows kullanıcısına özel şifreleyen ve %AppData%/BakimApp/user_credentials.dat 
    /// konumunda güvenle saklayan servis.
    /// </summary>
    public static class CredentialStorageService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BakimApp",
            "user_credentials.dat");

        public static void SaveCredentials(string username, string password)
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                byte[] raw = Encoding.UTF8.GetBytes($"{username}|{password}");
                byte[] encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, encrypted);
            }
            catch { }
        }

        public static (string Username, string Password)? LoadCredentials()
        {
            if (!File.Exists(FilePath)) return null;

            try
            {
                byte[] encrypted = File.ReadAllBytes(FilePath);
                byte[] raw = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string text = Encoding.UTF8.GetString(raw);
                string[] parts = text.Split('|');
                if (parts.Length == 2)
                {
                    return (parts[0], parts[1]);
                }
            }
            catch { }

            return null;
        }

        public static void ClearCredentials()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch { }
        }
    }
}
