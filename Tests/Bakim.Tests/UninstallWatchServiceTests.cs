using System;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.Safety;
using Bakım.Services.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace Bakim.Tests;

/// <summary>Bakım dışından kaldırma algılama: gerçek HKCU Uninstall kaydıyla uçtan uca.</summary>
public class UninstallWatchServiceTests
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private static string CreateFakeApp(string name)
    {
        string keyName = "BakimTest_" + Guid.NewGuid().ToString("N");
        using var key = Registry.CurrentUser.CreateSubKey($@"{UninstallRoot}\{keyName}");
        key.SetValue("DisplayName", name);
        key.SetValue("Publisher", "Bakım Test");
        key.SetValue("UninstallString", @"C:\BakimTestYok\uninstall.exe");
        key.SetValue("InstallLocation", @"C:\BakimTestYok\" + keyName);
        return keyName;
    }

    private static void Delete(string keyName) =>
        Registry.CurrentUser.DeleteSubKeyTree($@"{UninstallRoot}\{keyName}", throwOnMissingSubKey: false);

    private static async Task<UninstallWatchService> StartAsync()
    {
        var watch = new UninstallWatchService(new UninstallerService(), NullLogService.Instance, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(200));
        watch.Start();
        for (int i = 0; i < 100 && !watch.IsBaselineReady; i++) await Task.Delay(100);
        Assert.True(watch.IsBaselineReady);
        return watch;
    }

    [Fact]
    public async Task ExternalUninstall_RaisesEvent_WithSnapshotDetails()
    {
        string name = "Bakım Harici Kaldırma Testi " + Guid.NewGuid().ToString("N")[..6];
        string keyName = CreateFakeApp(name);
        var watch = await StartAsync();
        try
        {
            var seen = new TaskCompletionSource<InstalledAppItem>(TaskCreationOptions.RunContinuationsAsynchronously);
            watch.AppUninstalledExternally += app => { if (app.DisplayName == name) seen.TrySetResult(app); };

            Delete(keyName);
            var winner = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.Same(seen.Task, winner);
            var removed = await seen.Task;
            Assert.Equal("Bakım Test", removed.Publisher);
            Assert.EndsWith(keyName, removed.InstallLocation);
        }
        finally
        {
            watch.Stop();
            Delete(keyName);
        }
    }

    [Fact]
    public async Task BakimInitiatedUninstall_IsNotReported()
    {
        string name = "Bakım İç Kaldırma Testi " + Guid.NewGuid().ToString("N")[..6];
        string keyName = CreateFakeApp(name);
        var watch = await StartAsync();
        try
        {
            bool reported = false;
            watch.AppUninstalledExternally += app => { if (app.DisplayName == name) reported = true; };

            var apps = await new UninstallerService().GetInstalledAppsAsync();
            var app = apps.Find(a => a.DisplayName == name)!;
            UninstallWatchService.MarkInitiatedByBakim(app.RegistryKeyPath);
            Delete(keyName);

            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(reported);
        }
        finally
        {
            watch.Stop();
            Delete(keyName);
        }
    }
}
