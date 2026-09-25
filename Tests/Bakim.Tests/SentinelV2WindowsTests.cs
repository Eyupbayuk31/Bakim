using System;
using System.IO;
using System.Linq;
using System.Text;
using Bakım.Core.Sentinel;
using Bakım.Services.Sentinel.Detection;
using Bakım.Services.Sentinel.Sensors;
using Xunit;

namespace Bakim.Tests;

/// <summary>Nöbetçi v2 (Faz 7) Windows tarafı: sistem sensörü ve kurulum dosyası incelemesi.</summary>
public sealed class SentinelV2WindowsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bakim-sentinel-" + Guid.NewGuid().ToString("N"));

    public SentinelV2WindowsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void SystemState_CapturesReadableAreas_AndDiffOfSameStateIsEmpty()
    {
        var first = SystemStateSensor.Capture();
        Assert.Contains(SystemArea.EnvironmentPath, first.ReadableAreas);
        Assert.Contains(SystemArea.Hosts, first.ReadableAreas);

        var diff = SystemStateSnapshot.Diff(first.Values, first.Values, first.ReadableAreas, first.ReadableAreas);
        Assert.Empty(diff);
    }

    [Fact]
    public void Installer_FrameworkAndMotw_AreRead()
    {
        string path = Path.Combine(_dir, "setup.exe");
        var bytes = new byte[4096];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        Encoding.ASCII.GetBytes("Inno Setup Setup Data (6.2.2)").CopyTo(bytes, 200);
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.org/setup.exe\r\n");

        var (framework, motw) = InstallerInspector.Quick(path);
        Assert.Equal(InstallerFramework.InnoSetup, framework);
        Assert.True(motw?.IsFromInternet);
        Assert.Equal(60, InstallerInspector.ExtraScore(path));

        var info = InstallerInspector.Inspect(path);
        Assert.Equal("example.org", info.SourceSite);
        Assert.NotNull(info.Sha256);
        Assert.False(info.Signed);
    }

    [Fact]
    public void HiddenPe_IsDetected()
    {
        string fake = Path.Combine(_dir, "photo.jpg");
        File.WriteAllBytes(fake, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
        string real = Path.Combine(_dir, "real.jpg");
        File.WriteAllBytes(real, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        Assert.True(InstallerInspector.LooksLikeHiddenPe(fake));
        Assert.False(InstallerInspector.LooksLikeHiddenPe(real));
    }
}
