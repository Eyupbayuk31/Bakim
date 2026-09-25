using System;
using System.IO;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.Activity;
using Bakım.Services.Sentinel.Detection;
using Xunit;

namespace Bakim.Tests;

/// <summary>Kaldırıcı v2 (Faz 6): nöbetçi koordinasyonu, iz yedekleri, yeni kalıntı türleri.</summary>
public sealed class UninstallerV2Tests
{
    [Fact]
    public void Suppression_CoversRootAndDescendants_ButNotStrangers()
    {
        int root = 900_001, child = 900_002, grandchild = 900_003, stranger = 900_004;
        using (SentinelSuppression.SuppressProcessTree(root))
        {
            Assert.True(SentinelSuppression.IsSuppressed(root, 1234));
            Assert.True(SentinelSuppression.IsSuppressed(child, root));
            // Ebeveyn (child) çıkmış olsa bile kimliği ağaçta kalır.
            Assert.True(SentinelSuppression.IsSuppressed(grandchild, child));
            Assert.False(SentinelSuppression.IsSuppressed(stranger, 4321));
            Assert.False(SentinelSuppression.IsSuppressed(stranger + 1, null));
        }
        // Kaldırma bittikten sonra kısa bir süre daha geçerli (geç başlayan alt süreçler).
        Assert.True(SentinelSuppression.IsSuppressed(grandchild, child));
    }

    [Fact]
    public void FootprintBackup_AloneIsRestorable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bakim-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Assert.False(RegImportUndoHandler.HasRestorableBackup(dir));

            var backup = new FootprintBackup();
            backup.FirewallRules.Add(new FirewallRuleBackup("{rule}", "v2.30|Action=Allow|App=C:\\x\\y.exe|"));
            ActivityPayload.Write(dir, ActivityPayload.FootprintFile, backup);

            Assert.True(RegImportUndoHandler.HasRestorableBackup(dir));
            var read = ActivityPayload.Read<FootprintBackup>(dir, ActivityPayload.FootprintFile);
            Assert.NotNull(read);
            Assert.Single(read!.FirewallRules);
            Assert.False(read.IsEmpty);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(ResidualType.Service, true, "Hizmet")]
    [InlineData(ResidualType.ScheduledTask, true, "Zamanlanmış Görev")]
    [InlineData(ResidualType.FirewallRule, true, "Güvenlik Duvarı")]
    [InlineData(ResidualType.RegistryValue, false, "Kayıt Değeri")]
    [InlineData(ResidualType.Folder, false, "Klasör")]
    public void NewResidualTypes_AreLabelledAndNeedAdmin(ResidualType type, bool needsAdmin, string label)
    {
        var item = new ResidualItem { Type = type, Path = "x" };
        Assert.Equal(needsAdmin, item.NeedsAdmin);
        Assert.Equal(label, item.TypeName);
    }

    [Theory]
    [InlineData(LeftoverType.Service)]
    [InlineData(LeftoverType.ScheduledTask)]
    [InlineData(LeftoverType.FirewallRule)]
    [InlineData(LeftoverType.RegistryValue)]
    [InlineData(LeftoverType.File)]
    public void TypeMapping_RoundTrips(LeftoverType type) =>
        Assert.Equal(type, ResidualScannerEngine.MapType(ResidualScannerEngine.MapType(type)));
}
