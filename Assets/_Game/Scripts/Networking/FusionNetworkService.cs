using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace CluckWars.Networking
{
    /// <summary>
    /// Photon Fusion 2 implementation of <see cref="INetworkService"/>. Owns the
    /// <see cref="NetworkRunner"/> component and pumps per-tick input from the
    /// injected <see cref="IInputProvider"/>.
    /// </summary>
    /// <remarks>
    /// Lives in the Game scene as a single GameObject. Bound by <c>GameInstaller</c>
    /// via <c>FromComponentInHierarchy().AsSingle()</c>. The runner and a
    /// <c>NetworkSceneManagerDefault</c> are added to the same GameObject at startup.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FusionNetworkService : MonoBehaviour, INetworkService, INetworkRunnerCallbacks
    {
        private const string Source = "Fusion";

        private IInputProvider _inputProvider;
        private MatchConfigSO _matchConfig;
        private ILogService _log;
        private NetworkRunner _runner;

        // Latched ability-press edges. Edge-triggered presses (HoldButton /
        // keyboard wasPressedThisFrame) live for a single render frame, but
        // OnInput runs at tick rate (~32 Hz), not every frame (~60 Hz) — so a
        // press landing on a non-tick frame was being cleared before OnInput ever
        // read it and got silently dropped (≈half of taps "did nothing"). We OR
        // every render frame's press into these latches and consume them in
        // OnInput, so no press is ever lost regardless of frame/tick alignment.
        private bool _pendingAbility1, _pendingAbility2, _pendingAbility3, _pendingAbility4, _pendingAbilityCancel;

        public bool IsRunning => _runner != null && _runner.IsRunning;
        public bool IsReady => _ready && IsRunning;
        private bool _ready;
        public NetworkRunner Runner => _runner;

        public event Action<NetworkRunner> OnRunnerReady;
        public event Action<NetworkRunner, PlayerRef> OnPlayerJoined;
        public event Action<NetworkRunner, PlayerRef> OnPlayerLeft;
        public event Action<ShutdownReason> OnShutdown;

        [Inject]
        public void Construct(IInputProvider inputProvider, MatchConfigSO matchConfig, ILogService log)
        {
            _inputProvider = inputProvider;
            _matchConfig = matchConfig;
            _log = log;
        }

        public Task StartSoloAsync() => StartAsync(GameMode.Single, "Solo");
        public Task StartHostAsync(string sessionName) => StartAsync(GameMode.Shared, sessionName);
        public Task JoinSessionAsync(string sessionName) => StartAsync(GameMode.Shared, sessionName);

        private async Task StartAsync(GameMode mode, string sessionName)
        {
            if (_runner != null)
            {
                _log?.Warn(Source, "StartAsync called while a runner is already active. Ignoring.");
                return;
            }

            _log?.Info(Source, $"StartGame requested: mode={mode}, session='{sessionName}'.");

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

            // Do NOT pass the current scene as sceneInfo. The scene is already
            // loaded by Unity; telling Fusion to load it additively would create
            // a second copy of every MonoBehaviour (MatchBootstrapper, MapGenerator,
            // etc.), causing duplicate chicken spawns and double bot registration.
            // Fusion registers existing NetworkObjects in the active scene
            // automatically without needing an explicit SceneRef here.
            int maxPlayers = _matchConfig != null ? Mathf.Clamp(_matchConfig.MaxPlayers, 1, 16) : 4;
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = sessionName,
                SceneManager = sceneManager,
                PlayerCount = maxPlayers,
            });

            if (result.Ok)
            {
                _log?.Info(Source, $"StartGame OK. LocalPlayer={_runner.LocalPlayer}, MaxPlayers={maxPlayers}, IsSharedModeMasterClient={_runner.IsSharedModeMasterClient}.");
                _ready = true;
                OnRunnerReady?.Invoke(_runner);
            }
            else
            {
                _log?.Error(Source, $"StartGame failed: result.ShutdownReason={result.ShutdownReason}, errorMessage='{result.ErrorMessage}'. Common causes: wrong AppId, region mismatch, room full ({maxPlayers}/{maxPlayers}), or no internet.");
                Destroy(_runner);
                Destroy(sceneManager);
                _runner = null;
            }
        }

        public async Task ShutdownAsync()
        {
            if (_runner != null)
            {
                await _runner.Shutdown();
            }
        }

        // ---------- INetworkRunnerCallbacks ----------

        // Every render frame: latch any ability-press edge so it survives until the
        // next OnInput tick. Movement is continuous and stays read live in OnInput.
        private void Update()
        {
            if (_inputProvider == null) return;

            if (GameManager.Instance == null || !GameManager.Instance.IsMatchRunning || LeaveSheetState.IsOpen)
            {
                _pendingAbility1 = false;
                _pendingAbility2 = false;
                _pendingAbility3 = false;
                _pendingAbility4 = false;
                _pendingAbilityCancel = false;
                return;
            }

            if (_inputProvider.GetAbility1Pressed()) _pendingAbility1 = true;
            if (_inputProvider.GetAbility2Pressed()) _pendingAbility2 = true;
            if (_inputProvider.GetAbility3Pressed()) _pendingAbility3 = true;
            if (_inputProvider.GetAbility4Pressed()) _pendingAbility4 = true;
            if (_inputProvider.GetAbilityCancelPressed()) _pendingAbilityCancel = true;
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (_inputProvider == null) return;

            // "Leave match?" is up: this peer's bird stands still and nothing it presses reaches the simulation.
            // The only thing sent is a cancel, so a move being aimed when the sheet opened is dropped, not fired.
            if (LeaveSheetState.IsOpen)
            {
                _pendingAbility1 = _pendingAbility2 = _pendingAbility3 = _pendingAbility4 = _pendingAbilityCancel = false;
                var cancelOnly = new NetworkButtons();
                cancelOnly.Set((int)InputButton.AbilityCancel, true);
                input.Set(new PlayerNetworkInput { Buttons = cancelOnly });
                return;
            }

            var movement = _inputProvider.GetMovement();
            var worldMovement = movement;
            float cameraYaw = CluckWars.Visuals.MatchCamera.Instance != null
                ? CluckWars.Visuals.MatchCamera.Instance.CurrentYaw
                : 0f;
            if (CluckWars.Visuals.MatchCamera.Instance != null && movement.sqrMagnitude > 0.0001f)
                worldMovement = AimRules.StickToWorld(movement, cameraYaw);

            var buttons = new NetworkButtons();
            // Consume the latch OR a same-frame live press (covers either Update/OnInput
            // ordering), then clear so each press activates exactly once.
            if (_pendingAbility1 || _inputProvider.GetAbility1Pressed()) buttons.Set((int)InputButton.Ability1, true);
            if (_pendingAbility2 || _inputProvider.GetAbility2Pressed()) buttons.Set((int)InputButton.Ability2, true);
            if (_pendingAbility3 || _inputProvider.GetAbility3Pressed()) buttons.Set((int)InputButton.Ability3, true);
            if (_pendingAbility4 || _inputProvider.GetAbility4Pressed()) buttons.Set((int)InputButton.Ability4, true);
            if (_pendingAbilityCancel || _inputProvider.GetAbilityCancelPressed()) buttons.Set((int)InputButton.AbilityCancel, true);
            _pendingAbility1 = _pendingAbility2 = _pendingAbility3 = _pendingAbility4 = _pendingAbilityCancel = false;

            // Hold state is level-triggered and read live every tick — no latch, since a
            // hold spans many ticks by definition (unlike the one-shot presses above).
            if (_inputProvider.GetAbilityHeld(0)) buttons.Set((int)InputButton.AbilityHold1, true);
            if (_inputProvider.GetAbilityHeld(1)) buttons.Set((int)InputButton.AbilityHold2, true);
            if (_inputProvider.GetAbilityHeld(2)) buttons.Set((int)InputButton.AbilityHold3, true);
            if (_inputProvider.GetAbilityHeld(3)) buttons.Set((int)InputButton.AbilityHold4, true);

            // Level-triggered preferences, re-sent every tick so the state authority decides on them (A6, A8).
            if (CluckWars.Settings.PlayerPreferences.AutoPeckEnabled) buttons.Set((int)InputButton.AutoPeck, true);
            if (CluckWars.Settings.PlayerPreferences.QuickMovesEnabled) buttons.Set((int)InputButton.QuickMoves, true);

            // Phase 6 chunk 5 (A7): the aim byte, and which device the soft-lock magnetism should treat this input as.
            // Resolved here, on the local client, from the device; the state authority only ever sees the byte and bits.
            var localBird = FindLocalChicken();
            byte aim = 0;
            if (localBird != null)
            {
                var birdPos = localBird.transform.position;
                var aimWorld = AimRules.Resolve(_inputProvider.GetAim(birdPos.y), birdPos, cameraYaw);
                aim = AimQuantizer.Encode(aimWorld);
            }
            switch (_inputProvider.Device)
            {
                case InputDeviceKind.Touch: buttons.Set((int)InputButton.SoftLockTouch, true); break;
                case InputDeviceKind.Gamepad: buttons.Set((int)InputButton.SoftLockPad, true); break;
            }

            input.Set(new PlayerNetworkInput
            {
                Movement = worldMovement,
                Buttons = buttons,
                Aim = aim,
            });

            // Verbose-only because this fires every simulation tick (32 Hz). Gated by IsEnabled
            // so we don't allocate the formatted string when filtered out.
            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose) && (movement.sqrMagnitude > 0.0001f || buttons.Bits != 0))
            {
                _log.Verbose(Source, $"OnInput tick: move={movement}, a1={buttons.IsSet((int)InputButton.Ability1)}, a2={buttons.IsSet((int)InputButton.Ability2)}, a3={buttons.IsSet((int)InputButton.Ability3)}.");
            }
        }

        /// <summary>The chicken this client drives (input authority, not a bot), or null before it spawns.</summary>
        private static ChickenController FindLocalChicken()
        {
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && c.Object != null && c.Object.IsValid && c.HasInputAuthority && !c.IsBot) return c;
            }
            return null;
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            _log?.Debug(Source, $"OnPlayerJoined: player={player}.");
            OnPlayerJoined?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            _log?.Debug(Source, $"OnPlayerLeft: player={player}.");
            OnPlayerLeft?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _log?.Info(Source, $"OnShutdown: reason={shutdownReason}.");
            _ready = false;
            OnShutdown?.Invoke(shutdownReason);
            var sceneManager = GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager != null)
            {
                Destroy(sceneManager);
            }
            _runner = null;
        }

        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        // Connect / disconnect callbacks now log explicitly so cross-device
        // join failures (3rd-player-can't-connect, etc.) leave a trail in
        // adb logcat instead of silently failing.
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
        {
            _log?.Info(Source, $"OnConnectedToServer. LocalPlayer={runner.LocalPlayer}, IsSharedModeMasterClient={runner.IsSharedModeMasterClient}.");
        }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            _log?.Warn(Source, $"OnDisconnectedFromServer. reason={reason}.");
        }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            _log?.Debug(Source, $"OnConnectRequest from {request.RemoteAddress}.");
        }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            _log?.Error(Source, $"OnConnectFailed: remote={remoteAddress}, reason={reason}.");
        }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
