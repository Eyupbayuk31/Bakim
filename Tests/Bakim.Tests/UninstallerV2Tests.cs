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

/// <summary>Tek örnek istek kutusu (KAL C2): taze istek işlenir ve silinir, eskisi yok sayılır.</summary>
public sealed class SingleInstanceInboxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-ipc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Drop(IpcRequest request)
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(request));
        return file;
    }

    [Fact]
    public void FreshRequest_IsHandledAndDeleted()
    {
        IpcRequest? handled = null;
        using var service = new SingleInstanceService(_dir);
        service.Listen(r => handled = r);

        string file = Drop(new IpcRequest(IpcRequestKind.UninstallTarget, @"C:\x\y.exe", DateTime.UtcNow));
        var result = service.Consume(file);

        Assert.NotNull(result);
        Assert.False(File.Exists(file));
        Assert.Equal(IpcRequestKind.UninstallTarget, handled?.Kind);
        Assert.Equal(@"C:\x\y.exe", handled?.Target);
    }

    [Fact]
    public void StaleRequest_IsIgnoredButRemoved()
    {
        bool called = false;
        using var service = new SingleInstanceService(_dir);
        string file = Drop(new IpcRequest(IpcRequestKind.Activate, null, DateTime.UtcNow - TimeSpan.FromMinutes(5)));
        service.Listen(_ => called = true);   // açılışta birikmiş istekler taranır

        Assert.False(called);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Send_WithoutListener_WithdrawsRequest()
    {
        using var service = new SingleInstanceService(_dir);
        bool sent = service.Send(new IpcRequest(IpcRequestKind.Activate, null, DateTime.UtcNow), TimeSpan.FromMilliseconds(300));
        Assert.False(sent);
        Assert.Empty(Directory.GetFiles(_dir, "*.json"));
    }
}

/// <summary>"Activity?kind=Uninstall": Kaldırıcı'daki Geçmiş düğmesi Etkinlik Merkezi'ni filtreli açar.</summary>
public sealed class ActivityDeepLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-act-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("kind=Uninstall", Bakım.Core.Activity.ActivityKind.Uninstall)]
    [InlineData("KIND=uninstall", Bakım.Core.Activity.ActivityKind.Uninstall)]
    public void KindParameter_SetsFilterAndClearsOthers(string parameter, Bakım.Core.Activity.ActivityKind expected)
    {
        var vm = new Bakım.ViewModels.ActivityCenterViewModel(
            new ActivityService(new Bakım.Core.Activity.ActivityStore(_dir), Array.Empty<IUndoHandler>()),
            new NavigationService());
        vm.SearchText = "eski arama";
        vm.UndoableOnly = true;

        vm.ApplyNavigationParameter(parameter);

        Assert.Equal(expected, vm.SelectedKind);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.UndoableOnly);
    }

    [Fact]
    public void UnknownParameter_LeavesFiltersAlone()
    {
        var vm = new Bakım.ViewModels.ActivityCenterViewModel(
            new ActivityService(new Bakım.Core.Activity.ActivityStore(_dir), Array.Empty<IUndoHandler>()),
            new NavigationService());
        vm.SearchText = "x";
        vm.ApplyNavigationParameter("kind=Nope");
        Assert.Null(vm.SelectedKind);
        Assert.Equal("x", vm.SearchText);
    }
}
