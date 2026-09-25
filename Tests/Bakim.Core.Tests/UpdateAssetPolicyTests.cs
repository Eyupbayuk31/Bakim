using System;
using Bakım.Core.Update;
using Xunit;

namespace Bakim.Core.Tests;

public class UpdateAssetPolicyTests
{
    [Theory]
    [InlineData("Bakim-v3.21.0-Setup.exe", true)]
    [InlineData("bakim-3.21.0-setup.exe", true)]
    [InlineData("Bakim-v3.21.0.1-Setup.exe", true)]
    [InlineData("Bakim.exe", false)]
    [InlineData("Bakim-v3.21.0-Setup.exe.zip", false)]
    [InlineData("Bakim-Setup.exe", false)]
    [InlineData("evil.exe", false)]
    [InlineData(null, false)]
    public void InstallerAssetName(string? name, bool expected) =>
        Assert.Equal(expected, UpdateAssetPolicy.IsInstallerAssetName(name));

    [Theory]
    [InlineData("https://github.com/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", true)]
    [InlineData("https://GitHub.com/eyupbayuk31/bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", true)]
    [InlineData("http://github.com/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://github.com/Attacker/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://github.com/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim.exe", false)]
    [InlineData("https://github.com/Eyupbayuk31/Bakim/releases/download/x/y/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://github.com.evil.io/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://user@github.com/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://github.com:8443/Eyupbayuk31/Bakim/releases/download/v3.21.0/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("https://objects.githubusercontent.com/Bakim-v3.21.0-Setup.exe", false)]
    [InlineData("", false)]
    public void OfficialDownloadUrl(string url, bool expected) =>
        Assert.Equal(expected, UpdateAssetPolicy.IsOfficialDownloadUrl(url));

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://evil.example/x", false)]
    [InlineData("http://objects.githubusercontent.com/x", false)]
    public void FinalHost(string url, bool expected) =>
        Assert.Equal(expected, UpdateAssetPolicy.IsAllowedFinalHost(new Uri(url)));

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(99, 100, false)]
    [InlineData(100, 0, true)]
    [InlineData(0, 0, false)]
    public void Size(long downloaded, long expected, bool ok) =>
        Assert.Equal(ok, UpdateAssetPolicy.IsSizeAcceptable(downloaded, expected));
}
