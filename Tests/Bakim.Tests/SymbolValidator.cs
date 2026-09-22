using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Wpf.Ui.Controls;
using Xunit;

namespace Bakim.Tests
{
    public class SymbolValidator
    {
        [Fact]
        public void All_Xaml_SymbolRegular_Values_Must_Be_Valid()
        {
            var validNames = new HashSet<string>(Enum.GetNames(typeof(SymbolRegular)));
            Assert.NotEmpty(validNames);

            // Locate repo root from test assembly location
            string baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bakım.csproj")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            string repoRoot = dir.FullName;

            var xamlFiles = Directory.GetFiles(repoRoot, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"))
                .ToList();

            Assert.NotEmpty(xamlFiles);

            var badList = new List<string>();

            foreach (var file in xamlFiles)
            {
                string content = File.ReadAllText(file);

                // Match {ui:SymbolIcon SomeSymbol}
                var m1 = Regex.Matches(content, @"\{ui:SymbolIcon\s+([A-Za-z0-9]+)\}");
                foreach (Match m in m1)
                {
                    string sym = m.Groups[1].Value;
                    if (!validNames.Contains(sym))
                    {
                        badList.Add($"{Path.GetFileName(file)}: {sym} (in {{ui:SymbolIcon {sym}}})");
                    }
                }

                // Match Symbol="SomeSymbol" (excluding Binding)
                var m2 = Regex.Matches(content, @"Symbol=""([A-Za-z0-9]+)""");
                foreach (Match m in m2)
                {
                    string sym = m.Groups[1].Value;
                    if (!validNames.Contains(sym))
                    {
                        badList.Add($"{Path.GetFileName(file)}: {sym} (in Symbol=\"{sym}\")");
                    }
                }
            }

            Assert.True(badList.Count == 0, "Invalid SymbolRegular names found in XAML:\n" + string.Join("\n", badList.Distinct()));
        }

        [Fact]
        public void All_Xaml_ControlAppearance_Values_Must_Be_Valid()
        {
            var validAppearances = new HashSet<string>(Enum.GetNames(typeof(ControlAppearance)));
            Assert.NotEmpty(validAppearances);

            string baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bakım.csproj")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            string repoRoot = dir.FullName;

            var xamlFiles = Directory.GetFiles(repoRoot, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"))
                .ToList();

            var badList = new List<string>();

            foreach (var file in xamlFiles)
            {
                string content = File.ReadAllText(file);

                // Match Appearance="SomeValue"
                var matches = Regex.Matches(content, @"Appearance\s*=\s*""([A-Za-z0-9]+)""");
                foreach (Match m in matches)
                {
                    string val = m.Groups[1].Value;
                    if (!validAppearances.Contains(val))
                    {
                        badList.Add($"{Path.GetFileName(file)}: {val} (in Appearance=\"{val}\")");
                    }
                }
            }

            Assert.True(badList.Count == 0, "Invalid ControlAppearance values found in XAML:\n" + string.Join("\n", badList.Distinct()));
        }
    }
}
