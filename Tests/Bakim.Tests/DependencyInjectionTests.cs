using Microsoft.Extensions.DependencyInjection;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Konteyner bütünlük denetimi.
///
/// v3.0'da DI konteyneri kayıtlıydı ama hiç kullanılmıyordu: ViewModel'ler
/// servislerini `?? new X()` ile kendileri üretiyordu. Bu yüzden eksik bir
/// kayıt hiçbir zaman fark edilmiyor, sadece yanlış (tekil olmayan) örnek
/// sessizce kullanılıyordu — tema servisinin çift örnekli olması bu yüzdendi.
///
/// Bu testler o sınıf hataların bir daha sessizce geçmesini engeller.
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildContainer()
    {
        var services = new ServiceCollection();

        // Gerçek kayıt grafiği — testte kopyası değil, uygulamanın kendisi.
        Bakım.App.ConfigureServices(services, NullLogService.Instance, new StubSettingsService());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void Container_BuildsWithoutMissingRegistrations()
    {
        // ValidateOnBuild her kaydın bağımlılıklarını çözümler.
        // Eksik bir kayıt varsa bu satır AggregateException fırlatır.
        using var provider = BuildContainer();
        Assert.NotNull(provider);
    }

    public static TheoryData<Type> ViewModelTypes() => new()
    {
        typeof(MainViewModel), typeof(DashboardViewModel), typeof(CleanerViewModel),
        typeof(OptimizerViewModel), typeof(StartupViewModel), typeof(SystemInfoViewModel),
        typeof(NetworkMonitorViewModel), typeof(ServiceManagerViewModel),
        typeof(PrivacyDebloatViewModel), typeof(CrashAnalyzerViewModel),
        typeof(UninstallerViewModel), typeof(AnalyzerViewModel),
        typeof(WindowsTweakerViewModel), typeof(TweakerCategoriesViewModel),
        typeof(SettingsViewModel), typeof(LoginViewModel),
    };

    [Theory]
    [MemberData(nameof(ViewModelTypes))]
    public void EveryViewModel_IsRegistered(Type vmType)
    {
        var services = new ServiceCollection();
        Bakım.App.ConfigureServices(services, NullLogService.Instance, new StubSettingsService());

        Assert.Contains(services, d => d.ServiceType == vmType);
    }

    [Fact]
    public void AllConcreteViewModels_MustBeRegisteredInContainer()
    {
        var services = new ServiceCollection();
        Bakım.App.ConfigureServices(services, NullLogService.Instance, new StubSettingsService());

        var registeredTypes = services.Select(s => s.ServiceType).ToHashSet();

        // Diyalog fabrika ViewModel'leri (çalışma zamanı parametresiyle new'lenenler) hariç
        var skipDialogFactories = new HashSet<string>
        {
            "ResidualCleanupViewModel",
            "ThreatAnalysisViewModel",
            "DeepUninstallWizardViewModel"
        };

        var viewModels = typeof(MainViewModel).Assembly.GetTypes()
            .Where(t => t.Namespace == "Bakım.ViewModels"
                        && t.Name.EndsWith("ViewModel")
                        && !t.IsAbstract
                        && !skipDialogFactories.Contains(t.Name))
            .ToList();

        var missing = viewModels.Where(vm => !registeredTypes.Contains(vm)).Select(vm => vm.Name).ToList();

        Assert.True(missing.Count == 0,
            "DI konteynerine kaydedilmesi unutulmus ViewModel'ler tespit edildi: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThemeService_IsSingleton_AndSameAsSharedInstance()
    {
        using var provider = BuildContainer();

        var first = provider.GetRequiredService<IThemeService>();
        var second = provider.GetRequiredService<IThemeService>();

        Assert.Same(first, second);
        // Konteyner ile statik erişim aynı örneği vermeli; aksi halde
        // Ayarlar ekranı ile başlık çubuğu yine ayrı tema durumu taşırdı.
        Assert.Same(ThemeService.Shared, first);
    }

    [Theory]
    [InlineData(typeof(IAppSettingsService))]
    [InlineData(typeof(ILogService))]
    [InlineData(typeof(ISystemCleanService))]
    [InlineData(typeof(ITelemetryService))]
    [InlineData(typeof(ICommandPaletteService))]
    [InlineData(typeof(IFileThreatAnalyzerService))]
    [InlineData(typeof(INavigationService))]
    [InlineData(typeof(ITrayIconService))]
    [InlineData(typeof(IBackgroundMaintenanceService))]
    [InlineData(typeof(IExitCleanupService))]
    public void CoreService_ResolvesAsSingleton(Type serviceType)
    {
        using var provider = BuildContainer();

        var first = provider.GetService(serviceType);
        var second = provider.GetService(serviceType);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void NoViewModel_HasParameterlessConstructor()
    {
        // Parametresiz yapıcı = DI'ı atlama kapısı. Kapalı kalmalı.
        var offenders = typeof(MainViewModel).Assembly.GetTypes()
            .Where(t => t.Namespace == "Bakım.ViewModels"
                        && t.Name.EndsWith("ViewModel")
                        && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Parametresiz yapicisi olan ViewModel'ler: " + string.Join(", ", offenders));
    }

    /// <summary>Gerçek ayar dosyasına dokunmayan sahte ayar servisi.</summary>
    private sealed class StubSettingsService : IAppSettingsService
    {
        public Bakım.Models.AppSettingsData Current { get; } = new();
        public string SettingsFilePath => "(test)";
        public event Action<Bakım.Models.AppSettingsData>? SettingsChanged;
        public Bakım.Models.AppSettingsData Load() => Current;
        public void Save(Bakım.Models.AppSettingsData data) => SettingsChanged?.Invoke(data);
        public void Update(Action<Bakım.Models.AppSettingsData> mutate) => mutate(Current);
    }
}
