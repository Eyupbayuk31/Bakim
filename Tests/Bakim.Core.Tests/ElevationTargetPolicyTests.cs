using Bakım.Core.Security;
using Xunit;

namespace Bakim.Core.Tests;

public class ElevationTargetPolicyTests
{
    private const string UsersSid = "S-1-5-32-545";
    private const string AuthenticatedUsersSid = "S-1-5-11";
    private const string SomeUserSid = "S-1-5-21-1-2-3-1001";
    private const int ReadAndExecute = 0x1200A9;
    private const int Modify = 0x1301BF;
    private const int FullControl = 0x1F01FF;
    private const int WriteAttributesOnly = 0x0100;

    private static SecuritySnapshot Secure(string path) => new(path, ElevationTargetPolicy.AdministratorsSid, new[]
    {
        new AccessEntry(ElevationTargetPolicy.SystemSid, FullControl, true, false),
        new AccessEntry(ElevationTargetPolicy.AdministratorsSid, FullControl, true, false),
        new AccessEntry(ElevationTargetPolicy.TrustedInstallerSid, FullControl, true, false),
        new AccessEntry(UsersSid, ReadAndExecute, true, false),
        new AccessEntry(ElevationTargetPolicy.CreatorOwnerSid, 0x1000_0000, true, true),
    });

    [Fact]
    public void DefaultProgramFilesAcl_IsSafe()
    {
        Assert.Null(ElevationTargetPolicy.FindUntrustedWriter(Secure(@"C:\Program Files\App\app.exe")));
    }

    [Fact]
    public void UsersWithModify_IsUnsafe()
    {
        var s = Secure(@"C:\Program Files (x86)\Steam");
        s = s with { Entries = s.Entries.Append(new AccessEntry(UsersSid, Modify, true, false)).ToList() };
        Assert.Equal(UsersSid, ElevationTargetPolicy.FindUntrustedWriter(s));
    }

    [Fact]
    public void NonAdminOwner_IsUnsafe_BecauseOwnerCanRewriteDacl()
    {
        var s = Secure(@"C:\Program Files\App\app.exe") with { OwnerSid = SomeUserSid };
        Assert.Equal(SomeUserSid, ElevationTargetPolicy.FindUntrustedWriter(s));
    }

    [Fact]
    public void InheritOnlyAndDenyAndAttributeOnlyEntries_AreIgnored()
    {
        var s = Secure(@"C:\Program Files\App");
        s = s with
        {
            Entries = s.Entries
                .Append(new AccessEntry(AuthenticatedUsersSid, Modify, true, true))      // yalnızca alt nesnelere
                .Append(new AccessEntry(AuthenticatedUsersSid, Modify, false, false))    // Deny
                .Append(new AccessEntry(SomeUserSid, WriteAttributesOnly, true, false))  // öznitelik
                .ToList()
        };
        Assert.Null(ElevationTargetPolicy.FindUntrustedWriter(s));
    }

    [Fact]
    public void CreateFilesOnDirectory_IsUnsafe_DllPlanting()
    {
        var s = Secure(@"C:\Program Files\App");
        s = s with { Entries = s.Entries.Append(new AccessEntry(UsersSid, 0x0002, true, false)).ToList() };
        Assert.Equal(UsersSid, ElevationTargetPolicy.FindUntrustedWriter(s));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files")]
    [InlineData(@"c:\program files (x86)\App\app.exe", @"C:\Program Files (x86)")]
    [InlineData(@"C:\Windows\System32\cmd.exe", @"C:\Windows")]
    [InlineData(@"C:\Users\Ali\AppData\Local\App\app.exe", null)]
    [InlineData(@"C:\Program Files Evil\app.exe", null)]
    [InlineData(@"C:\Program Files\..\Users\x.exe", null)]
    [InlineData(@"C:\Program Files", null)]
    public void OnlyProgramFilesAndWindows_AreAllowed(string target, string? expectedRoot)
    {
        var roots = new[] { @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows" };
        Assert.Equal(expectedRoot, ElevationTargetPolicy.FindAllowedRoot(target, roots));
    }

    [Fact]
    public void ChainToRoot_ListsFileParentsAndRoot()
    {
        var chain = ElevationTargetPolicy.ChainToRoot(@"C:\Program Files\Vendor\App\app.exe", @"C:\Program Files");
        Assert.Equal(new[]
        {
            @"C:\Program Files\Vendor\App\app.exe",
            @"C:\Program Files\Vendor\App",
            @"C:\Program Files\Vendor",
            @"C:\Program Files"
        }, chain);
    }

    [Fact]
    public void FindFirstUnsafe_ReportsTheWritableParent()
    {
        var chain = new[]
        {
            Secure(@"C:\Program Files\Vendor\App\app.exe"),
            Secure(@"C:\Program Files\Vendor\App") with { OwnerSid = SomeUserSid },
            Secure(@"C:\Program Files\Vendor"),
        };
        var unsafeItem = ElevationTargetPolicy.FindFirstUnsafe(chain);
        Assert.NotNull(unsafeItem);
        Assert.Equal(@"C:\Program Files\Vendor\App", unsafeItem.Value.Path);
    }
}
