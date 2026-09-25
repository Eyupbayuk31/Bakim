using System;
using Bakım.Core.Sentinel;
using Xunit;

namespace Bakim.Core.Tests
{
    public class WindowsSandboxConfigurationTests
    {
        [Fact]
        public void BuildWsbXml_ThrowsOnEmptyHostFolder()
        {
            Assert.Throws<ArgumentException>(() => WindowsSandboxConfiguration.BuildWsbXml(""));
        }

        [Fact]
        public void BuildWsbXml_GeneratesValidWsbXmlStructure()
        {
            string hostFolder = @"C:\TestInstallers";
            string logonCmd = WindowsSandboxConfiguration.GenerateInstallerLogonCommand("setup.exe");

            string xml = WindowsSandboxConfiguration.BuildWsbXml(hostFolder, logonCmd, readOnly: true, enableNetworking: false);

            Assert.Contains("<Configuration>", xml);
            Assert.Contains("</Configuration>", xml);
            Assert.Contains("<VGpu>Default</VGpu>", xml);
            Assert.Contains("<Networking>Disable</Networking>", xml);
            Assert.Contains("<MappedFolders>", xml);
            Assert.Contains("<ReadOnly>true</ReadOnly>", xml);
            Assert.Contains(@"<SandboxFolder>C:\SandboxShared</SandboxFolder>", xml);
            Assert.Contains(@"C:\SandboxShared\setup.exe", xml);
            Assert.Contains("<LogonCommand>", xml);
        }

        [Fact]
        public void GenerateInstallerLogonCommand_ExtractsBaseNameSafely()
        {
            string cmd = WindowsSandboxConfiguration.GenerateInstallerLogonCommand(@"D:\Downloads\Complex Installer (v2).exe");
            Assert.Equal(@"cmd.exe /c start """" ""C:\SandboxShared\Complex Installer (v2).exe""", cmd);
        }
    }
}
