using System.Collections.Generic;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// Unity Gaming Services facade: Auth + Lobby.
    /// Demo / offline builds bind <see cref="NullUGSService"/>.
    /// Phase 10+ builds bind <see cref="UGSService"/>.
    /// </summary>
    public interface IUGSService
    {
        bool IsAvailable { get; }
        bool IsSignedIn  { get; }

        /// <summary>The lobby this client currently belongs to, or null.</summary>
        LobbyInfo CurrentLobby { get; }

        // ---- Auth -------------------------------------------------------

        Task InitializeAsync();
        Task SignInAnonymouslyAsync();
        Task SignOutAsync();

        // ---- Lobby ------------------------------------------------------

        /// <summary>
        /// Creates a public lobby. Returns a <see cref="LobbyInfo"/> whose
        /// <c>JoinCode</c> is also used as the Photon Fusion session name.
        /// Starts the host heartbeat loop automatically.
        /// </summary>
        Task<LobbyInfo> CreateLobbyAsync(string lobbyName, int maxPlayers);

        /// <summary>
        /// Joins a lobby by its short join code (e.g. "ABC123").
        /// The returned <c>JoinCode</c> is used as the Fusion session name.
        /// </summary>
        Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode);

        /// <summary>
        /// Joins a lobby by its ID (used from the lobby browser).
        /// </summary>
        Task<LobbyInfo> JoinLobbyAsync(LobbyInfo lobby);

        /// <summary>Returns the current list of public, joinable lobbies.</summary>
        Task<List<LobbyInfo>> QueryLobbiesAsync();

        /// <summary>
        /// Leaves (or deletes, if host) the current lobby and stops the heartbeat.
        /// Safe to call when not in a lobby.
        /// </summary>
        Task LeaveLobbyAsync();
    }
}
