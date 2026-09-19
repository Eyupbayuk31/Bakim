using System;

namespace Bakım.Services
{
    /// <summary>
    /// Uygulama içi merkezi sayfa ve kategori navigasyon servisi arayüzü.
    /// </summary>
    public interface INavigationService
    {
        event Action<string>? NavigationRequested;
        event Action<string>? TweakerCategoryRequested;

        void Navigate(string targetPageKey);
        void NavigateToTweakerCategory(string categoryKey);
    }

    /// <summary>
    /// Modüller ve ViewModel'ler arası gevşek bağlı (loosely coupled) 
    /// merkezi navigasyon yöneticisi.
    /// </summary>
    public class NavigationService : INavigationService
    {
        private static readonly Lazy<NavigationService> _instance = new(() => new NavigationService());
        public static NavigationService Instance => _instance.Value;

        public event Action<string>? NavigationRequested;
        public event Action<string>? TweakerCategoryRequested;

        public void Navigate(string targetPageKey)
        {
            if (string.IsNullOrWhiteSpace(targetPageKey)) return;
            NavigationRequested?.Invoke(targetPageKey);
        }

        public void NavigateToTweakerCategory(string categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey)) return;
            TweakerCategoryRequested?.Invoke(categoryKey);
        }
    }
}
