using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

public sealed class DiskMapServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bakim-diskmap-" + Guid.NewGuid().ToString("N"));

    public DiskMapServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "big", "inner"));
        Directory.CreateDirectory(Path.Combine(_root, "small"));
        File.WriteAllBytes(Path.Combine(_root, "big", "a.bin"), new byte[3000]);
        File.WriteAllBytes(Path.Combine(_root, "big", "inner", "b.bin"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(_root, "small", "c.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "root.bin"), new byte[50]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task Scan_ComputesRecursiveSizes_AndFileCounts()
    {
        var node = await new DiskMapService().ScanAsync(_root, null, CancellationToken.None);

        Assert.Equal(5150, node.SizeBytes);
        Assert.Equal(4, node.FileCount);
        var big = node.Children.Single(c => c.Name == "big");
        Assert.Equal(5000, big.SizeBytes);
        Assert.Same(node, big.Parent);
        // Kökteki doğrudan dosyalar "Bu klasördeki dosyalar" düğümünde
        var direct = node.Children.Single(c => c.IsAggregate);
        Assert.Equal(50, direct.SizeBytes);
    }

    [Fact]
    public async Task Scan_IsCancellable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DiskMapService().ScanAsync(_root, null, cts.Token));
    }
}
