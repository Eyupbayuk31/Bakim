using System;
using System.Linq;
using Bakım.Core.Storage;
using Xunit;

namespace Bakim.Core.Tests;

public sealed class TreemapTests
{
    private static readonly TreemapRect Bounds = new(0, 0, 600, 400);

    [Fact]
    public void Areas_AreProportional_AndFillBounds()
    {
        double[] values = { 6, 6, 4, 3, 2, 2, 1 };
        var rects = Treemap.Layout(values, Bounds);

        double total = values.Sum();
        for (int i = 0; i < values.Length; i++)
            Assert.Equal(values[i] / total * Bounds.Area, rects[i].Area, 6);
        Assert.Equal(Bounds.Area, rects.Sum(r => r.Area), 6);
    }

    [Fact]
    public void Rectangles_StayInsideBounds_AndDoNotOverlap()
    {
        double[] values = { 50, 30, 10, 5, 3, 1, 1 };
        var rects = Treemap.Layout(values, Bounds);
        const double eps = 1e-6;
        foreach (var r in rects)
        {
            Assert.True(r.X >= -eps && r.Y >= -eps);
            Assert.True(r.X + r.Width <= Bounds.Width + eps && r.Y + r.Height <= Bounds.Height + eps);
        }
        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
        {
            var a = rects[i]; var b = rects[j];
            double ox = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
            double oy = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
            Assert.False(ox > eps && oy > eps, $"{i} ve {j} çakışıyor");
        }
    }

    [Fact]
    public void Squarified_KeepsAspectRatiosReasonable()
    {
        double[] values = Enumerable.Range(1, 20).Select(i => (double)i).Reverse().ToArray();
        var rects = Treemap.Layout(values, Bounds);
        double worst = rects.Max(r => Math.Max(r.Width / r.Height, r.Height / r.Width));
        Assert.True(worst < 6, $"en kötü oran {worst:F2}");
    }

    [Fact]
    public void ZeroAndNegative_AreSkipped()
    {
        var rects = Treemap.Layout(new double[] { 5, 0, -1, 5 }, Bounds);
        Assert.Equal(0, rects[1].Area);
        Assert.Equal(0, rects[2].Area);
        Assert.Equal(Bounds.Area / 2, rects[0].Area, 6);
    }

    [Fact]
    public void EmptyOrZeroBounds_ReturnEmptyRects()
    {
        Assert.All(Treemap.Layout(new double[] { 1, 2 }, new TreemapRect(0, 0, 0, 100)), r => Assert.Equal(0, r.Area));
        Assert.Empty(Treemap.Layout(Array.Empty<double>(), Bounds));
    }

    [Fact]
    public void Compact_KeepsTopChildren_AndAggregatesRest()
    {
        var root = new FolderNode("C:", @"C:\");
        for (int i = 1; i <= 10; i++)
            root.Children.Add(new FolderNode("d" + i, @"C:\d" + i) { SizeBytes = i * 100, FileCount = i, Parent = root });

        root.Compact(keep: 3, directFilesBytes: 50, directFileCount: 2);

        Assert.Equal(4, root.Children.Count);
        Assert.Equal(new[] { "d10", "d9", "d8" }, root.Children.Take(3).Select(c => c.Name));
        var other = root.Children[3];
        Assert.True(other.IsAggregate);
        Assert.Equal((1 + 2 + 3 + 4 + 5 + 6 + 7) * 100 + 50, other.SizeBytes);
        Assert.Contains("Diğer 7 klasör", other.Name);
    }
}
