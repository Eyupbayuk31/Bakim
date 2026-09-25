using System.Linq;
using Bakım.Core.Store;
using Xunit;

namespace Bakim.Core.Tests;

public class WingetTableTests
{
    private const string English =
        "   - \r   \\ \r\n" +
        "Name                     Id                         Version     Available   Source\n" +
        "--------------------------------------------------------------------------------------\n" +
        "7-Zip 23.01 (x64)        7zip.7zip                  23.01       24.08       winget\n" +
        "Mozilla Firefox (x64 tr) Mozilla.Firefox            128.0       129.0.1     winget\n" +
        "\n" +
        "2 upgrades available.\n";

    private const string Turkish =
        "Ad                  Kimlik              Sürüm       Kullanılabilir Kaynak\n" +
        "--------------------------------------------------------------------------\n" +
        "Notepad++ (64-bit)  Notepad++.Notepad++ 8.6.2       8.6.9          winget\n";

    [Fact]
    public void ParsesEnglishTable_IgnoringSpinnerAndFooter()
    {
        var rows = WingetTable.ParseUpgrades(English);
        Assert.Equal(2, rows.Count);
        Assert.Equal("7zip.7zip", rows[0].Id);
        Assert.Equal("24.08", rows[0].Available);
        Assert.Equal("Mozilla Firefox (x64 tr)", rows[1].Name);
        Assert.Equal("winget", rows[1].Source);
    }

    [Fact]
    public void ParsesLocalizedHeaders()
    {
        var row = WingetTable.ParseUpgrades(Turkish).Single();
        Assert.Equal("Notepad++.Notepad++", row.Id);
        Assert.Equal("8.6.2", row.Version);
        Assert.Equal("8.6.9", row.Available);
    }

    [Fact]
    public void NoTable_NoRows()
    {
        Assert.Empty(WingetTable.ParseUpgrades("No installed package found matching input criteria."));
        Assert.Empty(WingetTable.ParseUpgrades(null));
    }
}
