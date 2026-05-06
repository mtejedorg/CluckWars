using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CluckWars.Input;
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
        private IInputProvider _inputProvider;
        private NetworkRunner _runner;

        public bool IsRunning => _runner != null && _runner.IsRunning;
        public NetworkRunner Runner => _runner;

        public event Action<NetworkRunner> OnRunnerReady;
        public event Action<NetworkRunner, PlayerRef> OnPlayerJoined;
        public event Action<NetworkRunner, PlayerRef> OnPlayerLeft;
        public event Action<ShutdownReason> OnShutdown;

        [Inject]
        public void Construct(IInputProvider inputProvider)
        {
            _inputProvider = inputProvider;
        }

        public Task StartSoloAsync() => StartAsync(GameMode.Single, "Solo");
        public Task StartHostAsync(string sessionName) => StartAsync(GameMode.Shared, sessionName);
        public Task JoinSessionAsync(string sessionName) => StartAsync(GameMode.Shared, sessionName);

        private async Task StartAsync(GameMode mode, string sessionName)
        {
            if (_runner != null)
            {
                Debug.LogWarning("[FusionNetworkService] StartAsync called while a runner is already active.");
                return;
            }

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            var sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
            var sceneInfo = new NetworkSceneInfo();
            var sceneRef = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
            if (sceneRef.IsValid)
            {
                sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Additive);
            }

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = sessionName,
                Scene = sceneInfo,
                SceneManager = sceneManager,
                PlayerCount = 4,
            });

            if (result.Ok)
            {
                OnRunnerReady?.Invoke(_runner);
            }
            else
            {
                Debug.LogError($"[FusionNetworkService] StartGame failed: {result.ShutdownReason}");
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

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (_inputProvider == null) return;

            var buttons = new NetworkButtons();
            if (_inputProvider.GetAttackHeld()) buttons.Set((int)InputButton.Attack, true);
            if (_inputProvider.GetAbility1Pressed()) buttons.Set((int)InputButton.Ability1, true);
            if (_inputProvider.GetAbility2Pressed()) buttons.Set((int)InputButton.Ability2, true);

            input.Set(new PlayerNetworkInput
            {
                Movement = _inputProvider.GetMovement(),
                Buttons = buttons,
            });
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
            => OnPlayerJoined?.Invoke(runner, player);

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
            => OnPlayerLeft?.Invoke(runner, player);

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            OnShutdown?.Invoke(shutdownReason);
            _runner = null;
        }

        // Phase 1: stub the rest. Wire them up as features land.
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
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
