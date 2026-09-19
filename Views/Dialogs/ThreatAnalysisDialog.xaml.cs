using System;
using System.Windows;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;

namespace Bakım.Views.Dialogs
{
    public partial class ThreatAnalysisDialog : Window
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

            ViewModel.RequestClose += () =>
            {
                Close();
            };

            MouseDown += (s, e) =>
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    try { DragMove(); } catch { }
                }
            };
        }
    }
}
