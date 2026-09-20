using Bakım.Models;
using Xunit;

namespace Bakim.Tests;

public class TweakerMasterDetailTests
{
    [Fact]
    public void TweakerCategoryModel_CountBadge_ReflectsActiveAndTotalCounts()
    {
        var category = new TweakerCategoryModel
        {
            Key = "Windows11",
            DisplayName = "Windows 11",
            TotalCount = 25,
            ActiveCount = 10
        };

        Assert.Equal("10/25", category.CountBadge);

        category.ActiveCount = 15;
        Assert.Equal("15/25", category.CountBadge);

        category.TotalCount = 30;
        Assert.Equal("15/30", category.CountBadge);
    }

    [Fact]
    public void TweakerCategoryModel_SelectionState_ChangesCorrectly()
    {
        var category = new TweakerCategoryModel
        {
            Key = "Behavior",
            DisplayName = "Sistem Davranışları",
            IsSelected = false
        };

        bool eventRaised = false;
        category.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TweakerCategoryModel.IsSelected))
            {
                eventRaised = true;
            }
        };

        category.IsSelected = true;

        Assert.True(category.IsSelected);
        Assert.True(eventRaised);
    }

    [Fact]
    public void SystemTweakItem_UserFacingNotices_DoNotContainRawRegistryJargon()
    {
        var tweak = new SystemTweakItem
        {
            Id = "disable_telemetry",
            Title = "Telemetriyi Kapat",
            Description = "Windows arka plan tanılama verisi toplamasını kapatır.",
            Category = "Davranışlar (Behavior)",
            IsRecommended = true,
            RequiresRestart = true
        };

        // Kullanıcı dostu metinler Registry veya DWORD jargonu içermemelidir
        Assert.DoesNotContain("REG_DWORD", tweak.UserBenefitText);
        Assert.DoesNotContain("HKCU", tweak.UserBenefitText);
        Assert.DoesNotContain("HKLM", tweak.UserBenefitText);

        Assert.DoesNotContain("REG_DWORD", tweak.SafetyNotice);
        Assert.DoesNotContain("HKCU", tweak.SafetyNotice);
        Assert.DoesNotContain("HKLM", tweak.SafetyNotice);

        Assert.DoesNotContain("REG_DWORD", tweak.ExecutionNotice);
        Assert.DoesNotContain("HKCU", tweak.ExecutionNotice);
        Assert.DoesNotContain("HKLM", tweak.ExecutionNotice);

        Assert.DoesNotContain("REG_DWORD", tweak.RevertNotice);
        Assert.DoesNotContain("HKCU", tweak.RevertNotice);
        Assert.DoesNotContain("HKLM", tweak.RevertNotice);

        // İnsanca açıklamalar doğrulanır
        Assert.Contains("yeniden başlat", tweak.ExecutionNotice);
        Assert.Contains("güvenli", tweak.SafetyNotice);
        Assert.Contains("orijinal", tweak.RevertNotice);
    }

    [Fact]
    public void SystemTweakItem_ExecutionNotice_ImmediateWhenNoRestartRequired()
    {
        var tweak = new SystemTweakItem
        {
            RequiresRestart = false
        };

        Assert.Contains("Anında", tweak.ExecutionNotice);
        Assert.DoesNotContain("bilgisayarı yeniden başlatmanız gerekir", tweak.ExecutionNotice);
    }
}
