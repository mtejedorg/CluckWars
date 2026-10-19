using CluckWars.Gameplay;
using CluckWars.Networking;
using CluckWars.Visuals;
using Zenject;

namespace CluckWars.Installers
{
    /// <summary>
    /// Game-scene bindings. Lives on the <c>SceneContext</c> GameObject in
    /// <c>Game.unity</c>. Inherits all bindings from <see cref="ProjectInstaller"/>.
    /// </summary>
    public sealed class GameInstaller : MonoInstaller<GameInstaller>
    {
        [UnityEngine.Tooltip("Bind the real ArenaReadinessDriver (warm-up behind the arena cover). Untick in a scene with no arena to warm " +
                             "(the Ability Lab): it then binds NullArenaReadiness (always ready).")]
        [UnityEngine.SerializeField] private bool _useArenaReadiness = true;

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

            // Local arena readiness (presentation only): ticks from the Game scene's first frame, runs the warm-up behind
            // the arena cover, and tells GameManager (settle gate) and the cover (reveal) when this peer's arena is ready.
            // Scene-scoped so every scene load starts a new readiness; a missing binding is a wiring bug that the consumers
            // log once before falling back to the old behaviour.
            if (!_useArenaReadiness)
            {
                Container.Bind<IArenaReadiness>().FromInstance(NullArenaReadiness.Instance).AsSingle();
                return;
            }
            Container.Bind<IArenaReadiness>()
                .To<ArenaReadinessDriver>()
                .FromNewComponentOnNewGameObject()
                .WithGameObjectName("ArenaReadiness")
                .AsSingle()
                .NonLazy();
        }
    }
}
