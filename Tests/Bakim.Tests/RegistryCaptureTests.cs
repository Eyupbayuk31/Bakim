using System.Linq;
using Bakım.Helpers;
using Microsoft.Win32;
using Xunit;

namespace Bakim.Tests;

/// <summary>Değer düzeyinde ince ayar yedeği (H-13): yakala → değiştir → birebir geri yükle.</summary>
public sealed class RegistryCaptureTests : System.IDisposable
{
    private readonly string _key = @"Software\BakimTests\Capture_" + System.Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_key, false); } catch { }
    }

    [Fact]
    public void CapturedValues_RestoreExactly_IncludingAbsentOnes()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
        {
            k.SetValue("Existing", 7, RegistryValueKind.DWord);
            k.SetValue("Text", "özgün", RegistryValueKind.String);
            k.SetValue("Blob", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
        }

        System.Collections.Generic.IReadOnlyList<RegistryValueSnapshot> captured;
        using (var capture = RegistryCapture.Begin())
        {
            Assert.True(VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "Existing", 0));
            Assert.True(VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "Existing", 1)); // ikinci yazım: özgün korunmalı
            Assert.True(VerifiedRegistry.SetString(Registry.CurrentUser, _key, "Text", "değişti"));
            Assert.True(VerifiedRegistry.DeleteValue(Registry.CurrentUser, _key, "Blob"));
            Assert.True(VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "New", 5)); // önceden yoktu
            captured = capture.Items;
        }

        Assert.Equal(4, captured.Count);
        Assert.All(captured, s => Assert.True(RegistryCapture.Restore(s)));

        using var check = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Equal(7, check.GetValue("Existing"));
        Assert.Equal("özgün", check.GetValue("Text"));
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])check.GetValue("Blob")!);
        Assert.Null(check.GetValue("New"));
    }

    [Fact]
    public void DeletedKeyTree_IsRestored()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key + @"\Tree\Child"))
            k.SetValue("V", "x");

        System.Collections.Generic.IReadOnlyList<RegistryValueSnapshot> captured;
        using (var capture = RegistryCapture.Begin())
        {
            Assert.True(VerifiedRegistry.DeleteKeyTree(Registry.CurrentUser, _key + @"\Tree"));
            captured = capture.Items;
        }

        Assert.True(RegistryCapture.Restore(captured.Single()));
        using var check = Registry.CurrentUser.OpenSubKey(_key + @"\Tree\Child");
        Assert.Equal("x", check?.GetValue("V"));
    }

    [Fact]
    public void WithoutScope_NothingIsCaptured()
    {
        Assert.True(VerifiedRegistry.SetDword(Registry.CurrentUser, _key, "Free", 1));
        using var capture = RegistryCapture.Begin();
        Assert.Empty(capture.Items);
    }
}
