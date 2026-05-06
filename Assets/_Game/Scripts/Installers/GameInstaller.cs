using CluckWars.Gameplay;
using CluckWars.Networking;
using UnityEngine;
using Zenject;

namespace CluckWars.Installers
{
    /// <summary>
    /// Game-scene bindings. Lives on the <c>SceneContext</c> GameObject in
    /// <c>Game.unity</c>. Inherits all bindings from <see cref="ProjectInstaller"/>.
    /// </summary>
    public sealed class GameInstaller : MonoInstaller<GameInstaller>
    {
        [Header("Match")]
        [SerializeField] private MatchConfigSO _matchConfig;

        public override void InstallBindings()
        {
            // Match-level config asset, single instance, accessible to any consumer.
            Container.Bind<MatchConfigSO>().FromInstance(_matchConfig).AsSingle();

            // Spin up the network service on its own GameObject. Zenject injects the
            // IInputProvider on construction; the component then sits idle until
            // MatchBootstrapper calls StartSoloAsync().
            Container.Bind<INetworkService>()
                .To<FusionNetworkService>()
                .FromNewComponentOnNewGameObject()
                .WithGameObjectName("NetworkService")
                .AsSingle()
                .NonLazy();
        }
    }
}
