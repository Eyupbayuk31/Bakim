using System;
using Bakım.Core.Uninstall;
using Xunit;

namespace Bakim.Core.Tests
{
    public sealed class MsiGuidHelperTests
    {
        [Theory]
        [InlineData("{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}", "3458F6C58AE79B047BF8407C3800EDF7")]
        [InlineData("5C6F8543-7EA8-40B9-B78F-04C78300DE7F", "3458F6C58AE79B047BF8407C3800EDF7")]
        [InlineData("{06431BDC-FD40-8074-7ADF-C44B0A866450}", "CDB1346004DF4708A7FD4CB4A0684605")]
        public void ToPackedGuid_ProducesExactWindowsInstallerFormat(string input, string expectedPacked)
        {
            string? actual = MsiGuidHelper.ToPackedGuid(input);
            Assert.Equal(expectedPacked, actual);
        }

        [Theory]
        [InlineData("3458F6C58AE79B047BF8407C3800EDF7", "{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}")]
        [InlineData("CDB1346004DF4708A7FD4CB4A0684605", "{06431BDC-FD40-8074-7ADF-C44B0A866450}")]
        public void FromPackedGuid_ReversesToOriginalGuid(string packed, string expectedGuid)
        {
            string? actual = MsiGuidHelper.FromPackedGuid(packed);
            Assert.Equal(expectedGuid, actual);
        }

        [Theory]
        [InlineData("MsiExec.exe /I{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}", null, "{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}")]
        [InlineData("MsiExec.exe /X{5C6F8543-7EA8-40B9-B78F-04C78300DE7F} /qn", null, "{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}")]
        [InlineData(null, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}", "{5C6F8543-7EA8-40B9-B78F-04C78300DE7F}")]
        [InlineData("\"C:\\Program Files\\App\\uninstall.exe\"", null, null)]
        public void ExtractProductCode_ExtractsCorrectGuid(string? uninstallString, string? registryKey, string? expected)
        {
            string? actual = MsiGuidHelper.ExtractProductCode(uninstallString, registryKey);
            Assert.Equal(expected, actual);
        }
    }
}
