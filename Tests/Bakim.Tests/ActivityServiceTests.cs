using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Core.Activity;
using Bakım.Helpers;
using Bakım.Services.Activity;
using Microsoft.Win32;
using Xunit;

namespace Bakim.Tests;

/// <summary>Etkinlik Merkezi (§7): kayıt → geri al → özgün değer ve dürüst rapor.</summary>
public sealed class ActivityServiceTests : IDisposable
{
    private readonly string _key = @"Software\BakimTests\Activity_" + Guid.NewGuid().ToString("N");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-activity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_key, false); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ActivityService NewService() => new(new ActivityStore(_dir), new IUndoHandler[]
    {
        new RegistryValuesUndoHandler(),
        new RegistryValuesUndoHandler(UndoHandlers.StartupApproved),
        new RegImportUndoHandler(),
    });

    [Fact]
    public async Task RegistryChange_IsUndoneExactly_AndRestoreIsLogged()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
            k.SetValue("Existing", 7, RegistryValueKind.DWord);

        var service = NewService();
        ActivityEntry? entry;
        using (var capture = RegistryCapture.Begin())
        {
            VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "Existing", 0);
            VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "New", 1);
            entry = service.RecordRegistryChange(ActivityKind.Tweak, "Test", "Ayar uygulandı", "", ActivityOutcome.Succeeded, capture.Items);
        }

        Assert.NotNull(entry);
        Assert.True(entry!.CanUndo);

        var result = await service.UndoAsync(entry.Id);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Restored);
        using (var check = Registry.CurrentUser.OpenSubKey(_key)!)
        {
            Assert.Equal(7, check.GetValue("Existing"));
            Assert.Null(check.GetValue("New"));
        }

        // Kalıcı: yeni bir örnek aynı durumu okur.
        var reloaded = NewService().Entries;
        Assert.Equal(UndoState.Undone, reloaded.Single(e => e.Id == entry.Id).Undo);
        var restore = reloaded.Single(e => e.Kind == ActivityKind.Restore);
        Assert.Equal(entry.Id, restore.RelatedId);
    }

    [Fact]
    public async Task SecondUndo_IsRefused()
    {
        var service = NewService();
        ActivityEntry? entry;
        using (var capture = RegistryCapture.Begin())
        {
            VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "V", 1);
            entry = service.RecordRegistryChange(ActivityKind.Tweak, "Test", "t", "", ActivityOutcome.Succeeded, capture.Items);
        }

        Assert.True((await service.UndoAsync(entry!.Id)).Success);
        var second = await service.UndoAsync(entry.Id);
        Assert.False(second.Success);
    }

    [Fact]
    public void ValueAlreadyOriginal_CountsAsCurrent()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
            k.SetValue("Same", "a", RegistryValueKind.String);

        using var capture = RegistryCapture.Begin();
        using (var k = Registry.CurrentUser.OpenSubKey(_key, true)!)
        {
            RegistryCapture.Track(k, "Same");
            RegistryCapture.Track(k, "Missing");
        }

        Assert.All(capture.Items, s => Assert.True(RegistryCapture.IsCurrent(s)));
    }

    [Fact]
    public async Task DirectSetValue_WithTrack_IsUndoable()
    {
        var service = NewService();
        ActivityEntry? entry;
        using (var capture = RegistryCapture.Begin())
        {
            using (var k = Registry.CurrentUser.CreateSubKey(_key))
            {
                RegistryCapture.Track(k, "Direct");
                k.SetValue("Direct", 1, RegistryValueKind.DWord);
            }
            entry = service.RecordRegistryChange(ActivityKind.Tweak, "Test", "t", "", ActivityOutcome.Succeeded, capture.Items);
        }

        Assert.True((await service.UndoAsync(entry!.Id)).Success);
        using var check = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Null(check.GetValue("Direct"));
    }

    [Fact]
    public void FailedOperation_IsNotUndoable()
    {
        var service = NewService();
        using var capture = RegistryCapture.Begin();
        VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "V", 1);
        var entry = service.RecordRegistryChange(ActivityKind.Tweak, "Test", "t", "", ActivityOutcome.Failed, capture.Items);
        Assert.False(entry!.CanUndo);
    }

    [Fact]
    public void MissingPayload_ShowsAsExpired()
    {
        var service = NewService();
        ActivityEntry? entry;
        using (var capture = RegistryCapture.Begin())
        {
            VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "V", 1);
            entry = service.RecordRegistryChange(ActivityKind.Tweak, "Test", "t", "", ActivityOutcome.Succeeded, capture.Items);
        }
        Directory.Delete(entry!.PayloadPath!, true);

        var reloaded = NewService().Entries.Single(e => e.Id == entry.Id);
        Assert.Equal(UndoState.Expired, reloaded.Undo);
    }

    [Fact]
    public void UnknownHandler_IsRecordedAsNotUndoable()
    {
        var entry = NewService().Record(new ActivityEntry
        {
            Title = "x",
            Undo = UndoState.Undoable,
            UndoHandler = "no-such-handler"
        });
        Assert.False(entry.CanUndo);
    }

    [Theory]
    [InlineData(false, null, null, "Remove-ItemProperty")]
    [InlineData(true, "DWord", "5", "-PropertyType DWord -Value ([int]5)")]
    [InlineData(true, "String", "it's", "-Value 'it''s'")]
    [InlineData(true, "MultiString", "[\"a\",\"b\"]", "@('a','b')")]
    [InlineData(true, "Binary", "AQID", "([byte[]]@(1,2,3))")]
    public void PowerShellRestore_QuotesAndTypesCorrectly(bool existed, string? kind, string? data, string expected)
    {
        var s = new RegistryValueSnapshot("HKEY_LOCAL_MACHINE", @"SOFTWARE\Test", "Val", existed, kind, data);
        string? ps = RegistryCapture.ToPowerShell(s);
        Assert.NotNull(ps);
        Assert.Contains(expected, ps);
        Assert.Contains("'Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Test'", ps);
    }

    [Fact]
    public void PowerShellRestore_KeyTrees()
    {
        var created = new RegistryValueSnapshot("HKEY_LOCAL_MACHINE", @"SOFTWARE\Test", null, false, null, null);
        Assert.Contains("Remove-Item", RegistryCapture.ToPowerShell(created));
        var existing = new RegistryValueSnapshot("HKEY_LOCAL_MACHINE", @"SOFTWARE\Test", null, true, "Tree", "{}");
        Assert.Null(RegistryCapture.ToPowerShell(existing));
    }
}
