using System.Windows;
using System.Windows.Media;

namespace Bakım.Services
{
    public enum AppThemeKind
    {
        MicaDark,
        AmoledBlack,
        CyberpunkPurple
    }

    public interface IThemeService
    {
        AppThemeKind CurrentTheme { get; }
        void ApplyTheme(AppThemeKind theme);
        void ToggleNextTheme();
    }

    public class ThemeService : IThemeService
    {
        public AppThemeKind CurrentTheme { get; private set; } = AppThemeKind.MicaDark;

        public void ApplyTheme(AppThemeKind theme)
        {
            CurrentTheme = theme;
            if (Application.Current == null || Application.Current.Resources == null) return;
            var res = Application.Current.Resources;

            switch (theme)
            {
                case AppThemeKind.AmoledBlack:
                    res["Brush.Window.Background"] = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
                    res["Brush.Surface.Background"] = new SolidColorBrush(Color.FromRgb(0x0D, 0x0E, 0x12));
                    res["Brush.Surface.Border"] = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A));
                    res["Brush.Surface.Hover"] = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B));
                    res["Brush.Accent"] = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7)); // Purple
                    res["Brush.Primary"] = new SolidColorBrush(Color.FromRgb(0x93, 0x33, 0xEA));
                    break;

                case AppThemeKind.CyberpunkPurple:
                    res["Brush.Window.Background"] = new SolidColorBrush(Color.FromRgb(0x09, 0x05, 0x14));
                    res["Brush.Surface.Background"] = new SolidColorBrush(Color.FromRgb(0x15, 0x0D, 0x2A));
                    res["Brush.Surface.Border"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x1A, 0x62));
                    res["Brush.Surface.Hover"] = new SolidColorBrush(Color.FromRgb(0x22, 0x13, 0x40));
                    res["Brush.Accent"] = new SolidColorBrush(Color.FromRgb(0xF4, 0x3F, 0x5E)); // Neon Rose
                    res["Brush.Primary"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)); // Electric Violet
                    break;

                case AppThemeKind.MicaDark:
                default:
                    res["Brush.Window.Background"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)); // Slate dark
                    res["Brush.Surface.Background"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                    res["Brush.Surface.Border"] = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
                    res["Brush.Surface.Hover"] = new SolidColorBrush(Color.FromRgb(0x27, 0x35, 0x49));
                    res["Brush.Accent"] = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky Cyan
                    res["Brush.Primary"] = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                    break;
            }
        }

        public void ToggleNextTheme()
        {
            var next = CurrentTheme switch
            {
                AppThemeKind.MicaDark => AppThemeKind.AmoledBlack,
                AppThemeKind.AmoledBlack => AppThemeKind.CyberpunkPurple,
                AppThemeKind.CyberpunkPurple => AppThemeKind.MicaDark,
                _ => AppThemeKind.MicaDark
            };
            ApplyTheme(next);
        }
    }
}
