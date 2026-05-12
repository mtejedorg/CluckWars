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

        [Tooltip("Drop the AudioRegistry asset here so audio cues resolve. If null, an empty runtime instance is bound and all SFX are silent.")]
        [SerializeField] private AudioRegistrySO _audioRegistry;

        [Tooltip("Drop the PrefabRegistry asset here so runtime-spawned NetworkObjects (chicken, base, pile, pickup, GameManager) resolve from one place instead of scattered slots.")]
        [SerializeField] private PrefabRegistrySO _prefabRegistry;

        public override void InstallBindings()
        {
            // Logger bound first so other bindings can complain through it during install if they need to.
            Container.Bind<ILogService>()
                .To<UnityLogService>()
                .AsSingle()
                .WithArguments(_logMinLevel);

            // Stateless service stand-ins.
            Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();

            // UnityAudioService lives under the ProjectContext transform so its
            // AudioSources survive scene loads. NullAudioService can still be bound
            // here in headless / test builds.
            Container.Bind<IAudioService>()
                .FromMethod(_ => new UnityAudioService(transform))
                .AsSingle();

            // Input: keyboard + on-screen touch HUD compose into one provider so the
            // local player can drive the chicken from either source. The touch HUD is
            // dormant until the Game scene's TouchControlsHud builds itself; on PC the
            // keyboard side dominates, on mobile the touch side does.
            Container.Bind<IInputProvider>()
                .FromMethod(_ => new CompositeInputProvider(
                    new KeyboardInputProvider(),
                    new TouchInputProvider()))
                .AsSingle();

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

            // AudioRegistry: bind whatever asset is supplied, or a runtime-empty
            // instance so consumers don't have to null-check the binding. Empty
            // clip fields are already a silent no-op in UnityAudioService.
            var audioRegistry = _audioRegistry != null
                ? _audioRegistry
                : ScriptableObject.CreateInstance<AudioRegistrySO>();
            Container.Bind<AudioRegistrySO>().FromInstance(audioRegistry).AsSingle();
            if (_audioRegistry == null)
            {
                Debug.LogWarning(
                    "[ProjectInstaller] AudioRegistry not assigned. " +
                    "All audio cues will be silent.");
            }

            // PrefabRegistry: same shape as AudioRegistry. Empty instance is bound
            // when the asset slot is null so consumers can always inject it; they
            // fall back to per-component SerializeField slots when a field is null.
            var prefabRegistry = _prefabRegistry != null
                ? _prefabRegistry
                : ScriptableObject.CreateInstance<PrefabRegistrySO>();
            Container.Bind<PrefabRegistrySO>().FromInstance(prefabRegistry).AsSingle();
            if (_prefabRegistry == null)
            {
                Debug.LogWarning(
                    "[ProjectInstaller] PrefabRegistry not assigned. " +
                    "Each consumer will fall back to its legacy SerializeField slot.");
            }
        }
    }
}
