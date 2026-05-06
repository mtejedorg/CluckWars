using CluckWars.Audio;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using CluckWars.Services;
using UnityEngine;
using Zenject;

namespace CluckWars.Installers
{
    /// <summary>
    /// App-wide bindings. Lives as a component on
    /// <c>Assets/_Game/Resources/ProjectContext.prefab</c> — Zenject auto-loads that
    /// prefab the first time any container is resolved. Anything bound here survives
    /// scene loads and is shared by every <c>SceneContext</c>.
    /// </summary>
    public sealed class ProjectInstaller : MonoInstaller<ProjectInstaller>
    {
        [Header("Logging")]
        [Tooltip("Minimum level emitted by ILogService. Default Verbose during dev; raise for builds.")]
        [SerializeField] private LogLevel _logMinLevel = LogLevel.Verbose;

        [Header("Static Data")]
        [Tooltip("Drop the ChickenClassRegistry asset here so Game-scene systems can resolve it.")]
        [SerializeField] private ChickenClassRegistrySO _chickenClassRegistry;

        public override void InstallBindings()
        {
            // Logger bound first so other bindings can complain through it during install if they need to.
            Container.Bind<ILogService>()
                .To<UnityLogService>()
                .AsSingle()
                .WithArguments(_logMinLevel);

            // Stateless service stand-ins.
            Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();
            Container.Bind<IAudioService>().To<NullAudioService>().AsSingle();
            Container.Bind<IInputProvider>().To<KeyboardInputProvider>().AsSingle();

            // Cross-scene mutable state for menu → match handoff.
            Container.Bind<ISessionSelectionService>().To<SessionSelectionService>().AsSingle();

            // Static data assets — bound by instance so the same SO ships to every consumer.
            if (_chickenClassRegistry != null)
            {
                Container.Bind<ChickenClassRegistrySO>().FromInstance(_chickenClassRegistry).AsSingle();
            }
            else
            {
                Debug.LogWarning(
                    "[ProjectInstaller] ChickenClassRegistry not assigned. " +
                    "Class-aware spawning will fall back to the prefab's serialized stats.");
            }
        }
    }
}
