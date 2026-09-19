using Wpf.Ui.Appearance;

namespace Bakım
{
    public static class ThemeManager
    {
        public static bool IsDarkTheme => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        public static void ApplyTheme(bool dark)
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }

        public static void ToggleTheme()
        {
            var currentTheme = ApplicationThemeManager.GetAppTheme();
            var newTheme = currentTheme == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
            ApplicationThemeManager.Apply(newTheme);
        }

        public static void SyncWithSystemTheme()
        {
            ApplicationThemeManager.ApplySystemTheme();
        }
    }
}
