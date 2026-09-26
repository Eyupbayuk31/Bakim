using System.Windows;
using System.Windows.Controls;

namespace Bakım.Views.Modules
{
    /// <summary>Oyun Modu. DataContext: <see cref="ViewModels.GameModeViewModel"/>.</summary>
    public partial class GameModeModuleView : UserControl
    {
        /// <summary>Bu genişliğin altında sağ sütun (özet, oturumlar) ayarların altına iner.</summary>
        private const double TwoColumnMinWidth = 900;

        public GameModeModuleView()
        {
            InitializeComponent();
            BodyGrid.SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
        }

        private bool? _isWide;

        private void ApplyLayout(double width)
        {
            bool wide = width >= TwoColumnMinWidth;
            if (_isWide == wide) return;
            _isWide = wide;

            MainColumn.Width = new GridLength(wide ? 3 : 1, GridUnitType.Star);
            GutterColumn.Width = new GridLength(wide ? 24 : 0);
            SideColumn.Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);

            // Dar düzende "Açınca ne olacak" ayarlardan önce okunur: özet üstte, ayarlar altta.
            Grid.SetColumn(SummaryColumn, wide ? 2 : 0);
            Grid.SetRow(SettingsColumn, wide ? 0 : 1);
        }
    }
}
