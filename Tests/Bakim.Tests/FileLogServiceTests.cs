using System;
using System.IO;
using System.Linq;
using Bakım.Services;
using Xunit;

namespace Bakim.Tests;

public class FileLogServiceTests
{
    /// <summary>
    /// İki Bakım süreci aynı günlük dosyasına yazınca satırlar eziliyordu ("-09-27 14:16:19…" gibi kesik satırlar).
    /// İki ayrı yazıcı (iki süreç gibi) sırayla yazdığında hiçbir satır kaybolmamalı ve bozulmamalı.
    /// </summary>
    [Fact]
    public void TwoWriters_SameFile_DoNotOverwriteEachOther()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bakim_log_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var a = new FileLogService(dir);
            using var b = new FileLogService(dir);
            for (int i = 0; i < 50; i++)
            {
                a.Info($"A satırı {i}", "Test");
                b.Info($"B satırı {i}", "Test");
            }

            string file = Directory.GetFiles(dir, "bakim-*.log").Single();
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var lines = new StreamReader(stream).ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(100, lines.Length);
            Assert.All(lines, l => Assert.Matches(@"^\d{4}-\d{2}-\d{2} ", l));
            for (int i = 0; i < 50; i++)
            {
                Assert.Contains(lines, l => l.EndsWith($"A satırı {i}"));
                Assert.Contains(lines, l => l.EndsWith($"B satırı {i}"));
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
