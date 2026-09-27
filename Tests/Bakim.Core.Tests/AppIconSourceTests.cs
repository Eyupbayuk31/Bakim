using System;
using System.Collections.Generic;
using Bakım.Core.Uninstall;
using Xunit;

namespace Bakim.Core.Tests;

public class AppIconSourceTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App\app.exe", 0)]
    [InlineData(@"C:\Program Files\App\app.exe,-101", @"C:\Program Files\App\app.exe", -101)]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App\app.exe", 0)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam\games\abc.ico", @"C:\Program Files (x86)\Steam\steam\games\abc.ico", 0)]
    [InlineData(@"""C:\App\res.dll"", 3", @"C:\App\res.dll", 3)]
    [InlineData(@"%ProgramFiles%\App\app.exe,1", @"C:\Program Files\App\app.exe", 1)]
    public void ParsesDisplayIcon(string raw, string path, int index)
    {
        var loc = AppIconSource.ParseDisplayIcon(raw, s => s.Replace("%ProgramFiles%", @"C:\Program Files"));
        Assert.NotNull(loc);
        Assert.Equal(path, loc!.Path);
        Assert.Equal(index, loc.Index);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"C:\App\readme.txt")]
    [InlineData(@"C:\App\app.exe,abc")]
    public void RejectsUnusable(string? raw) => Assert.Null(AppIconSource.ParseDisplayIcon(raw));

    [Fact]
    public void Candidates_OrderAndFiltering()
    {
        var disk = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\App\app.exe", @"C:\App\unins000.exe", @"C:\Windows\System32\msiexec.exe" };
        var list = AppIconSource.Candidates(@"C:\App\gone.ico", @"C:\App\app.exe", @"C:\App\unins000.exe", disk.Contains);
        Assert.Equal(new[] { @"C:\App\app.exe", @"C:\App\unins000.exe" }, list.ConvertAll(l => l.Path));

        var msi = AppIconSource.Candidates(null, null, @"C:\Windows\System32\msiexec.exe", disk.Contains);
        Assert.Empty(msi);
    }
}

file static class ListExtensions
{
    public static List<TOut> ConvertAll<TIn, TOut>(this IReadOnlyList<TIn> list, Func<TIn, TOut> map)
    {
        var result = new List<TOut>(list.Count);
        foreach (var item in list) result.Add(map(item));
        return result;
    }
}
