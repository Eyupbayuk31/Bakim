using Bakım.Core.Text;
using Xunit;

namespace Bakim.Core.Tests;

public class SearchTextTests
{
    [Theory]
    [InlineData("Kaldırıcı", "kaldirici")]
    [InlineData("GEÇMİŞ", "gecmis")]
    [InlineData("Işık Öğesi Çöz", "isik ogesi coz")]
    public void Folds(string input, string expected) => Assert.Equal(expected, SearchText.Fold(input));

    [Fact]
    public void Scores_AllTokensRequired_TitlePreferred()
    {
        Assert.True(SearchText.Score("kaldirici gecmis", "Kaldırma geçmişi", "Kaldırıcı") > 0);
        Assert.Equal(0, SearchText.Score("kaldirici xyz", "Kaldırma geçmişi", "Kaldırıcı"));
        int title = SearchText.Score("temiz", "Temizleyici", "");
        int description = SearchText.Score("temiz", "Depolama", "temizlik önerileri");
        Assert.True(title > description && description > 0);
        Assert.Equal(1, SearchText.Score("  ", "x"));
    }
}
