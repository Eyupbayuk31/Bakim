using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakım.Core.Tweaks;
using Xunit;

namespace Bakim.Core.Tests;

public class TweakCatalogTests
{
    private sealed class FakeRegistry : ITweakRegistryReader
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public object? GetValue(string root, string key, string? name) => Values.TryGetValue($@"{root}\{key}\{name}", out var v) ? v : null;
        public bool KeyExists(string root, string key) => Keys.Contains($@"{root}\{key}");
    }

    private const string Sample = """
    [
      {
        "id": "demo_toggle", "category": "Dosya Gezgini", "title": "Demo", "description": "d",
        "on":  [ { "op": "SetDword", "root": "HKCU", "key": "Software\\Demo", "name": "Flag", "dword": 1 } ],
        "off": [ { "op": "DeleteValue", "root": "HKCU", "key": "Software\\Demo", "name": "Flag" } ],
      },
      {
        "id": "demo_machine", "category": "Önyükleme", "title": "Makine", "description": "d", "minBuild": 22000,
        "on":  [ { "op": "SetString", "root": "HKLM", "key": "SOFTWARE\\Demo", "name": "Mode", "text": "Off" },
                 { "op": "DeleteKey", "root": "HKLM", "key": "SOFTWARE\\Demo\\Sub" } ],
        "off": [ { "op": "DeleteValue", "root": "HKLM", "key": "SOFTWARE\\Demo", "name": "Mode" } ]
      }
    ]
    """;

    [Fact]
    public void Parses_AndDerivesAdmin()
    {
        var defs = TweakCatalog.Parse(Sample);
        Assert.Equal(2, defs.Count);
        Assert.Empty(TweakCatalog.Validate(defs));
        Assert.False(defs[0].RequiresAdmin);
        Assert.True(defs[1].RequiresAdmin);
        Assert.False(defs[1].AppliesTo(19045));
        Assert.True(defs[1].AppliesTo(26100));
        Assert.Contains(@"HKCU\Software\Demo → Flag = 1 (DWORD)", defs[0].On[0].Describe());
    }

    [Fact]
    public void Detection_RequiresAllOnOps()
    {
        var defs = TweakCatalog.Parse(Sample);
        var reg = new FakeRegistry();
        Assert.False(TweakCatalog.IsOn(defs[0], reg));
        reg.Values[@"HKCU\Software\Demo\Flag"] = 1;
        Assert.True(TweakCatalog.IsOn(defs[0], reg));

        reg.Values[@"HKLM\SOFTWARE\Demo\Mode"] = "off";           // büyük/küçük harf duyarsız
        reg.Keys.Add(@"HKLM\SOFTWARE\Demo\Sub");                  // silinmesi gereken anahtar hâlâ var
        Assert.False(TweakCatalog.IsOn(defs[1], reg));
        reg.Keys.Clear();
        Assert.True(TweakCatalog.IsOn(defs[1], reg));
    }

    [Fact]
    public void Plan_SkipsOpsAlreadyInEffect()
    {
        var defs = TweakCatalog.Parse(Sample);
        var reg = new FakeRegistry();
        reg.Values[@"HKLM\SOFTWARE\Demo\Mode"] = "Off";
        var plan = TweakCatalog.Plan(defs[1], enable: true, reg);
        Assert.Empty(plan); // değer zaten doğru, anahtar zaten yok
        Assert.Single(TweakCatalog.Plan(defs[1], enable: false, reg));
    }

    [Fact]
    public void Validate_ReportsProblems()
    {
        var bad = new[]
        {
            new TweakDefinition { Id = "x", Title = "t", Category = "c",
                On = new[] { new TweakOp { Op = TweakOpKind.SetDword, Root = "HKU", Key = "k", Name = "n" } },
                Off = Array.Empty<TweakOp>() },
            new TweakDefinition { Id = "x", Title = "t", Category = "c" },
        };
        var errors = TweakCatalog.Validate(bad);
        Assert.Contains(errors, e => e.Contains("yinelenen"));
        Assert.Contains(errors, e => e.Contains("geçersiz kök"));
        Assert.Contains(errors, e => e.Contains("SetDword değeri yok"));
        Assert.Contains(errors, e => e.Contains("açma ve kapama"));
    }

    /// <summary>Depodaki gerçek katalog dosyaları geçerli olmalı (CI'da derlemeden önce yakalanır).</summary>
    [Fact]
    public void ShippedCatalog_IsValid()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "tweaks"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) return; // katalog henüz yoksa
        var all = Directory.GetFiles(Path.Combine(dir, "Assets", "tweaks"), "*.json")
            .SelectMany(f => TweakCatalog.Parse(File.ReadAllText(f))).ToList();
        Assert.NotEmpty(all);
        Assert.Empty(TweakCatalog.Validate(all));
    }
}
