using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Tweaker kategori gezinmesinin Lazy&lt;T&gt; yeniden giriş regresyonu.
///
/// v3.10'da üretim günlüğünde şu hata görüldü:
///   InvalidOperationException: ValueFactory attempted to access the Value
///   property of this instance.  →  MainViewModel.get_TweakerCategories()
///
/// Sebep: TweakerCategoriesViewModel yapıcı metodu SelectCategory("All")
/// çağırıyor, bu da uygulama geneli navigasyon olayı yayıyordu. Olay
/// MainViewModel'e dönüyor, o da hâlâ kurulmakta olan Lazy örneğini
/// okumaya çalışıyordu.
///
/// Faz 2'deki lazy modül yüklemesiyle ortaya çıktı: önceden tüm ViewModel'ler
/// baştan kurulduğu için yeniden giriş mümkün değildi.
/// </summary>
public class TweakerNavigationTests
{
    [Fact]
    public void Constructor_DoesNotBroadcastNavigation()
    {
        var navigation = new NavigationService();
        int categoryEvents = 0;
        navigation.TweakerCategoryRequested += _ => categoryEvents++;

        // Yapıcı metot çalışırken hiçbir navigasyon olayı yayılmamalı
        var vm = new TweakerCategoriesViewModel(navigation);

        Assert.Equal(0, categoryEvents);
        Assert.Equal("All", vm.ActiveCategoryKey);
    }

    [Fact]
    public void Constructor_StillSelectsDefaultCategory()
    {
        var vm = new TweakerCategoriesViewModel(new NavigationService());

        // Varsayılan seçim korunmalı: olay yaymamak seçimi kaybetmek demek değil
        var selected = vm.Categories.Where(c => c.IsSelected).ToList();

        Assert.Single(selected);
        Assert.Equal("All", selected[0].Key);
    }

    [Fact]
    public void SelectCategory_StillBroadcasts_WhenCalledExplicitly()
    {
        var navigation = new NavigationService();
        string? received = null;
        navigation.TweakerCategoryRequested += key => received = key;

        var vm = new TweakerCategoriesViewModel(navigation);

        // Kullanıcı tıklaması hâlâ navigasyon tetiklemeli
        vm.SelectCategory("Appearance");

        Assert.Equal("Appearance", received);
        Assert.Equal("Appearance", vm.ActiveCategoryKey);
    }

    [Fact]
    public void SetSelectedCategorySilent_DoesNotBroadcast()
    {
        var navigation = new NavigationService();
        int events = 0;
        navigation.TweakerCategoryRequested += _ => events++;

        var vm = new TweakerCategoriesViewModel(navigation);
        vm.SetSelectedCategorySilent("Behavior");

        Assert.Equal(0, events);
        Assert.Equal("Behavior", vm.ActiveCategoryKey);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Appearance")]
    [InlineData("PrivacyDebloat")]
    [InlineData("Tools")]
    public void SelectedCategory_IsExclusive(string key)
    {
        var vm = new TweakerCategoriesViewModel(new NavigationService());
        vm.SetSelectedCategorySilent(key);

        // Aynı anda yalnızca bir kategori seçili olmalı
        Assert.Equal(1, vm.Categories.Count(c => c.IsSelected));
        Assert.Equal(key, vm.Categories.Single(c => c.IsSelected).Key,
            StringComparer.OrdinalIgnoreCase);
    }
}
