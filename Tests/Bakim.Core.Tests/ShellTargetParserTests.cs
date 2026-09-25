using Bakım.Core.Shell;
using Xunit;

namespace Bakim.Core.Tests
{
    public class ShellTargetParserTests
    {
        [Fact]
        public void ParseTarget_WithNullOrEmpty_ReturnsNull()
        {
            Assert.Null(ShellTargetParser.ParseTarget(null));
            Assert.Null(ShellTargetParser.ParseTarget([]));
        }

        [Fact]
        public void ParseTarget_WithUninstallTargetFlag_ReturnsTarget()
        {
            string[] args = ["--uninstall-target", @"C:\Program Files\App\app.exe"];
            string? result = ShellTargetParser.ParseTarget(args);
            Assert.Equal(@"C:\Program Files\App\app.exe", result);
        }

        [Fact]
        public void ParseTarget_WithUninstallTargetFlagAndQuotes_StripsQuotes()
        {
            string[] args = ["--uninstall-target", "\"C:\\Program Files\\App\\app.exe\""];
            string? result = ShellTargetParser.ParseTarget(args);
            Assert.Equal(@"C:\Program Files\App\app.exe", result);
        }

        [Fact]
        public void ParseTarget_WithCombinedFlag_ReturnsTarget()
        {
            string[] args = ["--uninstall-target=\"D:\\Games\\Game.exe\""];
            string? result = ShellTargetParser.ParseTarget(args);
            Assert.Equal(@"D:\Games\Game.exe", result);
        }

        [Fact]
        public void ParseTarget_WithDirectFilePath_ReturnsPath()
        {
            string[] args = [@"C:\Users\User\Desktop\Shortcut.lnk"];
            string? result = ShellTargetParser.ParseTarget(args);
            Assert.Equal(@"C:\Users\User\Desktop\Shortcut.lnk", result);
        }

        [Fact]
        public void ParseTarget_WithUnrelatedFlag_ReturnsNull()
        {
            string[] args = ["--minimized", "--tray"];
            string? result = ShellTargetParser.ParseTarget(args);
            Assert.Null(result);
        }
    }
}
