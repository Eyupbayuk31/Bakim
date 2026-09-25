using System.Windows;
using System.Windows.Controls;
using Bakım.Models;

namespace Bakım.Controls
{
    /// <summary>
    /// Risk rozeti (§3.5): <see cref="RiskLevel"/> → metin + simge + renk. Aynı risk her ekranda
    /// aynı görünür; renk tek başına anlam taşımaz (simge ve metin de değişir).
    /// </summary>
    public sealed class RiskBadge : ContentControl
    {
        private readonly StatusBadge _badge = new();

        public RiskBadge()
        {
            Content = _badge;
            Focusable = false;
            Apply();
        }

        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register(nameof(Level), typeof(RiskLevel), typeof(RiskBadge),
                new PropertyMetadata(RiskLevel.Clean, (d, _) => ((RiskBadge)d).Apply()));

        public RiskLevel Level
        {
            get => (RiskLevel)GetValue(LevelProperty);
            set => SetValue(LevelProperty, value);
        }

        /// <summary>İsteğe bağlı: "Riskli 72" gibi skorla birlikte gösterim.</summary>
        public static readonly DependencyProperty ScoreProperty =
            DependencyProperty.Register(nameof(Score), typeof(int?), typeof(RiskBadge),
                new PropertyMetadata(null, (d, _) => ((RiskBadge)d).Apply()));

        public int? Score
        {
            get => (int?)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }

        public static string LabelOf(RiskLevel level) => level switch
        {
            RiskLevel.Clean => "Temiz",
            RiskLevel.Low => "Düşük risk",
            RiskLevel.Medium => "Dikkat",
            RiskLevel.High => "Şüpheli",
            RiskLevel.Critical => "Tehlikeli",
            _ => level.ToString()
        };

        private void Apply()
        {
            var (intent, icon) = Level switch
            {
                RiskLevel.Clean => (Intent.Success, "ShieldCheckmark20"),
                RiskLevel.Low => (Intent.Accent, "Info20"),
                RiskLevel.Medium => (Intent.Caution, "Warning20"),
                RiskLevel.High => (Intent.Caution, "ShieldError20"),
                RiskLevel.Critical => (Intent.Critical, "ShieldError20"),
                _ => (Intent.Neutral, "Info20")
            };
            _badge.Intent = intent;
            _badge.Icon = icon;
            _badge.Text = Score is { } s ? $"{LabelOf(Level)} {s}" : LabelOf(Level);
            System.Windows.Automation.AutomationProperties.SetName(this, _badge.Text);
        }
    }

    /// <summary>
    /// Durum hapı (§3.5): Etkin / Devre dışı / Çalışıyor / Durdu / Yönetici gerekli /
    /// Yeniden başlatma gerekli. Metinler tek yerde; modüller aynı kelimeleri kullanır.
    /// </summary>
    public sealed class StatusPill : ContentControl
    {
        private readonly StatusBadge _badge = new();

        public StatusPill()
        {
            Content = _badge;
            Focusable = false;
            Apply();
        }

        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(nameof(State), typeof(PillState), typeof(StatusPill),
                new PropertyMetadata(PillState.Unknown, (d, _) => ((StatusPill)d).Apply()));

        public PillState State
        {
            get => (PillState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        private void Apply()
        {
            var (intent, icon, text) = State switch
            {
                PillState.Enabled => (Intent.Success, "CheckmarkCircle20", "Etkin"),
                PillState.Disabled => (Intent.Neutral, "DismissCircle20", "Devre dışı"),
                PillState.Running => (Intent.Success, "Play20", "Çalışıyor"),
                PillState.Stopped => (Intent.Neutral, "Stop20", "Durdu"),
                PillState.AdminRequired => (Intent.Caution, "Shield20", "Yönetici gerekli"),
                PillState.RestartRequired => (Intent.Caution, "ArrowClockwise20", "Yeniden başlatma gerekli"),
                _ => (Intent.Neutral, "Question20", "Bilinmiyor")
            };
            _badge.Intent = intent;
            _badge.Icon = icon;
            _badge.Text = text;
            System.Windows.Automation.AutomationProperties.SetName(this, text);
        }
    }
}
