using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Logging;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace CluckWars.Services
{
    /// <summary>
    /// Live UGS implementation: anonymous Auth + Lobby create/join/query.
    /// The UGS Lobby join code is reused as the Photon Fusion session name,
    /// so a single code gives both UGS tracking and Fusion matchmaking.
    ///
    /// Bound in <see cref="CluckWars.Installers.ProjectInstaller"/> when
    /// <c>UGS_DISABLED</c> is NOT defined. For offline / demo builds,
    /// <see cref="NullUGSService"/> is bound instead.
    /// </summary>
    public sealed class UGSService : IUGSService
    {
        private const string Source = "UGS";
        private const int HeartbeatIntervalMs = 15_000;
        private const int MaxQueryResults = 16;

        private readonly ILogService _log;

        private bool  _initialized;
        private Lobby _currentLobby;
        private bool  _isHost;
        private CancellationTokenSource _heartbeatCts;

        public bool IsAvailable => _initialized;
        public bool IsSignedIn  => _initialized && AuthenticationService.Instance.IsSignedIn;

        public LobbyInfo CurrentLobby =>
            _currentLobby != null ? ToInfo(_currentLobby) : null;

        public UGSService(ILogService log) => _log = log;

        // ---- Auth -------------------------------------------------------

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            await UnityServices.InitializeAsync();
            _initialized = true;
            _log.Info(Source, "Unity Services initialized.");
        }

        public async Task SignInAnonymouslyAsync()
        {
            await InitializeAsync();
            if (AuthenticationService.Instance.IsSignedIn) return;
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _log.Info(Source, $"Signed in anonymously. PlayerId={AuthenticationService.Instance.PlayerId}");
        }

        public Task SignOutAsync()
        {
            if (!IsSignedIn) return Task.CompletedTask;
            AuthenticationService.Instance.SignOut();
            _log.Info(Source, "Signed out.");
            return Task.CompletedTask;
        }

        // ---- Lobby ------------------------------------------------------

        public async Task<LobbyInfo> CreateLobbyAsync(string lobbyName, int maxPlayers)
        {
            await SignInAnonymouslyAsync();

            var name    = string.IsNullOrWhiteSpace(lobbyName) ? "Cluck Wars" : lobbyName.Trim();
            var options = new CreateLobbyOptions { IsPrivate = false };
            var lobby   = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, options);

            _currentLobby = lobby;
            _isHost       = true;
            StartHeartbeat();

            _log.Info(Source, $"Lobby created. Name={lobby.Name} Id={lobby.Id} Code={lobby.LobbyCode} MaxPlayers={lobby.MaxPlayers}");
            return ToInfo(lobby);
        }

        public async Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode)
        {
            await SignInAnonymouslyAsync();

            var code  = joinCode.Trim().ToUpper();
            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);

            _currentLobby = lobby;
            _isHost       = false;

            _log.Info(Source, $"Joined lobby by code. Id={lobby.Id} Code={lobby.LobbyCode} Players={lobby.Players?.Count}/{lobby.MaxPlayers}");
            return ToInfo(lobby);
        }

        public async Task<LobbyInfo> JoinLobbyAsync(LobbyInfo lobby)
        {
            await SignInAnonymouslyAsync();

            var joined = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.LobbyId);

            _currentLobby = joined;
            _isHost       = false;

            _log.Info(Source, $"Joined lobby by id. Id={joined.Id} Code={joined.LobbyCode} Players={joined.Players?.Count}/{joined.MaxPlayers}");
            return ToInfo(joined);
        }

        public async Task<List<LobbyInfo>> QueryLobbiesAsync()
        {
            await SignInAnonymouslyAsync();

            var options = new QueryLobbiesOptions { Count = MaxQueryResults };
            var response = await LobbyService.Instance.QueryLobbiesAsync(options);

            var result = new List<LobbyInfo>(response.Results.Count);
            foreach (var l in response.Results)
                result.Add(ToInfo(l));

            _log.Info(Source, $"Query returned {result.Count} lobby/lobbies.");
            return result;
        }

        public async Task LeaveLobbyAsync()
        {
            StopHeartbeat();
            if (_currentLobby == null) return;

            try
            {
                if (_isHost)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                    _log.Info(Source, $"Deleted lobby {_currentLobby.Id} (was host).");
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(
                        _currentLobby.Id, AuthenticationService.Instance.PlayerId);
                    _log.Info(Source, $"Left lobby {_currentLobby.Id}.");
                }
            }
            catch (Exception e)
            {
                _log.Warn(Source, $"LeaveLobby error (ignored — lobby may have already expired): {e.Message}");
            }
            finally
            {
                _currentLobby = null;
                _isHost       = false;
            }
        }

        // ---- Heartbeat (host only) --------------------------------------

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatCts = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
        }

        private void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _currentLobby != null)
            {
                try
                {
                    await Task.Delay(HeartbeatIntervalMs, ct);
                    if (ct.IsCancellationRequested || _currentLobby == null) break;
                    await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                    _log.Verbose(Source, "Lobby heartbeat sent.");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _log.Warn(Source, $"Heartbeat failed: {e.Message} — stopping.");
                    break;
                }
            }
        }

        // ---- Helpers ----------------------------------------------------

        private static LobbyInfo ToInfo(Lobby l) =>
            new LobbyInfo(l.Id, l.Name, l.LobbyCode, l.Players?.Count ?? 0, l.MaxPlayers);
    }
}
