using System;
using System.Linq;
using System.Threading.Tasks;
using Bakım.Core.History;
using Bakım.Models;
using Bakım.Services.Safety;

namespace Bakım.Services.History
{
    /// <summary>
    /// <see cref="IFileThreatAnalyzerService"/> dekoratörü: analiz hangi modülden çağrılırsa
    /// çağrılsın sonucu Analizör Geçmişi'ne yazar (§6.4). Kaynak etiketi
    /// <see cref="AnalysisContext"/>'ten okunur; modüllerin kayıt kodu yazması gerekmez.
    /// Kayıt başarısız olursa analiz sonucu yine döner (geçmiş asla analizi bozmaz).
    /// </summary>
    public sealed class RecordingFileThreatAnalyzer : IFileThreatAnalyzerService
    {
        private readonly IFileThreatAnalyzerService _inner;
        private readonly IAnalysisHistoryService _history;
        private readonly ILogService _log;

        public RecordingFileThreatAnalyzer(IFileThreatAnalyzerService inner, IAnalysisHistoryService history, ILogService log)
        {
            _inner = inner;
            _history = history;
            _log = log;
        }

        public async Task<ThreatAnalysisResult> AnalyzeFileAsync(string filePath, string? commandArgs = null, PersistenceItem? autorunItem = null)
        {
            var result = await _inner.AnalyzeFileAsync(filePath, commandArgs, autorunItem);

            var source = AnalysisContext.Source;
            if (source == AnalysisSource.Unknown && autorunItem != null) source = AnalysisSource.Analyzer;

            try
            {
                await _history.RecordAsync(result, source, AnalysisContext.Detail);
            }
            catch (Exception ex)
            {
                _log.Warning($"Analiz geçmişe yazılamadı: {filePath}", ex, nameof(RecordingFileThreatAnalyzer));
            }
            return result;
        }

        public Task<OperationResult> KillProcessAsync(int processId) => _inner.KillProcessAsync(processId);

        public async Task<OperationResult> RemoveFileAsync(string filePath)
        {
            var result = await _inner.RemoveFileAsync(filePath);
            if (result.Succeeded)
            {
                try
                {
                    var latest = _history.GetByPath(filePath).FirstOrDefault();
                    if (latest != null) await _history.SetDecisionAsync(latest.Id, UserDecision.Deleted);
                }
                catch (Exception ex)
                {
                    _log.Warning("Silme kararı geçmişe yazılamadı.", ex, nameof(RecordingFileThreatAnalyzer));
                }
            }
            return result;
        }
    }
}
