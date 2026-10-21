using System.Collections.Generic;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// Inert UGS implementation for demo / offline / <c>UGS_DISABLED</c> builds.
    /// CreateLobby returns the legacy "cluck-lan" session name so the offline
    /// Host flow still works without any real UGS connection. It still remembers the
    /// last code (<see cref="LastJoinCode"/>), so REJOIN works offline too (CW-218).
    /// </summary>
    public sealed class NullUGSService : IUGSService
    {
        private static readonly LobbyInfo OfflineLobby =
            new LobbyInfo("offline", "Offline", "cluck-lan", 1, 4);

        public bool IsAvailable    => false;
        public bool IsSignedIn     => false;
        public LobbyInfo CurrentLobby => null;
        public string LastJoinCode { get; private set; }

        public Task InitializeAsync()        => Task.CompletedTask;
        public Task SignInAnonymouslyAsync() => Task.CompletedTask;
        public Task SignOutAsync()           => Task.CompletedTask;

        public Task LeaveLobbyAsync()
        {
            LastJoinCode = null;
            return Task.CompletedTask;
        }

        public Task<LobbyInfo> CreateLobbyAsync(string lobbyName, int maxPlayers)
        {
            LastJoinCode = OfflineLobby.JoinCode;
            return Task.FromResult(OfflineLobby);
        }

        public Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode)
        {
            var lobby = new LobbyInfo("offline", "Offline", joinCode.Trim(), 1, 4);
            LastJoinCode = lobby.JoinCode;
            return Task.FromResult(lobby);
        }

        public Task<LobbyInfo> JoinLobbyAsync(LobbyInfo lobby)
        {
            LastJoinCode = lobby?.JoinCode;
            return Task.FromResult(lobby);
        }

        public Task<List<LobbyInfo>> QueryLobbiesAsync() =>
            Task.FromResult(new List<LobbyInfo>());

        /// <summary>No lobby service to ask: always <see cref="LobbyAvailability.Unknown"/>.</summary>
        public Task<LobbyAvailability> CheckLobbyAsync(string joinCode) =>
            Task.FromResult(LobbyAvailability.Unknown);
    }
}
