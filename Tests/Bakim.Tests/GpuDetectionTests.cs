using System;
using System.Threading.Tasks;
using Bakım.Models;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests
{
    public class GpuDetectionTests
    {
        [Fact]
        public void FormatVramGB_ZeroOrMinimalVram_ReturnsSharedOrExternal_NeverZeroGb()
        {
            // Zero or tiny VRAM must never format as "0 GB VRAM"
            string integratedZero = GpuInfoProvider.FormatVramGB(0.0, isDiscrete: false);
            string discreteZero = GpuInfoProvider.FormatVramGB(0.0, isDiscrete: true);
            string tinyIntegrated = GpuInfoProvider.FormatVramGB(0.04, isDiscrete: false);

            Assert.Equal("Paylaşımlı VRAM", integratedZero);
            Assert.Equal("Harici Bellek", discreteZero);
            Assert.Equal("Paylaşımlı VRAM", tinyIntegrated);
            Assert.DoesNotContain("0 GB", integratedZero);
            Assert.DoesNotContain("0 GB", discreteZero);
        }

        [Fact]
        public void FormatVramGB_128MbDvmtIntegrated_ReturnsPaylasimliVram_NeverZeroGb()
        {
            // Intel HD Graphics 128 MB DVMT (0.125 GB)
            double dvmtGb = 134217728.0 / (1024.0 * 1024.0 * 1024.0); // ~0.125 GB
            string formatted = GpuInfoProvider.FormatVramGB(dvmtGb, isDiscrete: false);

            Assert.Equal("Paylaşımlı VRAM", formatted);
            Assert.DoesNotContain("0 GB", formatted);
        }

        [Fact]
        public void FormatVramGB_SubGigabyteDiscrete_ReturnsMegabytes()
        {
            // 512 MB Discrete GPU (0.5 GB)
            string formatted = GpuInfoProvider.FormatVramGB(0.5, isDiscrete: true);

            Assert.Equal("512 MB VRAM", formatted);
        }

        [Theory]
        [InlineData(1.0, "1 GB VRAM")]
        [InlineData(2.0, "2 GB VRAM")]
        [InlineData(4.0, "4 GB VRAM")]
        [InlineData(6.0, "6 GB VRAM")]
        [InlineData(8.0, "8 GB VRAM")]
        [InlineData(12.0, "12 GB VRAM")]
        [InlineData(16.0, "16 GB VRAM")]
        [InlineData(24.0, "24 GB VRAM")]
        public void FormatVramGB_StandardCapacities_ReturnsExactGigabytes(double vramGb, string expected)
        {
            string result = GpuInfoProvider.FormatVramGB(vramGb, isDiscrete: true);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FormatVramGB_NonStandardCapacity_ReturnsDecimalGigabytes()
        {
            string result = GpuInfoProvider.FormatVramGB(3.5, isDiscrete: true);
            Assert.Equal("3.5 GB VRAM", result);
        }

        [Fact]
        public void GetGpuConfiguration_ResolvesPrimaryGpuWithoutCrash()
        {
            var config = GpuInfoProvider.GetGpuConfiguration(forceRefresh: true);

            Assert.NotNull(config);
            Assert.NotNull(config.PrimaryGpu);
            Assert.False(string.IsNullOrWhiteSpace(config.PrimaryGpu.Name));
            Assert.False(string.IsNullOrWhiteSpace(config.PrimaryGpu.FormattedVram));
            Assert.DoesNotContain("0 GB VRAM", config.PrimaryGpu.FormattedVram);
            Assert.NotEmpty(config.AllGpus);
        }

        [Fact]
        public void DualGpuSystem_DetectsBothDiscreteAndIntegrated()
        {
            var config = GpuInfoProvider.GetGpuConfiguration();

            // Dual-GPU laptop (e.g. AMD Radeon + Intel HD Graphics)
            if (config.HasDualGpu)
            {
                Assert.NotNull(config.SecondaryGpu);
                Assert.True(config.PrimaryGpu.IsDiscrete);
                Assert.False(string.IsNullOrWhiteSpace(config.SecondaryGpu.Name));
                Assert.Contains("Çift GPU", config.DetailedTooltip);
                Assert.Contains(config.PrimaryGpu.Name, config.DetailedTooltip);
                Assert.Contains(config.SecondaryGpu.Name, config.DetailedTooltip);
            }
        }

        [Fact]
        public async Task SystemInfoAndTelemetry_ProvideConsistentGpuDetails()
        {
            var sysInfoService = new SystemInfoService();
            var telemetryService = new TelemetryService();

            var hardware = await sysInfoService.GetSystemHardwareAsync();
            var metrics = await telemetryService.SampleMetricsAsync();

            Assert.Equal(hardware.GpuName, metrics.GpuName);
            Assert.Equal(hardware.GpuVram, metrics.GpuVram);
            Assert.Equal(hardware.HasDualGpu, metrics.HasDualGpu);
            Assert.DoesNotContain("0 GB VRAM", hardware.GpuVram);
            Assert.DoesNotContain("0 GB VRAM", metrics.GpuVram);
        }
    }
}
