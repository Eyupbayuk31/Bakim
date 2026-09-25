using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Bakım.Services
{
    public enum AppThemeKind
    {
        MicaDark,
        AmoledBlack,
        CyberpunkPurple,
        FluentLight,
        /// <summary>Windows Yüksek Kontrast renkleri (SystemColors). Sistem ayarı açıksa otomatik seçilir.</summary>
        HighContrast
    }

    /// <summary>Bir temanın tüm renk kararlarını taşıyan salt-okunur tanım.</summary>
    public sealed class ThemeDefinition
    {
        public required AppThemeKind Kind { get; init; }
        public required string DisplayName { get; init; }
        public required bool IsDark { get; init; }

        // Yüzeyler
        public required Color WindowBackground { get; init; }
        public required Color CardBackground { get; init; }
        public required Color CardBackgroundAlt { get; init; }
        public required Color CardStroke { get; init; }
        public required Color ControlFill { get; init; }
        public required Color ControlFillAlt { get; init; }
        public required Color ControlStroke { get; init; }
        public required Color SubtleHover { get; init; }
        public required Color SubtlePressed { get; init; }

        // Metin
        public required Color TextPrimary { get; init; }
        public required Color TextSecondary { get; init; }
        public required Color TextTertiary { get; init; }

        // Anlam (intent)
        public required Color Accent { get; init; }
        public required Color Primary { get; init; }
        public required Color Success { get; init; }
        public required Color Caution { get; init; }
        public required Color Critical { get; init; }
    }

    public interface IThemeService
    {
        AppThemeKind CurrentTheme { get; }
        ThemeDefinition CurrentDefinition { get; }
        IReadOnlyList<ThemeDefinition> AvailableThemes { get; }

        /// <summary>Temayı uygular ve (ayar servisi bağlıysa) kalıcı hale getirir.</summary>
        void ApplyTheme(AppThemeKind theme, bool persist = true);

        void ToggleNextTheme();

        /// <summary>Kayıtlı temayı diskten okuyup uygular. Açılışta bir kez çağrılır.</summary>
        void RestorePersistedTheme();

        /// <summary>Mica/Acrylic arka plan efektini açar veya kapatır.</summary>
        void ApplyBackdrop(bool micaEnabled);

        /// <summary>Vurgu rengi Windows'u takip ediyor mu (§3.1).</summary>
        bool FollowWindowsAccent { get; }

        void SetFollowWindowsAccent(bool follow);

        event Action<AppThemeKind>? ThemeChanged;
    }

    /// <summary>
    /// Uygulamanın tek tema motoru.
    ///
    /// Önemli: Arayüz neredeyse tamamen WPF-UI'ın anlamsal fırçalarını (TextFillColorPrimaryBrush,
    /// CardBackgroundFillColorDefaultBrush ...) kullanıyor. Bu yüzden tema değişimi yalnızca
    /// yerel Brush.* anahtarlarını değil, o WPF-UI anahtarlarını da Application.Resources
    /// seviyesinde geçersiz kılmak zorundadır; aksi halde ekranın büyük kısmı boyanmaz.
    /// </summary>
    public sealed class ThemeService : IThemeService
    {
        private static readonly object SharedGate = new();
        private static ThemeService? _shared;

        /// <summary>
        /// Süreç genelinde tek örnek. DI de bu örneği kaydeder, böylece MainViewModel ile
        /// SettingsViewModel'in ayrı tema durumuna sahip olması mümkün değildir.
        /// </summary>
        public static ThemeService Shared
        {
            get
            {
                if (_shared != null) return _shared;
                lock (SharedGate) { return _shared ??= new ThemeService(); }
            }
        }

        private IAppSettingsService? _settings;
        private ILogService _log = NullLogService.Instance;

        public AppThemeKind CurrentTheme { get; private set; } = AppThemeKind.MicaDark;

        public ThemeDefinition CurrentDefinition => GetDefinition(CurrentTheme);

        public IReadOnlyList<ThemeDefinition> AvailableThemes { get; } = new List<ThemeDefinition>
        {
            Definitions.MicaDark,
            Definitions.AmoledBlack,
            Definitions.CyberpunkPurple,
            Definitions.FluentLight,
            Definitions.HighContrast
        };

        public event Action<AppThemeKind>? ThemeChanged;

        /// <summary>Açılışta App tarafından bağlanır; kalıcılık ve günlükleme bundan sonra etkinleşir.</summary>
        public void Attach(IAppSettingsService settings, ILogService log)
        {
            _settings = settings;
            _log = log ?? NullLogService.Instance;
        }

        /// <summary>Windows vurgu rengini takip et (Ayarlar). Açıkken Accent/Primary sistemden gelir.</summary>
        public bool FollowWindowsAccent { get; private set; }

        public void SetFollowWindowsAccent(bool follow)
        {
            FollowWindowsAccent = follow;
            ApplyTheme(CurrentTheme, persist: false);
        }

        public void RestorePersistedTheme()
        {
            var saved = _settings?.Current;
            var kind = ParseKind(saved?.Theme);
            FollowWindowsAccent = saved?.FollowWindowsAccent ?? false;

            // Windows Yüksek Kontrast açıksa kayıtlı temadan önce gelir (§3.1, §3.8).
            if (SystemParameters.HighContrast) kind = AppThemeKind.HighContrast;
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
                var target = SystemParameters.HighContrast ? AppThemeKind.HighContrast : ParseKind(_settings?.Current.Theme);
                ApplyTheme(target, persist: false);
            };

            ApplyTheme(kind, persist: false);
            ApplyBackdrop(saved?.IsMicaEnabled ?? true);

            _log.Info($"Kayıtlı tema geri yüklendi: {kind}", nameof(ThemeService));
        }

        public void ApplyTheme(AppThemeKind theme, bool persist = true)
        {
            var def = WithWindowsAccent(GetDefinition(theme));
            CurrentTheme = theme;

            var app = Application.Current;
            if (app?.Resources == null)
            {
                // Tasarım zamanı veya kapanış anı: yalnızca durumu güncelle.
                return;
            }

            void Paint()
            {
                try
                {
                    // 1) WPF-UI temel temasını hizala (kontrol şablonlarının varsayılanları için)
                    ApplicationThemeManager.Apply(
                        def.IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                        updateAccent: false);

                    var res = app.Resources;

                    // 2) WPF-UI anlamsal fırçalarını geçersiz kıl.
                    //    Application.Resources'a doğrudan yazılan anahtarlar, birleştirilmiş
                    //    sözlüklerdeki aynı anahtarları gölgeler; DynamicResource bağlamaları
                    //    anında yeni değeri alır.
                    Set(res, "ApplicationBackgroundBrush", def.WindowBackground);

                    Set(res, "CardBackgroundFillColorDefaultBrush", def.CardBackground);
                    Set(res, "CardBackgroundFillColorSecondaryBrush", def.CardBackgroundAlt);
                    Set(res, "CardStrokeColorDefaultBrush", def.CardStroke);

                    Set(res, "ControlFillColorDefaultBrush", def.ControlFill);
                    Set(res, "ControlFillColorSecondaryBrush", def.ControlFill);
                    Set(res, "ControlFillColorTertiaryBrush", def.ControlFillAlt);
                    Set(res, "ControlStrokeColorDefaultBrush", def.ControlStroke);

                    Set(res, "SubtleFillColorSecondaryBrush", def.SubtleHover);
                    Set(res, "SubtleFillColorTertiaryBrush", def.SubtlePressed);

                    Set(res, "TextFillColorPrimaryBrush", def.TextPrimary);
                    Set(res, "TextFillColorSecondaryBrush", def.TextSecondary);
                    Set(res, "TextFillColorTertiaryBrush", def.TextTertiary);

                    Set(res, "AccentTextFillColorPrimaryBrush", def.Accent);
                    Set(res, "AccentFillColorDefaultBrush", def.Primary);
                    Set(res, "TextOnAccentFillColorPrimaryBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
                    Set(res, "TextOnAccentFillColorSecondaryBrush", Color.FromRgb(0xEE, 0xEE, 0xEE));

                    Set(res, "SystemFillColorSuccessBrush", def.Success);
                    Set(res, "SystemFillColorCautionBrush", def.Caution);
                    Set(res, "SystemFillColorCriticalBrush", def.Critical);

                    // 3) Yerel anlamsal tasarım sistemi anahtarları (Brush.*)
                    Set(res, "Brush.Window.Background", def.WindowBackground);
                    Set(res, "Brush.Surface.Background", def.CardBackground);
                    Set(res, "Brush.Surface.Border", def.CardStroke);
                    Set(res, "Brush.Surface.Hover", def.SubtleHover);
                    Set(res, "Brush.Card.Background", def.CardBackground);
                    Set(res, "Brush.Card.Border", def.CardStroke);
                    Set(res, "Brush.Text.Primary", def.TextPrimary);
                    Set(res, "Brush.Text.Secondary", def.TextSecondary);
                    Set(res, "Brush.Text.Muted", def.TextTertiary);
                    Set(res, "Brush.Primary", def.Primary);
                    Set(res, "Brush.Accent", def.Accent);
                    Set(res, "Brush.Success", def.Success);
                    Set(res, "Brush.Warning", def.Caution);
                    Set(res, "Brush.Danger", def.Critical);
                    Set(res, "Brush.Input.Background", def.WindowBackground);
                    Set(res, "Brush.Input.Border", def.CardStroke);
                    Set(res, "Brush.Input.Border.Focus", def.Accent);
                    Set(res, "Brush.Input.Foreground", def.TextPrimary);
                    Set(res, "Brush.Badge.Background", def.ControlFill);
                    Set(res, "Brush.Badge.Border", def.ControlStroke);
                    Set(res, "Brush.Nav.Active", def.Primary);
                    Set(res, "Brush.Nav.Hover", def.SubtleHover);

                    // 4) Tonlu (subtle) zeminler — uyarı/başarı/bilgi kutularının arka planı.
                    //    Anlam rengi, yüzeyin üzerine düşük alfayla serilir.
                    Set(res, "Brush.Accent.Subtle", WithAlpha(def.Accent, 0x22));
                    Set(res, "Brush.Primary.Subtle", WithAlpha(def.Primary, 0x22));
                    Set(res, "Brush.Success.Subtle", WithAlpha(def.Success, 0x22));
                    Set(res, "Brush.Caution.Subtle", WithAlpha(def.Caution, 0x22));
                    Set(res, "Brush.Critical.Subtle", WithAlpha(def.Critical, 0x25));
                    Set(res, "Brush.Muted.Subtle", WithAlpha(def.TextTertiary, 0x1A));

                    // 5) Tonlu zemin üzerindeki metin renkleri.
                    //    Koyu temada anlam rengi beyaza, açık temada siyaha doğru kaydırılır ki
                    //    kendi tonlu zemini üzerinde okunabilir kalsın.
                    Set(res, "Brush.Success.Text", ReadableOn(def.Success, def.IsDark));
                    Set(res, "Brush.Caution.Text", ReadableOn(def.Caution, def.IsDark));
                    Set(res, "Brush.Critical.Text", ReadableOn(def.Critical, def.IsDark));
                    Set(res, "Brush.Danger.Text", ReadableOn(def.Critical, def.IsDark)); // eski ad
                    Set(res, "Brush.Danger.Background", WithAlpha(def.Critical, 0x25));

                    // 6) Kaplama (modal arka planı)
                    Set(res, "Brush.Overlay.Scrim", WithAlpha(def.WindowBackground, def.IsDark ? (byte)0xD0 : (byte)0xA8));

                    // 7) Parıltı katmanları — tarama animasyonları ve vurgu çerçeveleri
                    Set(res, "Brush.Glow.Strong", WithAlpha(def.Accent, 0xFF));
                    Set(res, "Brush.Glow.Medium", WithAlpha(def.Accent, 0xCC));
                    Set(res, "Brush.Glow.Soft", WithAlpha(def.Accent, 0x66));

                    // 9) TASARIM SİSTEMİ v2 — anlamsal katman (MASTER_PLAN §3.1).
                    //    XAML yalnızca bu anlamsal anahtarları kullanır; ham palet burada kalır.
                    PublishSemanticV2(res, def);

                    // 8) HAM RENKLER — GradientStop ve DropShadowEffect yalnızca Color kabul eder,
                    //    Brush kabul etmez. Bu yüzden aynı palet ayrıca Color olarak da yayınlanır.
                    SetColor(res, "Color.Window.Background", def.WindowBackground);
                    SetColor(res, "Color.Surface", def.CardBackground);
                    SetColor(res, "Color.Surface.Alt", def.CardBackgroundAlt);
                    SetColor(res, "Color.Border", def.CardStroke);
                    SetColor(res, "Color.Control", def.ControlFill);
                    SetColor(res, "Color.Text.Primary", def.TextPrimary);
                    SetColor(res, "Color.Text.Secondary", def.TextSecondary);
                    SetColor(res, "Color.Text.Tertiary", def.TextTertiary);
                    SetColor(res, "Color.Accent", def.Accent);
                    SetColor(res, "Color.Primary", def.Primary);
                    SetColor(res, "Color.Success", def.Success);
                    SetColor(res, "Color.Caution", def.Caution);
                    SetColor(res, "Color.Critical", def.Critical);
                    SetColor(res, "Color.Critical.Text", ReadableOn(def.Critical, def.IsDark));

                    // Gradient bitiş noktaları: aynı renk, sıfır opaklık —
                    // böylece geçiş griye değil şeffaflığa doğru solar.
                    SetColor(res, "Color.Accent.Transparent", WithAlpha(def.Accent, 0x00));
                    SetColor(res, "Color.Accent.Glow", WithAlpha(def.Accent, 0xCC));
                    SetColor(res, "Color.Success.Transparent", WithAlpha(def.Success, 0x00));
                    SetColor(res, "Color.Surface.Transparent", WithAlpha(def.CardBackground, 0x00));

                    // Gölge rengi: koyu temada saf siyah, açık temada yumuşatılmış lacivert
                    SetColor(res, "Color.Shadow", def.IsDark
                        ? Color.FromRgb(0, 0, 0)
                        : Color.FromRgb(0x0F, 0x17, 0x2A));

                    _log.Info($"Tema uygulandı: {def.DisplayName}", nameof(ThemeService));
                }
                catch (Exception ex)
                {
                    _log.Error($"Tema uygulanamadı: {theme}", ex, nameof(ThemeService));
                }
            }

            if (app.Dispatcher.CheckAccess()) Paint();
            else app.Dispatcher.Invoke(Paint);

            if (persist)
            {
                try
                {
                    _settings?.Update(s => s.Theme = theme.ToString());
                }
                catch (Exception ex)
                {
                    _log.Error("Tema tercihi kaydedilemedi.", ex, nameof(ThemeService));
                }
            }

            try { ThemeChanged?.Invoke(theme); }
            catch (Exception ex) { _log.Error("Tema değişikliği aboneleri hata verdi.", ex, nameof(ThemeService)); }
        }

        public void ToggleNextTheme()
        {
            var next = CurrentTheme switch
            {
                AppThemeKind.MicaDark => AppThemeKind.AmoledBlack,
                AppThemeKind.AmoledBlack => AppThemeKind.CyberpunkPurple,
                AppThemeKind.CyberpunkPurple => AppThemeKind.FluentLight,
                AppThemeKind.FluentLight => AppThemeKind.HighContrast,
                AppThemeKind.HighContrast => AppThemeKind.MicaDark,
                _ => AppThemeKind.MicaDark
            };

            ApplyTheme(next);
        }

        public void ApplyBackdrop(bool micaEnabled)
        {
            var app = Application.Current;
            if (app == null) return;

            void Apply()
            {
                try
                {
                    var backdrop = micaEnabled
                        ? Wpf.Ui.Controls.WindowBackdropType.Mica
                        : Wpf.Ui.Controls.WindowBackdropType.None;

                    foreach (Window window in app.Windows)
                    {
                        if (window is Wpf.Ui.Controls.FluentWindow fluent)
                            fluent.WindowBackdropType = backdrop;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning("Arka plan efekti (backdrop) uygulanamadı.", ex, nameof(ThemeService));
                }
            }

            if (app.Dispatcher.CheckAccess()) Apply();
            else app.Dispatcher.Invoke(Apply);
        }

        public static AppThemeKind ParseKind(string? value)
            => Enum.TryParse<AppThemeKind>(value, ignoreCase: true, out var kind)
                ? kind
                : AppThemeKind.MicaDark;

        public static ThemeDefinition GetDefinition(AppThemeKind kind) => kind switch
        {
            AppThemeKind.AmoledBlack => Definitions.AmoledBlack,
            AppThemeKind.CyberpunkPurple => Definitions.CyberpunkPurple,
            AppThemeKind.FluentLight => Definitions.FluentLight,
            AppThemeKind.HighContrast => Definitions.HighContrast,
            _ => Definitions.MicaDark
        };

        /// <summary>Surface / Border / Text / Status / Risk / Chart anahtarları (v2).</summary>
        private static void PublishSemanticV2(ResourceDictionary res, ThemeDefinition def)
        {
            foreach (var (key, color) in SemanticV2(def)) Set(res, key, color);
        }

        /// <summary>
        /// v2 anlamsal paletinin tek tanımı. Açılış paleti (Palette.Bootstrap.xaml) ve testler
        /// aynı değerleri buradan üretir.
        /// </summary>
        public static IEnumerable<(string Key, Color Color)> SemanticV2(ThemeDefinition def)
        {
            var white = Color.FromRgb(0xFF, 0xFF, 0xFF);

            // Yüzeyler
            yield return ("Surface.Base", def.WindowBackground);
            yield return ("Surface.Raised", def.CardBackground);
            yield return ("Surface.Overlay", def.IsDark ? def.ControlFill : def.CardBackground);
            yield return ("Surface.Sunken", def.CardBackgroundAlt);
            yield return ("Surface.Hover", def.SubtleHover);
            yield return ("Surface.Pressed", def.SubtlePressed);
            yield return ("Surface.Selected", WithAlpha(def.Primary, 0x33));

            // Kenarlıklar
            yield return ("Border.Subtle", WithAlpha(def.CardStroke, 0x99));
            yield return ("Border.Default", def.CardStroke);
            yield return ("Border.Strong", def.ControlStroke);
            yield return ("Border.Focus", def.Accent);

            // Metin
            yield return ("Text.Primary", def.TextPrimary);
            yield return ("Text.Secondary", def.TextSecondary);
            yield return ("Text.Tertiary", def.TextTertiary);
            yield return ("Text.Disabled", WithAlpha(def.TextTertiary, 0x99));
            yield return ("Text.OnAccent", white);
            yield return ("Text.Link", def.Accent);

            // Durum: Solid / Subtle / Text / Border
            foreach (var (name, color) in new[]
            {
                ("Success", def.Success), ("Caution", def.Caution), ("Critical", def.Critical),
                ("Info", def.Accent), ("Neutral", def.TextTertiary)
            })
            {
                yield return ($"Status.{name}.Solid", color);
                yield return ($"Status.{name}.Subtle", WithAlpha(color, 0x22));
                yield return ($"Status.{name}.Text", ReadableOn(color, def.IsDark));
                yield return ($"Status.{name}.Border", WithAlpha(color, 0x66));
            }

            // Risk (Analizör, Nöbetçi, Kaldırıcı): tek kaynak
            var high = Mix(def.Caution, def.Critical, 0.5);
            foreach (var (name, color) in new[]
            {
                ("Clean", def.Success), ("Low", def.Accent), ("Medium", def.Caution), ("High", high), ("Critical", def.Critical)
            })
            {
                yield return ($"Risk.{name}", color);
                yield return ($"Risk.{name}.Subtle", WithAlpha(color, 0x22));
                yield return ($"Risk.{name}.Text", ReadableOn(color, def.IsDark));
            }

            // Veri görselleştirme: Okabe-Ito (renk körlüğü dostu). CPU=1, RAM=2, Disk=3, Ağ=4 sabit.
            var series = def.IsDark
                ? new[] { "#56B4E9", "#E69F00", "#2EC4A0", "#CC79A7", "#F0E442", "#7AA7FF" }
                : new[] { "#0072B2", "#B36B00", "#007A5E", "#A6457F", "#8A7F00", "#3355CC" };
            for (int i = 0; i < series.Length; i++)
            {
                yield return ($"Chart.Series{i + 1}", Hex(series[i]));
                yield return ($"Chart.Series{i + 1}.Subtle", WithAlpha(Hex(series[i]), 0x22));
            }
            yield return ("Chart.Grid", WithAlpha(def.TextTertiary, 0x33));
            yield return ("Chart.Axis", def.TextTertiary);
        }

        /// <summary>"Windows vurgu rengini takip et" açıksa Accent/Primary sistem renginden türetilir.</summary>
        private ThemeDefinition WithWindowsAccent(ThemeDefinition def)
        {
            if (!FollowWindowsAccent || def.Kind == AppThemeKind.HighContrast) return def;
            Color accent;
            try
            {
                accent = SystemParameters.WindowGlassColor;
            }
            catch
            {
                return def;
            }
            accent = Color.FromRgb(accent.R, accent.G, accent.B);
            // Okunabilirlik: vurgu metni koyu temada açılır, açık temada koyulaşır.
            var accentText = def.IsDark ? Mix(accent, Color.FromRgb(0xFF, 0xFF, 0xFF), 0.35) : Mix(accent, Color.FromRgb(0, 0, 0), 0.25);
            return new ThemeDefinition
            {
                Kind = def.Kind, DisplayName = def.DisplayName, IsDark = def.IsDark,
                WindowBackground = def.WindowBackground, CardBackground = def.CardBackground, CardBackgroundAlt = def.CardBackgroundAlt,
                CardStroke = def.CardStroke, ControlFill = def.ControlFill, ControlFillAlt = def.ControlFillAlt,
                ControlStroke = def.ControlStroke, SubtleHover = def.SubtleHover, SubtlePressed = def.SubtlePressed,
                TextPrimary = def.TextPrimary, TextSecondary = def.TextSecondary, TextTertiary = def.TextTertiary,
                Accent = accentText, Primary = accent,
                Success = def.Success, Caution = def.Caution, Critical = def.Critical
            };
        }

        private static void Set(ResourceDictionary res, string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze(); // Dondurulmuş fırçalar thread-safe ve daha hızlıdır
            res[key] = brush;
        }

        private static void SetColor(ResourceDictionary res, string key, Color color)
            => res[key] = color;

        private static Color WithAlpha(Color color, byte alpha)
            => Color.FromArgb(alpha, color.R, color.G, color.B);

        /// <summary>
        /// Bir anlam rengini, kendi düşük alfalı zemini üzerinde okunabilir hale getirir.
        /// Koyu temada beyaza, açık temada siyaha doğru karıştırılır.
        /// </summary>
        private static Color ReadableOn(Color intent, bool isDark)
            => isDark
                ? Mix(intent, Color.FromRgb(0xFF, 0xFF, 0xFF), 0.45)
                : Mix(intent, Color.FromRgb(0x00, 0x00, 0x00), 0.30);

        private static Color Mix(Color a, Color b, double t)
        {
            byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromRgb(Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
        }

        private static Color Hex(string hex)
            => (Color)ColorConverter.ConvertFromString(hex)!;

        /// <summary>Tema paletleri. Metin/yüzey çiftleri WCAG AA (4.5:1) hedefiyle seçilmiştir.</summary>
        private static class Definitions
        {
            public static readonly ThemeDefinition MicaDark = new()
            {
                Kind = AppThemeKind.MicaDark,
                DisplayName = "Mica Koyu (Slate)",
                IsDark = true,
                WindowBackground = Hex("#0F172A"),
                CardBackground = Hex("#1E293B"),
                CardBackgroundAlt = Hex("#172033"),
                CardStroke = Hex("#334155"),
                ControlFill = Hex("#273549"),
                ControlFillAlt = Hex("#1E293B"),
                ControlStroke = Hex("#3A4A63"),
                SubtleHover = Hex("#273549"),
                SubtlePressed = Hex("#1B2637"),
                TextPrimary = Hex("#F8FAFC"),
                TextSecondary = Hex("#CBD5E1"),
                TextTertiary = Hex("#94A3B8"),
                Accent = Hex("#56CCF8"),
                Primary = Hex("#2563EB"),
                Success = Hex("#34D399"),
                Caution = Hex("#FBBF24"),
                Critical = Hex("#F87171")
            };

            public static readonly ThemeDefinition AmoledBlack = new()
            {
                Kind = AppThemeKind.AmoledBlack,
                DisplayName = "AMOLED Saf Siyah",
                IsDark = true,
                WindowBackground = Hex("#000000"),
                CardBackground = Hex("#0D0E12"),
                CardBackgroundAlt = Hex("#121317"),
                CardStroke = Hex("#3F3F46"), // 1.29:1 -> 1.85:1 (kart zemininden ayrilabilir)
                ControlFill = Hex("#18181B"),
                ControlFillAlt = Hex("#0D0E12"),
                ControlStroke = Hex("#3F3F46"),
                SubtleHover = Hex("#18181B"),
                SubtlePressed = Hex("#101013"),
                TextPrimary = Hex("#FAFAFA"),
                TextSecondary = Hex("#D4D4D8"),
                TextTertiary = Hex("#A1A1AA"),
                Accent = Hex("#C084FC"),
                Primary = Hex("#9333EA"),
                Success = Hex("#4ADE80"),
                Caution = Hex("#FACC15"),
                Critical = Hex("#FB7185")
            };

            public static readonly ThemeDefinition CyberpunkPurple = new()
            {
                Kind = AppThemeKind.CyberpunkPurple,
                DisplayName = "Cyberpunk Neon Mor",
                IsDark = true,
                WindowBackground = Hex("#090514"),
                CardBackground = Hex("#150D2A"),
                CardBackgroundAlt = Hex("#1B1136"),
                CardStroke = Hex("#3B1A62"),
                ControlFill = Hex("#221340"),
                ControlFillAlt = Hex("#150D2A"),
                ControlStroke = Hex("#4C2183"),
                SubtleHover = Hex("#221340"),
                SubtlePressed = Hex("#1A0E33"),
                TextPrimary = Hex("#F5F3FF"),
                TextSecondary = Hex("#DDD6FE"),
                TextTertiary = Hex("#A78BFA"),
                Accent = Hex("#FB7185"),
                Primary = Hex("#8B5CF6"),
                Success = Hex("#34D399"),
                Caution = Hex("#FBBF24"),
                Critical = Hex("#F43F5E")
            };

            /// <summary>
            /// Windows Yüksek Kontrast teması: renkler kullanıcının seçtiği sistem renkleridir.
            /// Durum renkleri de metin rengini kullanır; anlam simge ve metinle taşınır.
            /// </summary>
            public static ThemeDefinition HighContrast
            {
                get
                {
                    var window = SystemColors.WindowColor;
                    var text = SystemColors.WindowTextColor;
                    var gray = SystemColors.GrayTextColor;
                    var highlight = SystemColors.HighlightColor;
                    var link = SystemColors.HotTrackColor;
                    bool dark = (0.2126 * window.R + 0.7152 * window.G + 0.0722 * window.B) < 128;
                    return new ThemeDefinition
                    {
                        Kind = AppThemeKind.HighContrast,
                        DisplayName = "Yüksek Kontrast (Windows)",
                        IsDark = dark,
                        WindowBackground = window,
                        CardBackground = window,
                        CardBackgroundAlt = window,
                        CardStroke = text,
                        ControlFill = window,
                        ControlFillAlt = window,
                        ControlStroke = text,
                        SubtleHover = window,
                        SubtlePressed = window,
                        TextPrimary = text,
                        TextSecondary = text,
                        TextTertiary = gray,
                        Accent = link,
                        Primary = highlight,
                        Success = text,
                        Caution = text,
                        Critical = text
                    };
                }
            }

            public static readonly ThemeDefinition FluentLight = new()
            {
                Kind = AppThemeKind.FluentLight,
                DisplayName = "Fluent Açık",
                IsDark = false,
                WindowBackground = Hex("#F3F4F6"),
                CardBackground = Hex("#FFFFFF"),
                CardBackgroundAlt = Hex("#F9FAFB"),
                CardStroke = Hex("#CBD5E1"), // 1.24:1 -> 1.50:1 (beyaz kart uzerinde gorunur)
                ControlFill = Hex("#F1F5F9"),
                ControlFillAlt = Hex("#E8EDF3"),
                ControlStroke = Hex("#CBD5E1"),
                SubtleHover = Hex("#EEF2F7"),
                SubtlePressed = Hex("#E2E8F0"),
                TextPrimary = Hex("#0F172A"),
                TextSecondary = Hex("#475569"),
                TextTertiary = Hex("#64748B"),
                Accent = Hex("#1D4ED8"),
                Primary = Hex("#2563EB"),
                Success = Hex("#047857"),
                Caution = Hex("#B45309"),
                Critical = Hex("#B91C1C")
            };
        }
    }
}
