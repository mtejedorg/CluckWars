using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Balance;
using CluckWars.Diagnostics;
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
        /// <summary>Public lobby data key holding the host's live-ops balance hash (<see cref="BalanceBook.Hash"/>).</summary>
        public const string BalanceLobbyKey = "bal";

        private readonly ILogService _log;
        private readonly BalanceBook _balance;

        private bool  _initialized;
        // Single-flight (review F1): the boot balance fetch and the menu / auto-host can ask for init and sign-in in the
        // same second. Concurrent callers share the running task; a finished one (failed, or a sign-out since) is replaced.
        private Task  _initTask;
        private Task  _signInTask;
        private Lobby _currentLobby;
        private bool  _isHost;
        private CancellationTokenSource _heartbeatCts;

        public bool IsAvailable => _initialized;
        public bool IsSignedIn  => _initialized && AuthenticationService.Instance.IsSignedIn;

        public LobbyInfo CurrentLobby =>
            _currentLobby != null ? ToInfo(_currentLobby) : null;

        public string LastJoinCode { get; private set; }

        public UGSService(ILogService log, BalanceBook balance)
        {
            _log = log;
            _balance = balance;
        }

        // ---- Auth -------------------------------------------------------

        public Task InitializeAsync()
        {
            if (_initialized) return Task.CompletedTask;
            if (_initTask == null || _initTask.IsCompleted) _initTask = InitializeOnceAsync();
            return _initTask;
        }

        private async Task InitializeOnceAsync()
        {
            // Dev-tools builds only: cw_profile picks an authentication profile, so two clients on one PC get two
            // anonymous identities. Unset (and every production build): the default profile, as before.
            var profile = CluckWars.Diagnostics.DevTools.Available ? CluckWars.Diagnostics.AutoTestArgs.Read(null).Profile : null;
            try
            {
                if (profile != null)
                {
                    _log.Info(Source, $"Unity Services profile '{profile}' (cw_profile).");
                    await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(profile));
                }
                else await UnityServices.InitializeAsync();
            }
            catch (System.Exception e)
            {
                // The aggregate message ("Some services couldn't be initialized") hides the cause (e.g. no linked
                // project id in a headless build); log every inner exception before rethrowing.
                for (var inner = e; inner != null; inner = inner.InnerException)
                    _log.Error(Source, $"UnityServices.InitializeAsync failed: {inner.GetType().Name}: {inner.Message}");
                if (e is System.AggregateException agg)
                    foreach (var x in agg.Flatten().InnerExceptions)
                        _log.Error(Source, $"  inner: {x.GetType().Name}: {x.Message}");
                throw;
            }
            _initialized = true;
            _log.Info(Source, "Unity Services initialized.");
        }

        public Task SignInAnonymouslyAsync()
        {
            if (_initialized && AuthenticationService.Instance.IsSignedIn) return Task.CompletedTask;
            if (_signInTask == null || _signInTask.IsCompleted) _signInTask = SignInOnceAsync();
            return _signInTask;
        }

        private async Task SignInOnceAsync()
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
            // The hash the lobby advertises must be the balance the host plays: no delta applies after this point.
            _balance.CloseApplyWindow("lobby created");
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    [BalanceLobbyKey] = new DataObject(DataObject.VisibilityOptions.Public, _balance.Hash),
                },
            };
            DiagnosticsReadout.BalanceMismatch = null;
            var lobby   = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, options);

            _currentLobby = lobby;
            _isHost       = true;
            LastJoinCode  = lobby.LobbyCode;
            StartHeartbeat();

            _log.Info(Source, $"Lobby created. Name={lobby.Name} Id={lobby.Id} Code={lobby.LobbyCode} MaxPlayers={lobby.MaxPlayers}");
            return ToInfo(lobby);
        }

        public async Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode)
        {
            await SignInAnonymouslyAsync();

            var code  = joinCode.Trim().ToUpper();

            // CW-218 REJOIN: a peer that lost its Fusion connection never left its UGS lobby (nothing calls LeaveLobbyAsync
            // on a timeout), so it is still a member. Re-read that lobby instead of joining it again, and keep _isHost: a
            // host that timed out still owns (and heartbeats) the lobby.
            var member = _currentLobby;
            if (member != null && string.Equals(member.LobbyCode, code, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fresh = await LobbyService.Instance.GetLobbyAsync(member.Id);
                    _currentLobby = fresh;
                    LastJoinCode  = fresh.LobbyCode;
                    _log.Info(Source, $"Already a member of lobby {fresh.Id} (code {fresh.LobbyCode}, host={_isHost}); rejoining without a new lobby join. " +
                        $"Players={fresh.Players?.Count}/{fresh.MaxPlayers}");
                    CompareBalance(fresh);
                    return ToInfo(fresh);
                }
                catch (LobbyServiceException e)
                {
                    _log.Warn(Source, $"Re-reading lobby {member.Id} failed: Reason={e.Reason} ({(int)e.Reason}) ErrorCode={e.ErrorCode}: {e.Message}");
                    if (e.Reason == LobbyExceptionReason.LobbyNotFound) throw;   // gone: the caller reports "over or full"
                    // anything else: fall through to an ordinary join (the SDK resolves an 'already a member' conflict itself)
                }
            }

            Lobby lobby;
            try
            {
                lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
            }
            catch (LobbyServiceException e)
            {
                // Full / gone / already a member / rate limited read apart here; the caller only shows one line.
                _log.Warn(Source, $"JoinLobbyByCode '{code}' failed: Reason={e.Reason} ({(int)e.Reason}) ErrorCode={e.ErrorCode}: {e.Message}");
                throw;
            }

            _currentLobby = lobby;
            _isHost       = false;
            LastJoinCode  = lobby.LobbyCode;

            _log.Info(Source, $"Joined lobby by code. Id={lobby.Id} Code={lobby.LobbyCode} Players={lobby.Players?.Count}/{lobby.MaxPlayers}");
            CompareBalance(lobby);
            return ToInfo(lobby);
        }

        public async Task<LobbyInfo> JoinLobbyAsync(LobbyInfo lobby)
        {
            await SignInAnonymouslyAsync();

            var joined = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.LobbyId);

            _currentLobby = joined;
            _isHost       = false;
            LastJoinCode  = joined.LobbyCode;

            _log.Info(Source, $"Joined lobby by id. Id={joined.Id} Code={joined.LobbyCode} Players={joined.Players?.Count}/{joined.MaxPlayers}");
            CompareBalance(joined);
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
                LastJoinCode  = null;
                DiagnosticsReadout.BalanceMismatch = null;
            }
        }

        public async Task<LobbyAvailability> CheckLobbyAsync(string joinCode)
        {
            var lobby = _currentLobby;
            // Only this client's own lobby can be looked up (by id); anything else is "can't tell", and the join decides.
            if (!IsSignedIn || lobby == null || string.IsNullOrWhiteSpace(joinCode)
                || !string.Equals(lobby.LobbyCode, joinCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _log.Info(Source, $"Lobby check for '{joinCode}': not this client's current lobby (or not signed in); unknown.");
                return LobbyAvailability.Unknown;
            }

            try
            {
                await LobbyService.Instance.GetLobbyAsync(lobby.Id);
                _log.Info(Source, $"Lobby check: {lobby.Id} still exists.");
                return LobbyAvailability.Open;
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                _log.Info(Source, $"Lobby check: {lobby.Id} is gone ({e.Reason}).");
                return LobbyAvailability.Gone;
            }
            catch (Exception e)
            {
                _log.Warn(Source, $"Lobby check for {lobby.Id} failed ({e.GetType().Name}: {e.Message}); REJOIN stays offered.");
                return LobbyAvailability.Unknown;
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
                catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    _log.Warn(Source, $"Heartbeat: lobby is gone ({e.Reason}) — stopping.");
                    break;
                }
                catch (Exception e)
                {
                    // Keep beating: a transient outage (session-20261021-205856: "(0) Request timeout") used to stop the
                    // loop for good, so the lobby expired ~30 s later and the host's own REJOIN found it gone (CW-218).
                    _log.Warn(Source, $"Heartbeat failed: {e.Message} — retrying in {HeartbeatIntervalMs / 1000} s.");
                }
            }
        }

        // ---- Balance check (warn only, D3c) -----------------------------

        /// <summary>
        /// Compares the host's published balance hash with this peer's. A difference is a Warn and a debug-HUD line, never
        /// a refused join: each peer plays its own copy and judges claims against it (<see cref="BalanceBook"/> remarks).
        /// </summary>
        private void CompareBalance(Lobby lobby)
        {
            // Joined: from here this peer plays the balance it has, so the hash compared is the one it will play.
            _balance.CloseApplyWindow("lobby joined");
            if (_isHost) return; // the host published the hash itself
            string local = _balance.Hash;
            string host = lobby.Data != null && lobby.Data.TryGetValue(BalanceLobbyKey, out var data) ? data?.Value : null;
            if (host == null)
            {
                DiagnosticsReadout.BalanceMismatch = null;
                _log.Info(Source, $"Lobby {lobby.Id} publishes no balance hash (host on an older build); this peer plays bal={local}.");
                return;
            }
            if (host == local)
            {
                DiagnosticsReadout.BalanceMismatch = null;
                return;
            }
            DiagnosticsReadout.BalanceMismatch = $"host={host} local={local}";
            _log.Warn(Source, $"Balance mismatch with lobby {lobby.Id}: host bal={host}, this peer bal={local}. " +
                              "Joining anyway; the two peers play different numbers until both have the same delta.");
        }

        // ---- Helpers ----------------------------------------------------

        private static LobbyInfo ToInfo(Lobby l) =>
            new LobbyInfo(l.Id, l.Name, l.LobbyCode, l.Players?.Count ?? 0, l.MaxPlayers);
    }
}
