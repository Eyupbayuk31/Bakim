using System.Linq;
using Bakım.Core.Network;
using Xunit;

namespace Bakim.Core.Tests
{
    public class NetworkDiagnosticsTests
    {
        [Fact]
        public void TracerouteHop_Formatting_Success()
        {
            var hop = new TracerouteHop(1, "192.168.1.1", "modem.local", 2, true, "OK");
            Assert.True(hop.Success);
            Assert.Contains("192.168.1.1", hop.DisplayText);
            Assert.Contains("2 ms", hop.DisplayText);
        }

        [Fact]
        public void TracerouteHop_Formatting_Timeout()
        {
            var hop = new TracerouteHop(3, "*", "*", -1, false, "Timeout");
            Assert.False(hop.Success);
            Assert.Contains("Zaman aşımı", hop.DisplayText);
        }

        [Fact]
        public void DefaultDnsServers_ArePopulated()
        {
            var servers = DnsBenchmarkServer.DefaultServers;
            Assert.NotEmpty(servers);
            Assert.Contains(servers, s => s.Provider == "Cloudflare" && s.PrimaryIp == "1.1.1.1");
            Assert.Contains(servers, s => s.Provider == "Google" && s.PrimaryIp == "8.8.8.8");
            Assert.Contains(servers, s => s.Provider == "Quad9" && s.PrimaryIp == "9.9.9.9");
        }

        [Theory]
        [InlineData(true, true, "Dolaşım")]
        [InlineData(true, false, "Ölçülü")]
        [InlineData(false, false, "Sınırsız")]
        public void MeteredNetworkPolicy_DescribeCost(bool metered, bool roaming, string expectedSubstring)
        {
            string desc = MeteredNetworkPolicy.DescribeCost(metered, roaming);
            Assert.Contains(expectedSubstring, desc);
        }
    }
}
