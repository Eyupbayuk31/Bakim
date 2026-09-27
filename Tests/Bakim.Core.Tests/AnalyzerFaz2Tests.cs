using System.Linq;
using Bakım.Core.Analyzer;
using Bakım.Core.Risk;
using Bakım.Core.Sentinel;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 2: tek risk ölçeği ve açıklamalı satır puanı.</summary>
public class AnalyzerFaz2Tests
{
    [Theory]
    [InlineData(0, false, RiskVerdict.Clean)]
    [InlineData(5, false, RiskVerdict.Clean)]
    [InlineData(5, true, RiskVerdict.Info)]
    [InlineData(10, false, RiskVerdict.Info)]
    [InlineData(29, false, RiskVerdict.Info)]
    [InlineData(30, false, RiskVerdict.Caution)]
    [InlineData(49, false, RiskVerdict.Caution)]
    [InlineData(50, false, RiskVerdict.Suspicious)]
    [InlineData(74, false, RiskVerdict.Suspicious)]
    [InlineData(75, false, RiskVerdict.Dangerous)]
    [InlineData(100, false, RiskVerdict.Dangerous)]
    public void Scale_Thresholds(int score, bool findings, RiskVerdict expected) => Assert.Equal(expected, RiskScale.Verdict(score, findings));

    [Fact]
    public void Scale_IsSharedBySentinelAndHistory()
    {
        foreach (int score in new[] { 0, 12, 35, 55, 80 })
        {
            Assert.Equal(RiskScale.Verdict(score, true), SetupRiskEngine.VerdictFor(score, 1));
            Assert.Equal(RiskScale.Label(RiskScale.Verdict(score)), Bakım.Core.History.AnalysisVerdicts.Display(Bakım.Core.History.AnalysisVerdicts.FromScore(score)));
        }
        Assert.Equal("Tehlikeli", SetupRiskEngine.VerdictLabel(RiskVerdict.Dangerous));
    }

    private static PersistenceRiskInput Input(SignatureState sig = SignatureState.Verified, bool microsoft = false, bool exists = true, bool enabled = true,
        int malicious = -1, int total = -1, bool trusted = false, int? deep = null, AutorunCategory category = AutorunCategory.RegistryRun) =>
        new(category, sig, SignatureApplicable: true, microsoft, HasFilePath: true, FileExists: exists, enabled, IsRecentlyAdded: false,
            VirusTotalMalicious: malicious, VirusTotalTotal: total, DeepScore: deep, UserTrusted: trusted);

    [Fact]
    public void Risk_ExplainsItself()
    {
        var clean = PersistenceRisk.Evaluate(Input(microsoft: true));
        Assert.Equal(0, clean.Score);
        Assert.Contains("Microsoft", clean.Reasons.Single());

        var unsigned = PersistenceRisk.Evaluate(Input(SignatureState.Unsigned));
        Assert.Equal(40, unsigned.Score);
        Assert.Contains(unsigned.Reasons, r => r.Contains("imzası yok"));

        var wmi = PersistenceRisk.Evaluate(Input(SignatureState.Invalid, category: AutorunCategory.WmiEventConsumer));
        Assert.Equal(80, wmi.Score);
        Assert.Equal(2, wmi.Reasons.Count);

        // Doğrulanmış Microsoft imzalı Active Setup/Winlogon girdisi konum riski almaz; başka imzalı yarım alır.
        Assert.Equal(0, PersistenceRisk.Evaluate(Input(SignatureState.Verified, microsoft: true, category: AutorunCategory.WinlogonIfeo)).Score);
        Assert.Equal(10, PersistenceRisk.Evaluate(Input(SignatureState.Verified, category: AutorunCategory.WinlogonIfeo)).Score);
    }

    [Fact]
    public void Risk_UserTrust_LowersScore_ButNotAgainstVirusTotalConsensus()
    {
        var trusted = PersistenceRisk.Evaluate(Input(SignatureState.Unsigned, trusted: true));
        Assert.True(trusted.Score <= 5);
        Assert.Contains("güvenilir", trusted.Reasons.Single());

        var trustedButFlagged = PersistenceRisk.Evaluate(Input(SignatureState.Verified, trusted: true, malicious: 55, total: 70));
        Assert.True(trustedButFlagged.Score >= RiskScale.Dangerous);
        Assert.Contains(trustedButFlagged.Reasons, r => r.Contains("geçersiz kılıyor"));
    }

    [Fact]
    public void Risk_Orphan_Disabled_AndDeepScore()
    {
        var orphan = PersistenceRisk.Evaluate(Input(SignatureState.Unsigned, exists: false, category: AutorunCategory.WinlogonIfeo));
        Assert.InRange(orphan.Score, 5, 20);
        Assert.Contains("yetim", orphan.Reasons.Single());

        var disabled = PersistenceRisk.Evaluate(Input(SignatureState.Unsigned, enabled: false));
        Assert.Equal(16, disabled.Score);
        Assert.Contains(disabled.Reasons, r => r.Contains("devre dışı"));

        var deep = PersistenceRisk.Evaluate(Input(SignatureState.Unsigned, deep: 7));
        Assert.Equal(7, deep.Score);
    }
}
