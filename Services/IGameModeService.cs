using System;
using System.Threading.Tasks;

namespace Bakım.Services
{
    public interface IGameModeService
    {
        bool IsGameModeActive { get; }
        event Action<bool>? GameModeChanged;
        Task<long> EnableGameModeAsync();
        void DisableGameMode();
        Task<long> ToggleGameModeAsync();

        /// <summary>Son etkinleştirmede ne yapıldığının kullanıcıya gösterilecek özeti.</summary>
        string LastActionSummary { get; }

        /// <summary>
        /// Bakım Oyun Modu açıkken kapandıysa/çöktüyse önceki güç planını geri yükler.
        /// Uygulama açılışında bir kez çağrılır.
        /// </summary>
        void RecoverInterruptedSession();
    }
}
