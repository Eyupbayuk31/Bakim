using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public class UserAccount
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public interface IAuthService
    {
        Task<(bool Success, string Message)> AuthenticateAsync(string username, string password);
        Task<(bool Success, string Message)> RegisterAsync(string username, string password);
    }

    public class AuthService : IAuthService
    {
        private const string RemoteUsersUrl = "https://raw.githubusercontent.com/Eyupbayuk31/Bakim/main/users_db.json";
        private static readonly string LocalDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BakimApp",
            "users_db.json");

        private const string RootUsername = "Eyüp";
        private const string RootPassword = "1061";

        public static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                100000,
                HashAlgorithmName.SHA256,
                32);

            return (Convert.ToHexString(hashBytes), Convert.ToHexString(saltBytes));
        }

        public static bool VerifyPassword(string password, string hashHex, string saltHex)
        {
            try
            {
                byte[] saltBytes = Convert.FromHexString(saltHex);
                byte[] expectedHash = Convert.FromHexString(hashHex);
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    saltBytes,
                    100000,
                    HashAlgorithmName.SHA256,
                    32);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        public async Task<(bool Success, string Message)> AuthenticateAsync(string username, string password)
        {
            string cleanUser = (username ?? string.Empty).Trim();
            string cleanPass = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanUser) || string.IsNullOrEmpty(cleanPass))
            {
                return (false, "Lütfen kullanıcı adı ve şifrenizi girin.");
            }

            // 1. Root / Yönetici Doğrulaması (Çevrimdışı Kesintisiz Giriş)
            if (string.Equals(cleanUser, RootUsername, StringComparison.OrdinalIgnoreCase) && cleanPass == RootPassword)
            {
                return (true, "Giriş başarılı.");
            }

            // 2. Kullanıcı Veritabanı Yükleme (Önce Yerel + Uzak GitHub Senkronizasyonu)
            var users = await LoadUsersAsync();

            foreach (var user in users)
            {
                if (string.Equals(user.Username, cleanUser, StringComparison.OrdinalIgnoreCase))
                {
                    if (VerifyPassword(cleanPass, user.PasswordHash, user.Salt))
                    {
                        return (true, "Giriş başarılı.");
                    }
                    else
                    {
                        return (false, "Şifreniz hatalı, lütfen kontrol ediniz.");
                    }
                }
            }

            return (false, "Kullanıcı bulunamadı. Lütfen kayıt olun veya bilgilerinizi kontrol edin.");
        }

        public async Task<(bool Success, string Message)> RegisterAsync(string username, string password)
        {
            string cleanUser = (username ?? string.Empty).Trim();
            string cleanPass = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanUser) || cleanUser.Length < 3)
            {
                return (false, "Kullanıcı adı en az 3 karakter olmalıdır.");
            }

            if (string.IsNullOrEmpty(cleanPass) || cleanPass.Length < 4)
            {
                return (false, "Şifre en az 4 karakter olmalıdır.");
            }

            if (string.Equals(cleanUser, RootUsername, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Bu kullanıcı adı sistem yöneticisine aittir.");
            }

            var users = await LoadUsersAsync();

            if (users.Exists(u => string.Equals(u.Username, cleanUser, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "Bu kullanıcı adı zaten kayıtlı.");
            }

            // PBKDF2 (100.000 İterasyon) + 16 Byte Salt ile Hashleme
            var (hash, salt) = HashPassword(cleanPass);
            var newUser = new UserAccount
            {
                Username = cleanUser,
                PasswordHash = hash,
                Salt = salt,
                CreatedAt = DateTime.UtcNow
            };

            users.Add(newUser);
            await SaveLocalUsersAsync(users);

            return (true, "Kayıt işlemi başarıyla tamamlandı! Şimdi giriş yapabilirsiniz.");
        }

        private async Task<List<UserAccount>> LoadUsersAsync()
        {
            var userMap = new Dictionary<string, UserAccount>(StringComparer.OrdinalIgnoreCase);

            // 1. Yerel veritabanını oku
            try
            {
                if (File.Exists(LocalDbPath))
                {
                    string json = await File.ReadAllTextAsync(LocalDbPath);
                    var localList = JsonSerializer.Deserialize<List<UserAccount>>(json);
                    if (localList != null)
                    {
                        foreach (var u in localList) userMap[u.Username] = u;
                    }
                }
            }
            catch { }

            // 2. GitHub uzak users_db.json senkronizasyonu
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("User-Agent", "Bakim-AuthService");
                var response = await client.GetAsync(RemoteUsersUrl);

                if (response.IsSuccessStatusCode)
                {
                    string remoteJson = await response.Content.ReadAsStringAsync();
                    var remoteList = JsonSerializer.Deserialize<List<UserAccount>>(remoteJson);
                    if (remoteList != null)
                    {
                        foreach (var ru in remoteList)
                        {
                            if (!userMap.ContainsKey(ru.Username))
                            {
                                userMap[ru.Username] = ru;
                            }
                        }
                    }
                }
            }
            catch { }

            return new List<UserAccount>(userMap.Values);
        }

        private async Task SaveLocalUsersAsync(List<UserAccount> users)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LocalDbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(LocalDbPath, json);
            }
            catch { }
        }
    }
}
