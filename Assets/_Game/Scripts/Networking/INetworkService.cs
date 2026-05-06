using System;
using System.Threading.Tasks;
using Fusion;

namespace CluckWars.Networking
{
    /// <summary>
    /// All Fusion runner lifecycle goes through this service. Game systems consume the
    /// <see cref="Runner"/> property after <see cref="OnRunnerReady"/> fires; they should
    /// not new up <c>NetworkRunner</c> directly.
    /// </summary>
    public interface INetworkService
    {
        bool IsRunning { get; }

        /// <summary>The active runner. Null until <see cref="OnRunnerReady"/> fires.</summary>
        NetworkRunner Runner { get; }

        /// <summary>Single-player offline session (Fusion <c>GameMode.Single</c>) — used in Phase 1 dev.</summary>
        Task StartSoloAsync();

        /// <summary>Hosts a Shared Mode session that other LAN clients can join by name.</summary>
        Task StartHostAsync(string sessionName);

        /// <summary>Joins an existing Shared Mode session by name.</summary>
        Task JoinSessionAsync(string sessionName);

        Task ShutdownAsync();

        /// <summary>Fires once after a successful <c>StartGame</c>.</summary>
        event Action<NetworkRunner> OnRunnerReady;

        /// <summary>Fires when a player joins the runner (including the local player in Single mode).</summary>
        event Action<NetworkRunner, PlayerRef> OnPlayerJoined;

        /// <summary>Fires when a player leaves the runner.</summary>
        event Action<NetworkRunner, PlayerRef> OnPlayerLeft;

        /// <summary>Fires after the runner shuts down, normal or otherwise.</summary>
        event Action<ShutdownReason> OnShutdown;
    }
}
