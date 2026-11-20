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

        private bool _timeScaled;
        private float _scaledTo = 1f;
        private float _savedMaxDeltaTime;

        public bool IsRunning => _runner != null && _runner.IsRunning;
        public bool IsReady => _ready && IsRunning;
        private bool _ready;
        public NetworkRunner Runner => _runner;

        public event Action<NetworkRunner> OnRunnerReady;
        public event Action<NetworkRunner, PlayerRef> OnPlayerJoined;
        public event Action<NetworkRunner, PlayerRef> OnPlayerLeft;
        public event Action<ShutdownReason> OnShutdown;

        public NetDisconnectReason? LastDisconnectReason { get; private set; }

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
            LastDisconnectReason = null;

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
            // cw_timescale (dev tools, solo only): the runner gets its own copy of the config with scaled simulation time;
            // NetworkProjectConfig.fusion on disk stays unscaled. Every other mode ignores the flag.
            NetworkProjectConfig runnerConfig = null;
            float requestedScale = CluckWars.Diagnostics.DevTools.Available ? CluckWars.Diagnostics.AutoTestArgs.Read(_log).TimeScale : 1f;
            float timeScale = mode == GameMode.Single ? requestedScale : 1f;
            if (mode != GameMode.Single && requestedScale > 1f)
                _log?.Info(Source, $"cw_timescale ignored: only solo (GameMode.Single) matches can run scaled time, this is {mode}.");
            if (timeScale > 1f)
            {
                runnerConfig = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
                // The JSON round trip drops the prefab table (every Spawn then fails "baked with a guid ... failed to be translated").
                runnerConfig.PrefabTable = NetworkProjectConfig.Global.PrefabTable;
                runnerConfig.Simulation.SimulationUpdateTimeMode = SimulationConfig.SimulationTimeMode.DeltaTime;
            }

            int maxPlayers = _matchConfig != null ? Mathf.Clamp(_matchConfig.MaxPlayers, 1, 16) : 4;
            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = sessionName,
                SceneManager = sceneManager,
                PlayerCount = maxPlayers,
                Config = runnerConfig,
            });

            if (result.Ok)
            {
                _log?.Info(Source, $"StartGame OK. LocalPlayer={_runner.LocalPlayer}, MaxPlayers={maxPlayers}, IsSharedModeMasterClient={_runner.IsSharedModeMasterClient}.");
                _ready = true;
                if (timeScale > 1f)
                {
                    // Scaled simulation time follows Time.deltaTime; Unity caps it at maximumDeltaTime, so raise the cap
                    // to the scale or the clock would fall behind at high speeds.
                    _savedMaxDeltaTime = Time.maximumDeltaTime;
                    Time.maximumDeltaTime = Mathf.Max(Time.maximumDeltaTime, 0.1f * timeScale);
                    Time.timeScale = timeScale;
                    _timeScaled = true;
                    _scaledTo = timeScale;
                    _log?.Info(Source, $"cw_timescale: solo simulation at x{timeScale:0.##}.");
                }
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

        private void RestoreTimeScale()
        {
            if (!_timeScaled) return;
            _timeScaled = false;
            Time.timeScale = 1f;
            Time.maximumDeltaTime = _savedMaxDeltaTime;
        }

        private void OnDestroy() => RestoreTimeScale();

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
            // The execute hit-stop (HitStopDriver) ends by writing timeScale = 1; under cw_timescale that would drop the
            // whole run back to real time, so a plain 1 is put back to the flag's value (a dip below 1 is left alone).
            if (_timeScaled && Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = _scaledTo;
            TickNetDiagnostics();
            if (_inputProvider == null) return;

            if (GameManager.Instance == null || !GameManager.Instance.IsMatchRunning || LeaveSheetState.IsOpen)
            {
                _pendingAbility1 = false;
                _pendingAbility2 = false;
                _pendingAbility3 = false;
                _pendingAbility4 = false;
                _pendingAbilityCancel = false;
                _peckHold = default;
                _peckEdgeSeen = false;
                return;
            }

            // The slot holding Peck does not send its raw press: a tap pecks on release and a long press flips Auto-Peck
            // (PeckLongPressRules), so its press edge is fed to the gate instead of the latch. Every getter is still read.
            int peckSlot = LocalPeckSlot();
            bool p1 = _inputProvider.GetAbility1Pressed();
            bool p2 = _inputProvider.GetAbility2Pressed();
            bool p3 = _inputProvider.GetAbility3Pressed();
            bool p4 = _inputProvider.GetAbility4Pressed();
            bool cancel = _inputProvider.GetAbilityCancelPressed();
            if (p1 && peckSlot != 0) _pendingAbility1 = true;
            if (p2 && peckSlot != 1) _pendingAbility2 = true;
            if (p3 && peckSlot != 2) _pendingAbility3 = true;
            if (p4 && peckSlot != 3) _pendingAbility4 = true;
            if (cancel) _pendingAbilityCancel = true;
            StepPeckPress(peckSlot, peckSlot switch { 0 => p1, 1 => p2, 2 => p3, 3 => p4, _ => false }, cancel);
        }

        // Peck press gate state (local to this client; the preference it flips is local too).
        private PeckHoldState _peckHold;
        private bool _peckEdgeSeen;

        /// <summary>Slot of the local bird holding Peck (asked of its ability controller), or -1 (Spoiler and Thief have none).</summary>
        private static int LocalPeckSlot()
        {
            var local = ChickenController.Local;
            var abilities = local != null ? local.Abilities : null;
            return abilities != null ? abilities.FindPeckSlot() : AbilityController.InvalidSlot;
        }

        /// <summary>
        /// One frame of the Peck slot's gate: a tap latches one press for the next input tick, a long press flips Auto-Peck once.
        /// A press edge that only OnInput saw (<see cref="_peckEdgeSeen"/>) counts as "down" too, so a tap shorter than a frame is not lost.
        /// </summary>
        private void StepPeckPress(int peckSlot, bool pressEdge, bool cancel)
        {
            bool down = peckSlot >= 0 && (pressEdge || _peckEdgeSeen || _inputProvider.GetAbilityHeld(peckSlot));
            _peckEdgeSeen = false;
            switch (PeckLongPressRules.Step(ref _peckHold, peckSlot >= 0, down, cancel, Time.unscaledDeltaTime))
            {
                case PeckPressAction.FirePeck:
                    switch (peckSlot)
                    {
                        case 0: _pendingAbility1 = true; break;
                        case 1: _pendingAbility2 = true; break;
                        case 2: _pendingAbility3 = true; break;
                        case 3: _pendingAbility4 = true; break;
                    }
                    break;
                case PeckPressAction.ToggleAutoPeck:
                    bool on = !CluckWars.Settings.PlayerPreferences.AutoPeckEnabled;
                    CluckWars.Settings.PlayerPreferences.AutoPeckEnabled = on;
                    _log?.Info(Source, $"Auto-Peck {(on ? "on" : "off")} (long press on the Peck slot).");
                    break;
            }
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (_inputProvider == null) return;

            // "Leave match?" is up: this peer's bird stands still and nothing it presses reaches the simulation.
            // The only thing sent is a cancel, so a move being aimed when the sheet opened is dropped, not fired.
            if (LeaveSheetState.IsOpen)
            {
                _pendingAbility1 = _pendingAbility2 = _pendingAbility3 = _pendingAbility4 = _pendingAbilityCancel = false;
                _peckHold = default;
                _peckEdgeSeen = false;
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
            // The Peck slot's own press edge goes to the gate (StepPeckPress), never straight to the buffer; its hold bit is not sent either.
            int peckSlot = LocalPeckSlot();
            bool live1 = _inputProvider.GetAbility1Pressed(), live2 = _inputProvider.GetAbility2Pressed();
            bool live3 = _inputProvider.GetAbility3Pressed(), live4 = _inputProvider.GetAbility4Pressed();
            if ((peckSlot == 0 && live1) || (peckSlot == 1 && live2) || (peckSlot == 2 && live3) || (peckSlot == 3 && live4)) _peckEdgeSeen = true;
            if (_pendingAbility1 || (live1 && peckSlot != 0)) buttons.Set((int)InputButton.Ability1, true);
            if (_pendingAbility2 || (live2 && peckSlot != 1)) buttons.Set((int)InputButton.Ability2, true);
            if (_pendingAbility3 || (live3 && peckSlot != 2)) buttons.Set((int)InputButton.Ability3, true);
            if (_pendingAbility4 || (live4 && peckSlot != 3)) buttons.Set((int)InputButton.Ability4, true);
            if (_pendingAbilityCancel || _inputProvider.GetAbilityCancelPressed()) buttons.Set((int)InputButton.AbilityCancel, true);
            _pendingAbility1 = _pendingAbility2 = _pendingAbility3 = _pendingAbility4 = _pendingAbilityCancel = false;

            // Hold state is level-triggered and read live every tick — no latch, since a
            // hold spans many ticks by definition (unlike the one-shot presses above).
            if (_inputProvider.GetAbilityHeld(0) && peckSlot != 0) buttons.Set((int)InputButton.AbilityHold1, true);
            if (_inputProvider.GetAbilityHeld(1) && peckSlot != 1) buttons.Set((int)InputButton.AbilityHold2, true);
            if (_inputProvider.GetAbilityHeld(2) && peckSlot != 2) buttons.Set((int)InputButton.AbilityHold3, true);
            if (_inputProvider.GetAbilityHeld(3) && peckSlot != 3) buttons.Set((int)InputButton.AbilityHold4, true);

            // Level-triggered preferences, re-sent every tick so the state authority decides on them (A6, A8).
            if (CluckWars.Settings.PlayerPreferences.AutoPeckEnabled) buttons.Set((int)InputButton.AutoPeck, true);
            if (CluckWars.Settings.PlayerPreferences.QuickMovesEnabled) buttons.Set((int)InputButton.QuickMoves, true);

            // Which device the soft-lock magnetism should treat this input as. No device sends an aim: the Aim byte stays 0
            // ("use facing") on every client, exactly what a touch client has always sent.
            switch (_inputProvider.Device)
            {
                case InputDeviceKind.Touch: buttons.Set((int)InputButton.SoftLockTouch, true); break;
                case InputDeviceKind.Gamepad: buttons.Set((int)InputButton.SoftLockPad, true); break;
            }

            input.Set(new PlayerNetworkInput
            {
                Movement = worldMovement,
                Buttons = buttons,
            });

            // Verbose-only because this fires every simulation tick (32 Hz). Gated by IsEnabled
            // so we don't allocate the formatted string when filtered out.
            if (_log != null && _log.IsEnabled(Logging.LogLevel.Verbose) && (movement.sqrMagnitude > 0.0001f || buttons.Bits != 0))
            {
                _log.Verbose(Source, $"OnInput tick: move={movement}, a1={buttons.IsSet((int)InputButton.Ability1)}, a2={buttons.IsSet((int)InputButton.Ability2)}, a3={buttons.IsSet((int)InputButton.Ability3)}.");
            }
        }

        // ---------- Test-session network diagnostics (item 8) ----------
        // Breadcrumbs (always): join / leave / master change / shutdown, each with the cover and match state at that moment.
        // Net line (dev tools only): every 5 s, RTT to the session for the local player. Fusion 2 exposes no packet-loss
        // figure, so loss prints n/a; resimulations come from the runner's statistics when it has them enabled.
        private const float NetLineSeconds = 5f;
        private float _nextNetLineAt = -1f;
        private bool _masterKnown, _wasMaster;

        private void TickNetDiagnostics()
        {
            if (!IsReady)
            {
                _masterKnown = false;
                _nextNetLineAt = -1f;
                CluckWars.Diagnostics.DiagnosticsReadout.NetRttMs = -1f;
                return;
            }

            bool master = _runner.IsSharedModeMasterClient;
            if (!_masterKnown) { _masterKnown = true; _wasMaster = master; }
            else if (master != _wasMaster)
            {
                _wasMaster = master;
                _log?.Info(Source, $"Master client changed: this peer is {(master ? "now" : "no longer")} the master " +
                    $"(local={_runner.LocalPlayer}, players={CountPlayers(_runner)}, {DescribeMoment()}).");
            }

            if (!CluckWars.Diagnostics.DevTools.Available || _runner.GameMode == GameMode.Single) return;
            float now = Time.realtimeSinceStartup;
            if (_nextNetLineAt < 0f) { _nextNetLineAt = now + NetLineSeconds; return; }
            if (now < _nextNetLineAt) return;
            _nextNetLineAt = now + NetLineSeconds;

            float rttMs = (float)(_runner.GetPlayerRtt(_runner.LocalPlayer) * 1000.0);
            CluckWars.Diagnostics.DiagnosticsReadout.NetRttMs = rttMs;
            CluckWars.Diagnostics.DiagnosticsReadout.NetLoss = "n/a";
            string resim = "n/a";
            if (_runner.TryGetFusionStatistics(out var stats) && stats != null && stats.CompleteSnapshot != null)
                resim = stats.CompleteSnapshot.Resimulations.ToString();
            _log?.Info(Source, $"Net rtt={rttMs:0} loss=n/a players={CountPlayers(_runner)} master={(master ? 1 : 0)} " +
                $"tick={(int)_runner.Tick} resim={resim}");
        }

        private static int CountPlayers(NetworkRunner runner)
        {
            int n = 0;
            foreach (var p in runner.ActivePlayers) if (p.IsRealPlayer) n++;
            return n;
        }

        /// <summary><c>cover=.. revealed=.. state=..</c> at this moment (presentation + match state, read-only).</summary>
        private static string DescribeMoment()
        {
            var gm = GameManager.Instance;
            string state = gm != null && gm.Object != null && gm.Object.IsValid ? gm.State.ToString() : "none";
            return $"{CluckWars.Diagnostics.RevealState.Describe()} state={state}";
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            _log?.Info(Source, $"OnPlayerJoined: player={player} id={player.PlayerId} players={CountPlayers(runner)} " +
                $"local={(player == runner.LocalPlayer ? 1 : 0)} {DescribeMoment()}.");
            OnPlayerJoined?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            _log?.Info(Source, $"OnPlayerLeft: player={player} id={player.PlayerId} players={CountPlayers(runner)} {DescribeMoment()}.");
            OnPlayerLeft?.Invoke(runner, player);
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _log?.Info(Source, $"OnShutdown: reason={shutdownReason} {DescribeMoment()}.");
            _ready = false;
            RestoreTimeScale();
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
            LastDisconnectReason = reason;   // read by the OnShutdown handlers that follow (DisconnectRules.Classify, CW-218)
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
