using System.IO;
using Bakım.Helpers;
using Bakım.Models;
using Xunit;
using Xunit.Abstractions;

namespace Bakim.Tests;

/// <summary>
/// Authenticode denetleyicisinin regresyon testleri.
///
/// v3.10'a kadar imza denetimi yalnızca WTD_CHOICE_FILE (gömülü imza) ile
/// yapılıyordu. Windows sistem ikililerinin büyük kısmı gömülü imza taşımaz;
/// imzaları CatRoot altındaki .cat dosyalarındadır. Sonuç: meşru Windows
/// bileşenleri "İmzasız" görünüp risk puanı alıyordu.
///
/// Aşağıdaki testler o hatanın geri gelmesini engeller.
/// </summary>
public class SignatureInspectorTests
{
    private readonly ITestOutputHelper _output;

    public SignatureInspectorTests(ITestOutputHelper output) => _output = output;

    private static string System32(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    public static TheoryData<string> SystemBinaries() => new()
    {
        "svchost.exe",   // klasik katalog imzalı
        "notepad.exe",
        "taskmgr.exe",
        "cmd.exe",
        "kernel32.dll",
    };

    [Theory]
    [MemberData(nameof(SystemBinaries))]
    public void WindowsSystemBinary_IsRecognisedAsSigned(string fileName)
    {
        string path = System32(fileName);

        if (!File.Exists(path))
        {
            _output.WriteLine($"ATLANDI: {path} bulunamadi");
            return; // ortama bagli; CI'i kirma
        }

        var report = SignatureInspector.Inspect(path);
        _output.WriteLine($"{fileName}: {report.Describe()} " +
                          $"(katalog={report.IsCatalogSigned}, kaynak={report.CatalogPath})");

        Assert.Equal(SignatureStatus.Verified, report.Status);
        Assert.False(string.IsNullOrWhiteSpace(report.Signer),
            $"{fileName} dogrulandi ama imzalayan adi okunamadi");
    }

    [Fact]
    public void SignedBinary_ReportsMicrosoftAsSigner()
    {
        string path = System32("svchost.exe");
        if (!File.Exists(path)) return;

        var report = SignatureInspector.Inspect(path);

        Assert.Contains("Microsoft", report.Signer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsignedFile_IsReportedUnsigned_WithoutThrowing()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"bakim_unsigned_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(temp, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });

        try
        {
            var report = SignatureInspector.Inspect(temp);

            Assert.Equal(SignatureStatus.Unsigned, report.Status);
            Assert.False(report.IsTrusted);
            Assert.Equal("İmzasız", report.Describe());
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\olmayan\yol\dosya.exe")]
    public void MissingOrInvalidPath_ReturnsUnsigned_WithoutThrowing(string path)
    {
        var report = SignatureInspector.Inspect(path);

        Assert.Equal(SignatureStatus.Unsigned, report.Status);
    }

    [Fact]
    public void Describe_NeverReturnsEmpty()
    {
        // Kullanıcıya boş bir imza etiketi gösterilmemeli
        foreach (SignatureStatus status in Enum.GetValues<SignatureStatus>())
        {
            var report = new SignatureReport { Status = status, Signer = "Test Corp." };
            Assert.False(string.IsNullOrWhiteSpace(report.Describe()));
        }
    }
}
