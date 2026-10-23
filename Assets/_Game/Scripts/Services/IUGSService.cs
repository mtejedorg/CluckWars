using System.Collections.Generic;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>What a lobby check found (<see cref="IUGSService.CheckLobbyAsync"/>, CW-218 rejoin).</summary>
    public enum LobbyAvailability
    {
        /// <summary>Could not tell: offline service, not signed in, not this client's lobby, or the check failed.</summary>
        Unknown = 0,
        /// <summary>The lobby still exists.</summary>
        Open = 1,
        /// <summary>The lobby is gone (deleted or expired).</summary>
        Gone = 2,
    }

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

        /// <summary>
        /// The join code of the last lobby this client created or joined (also its Fusion session name), kept across
        /// scene loads so the menu can offer REJOIN after a lost connection (CW-218). Null before the first create /
        /// join and after <see cref="LeaveLobbyAsync"/>. Only the join code is kept, nothing else about the session.
        /// </summary>
        string LastJoinCode { get; }

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
        /// Whether the lobby with <paramref name="joinCode"/> still exists. Never throws: anything it cannot tell is
        /// <see cref="LobbyAvailability.Unknown"/> (logged), so the caller can still try the join.
        /// </summary>
        Task<LobbyAvailability> CheckLobbyAsync(string joinCode);

        /// <summary>
        /// Leaves (or deletes, if host) the current lobby and stops the heartbeat.
        /// Safe to call when not in a lobby.
        /// </summary>
        Task LeaveLobbyAsync();
    }
}
