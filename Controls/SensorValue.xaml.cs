using System.Windows;
using System.Windows.Controls;
using Bakım.Models;

namespace Bakım.Controls
{
    public partial class SensorValue : UserControl
    {
        public SensorValue()
        {
            InitializeComponent();
            Refresh();
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(SensorValue),
                new PropertyMetadata(string.Empty, (d, _) => ((SensorValue)d).Refresh()));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SensorValue),
                new PropertyMetadata(string.Empty, (d, _) => ((SensorValue)d).Refresh()));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public static readonly DependencyProperty QualityProperty =
            DependencyProperty.Register(nameof(Quality), typeof(MeasureQuality), typeof(SensorValue),
                new PropertyMetadata(MeasureQuality.Measured, (d, _) => ((SensorValue)d).Refresh()));

        public MeasureQuality Quality
        {
            get => (MeasureQuality)GetValue(QualityProperty);
            set => SetValue(QualityProperty, value);
        }

        /// <summary>Tahmin ya da ölçülemezlik nedeni (ipucu olarak gösterilir).</summary>
        public static readonly DependencyProperty ReasonProperty =
            DependencyProperty.Register(nameof(Reason), typeof(string), typeof(SensorValue),
                new PropertyMetadata(string.Empty, (d, _) => ((SensorValue)d).Refresh()));

        public string Reason
        {
            get => (string)GetValue(ReasonProperty);
            set => SetValue(ReasonProperty, value);
        }

        private void Refresh()
        {
            if (Display == null) return;
            string unit = string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit;
            bool empty = string.IsNullOrWhiteSpace(Value);

            (Display.Text, ToolTip) = Quality switch
            {
                MeasureQuality.Unavailable => ("—", string.IsNullOrEmpty(Reason) ? "Bu cihazda ölçülemiyor." : Reason),
                MeasureQuality.Estimated when !empty => ($"≈{Value}{unit}", string.IsNullOrEmpty(Reason) ? "Tahmini değer." : Reason),
                _ when empty => ("—", string.IsNullOrEmpty(Reason) ? null : Reason),
                _ => ($"{Value}{unit}", string.IsNullOrEmpty(Reason) ? null : Reason)
            };
            System.Windows.Automation.AutomationProperties.SetName(this,
                Display.Text == "—" ? $"Ölçülemedi. {ToolTip}" : Display.Text);
        }
    }
}
