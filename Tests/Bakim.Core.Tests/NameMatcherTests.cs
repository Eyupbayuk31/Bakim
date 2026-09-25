using Bakım.Core.Text;
using Xunit;

namespace Bakim.Core.Tests;

public class NameMatcherTests
{
    // Eski algoritmanın gerçek hatalarını yeniden üreten senaryolar.
    [Theory]
    [InlineData("Git", "The Git Development Community", "Digital Sound", null, MatchConfidence.None)]
    [InlineData("Git", "The Git Development Community", "Git", null, MatchConfidence.High)]
    [InlineData("VLC media player", "VideoLAN", "Windows Media Player", null, MatchConfidence.None)]
    [InlineData("VLC media player", "VideoLAN", "vlc", null, MatchConfidence.High)]
    [InlineData("VLC media player", "VideoLAN", "VLC", "VideoLAN", MatchConfidence.High)]
    [InlineData("Google Drive", "Google LLC", "Google", null, MatchConfidence.None)]
    [InlineData("Google Drive", "Google LLC", "Chrome", "Google", MatchConfidence.None)]
    [InlineData("Adobe Acrobat Reader", "Adobe", "Photoshop", "Adobe", MatchConfidence.None)]
    [InlineData("Adobe Acrobat Reader", "Adobe", "Adobe", null, MatchConfidence.None)]
    [InlineData("Adobe Acrobat Reader", "Adobe", "Acrobat", "Adobe", MatchConfidence.High)]
    [InlineData("Microsoft Edge", "Microsoft Corporation", "EdgeWebView", null, MatchConfidence.None)]
    [InlineData("Microsoft Edge", "Microsoft Corporation", "Microsoft", null, MatchConfidence.None)]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++ Team", "Notepad++", null, MatchConfidence.High)]
    [InlineData("7-Zip 24.08 (x64)", "Igor Pavlov", "7-Zip", null, MatchConfidence.High)]
    [InlineData("Microsoft Visual Studio Code (User)", "Microsoft Corporation", "Microsoft VS Code", null, MatchConfidence.None)]
    [InlineData("Discord", "Discord Inc.", "discord", null, MatchConfidence.High)]
    [InlineData("OBS Studio", "OBS Project", "obs-studio", null, MatchConfidence.High)]
    [InlineData("Mozilla Firefox (x64 tr)", "Mozilla", "Firefox", "Mozilla", MatchConfidence.High)]
    public void Match_ProducesExpectedConfidence(string app, string publisher, string name, string? parent, MatchConfidence expected)
    {
        var m = new NameMatcher(app, publisher);
        Assert.Equal(expected, m.Match(name, parent).Confidence);
    }

    [Fact]
    public void NameEvidence_IsNeverCertain()
    {
        var m = new NameMatcher("Some Application Name", "Vendor");
        var result = m.Match("Some Application Name");
        Assert.NotEqual(MatchConfidence.Certain, result.Confidence);
    }

    [Fact]
    public void PublisherFolder_IsFlaggedForLookInside()
    {
        var m = new NameMatcher("Google Drive", "Google LLC");
        Assert.True(m.IsPublisherName("Google"));
        Assert.Equal(MatchKind.PublisherRoot, m.Match("Google").Kind);
    }

    [Fact]
    public void LongDistinctiveToken_GivesLowConfidenceOnly()
    {
        var m = new NameMatcher("Blender", "Blender Foundation");
        Assert.Equal(MatchConfidence.High, m.Match("Blender").Confidence);
        Assert.Equal(MatchConfidence.Medium, m.Match("Blender Cache Files").Confidence);
    }

    [Theory]
    [InlineData("VideoLAN", new[] { "video", "lan" })]
    [InlineData("EdgeWebView", new[] { "edge", "web", "view" })]
    [InlineData("Notepad++", new[] { "notepad" })]
    [InlineData("obs-studio_v2", new[] { "obs", "studio", "v2" })]
    public void Tokenize_SplitsOnCaseAndPunctuation(string input, string[] expected)
    {
        Assert.Equal(expected, NameMatcher.Tokenize(input));
    }
}

public class NameMatcherGenericIdentityTests
{
    [Fact]
    public void GoogleDrive_OwnFolderUnderPublisher_IsHigh_ButStandaloneIsWeak()
    {
        var m = new NameMatcher("Google Drive", "Google LLC");
        Assert.Equal(MatchConfidence.High, m.Match("Drive", "Google").Confidence);
        Assert.Equal(MatchConfidence.Low, m.Match("Drive").Confidence);
        Assert.Equal(MatchConfidence.None, m.Match("Google Chrome").Confidence);
        Assert.Equal(MatchConfidence.None, m.Match("DriveFS", "Google").Confidence);
    }
}
