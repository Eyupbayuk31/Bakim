using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bakım.ViewModels;

namespace Bakım.Views.Modules
{
    public partial class AnalyzerView : UserControl
    {
        public AnalyzerView()
        {
            InitializeComponent();
        }

        /// <summary>Yalnızca dosya sürüklenirken "kopyala" imleci (ANL Faz 5).</summary>
        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Bırakılan dosyalar analiz edilir: tek dosya doğrudan analiz penceresinde, birden çok dosya
        /// "İncelenen dosyalar" olarak listeye eklenir. Klasörler atlanır.
        /// </summary>
        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (DataContext is not AnalyzerViewModel vm || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            var files = paths.Where(File.Exists).ToList();
            if (files.Count == 0) return;
            using (Core.History.AnalysisContext.Begin(Core.History.AnalysisSource.Analyzer, "Sürükle-bırak"))
            {
                await vm.AnalyzeSpecificFilesAsync(files);
            }
        }
    }
}
