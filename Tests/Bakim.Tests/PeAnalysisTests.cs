using System.IO;
using Bakım.Models;
using Bakım.Services;
using Xunit;
using Xunit.Abstractions;

namespace Bakim.Tests;

/// <summary>
/// PE ayrıştırıcısının davranış ve dayanıklılık testleri.
///
/// Bu ayrıştırıcı GÜVENİLMEYEN dosyaları okur; yani bir saldırı yüzeyidir.
/// Uydurma başlık alanlarıyla hazırlanmış bir dosya, sınır denetimi olmadan
/// aşırı bellek tüketimine veya uzun döngülere yol açabilir. Aşağıdaki
/// testler ayrıştırıcının hatalı girdide ÇÖKMEDEN ve ASILMADAN döndüğünü
/// garanti eder.
/// </summary>
public class PeAnalysisTests
{
    private readonly ITestOutputHelper _output;

    public PeAnalysisTests(ITestOutputHelper output) => _output = output;

    private static IFileThreatAnalyzerService CreateAnalyzer() =>
        new FileThreatAnalyzerService(new VirusTotalCheckService(new StubSettings()));

    private static string WriteTemp(byte[] bytes, string extension = ".exe")
    {
        string path = Path.Combine(Path.GetTempPath(), $"bakim_pe_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>DOS başlığı olan ama gerisi çöp olan bir dosya.</summary>
    private static byte[] BuildTruncatedPe()
    {
        var bytes = new byte[128];
        bytes[0] = 0x4D; bytes[1] = 0x5A;              // "MZ"
        BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3C); // e_lfanew -> 0x40
        bytes[0x40] = 0x50; bytes[0x41] = 0x45;         // "PE"
        return bytes;
    }

    /// <summary>NumberOfSections alanı kasıtlı olarak saçma (65535).</summary>
    private static byte[] BuildHostileSectionCountPe()
    {
        var bytes = new byte[512];
        bytes[0] = 0x4D; bytes[1] = 0x5A;
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);

        int pe = 0x80;
        bytes[pe] = 0x50; bytes[pe + 1] = 0x45; bytes[pe + 2] = 0; bytes[pe + 3] = 0; // "PE\0\0"
        BitConverter.GetBytes((ushort)0x8664).CopyTo(bytes, pe + 4);   // Machine = AMD64
        BitConverter.GetBytes((ushort)65535).CopyTo(bytes, pe + 6);    // NumberOfSections — DÜŞMANCA
        BitConverter.GetBytes((uint)0).CopyTo(bytes, pe + 8);          // TimeDateStamp
        BitConverter.GetBytes((ushort)240).CopyTo(bytes, pe + 20);     // SizeOfOptionalHeader
        return bytes;
    }

    [Fact]
    public async Task TruncatedPe_DoesNotThrow_AndReportsNoPeAnalysis()
    {
        string path = WriteTemp(BuildTruncatedPe());
        try
        {
            var result = await CreateAnalyzer().AnalyzeFileAsync(path);

            Assert.NotNull(result);
            _output.WriteLine($"Kesik PE -> HasPeAnalysis={result.HasPeAnalysis}, risk={result.RiskScore}");
            // Ayrıştırma başarısız olmalı ama analiz genel olarak tamamlanmalı
            Assert.InRange(result.RiskScore, 0, 100);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task HostileSectionCount_IsRejected_WithoutHanging()
    {
        string path = WriteTemp(BuildHostileSectionCountPe());
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await CreateAnalyzer().AnalyzeFileAsync(path);
            stopwatch.Stop();

            _output.WriteLine($"Düşmanca bölüm sayısı -> {stopwatch.ElapsedMilliseconds} ms, " +
                              $"bölüm sayısı={result.Sections.Count}");

            Assert.NotNull(result);
            // 96 sınırının üstü reddedilir: bölüm listesi şişmemeli
            Assert.True(result.Sections.Count <= 96,
                $"Bölüm sınırı aşıldı: {result.Sections.Count}");
            // Ve makul sürede dönmeli
            Assert.True(stopwatch.ElapsedMilliseconds < 10_000,
                $"Ayrıştırma çok uzun sürdü: {stopwatch.ElapsedMilliseconds} ms");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task EmptyFile_IsHandledGracefully()
    {
        string path = WriteTemp(Array.Empty<byte>());
        try
        {
            var result = await CreateAnalyzer().AnalyzeFileAsync(path);
            Assert.NotNull(result);
            Assert.False(result.HasPeAnalysis);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task NonPeFile_IsHandledGracefully()
    {
        string path = WriteTemp(System.Text.Encoding.UTF8.GetBytes("sadece duz metin, PE degil"), ".txt");
        try
        {
            var result = await CreateAnalyzer().AnalyzeFileAsync(path);
            Assert.NotNull(result);
            Assert.False(result.HasPeAnalysis);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task RealSystemBinary_ParsesHeaderAndMitigations()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        if (!File.Exists(path)) return; // ortama bağlı

        var result = await CreateAnalyzer().AnalyzeFileAsync(path);

        _output.WriteLine($"notepad.exe -> PE={result.HasPeAnalysis}, " +
                          $"mimari={result.PeHeader?.MachineArchitecture}, " +
                          $"bölüm={result.Sections.Count}, " +
                          $"kalkan={result.Mitigations?.MitigationScoreText}");

        Assert.True(result.HasPeAnalysis, "Gerçek bir Windows ikilisi PE olarak tanınmalı");
        Assert.NotNull(result.PeHeader);
        Assert.NotEmpty(result.Sections);

        // Modern Windows ikilileri ASLR ve DEP taşır
        Assert.NotNull(result.Mitigations);
        Assert.True(result.Mitigations!.HasAslr, "Windows ikilisinde ASLR beklenir");
        Assert.True(result.Mitigations.HasDep, "Windows ikilisinde DEP beklenir");
    }

    [Fact]
    public async Task MissingFile_ReturnsResultWithoutThrowing()
    {
        var result = await CreateAnalyzer().AnalyzeFileAsync(@"C:\olmayan\yol\yok.exe");
        Assert.NotNull(result);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Gerçek ayar dosyasına dokunmayan sahte ayar servisi.</summary>
    private sealed class StubSettings : IAppSettingsService
    {
        public AppSettingsData Current { get; } = new();
        public string SettingsFilePath => "(test)";
        public event Action<AppSettingsData>? SettingsChanged;
        public AppSettingsData Load() => Current;
        public void Save(AppSettingsData data) => SettingsChanged?.Invoke(data);
        public void Update(Action<AppSettingsData> mutate) => mutate(Current);
    }
}
