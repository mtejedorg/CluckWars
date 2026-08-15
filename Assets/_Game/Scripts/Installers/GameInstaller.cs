using CluckWars.Networking;
using Zenject;

namespace CluckWars.Installers
{
    /// <summary>
    /// Game-scene bindings. Lives on the <c>SceneContext</c> GameObject in
    /// <c>Game.unity</c>. Inherits all bindings from <see cref="ProjectInstaller"/>.
    /// </summary>
    public sealed class GameInstaller : MonoInstaller<GameInstaller>
    {
        public override void InstallBindings()
        {
            // MatchConfigSO is deliberately NOT bound here. It moved to ProjectInstaller
            // so the Bootstrap menu lobby can advertise the same rules the match enforces
            // from the one asset reference. This container inherits it.

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
