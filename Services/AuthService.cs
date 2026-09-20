using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// PBKDF2 tur sayısı. VARSAYILAN 0 OLMALIDIR: v3.0 ve öncesinde yazılan kayıtlarda
        /// bu alan yoktur ve JSON çözümlemesi alanı varsayılanında bırakır. Varsayılan
        /// güncel tur sayısı olsaydı, eski kayıtlar yanlış turla doğrulanır ve
        /// kullanıcı kendi hesabından kilitlenirdi. 0 = eski kayıt (100.000 tur).
        /// </summary>
        public int Iterations { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public interface IAuthService
    {
        Task<(bool Success, string Message)> AuthenticateAsync(string username, string password);
        Task<(bool Success, string Message)> RegisterAsync(string username, string password);

        /// <summary>Cihazda kayıtlı hesap var mı? Yoksa giriş ekranı ilk kurulum moduna geçer.</summary>
        Task<bool> HasAnyAccountAsync();

        /// <summary>
        /// Yerel hesap veritabanını siler ve uygulamayı ilk kurulum durumuna döndürür.
        /// Kullanıcı kendi cihazındaki yerel kilidi unuttuğunda kilitlenmeyi önler.
        /// </summary>
        Task<(bool Success, string Message)> ResetAccountsAsync();
    }

    /// <summary>
    /// Yerel hesap doğrulaması.
    ///
    /// Güvenlik notu: Bu giriş ekranı bir güvenlik sınırı değil, yerel bir kilittir —
    /// veriler zaten oturum açmış Windows kullanıcısının erişimindedir. Bu yüzden
    /// tasarım hedefi "aşılamaz olmak" değil, dürüst olmaktır:
    ///   • Kaynağa gömülü yönetici parolası yoktur (v3.1'de kaldırıldı).
    ///   • Hesap veritabanı yalnızca yereldir; uzak sunucudan hesap çekilmez.
    ///   • Parolalar PBKDF2-SHA256 / 210.000 tur ve hesaba özel tuz ile saklanır.
    ///   • Kaba kuvvet denemeleri artan bekleme süresiyle yavaşlatılır.
    /// </summary>
    public class AuthService : IAuthService
    {
        /// <summary>OWASP 2023 PBKDF2-SHA256 önerisi.</summary>
        public const int CurrentIterations = 210_000;

        /// <summary>v3.0 ve öncesinde kullanılan tur sayısı; eski kayıtlar bununla doğrulanır.</summary>
        public const int LegacyIterations = 100_000;

        private const int MinUsernameLength = 3;
        private const int MinPasswordLength = 6;
        private const int MaxFailuresBeforeLockout = 5;

        private static readonly string LocalDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BakimApp",
            "users_db.json");

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private readonly ILogService _log;
        private readonly object _throttleGate = new();
        private readonly Dictionary<string, FailureRecord> _failures = new(StringComparer.OrdinalIgnoreCase);

        private sealed class FailureRecord
        {
            public int Count;
            public DateTime LockedUntilUtc = DateTime.MinValue;
        }

        public AuthService() : this(AppLog.Current) { }

        public AuthService(ILogService log)
        {
            _log = log ?? NullLogService.Instance;
        }

        #region Hashing

        public static (string Hash, string Salt) HashPassword(string password)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                CurrentIterations,
                HashAlgorithmName.SHA256,
                32);

            return (Convert.ToHexString(hashBytes), Convert.ToHexString(saltBytes));
        }

        /// <summary>Kabul edilen en kısa hash (32 bayt) ve tuz (16 bayt) uzunlukları.</summary>
        private const int MinHashBytes = 32;
        private const int MinSaltBytes = 16;

        public static bool VerifyPassword(string password, string hashHex, string saltHex, int iterations)
        {
            try
            {
                // Tur sayısı taşımayan (v3.0 ve öncesi) kayıtlar eski turla doğrulanır.
                if (iterations <= 0) iterations = LegacyIterations;

                byte[] saltBytes = Convert.FromHexString(saltHex);
                byte[] expectedHash = Convert.FromHexString(hashHex);

                // GÜVENLİK: Boş veya kısa saklanmış değerler kesin reddedilir.
                // Aksi halde PasswordHash="" ve Salt="" taşıyan bozuk/kötü niyetli bir
                // kayıtta PBKDF2 sıfır bayt üretir, FixedTimeEquals(boş, boş) true döner
                // ve hesap HER parolayı kabul ederdi.
                if (expectedHash.Length < MinHashBytes || saltBytes.Length < MinSaltBytes)
                {
                    return false;
                }

                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    saltBytes,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        public async Task<bool> HasAnyAccountAsync()
        {
            var users = await LoadUsersAsync();
            return users.Count > 0;
        }

        public async Task<(bool Success, string Message)> AuthenticateAsync(string username, string password)
        {
            string cleanUser = (username ?? string.Empty).Trim();
            string cleanPass = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanUser) || string.IsNullOrEmpty(cleanPass))
                return (false, "Lütfen kullanıcı adı ve şifrenizi girin.");

            // Kaba kuvvet frenlemesi
            if (TryGetLockoutRemaining(cleanUser, out TimeSpan remaining))
            {
                _log.Warning($"Kilitli hesap için giriş denemesi: {cleanUser}", null, nameof(AuthService));
                return (false, $"Çok fazla hatalı deneme. Lütfen {FormatRemaining(remaining)} sonra tekrar deneyin.");
            }

            var users = await LoadUsersAsync();

            if (users.Count == 0)
                return (false, "Bu cihazda kayıtlı hesap yok. Lütfen önce bir yönetici hesabı oluşturun.");

            var user = users.Find(u => string.Equals(u.Username, cleanUser, StringComparison.OrdinalIgnoreCase));

            if (user == null)
            {
                RegisterFailure(cleanUser);
                _log.Info($"Bilinmeyen kullanıcı ile giriş denemesi: {cleanUser}", nameof(AuthService));
                return (false, "Kullanıcı bulunamadı. Lütfen kayıt olun veya bilgilerinizi kontrol edin.");
            }

            bool ok = await Task.Run(() =>
                VerifyPassword(cleanPass, user.PasswordHash, user.Salt, user.Iterations));

            if (!ok)
            {
                int failures = RegisterFailure(cleanUser);
                _log.Warning($"Hatalı parola denemesi ({failures}): {cleanUser}", null, nameof(AuthService));

                int left = MaxFailuresBeforeLockout - failures;
                return left is > 0 and <= 2
                    ? (false, $"Şifreniz hatalı. Kalan deneme hakkı: {left}.")
                    : (false, "Şifreniz hatalı, lütfen kontrol ediniz.");
            }

            ClearFailures(cleanUser);

            // Eski tur sayısıyla oluşturulmuş kayıtları sessizce güçlendir.
            if (user.Iterations < CurrentIterations)
                await UpgradeIterationsAsync(users, user, cleanPass);

            _log.Info($"Giriş başarılı: {cleanUser}", nameof(AuthService));
            return (true, "Giriş başarılı.");
        }

        public async Task<(bool Success, string Message)> RegisterAsync(string username, string password)
        {
            string cleanUser = (username ?? string.Empty).Trim();
            string cleanPass = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(cleanUser) || cleanUser.Length < MinUsernameLength)
                return (false, $"Kullanıcı adı en az {MinUsernameLength} karakter olmalıdır.");

            if (cleanPass.Length < MinPasswordLength)
                return (false, $"Şifre en az {MinPasswordLength} karakter olmalıdır.");

            var users = await LoadUsersAsync();

            if (users.Exists(u => string.Equals(u.Username, cleanUser, StringComparison.OrdinalIgnoreCase)))
                return (false, "Bu kullanıcı adı zaten kayıtlı.");

            var (hash, salt) = await Task.Run(() => HashPassword(cleanPass));

            users.Add(new UserAccount
            {
                Username = cleanUser,
                PasswordHash = hash,
                Salt = salt,
                Iterations = CurrentIterations,
                CreatedAt = DateTime.UtcNow
            });

            if (!await SaveLocalUsersAsync(users))
                return (false, "Hesap kaydedilemedi. Disk erişim izinlerini kontrol edin.");

            _log.Info($"Yeni hesap oluşturuldu: {cleanUser}", nameof(AuthService));
            return (true, "Kayıt işlemi başarıyla tamamlandı! Şimdi giriş yapabilirsiniz.");
        }

        public async Task<(bool Success, string Message)> ResetAccountsAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(LocalDbPath)) File.Delete(LocalDbPath);
                });

                lock (_throttleGate) { _failures.Clear(); }

                CredentialStorageService.ClearCredentials();

                _log.Warning("Yerel hesap veritabanı kullanıcı isteğiyle sıfırlandı.", null, nameof(AuthService));
                return (true, "Tüm yerel hesaplar silindi. Şimdi yeni bir yönetici hesabı oluşturabilirsiniz.");
            }
            catch (Exception ex)
            {
                _log.Error("Hesap sıfırlama başarısız.", ex, nameof(AuthService));
                return (false, $"Hesaplar sıfırlanamadı: {ex.Message}");
            }
        }

        #region Throttling

        private bool TryGetLockoutRemaining(string username, out TimeSpan remaining)
        {
            lock (_throttleGate)
            {
                if (_failures.TryGetValue(username, out var record))
                {
                    var left = record.LockedUntilUtc - DateTime.UtcNow;
                    if (left > TimeSpan.Zero)
                    {
                        remaining = left;
                        return true;
                    }
                }
            }

            remaining = TimeSpan.Zero;
            return false;
        }

        private int RegisterFailure(string username)
        {
            lock (_throttleGate)
            {
                if (!_failures.TryGetValue(username, out var record))
                {
                    record = new FailureRecord();
                    _failures[username] = record;
                }

                record.Count++;

                if (record.Count >= MaxFailuresBeforeLockout)
                {
                    // Artan bekleme: 5. hatada 30 sn, sonra 1 dk, 2 dk, 4 dk ... en çok 15 dk
                    int step = record.Count - MaxFailuresBeforeLockout;
                    double seconds = Math.Min(30 * Math.Pow(2, step), 900);
                    record.LockedUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
                }

                return record.Count;
            }
        }

        private void ClearFailures(string username)
        {
            lock (_throttleGate) { _failures.Remove(username); }
        }

        private static string FormatRemaining(TimeSpan span)
            => span.TotalMinutes >= 1
                ? $"{Math.Ceiling(span.TotalMinutes):0} dakika"
                : $"{Math.Ceiling(span.TotalSeconds):0} saniye";

        #endregion

        #region Storage

        private async Task<List<UserAccount>> LoadUsersAsync()
        {
            try
            {
                if (!File.Exists(LocalDbPath)) return new List<UserAccount>();

                string json = await File.ReadAllTextAsync(LocalDbPath);
                return JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
            }
            catch (Exception ex)
            {
                _log.Error("Yerel hesap veritabanı okunamadı.", ex, nameof(AuthService));
                return new List<UserAccount>();
            }
        }

        private async Task<bool> SaveLocalUsersAsync(List<UserAccount> users)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LocalDbPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // Atomik yazma: yarım kalmış kayıt hesap veritabanını bozmasın.
                string tempPath = LocalDbPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(users, WriteOptions));

                if (File.Exists(LocalDbPath))
                    File.Replace(tempPath, LocalDbPath, null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, LocalDbPath);

                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Yerel hesap veritabanı yazılamadı.", ex, nameof(AuthService));
                return false;
            }
        }

        private async Task UpgradeIterationsAsync(List<UserAccount> users, UserAccount user, string password)
        {
            try
            {
                var (hash, salt) = await Task.Run(() => HashPassword(password));
                user.PasswordHash = hash;
                user.Salt = salt;
                user.Iterations = CurrentIterations;

                await SaveLocalUsersAsync(users);
                _log.Info($"Parola türetme turu güçlendirildi: {user.Username}", nameof(AuthService));
            }
            catch (Exception ex)
            {
                // Güçlendirme başarısız olsa da giriş geçerlidir.
                _log.Warning("Parola türetme turu güçlendirilemedi.", ex, nameof(AuthService));
            }
        }

        #endregion
    }
}
