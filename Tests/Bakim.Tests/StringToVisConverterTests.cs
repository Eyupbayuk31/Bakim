using System.Globalization;
using System.Windows;
using Bakım.Converters;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// StringToVis iki kiplidir: parametreli (eşitlik) ve parametresiz (dolu dize).
/// Parametresiz kip eskiden her zaman Collapsed döndürüyordu; başlık açıklamaları,
/// rozet noktaları ve çip değerleri bu yüzden hiç görünmüyordu.
/// </summary>
public sealed class StringToVisConverterTests
{
    private static Visibility Convert(object? value, object? parameter, bool invert = false) =>
        (Visibility)new StringToVisConverter { Invert = invert }
            .Convert(value!, typeof(Visibility), parameter!, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("Açıklama", Visibility.Visible)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("   ", Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void WithoutParameter_ShowsNonEmptyStrings(string? value, Visibility expected) =>
        Assert.Equal(expected, Convert(value, null));

    [Theory]
    [InlineData("Açıklama", Visibility.Collapsed)]
    [InlineData("", Visibility.Visible)]
    [InlineData(null, Visibility.Visible)]
    public void WithoutParameter_Inverted_ShowsEmptyStrings(string? value, Visibility expected) =>
        Assert.Equal(expected, Convert(value, null, invert: true));

    [Theory]
    [InlineData("Services", "Services", Visibility.Visible)]
    [InlineData("services", "Services", Visibility.Visible)]
    [InlineData("Drivers", "Services", Visibility.Collapsed)]
    [InlineData(null, "Services", Visibility.Collapsed)]
    public void WithParameter_MatchesIgnoringCase(string? value, string parameter, Visibility expected) =>
        Assert.Equal(expected, Convert(value, parameter));

    [Fact]
    public void WithParameter_Inverted_ShowsNonMatching() =>
        Assert.Equal(Visibility.Visible, Convert("Drivers", "Services", invert: true));
}
