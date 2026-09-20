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
    }
}
