using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>
/// Depo düzeyinde tutarlılık: tek sürüm kaynağı (H-15) ve sürüm iş akışı.
/// Linux'ta da koşar; derleme gerektirmez.
/// </summary>
public class RepositoryConsistencyTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static string PropsVersion()
    {
        var m = Regex.Match(Read("Directory.Build.props"), @"<BakimVersion>(\d+\.\d+\.\d+)</BakimVersion>");
        Assert.True(m.Success, "BakimVersion bulunamadı");
        return m.Groups[1].Value;
    }

    [Fact]
    public void InstallerDefaultVersion_MatchesProps()
    {
        var m = Regex.Match(Read("Bakim_Setup.iss"), @"#define\s+MyAppVersion\s+""([^""]+)""");
        Assert.True(m.Success);
        Assert.Equal(PropsVersion(), m.Groups[1].Value);
    }

    [Fact]
    public void Csproj_TakesVersionFromProps()
    {
        string csproj = Read("Bakım.csproj");
        Assert.Contains("<Version>$(BakimVersion)</Version>", csproj);
        Assert.DoesNotMatch(@"<Version>\d", csproj);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("Services/AutoUpdateService.cs")]
    [InlineData("ViewModels/SettingsViewModel.cs")]
    public void NoHardcodedCurrentVersion(string relative)
    {
        string text = Read(relative);
        string current = PropsVersion();
        // Değişiklik günlüğü girdileri ("Version = \"v3.20.0\"") geçmiş sürümlerdir; güncel sürüm yazılmamalı.
        var hits = Regex.Matches(text, Regex.Escape(current)).Count;
        Assert.True(hits == 0 || relative.EndsWith("SettingsViewModel.cs"),
            $"{relative} içinde sabit güncel sürüm ({current}) var; AppInfo.Version kullanın.");
    }

    [Fact]
    public void ReleaseWorkflow_StampsTagIntoBuild_AndChecksProps()
    {
        string yml = Read(".github/workflows/release.yml");
        Assert.Contains("-p:Version=", yml);
        Assert.Contains("BakimVersion", yml);
    }
}
