using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Wpf.Ui.Controls;

namespace Bakım.Views.Dialogs
{
    /// <summary>
    /// Dosya röntgeni penceresi.
    ///
    /// v3.11'de <see cref="System.Windows.Window"/> + AllowsTransparency yerine
    /// <see cref="FluentWindow"/> kullanılır. Eski kurulum Mica ile uyumsuzdu,
    /// WPF'i yazılım render'a düşürüyor ve pencereyi uygulamanın geri kalanından
    /// görsel olarak kopuk bırakıyordu. Ayrıca elle DragMove() gerekiyordu;
    /// ui:TitleBar bunu yerleşik olarak sağlar.
    /// </summary>
    public partial class ThreatAnalysisDialog : FluentWindow
    {
        public ThreatAnalysisViewModel ViewModel { get; }

        public ThreatAnalysisDialog(
            ThreatAnalysisResult result,
            IFileThreatAnalyzerService? analyzerService = null,
            IAutorunsScannerEngine? autorunsEngine = null,
            IVirusTotalCheckService? virusTotalService = null)
        {
            InitializeComponent();

            ViewModel = new ThreatAnalysisViewModel(result, analyzerService, autorunsEngine, virusTotalService);
            DataContext = ViewModel;

            ViewModel.RequestClose += Close;
        }
    }
}
