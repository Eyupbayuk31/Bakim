using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Bakım.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Bakim.Tests;

/// <summary>
/// XAML komut bağlamalarının gerçekten var olduğunu doğrular.
///
/// WPF'te bozuk bir komut bağlaması İSTİSNA FIRLATMAZ: düğme sessizce
/// devre dışı kalır. Derleme yeşil, uygulama açılıyor, ama düğmeye
/// bastığınızda hiçbir şey olmuyor. Bu test o sessiz kırılmayı yakalar.
///
/// (Bu testi yazma sebebi somut: satır eylemleri yeniden düzenlenirken
/// var olmayan komut adları — ScanWithVirusTotalCommand, DeleteEntryCommand —
/// bağlanmıştı ve derleme hiç şikâyet etmedi.)
/// </summary>
public class CommandBindingTests
{
    private readonly ITestOutputHelper _output;

    public CommandBindingTests(ITestOutputHelper output) => _output = output;

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bakım.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı");
        }
    }

    /// <summary>Görünüm dosyası -> onu besleyen ViewModel tipi.</summary>
    public static TheoryData<string, Type> ViewToViewModel() => new()
    {
        { "Views/Modules/AnalyzerView.xaml",             typeof(AnalyzerViewModel) },
        { "Views/Modules/DashboardModuleView.xaml",      typeof(DashboardViewModel) },
        { "Views/Modules/CleanerModuleView.xaml",        typeof(CleanerViewModel) },
        { "Views/Modules/OptimizerModuleView.xaml",      typeof(OptimizerViewModel) },
        { "Views/Modules/StartupModuleView.xaml",        typeof(StartupViewModel) },
        { "Views/Modules/SystemInfoModuleView.xaml",     typeof(SystemInfoViewModel) },
        { "Views/Modules/NetworkMonitorModuleView.xaml", typeof(NetworkMonitorViewModel) },
        { "Views/Modules/ServiceManagerModuleView.xaml", typeof(ServiceManagerViewModel) },
        { "Views/Modules/PrivacyDebloatModuleView.xaml", typeof(PrivacyDebloatViewModel) },
        { "Views/Modules/CrashAnalyzerModuleView.xaml",  typeof(CrashAnalyzerViewModel) },
        { "Views/Modules/UninstallerModuleView.xaml",    typeof(UninstallerViewModel) },
        { "Views/Modules/WindowsTweakerModuleView.xaml", typeof(WindowsTweakerViewModel) },
        { "Views/Modules/SettingsModuleView.xaml",       typeof(SettingsViewModel) },
        { "Views/Modules/StorageModuleView.xaml",        typeof(StorageViewModel) },
        { "Views/Modules/WindowsToolsModuleView.xaml",   typeof(WindowsToolsViewModel) },
        { "Views/Modules/GameModeModuleView.xaml",       typeof(GameModeViewModel) },
        { "Views/Modules/SentinelModuleView.xaml",       typeof(SentinelViewModel) },
        { "Views/Modules/ActivityCenterView.xaml",       typeof(ActivityCenterViewModel) },
        { "Views/Modules/StoreModuleView.xaml",          typeof(StoreViewModel) },
    };

    /// <summary>
    /// DataContext.XxxCommand deseni: liste satırlarından kök ViewModel'e
    /// ulaşan bağlamalar. Bunlar tipik hata kaynağıdır çünkü RelativeSource
    /// ile yazılırlar ve IntelliSense yardımcı olmaz.
    /// </summary>
    private static readonly Regex DataContextCommand =
        new(@"DataContext\.(?<name>[A-Za-z0-9_]+Command)", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(ViewToViewModel))]
    public void EveryDataContextCommandBinding_ExistsOnViewModel(string relativeView, Type viewModelType)
    {
        string path = Path.Combine(RepoRoot, relativeView.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            _output.WriteLine($"ATLANDI: {relativeView} bulunamadi");
            return;
        }

        string xaml = File.ReadAllText(path);

        var referenced = DataContextCommand.Matches(xaml)
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (referenced.Count == 0)
        {
            _output.WriteLine($"{relativeView}: DataContext komut baglamasi yok");
            return;
        }

        var available = CommandNamesOf(viewModelType);
        var missing = referenced.Where(n => !available.Contains(n)).ToList();

        _output.WriteLine($"{relativeView} -> {viewModelType.Name}: " +
                          $"{referenced.Count} baglama, {missing.Count} eksik");
        foreach (var name in referenced)
            _output.WriteLine($"   {(available.Contains(name) ? "OK  " : "EKSIK")} {name}");

        Assert.True(missing.Count == 0,
            $"{relativeView} icinde {viewModelType.Name} uzerinde BULUNMAYAN komutlar: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// ViewModel üzerindeki ICommand özelliklerinin adları.
    /// CommunityToolkit.Mvvm [RelayCommand] kaynak üreteci bunları
    /// derleme zamanında üretir, bu yüzden yansımayla görünürler.
    /// </summary>
    private static HashSet<string> CommandNamesOf(Type viewModelType)
    {
        return viewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void AnalyzerView_RowActions_AreNotAmbiguous()
    {
        // Satırda birden fazla ETIKETSIZ kalkan simgesi olmamalı: iki kalkan
        // yan yana durduğunda hangisinin ne yaptığı anlaşılmıyordu.
        string path = Path.Combine(RepoRoot, "Views", "Modules", "AnalyzerView.xaml");
        if (!File.Exists(path)) return;

        string xaml = File.ReadAllText(path);

        // Satır şablonundaki eylem bloğunu al
        int start = xaml.IndexOf("Column 6:", StringComparison.Ordinal);
        Assert.True(start > 0, "Satir eylem blogu bulunamadi");

        int end = xaml.IndexOf("</StackPanel>", start, StringComparison.Ordinal);
        string block = xaml[start..end];

        int shieldIcons = Regex.Matches(block, @"SymbolIcon Shield[A-Za-z]*20").Count;

        _output.WriteLine($"Satir eylem blogunda kalkan simgesi: {shieldIcons}");

        // Birincil "Analiz Et" butonu bir kalkan taşır; ikincisi taşma menüsünde
        // metinle birlikte olduğu için karışıklık yaratmaz.
        Assert.True(shieldIcons <= 2,
            $"Satirda {shieldIcons} kalkan simgesi var; etiketsiz benzer simgeler kullaniciyi sasirtiyor");
    }

    [Fact]
    public void AnalyzerView_PrimaryRowAction_HasVisibleLabel()
    {
        string path = Path.Combine(RepoRoot, "Views", "Modules", "AnalyzerView.xaml");
        if (!File.Exists(path)) return;

        string xaml = File.ReadAllText(path);

        // Birincil eylem metin taşımalı: simge tek başına ne yaptığını anlatmaz
        Assert.True(xaml.Contains("Content=\"Analiz et\"") || xaml.Contains("Content=\"Analiz Et\""), "Birincil eylem metin taşımalı");
    }
}
