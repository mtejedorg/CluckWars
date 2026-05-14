using System.Collections.Generic;
using System.Threading.Tasks;

namespace CluckWars.Services
{
    /// <summary>
    /// Inert UGS implementation for demo / offline / <c>UGS_DISABLED</c> builds.
    /// CreateLobby returns the legacy "cluck-lan" session name so the offline
    /// Host flow still works without any real UGS connection.
    /// </summary>
    public sealed class NullUGSService : IUGSService
    {
        private static readonly LobbyInfo OfflineLobby =
            new LobbyInfo("offline", "Offline", "cluck-lan", 1, 4);

        public bool IsAvailable    => false;
        public bool IsSignedIn     => false;
        public LobbyInfo CurrentLobby => null;

        public Task InitializeAsync()        => Task.CompletedTask;
        public Task SignInAnonymouslyAsync() => Task.CompletedTask;
        public Task SignOutAsync()           => Task.CompletedTask;
        public Task LeaveLobbyAsync()        => Task.CompletedTask;

        public Task<LobbyInfo> CreateLobbyAsync(string lobbyName, int maxPlayers) =>
            Task.FromResult(OfflineLobby);

        public Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode) =>
            Task.FromResult(new LobbyInfo("offline", "Offline", joinCode.Trim().ToUpper(), 1, 4));

        public Task<LobbyInfo> JoinLobbyAsync(LobbyInfo lobby) =>
            Task.FromResult(lobby);

        public Task<List<LobbyInfo>> QueryLobbiesAsync() =>
            Task.FromResult(new List<LobbyInfo>());
    }
}
