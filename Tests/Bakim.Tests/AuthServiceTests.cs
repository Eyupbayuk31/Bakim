using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Parola doğrulamasının saf (dosya sistemine dokunmayan) kısmı.
///
/// Bu sınıftaki en kritik test <see cref="LegacyAccount_WithoutIterationsField_StillAuthenticates"/>:
/// v3.1'de tur sayısı 100.000'den 210.000'e çıkarıldı. UserAccount.Iterations
/// varsayılanı güncel değer olsaydı, alanı taşımayan v3.0 kayıtları yanlış turla
/// doğrulanır ve kullanıcılar kendi hesaplarından kilitlenirdi.
/// </summary>
public class AuthServiceTests
{
    private const string Password = "OrnekParola!2026";

    private static (string Hash, string Salt) MakeLegacyHash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            AuthService.LegacyIterations, HashAlgorithmName.SHA256, 32);

        return (Convert.ToHexString(hash), Convert.ToHexString(salt));
    }

    [Fact]
    public void MissingIterationsField_DeserializesToZero_NotCurrentIterations()
    {
        // v3.0 formatı: Iterations alanı yok
        const string legacyJson = """
        { "Username": "Eski", "PasswordHash": "AA", "Salt": "BB",
          "CreatedAt": "2026-09-19T14:00:00Z" }
        """;

        var account = JsonSerializer.Deserialize<UserAccount>(legacyJson);

        Assert.NotNull(account);
        Assert.Equal(0, account!.Iterations);
        Assert.NotEqual(AuthService.CurrentIterations, account.Iterations);
    }

    [Fact]
    public void LegacyAccount_WithoutIterationsField_StillAuthenticates()
    {
        var (hash, salt) = MakeLegacyHash(Password);

        // Iterations = 0 => eski kayıt => 100.000 tur ile doğrulanmalı
        Assert.True(AuthService.VerifyPassword(Password, hash, salt, 0));
    }

    [Fact]
    public void LegacyHash_DoesNotVerify_WithCurrentIterations()
    {
        // Hatanın kanıtı: güncel tur sayısı eski hash'i AÇMAMALI.
        // Bu test geçmezse, tur sayısı göçü sessizce bozulmuş demektir.
        var (hash, salt) = MakeLegacyHash(Password);

        Assert.False(AuthService.VerifyPassword(Password, hash, salt, AuthService.CurrentIterations));
    }

    [Fact]
    public void NewAccount_VerifiesWithCurrentIterations()
    {
        var (hash, salt) = AuthService.HashPassword(Password);

        Assert.True(AuthService.VerifyPassword(Password, hash, salt, AuthService.CurrentIterations));
    }

    [Fact]
    public void WrongPassword_IsRejected()
    {
        var (hash, salt) = AuthService.HashPassword(Password);

        Assert.False(AuthService.VerifyPassword("YanlisParola", hash, salt, AuthService.CurrentIterations));
    }

    [Fact]
    public void EachHash_UsesUniqueSalt()
    {
        var first = AuthService.HashPassword(Password);
        var second = AuthService.HashPassword(Password);

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData("ZZZZ", "YYYY")]   // geçersiz hex
    [InlineData("", "")]           // boş
    [InlineData("AA", "")]         // eksik tuz
    public void CorruptStoredValues_ReturnFalse_WithoutThrowing(string hash, string salt)
    {
        Assert.False(AuthService.VerifyPassword(Password, hash, salt, AuthService.CurrentIterations));
    }

    [Fact]
    public void CurrentIterations_MeetsOwaspMinimum()
    {
        // OWASP 2023, PBKDF2-SHA256 için en az 210.000 tur önerir.
        Assert.True(AuthService.CurrentIterations >= 210_000);
        Assert.True(AuthService.LegacyIterations < AuthService.CurrentIterations);
    }
}
