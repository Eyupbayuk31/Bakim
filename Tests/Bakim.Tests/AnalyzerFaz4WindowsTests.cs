using System;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Bakım.Services.Analyzer;
using Microsoft.Win32;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Analizör v3 Faz 4 (Windows, uçtan uca): canlı izleme HKCU Run'a eklenen değeri yakalar. Değer var
/// olmayan bir dosyayı gösterir (bırakılsa bile hiçbir şey çalıştırmaz) ve test sonunda silinir.
/// </summary>
public sealed class AnalyzerFaz4WindowsTests
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public async Task LiveWatch_DetectsNewRunValue()
    {
        string name = "BakimTest_" + Guid.NewGuid().ToString("N")[..8];
        var engine = new AutorunsScannerEngine(new VirusTotalCheckService(new AppSettingsService(new NullLogService())));
        using var watch = new PersistenceWatchService(engine, new NullLogService(), TimeSpan.Zero);
        var seen = new TaskCompletionSource<PersistenceItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        watch.PersistenceAdded += item => { if (item.RegistryValueName == name) seen.TrySetResult(item); };
        watch.Start();

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!watch.IsBaselineReady && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.True(watch.IsBaselineReady, "Taban alınamadı.");

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(name, @"""C:\BakimTest\yok\ornek.exe"" --tray");

            var winner = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.True(winner == seen.Task, "Canlı izleme yeni Run değerini 30 sn içinde yakalamadı.");
            var item = await seen.Task;
            Assert.Equal(PersistenceCategory.RegistryRun, item.Category);
            Assert.Equal(@"C:\BakimTest\yok\ornek.exe", item.FilePath);
            Assert.False(item.HasValidFile);
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            watch.Stop();
        }
    }
}
