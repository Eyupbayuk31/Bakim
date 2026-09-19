using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakım.Models;

namespace Bakım.Services
{
    public interface IAutorunsScannerEngine
    {
        IAsyncEnumerable<PersistenceItem> ScanAllAsync(IProgress<string>? progress = null);
        Task<bool> ToggleItemAsync(PersistenceItem item, bool enable);
        Task<bool> DeleteItemAsync(PersistenceItem item);
    }
}
