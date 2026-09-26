using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bakım.Models;

namespace Bakım.Controls
{
    /// <summary>
    /// Telemetri / KPI kartı (Fluent 2 Standart).
    ///
    /// Kullanım:
    ///   &lt;c:StatCard Icon="TopSpeed24" Label="Bellek Kullanımı"
    ///               Value="%58" Caption="9.3 GB / 16 GB" Intent="Accent"/&gt;
    /// </summary>
    public partial class StatCard : UserControl
    {
        public StatCard()
        {
            InitializeComponent();
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ClickCommand != null && ClickCommand.CanExecute(CommandParameter))
            {
                ClickCommand.Execute(CommandParameter);
                e.Handled = true;
            }
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Değerin altındaki ikincil açıklama (ör. "9.3 GB / 16 GB").</summary>
        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        public static readonly DependencyProperty IntentProperty =
            DependencyProperty.Register(nameof(Intent), typeof(Intent), typeof(StatCard),
                new PropertyMetadata(Models.Intent.Neutral));

        public Intent Intent
        {
            get => (Intent)GetValue(IntentProperty);
            set => SetValue(IntentProperty, value);
        }

        /// <summary>Fare üzerine gelince mikro yükselme ve tıklanabilirlik durumu.</summary>
        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(StatCard),
                new PropertyMetadata(false));

        public bool IsInteractive
        {
            get => (bool)GetValue(IsInteractiveProperty);
            set => SetValue(IsInteractiveProperty, value);
        }

        /// <summary>Filtre seçim durumunu temsil eden seçililik durumu.</summary>
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(StatCard),
                new PropertyMetadata(false));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        /// <summary>Tıklama komutu (filtreleme veya detay açma).</summary>
        /// <summary>
        /// Özet şeridi (SummaryStrip) içinde: kendi zemini/kenarlığı yok, solda ince ayırıcı.
        /// SummaryStrip bunu örtük stille kendisi ayarlar.
        /// </summary>
        public static readonly DependencyProperty IsEmbeddedProperty =
            DependencyProperty.Register(nameof(IsEmbedded), typeof(bool), typeof(StatCard),
                new PropertyMetadata(false));

        public bool IsEmbedded
        {
            get => (bool)GetValue(IsEmbeddedProperty);
            set => SetValue(IsEmbeddedProperty, value);
        }

        /// <summary>Şeritte ilk öğe değilse solda ayırıcı çizgi.</summary>
        public static readonly DependencyProperty ShowDividerProperty =
            DependencyProperty.Register(nameof(ShowDivider), typeof(bool), typeof(StatCard),
                new PropertyMetadata(true));

        public bool ShowDivider
        {
            get => (bool)GetValue(ShowDividerProperty);
            set => SetValue(ShowDividerProperty, value);
        }

        public static readonly DependencyProperty ClickCommandProperty =
            DependencyProperty.Register(nameof(ClickCommand), typeof(ICommand), typeof(StatCard),
                new PropertyMetadata(null));

        public ICommand ClickCommand
        {
            get => (ICommand)GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(StatCard),
                new PropertyMetadata(null));

        public object CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        /// <summary>Trend / rozet metni (ör. "+12%", "30 Gün", "Aktif").</summary>
        public static readonly DependencyProperty TrendTextProperty =
            DependencyProperty.Register(nameof(TrendText), typeof(string), typeof(StatCard),
                new PropertyMetadata(string.Empty));

        public string TrendText
        {
            get => (string)GetValue(TrendTextProperty);
            set => SetValue(TrendTextProperty, value);
        }

        /// <summary>Trend rozetinin anlamsal tonu.</summary>
        public static readonly DependencyProperty TrendIntentProperty =
            DependencyProperty.Register(nameof(TrendIntent), typeof(Intent), typeof(StatCard),
                new PropertyMetadata(Models.Intent.Neutral));

        public Intent TrendIntent
        {
            get => (Intent)GetValue(TrendIntentProperty);
            set => SetValue(TrendIntentProperty, value);
        }
    }
}
