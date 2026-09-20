using Bakım.Models;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Analizör listesinin sıralamasını belirleyen risk puanlaması.
///
/// Sıralama kullanıcının ilk gördüğü şeydir: en tehlikeli girdi en üstte
/// olmalı, kullanıcı yüzlerce satır arasında aramak zorunda kalmamalıdır.
/// Puanlama bozulursa bu sessizce kaybolur — bu yüzden test edilir.
/// </summary>
public class PersistenceRiskTests
{
    private static PersistenceItem Item(
        SignatureStatus signature = SignatureStatus.Verified,
        PersistenceCategory category = PersistenceCategory.RegistryRun,
        int malicious = -1,
        bool enabled = true,
        bool microsoft = false,
        DateTime? created = null)
    {
        return new PersistenceItem
        {
            Name = "test",
            Signature = signature,
            Category = category,
            MaliciousDetections = malicious,
            IsEnabled = enabled,
            IsMicrosoft = microsoft,
            FileCreatedUtc = created
        };
    }

    [Fact]
    public void SignedMicrosoftEntry_ScoresSafe()
    {
        var item = Item(SignatureStatus.Verified, microsoft: true);

        Assert.Equal(0, item.RiskScore);
        Assert.Equal(Intent.Success, item.RiskIntent);
        Assert.Equal("GÜVENLİ", item.RiskLabel);
    }

    [Fact]
    public void TamperedSignature_OutranksUnsigned()
    {
        var tampered = Item(SignatureStatus.InvalidOrTampered);
        var unsigned = Item(SignatureStatus.Unsigned);

        Assert.True(tampered.RiskScore > unsigned.RiskScore,
            "Bozulmuş imza, imzasızdan daha tehlikeli sayılmalı");
    }

    [Fact]
    public void VirusTotalDetections_RaiseScore()
    {
        var clean = Item(SignatureStatus.Unsigned, malicious: 0);
        var flagged = Item(SignatureStatus.Unsigned, malicious: 5);

        Assert.True(flagged.RiskScore > clean.RiskScore);
    }

    [Theory]
    [InlineData(PersistenceCategory.WmiEventConsumer)]
    [InlineData(PersistenceCategory.WinlogonIfeo)]
    public void HighValuePersistenceVectors_ScoreHigherThanRegistryRun(PersistenceCategory category)
    {
        var exotic = Item(SignatureStatus.Unsigned, category);
        var ordinary = Item(SignatureStatus.Unsigned, PersistenceCategory.RegistryRun);

        Assert.True(exotic.RiskScore > ordinary.RiskScore,
            $"{category} meşru yazılımda nadirdir; daha yüksek puanlanmalı");
    }

    [Fact]
    public void DisabledEntry_ScoresLowerThanEnabled()
    {
        var enabled = Item(SignatureStatus.Unsigned, enabled: true);
        var disabled = Item(SignatureStatus.Unsigned, enabled: false);

        Assert.True(disabled.RiskScore < enabled.RiskScore,
            "Devre dışı girdi çalışmıyor; riski düşük olmalı");
    }

    [Fact]
    public void RecentlyAdded_IsFlagged_AndRaisesScore()
    {
        var fresh = Item(SignatureStatus.Unsigned, created: DateTime.UtcNow.AddDays(-2));
        var old = Item(SignatureStatus.Unsigned, created: DateTime.UtcNow.AddDays(-200));

        Assert.True(fresh.IsRecentlyAdded);
        Assert.False(old.IsRecentlyAdded);
        Assert.True(fresh.RiskScore > old.RiskScore);
    }

    [Fact]
    public void RiskScore_IsAlwaysWithinBounds()
    {
        // En kötü senaryo: bozuk imza + çok tespit + egzotik vektör + taze
        var worst = Item(SignatureStatus.InvalidOrTampered,
            PersistenceCategory.WmiEventConsumer,
            malicious: 70,
            created: DateTime.UtcNow);

        Assert.InRange(worst.RiskScore, 0, 100);
        Assert.Equal(Intent.Critical, worst.RiskIntent);
    }

    [Fact]
    public void RiskIcon_DiffersByLevel_SoMeaningIsNotColourOnly()
    {
        // WCAG 1.4.1: anlam yalnızca renkle taşınmamalı
        var safe = Item(SignatureStatus.Verified, microsoft: true);
        var risky = Item(SignatureStatus.InvalidOrTampered, PersistenceCategory.WmiEventConsumer);

        Assert.NotEqual(safe.RiskIconSymbol, risky.RiskIconSymbol);
        Assert.NotEqual(safe.RiskLabel, risky.RiskLabel);
    }

    [Theory]
    [InlineData(0, "Bugün eklendi")]
    [InlineData(1, "Dün eklendi")]
    public void AgeDisplay_IsHumanReadable(int daysAgo, string expected)
    {
        var item = Item(created: DateTime.UtcNow.AddDays(-daysAgo).AddMinutes(-1));

        Assert.Equal(expected, item.AgeDisplay);
    }

    [Fact]
    public void AgeDisplay_WithoutTimestamp_SaysUnknown()
    {
        Assert.Equal("Bilinmiyor", Item().AgeDisplay);
        Assert.Equal("—", Item().CreatedDisplay);
    }
}
