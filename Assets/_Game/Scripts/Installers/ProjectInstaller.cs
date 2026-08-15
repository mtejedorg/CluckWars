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

        [Tooltip("Drop the ColorScheme asset here so HUD / food / button colors come from one palette instead of inline literals. If null, an empty instance is bound with its built-in defaults.")]
        [SerializeField] private ColorSchemeSO _colorScheme;

        [Tooltip("Drop the AbilityRegistry asset here (created via Cluck Wars / Ability Registry). " +
                 "Populates the global ability pool for the character-select screen (GDD v0.3 §7.1).")]
        [SerializeField] private AbilityRegistrySO _abilityRegistry;

        [Tooltip("Drop the MatchConfig asset here. This is the ONLY serialized reference to it in " +
                 "the project — the Bootstrap menu lobby and the Game-scene match systems both read " +
                 "it from this one slot, so the advertised rules and the played rules cannot drift.")]
        [SerializeField] private MatchConfigSO _matchConfig;

        public override void InstallBindings()
        {
            // Logger bound first so other bindings can complain through it during install if they need to.
            Container.Bind<ILogService>()
                .To<UnityLogService>()
                .AsSingle()
                .WithArguments(_logMinLevel);

            // UGS: real service when the packages are present; NullUGSService in
            // offline/demo builds (define UGS_DISABLED in Project Settings to force null).
#if UGS_DISABLED
            Container.Bind<IUGSService>().To<NullUGSService>().AsSingle();
#else
            Container.Bind<IUGSService>().To<UGSService>().AsSingle();
#endif

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
            // Synthesise any clip the asset doesn't supply, so the game has sound
            // even with no recorded audio assets. Authored clips win; only null
            // fields are filled. (Counterpart to the procedural placeholder meshes.)
            CluckWars.Audio.ProceduralAudioBank.FillMissing(audioRegistry);
            Container.Bind<AudioRegistrySO>().FromInstance(audioRegistry).AsSingle();
            if (_audioRegistry == null)
            {
                Debug.Log(
                    "[ProjectInstaller] AudioRegistry asset not assigned — using " +
                    "fully procedural SFX bank.");
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

            // ColorScheme: empty instance still carries the SO's built-in default
            // colors (defined as field initializers), so consumers always get a
            // usable palette even when the asset isn't assigned.
            var colorScheme = _colorScheme != null
                ? _colorScheme
                : ScriptableObject.CreateInstance<ColorSchemeSO>();
            Container.Bind<ColorSchemeSO>().FromInstance(colorScheme).AsSingle();

            // AbilityRegistry: empty instance is safe — CharacterSelectController falls back
            // to an empty pool and shows "Default" labels for each slot.
            var abilityRegistry = _abilityRegistry != null
                ? _abilityRegistry
                : ScriptableObject.CreateInstance<AbilityRegistrySO>();
            Container.Bind<AbilityRegistrySO>().FromInstance(abilityRegistry).AsSingle();
            if (_abilityRegistry == null)
            {
                Debug.LogWarning(
                    "[ProjectInstaller] AbilityRegistry not assigned. " +
                    "Character-select ability picker will show no abilities. " +
                    "Create an AbilityRegistry asset and assign it here.");
            }

            // MatchConfig: project-scoped rather than scene-scoped, because the Bootstrap
            // menu lobby advertises the same duration/goal the Game scene enforces. An
            // empty instance still carries the SO's field initialisers, so consumers never
            // null-check — but a silently-defaulted match config means the match runs on
            // numbers nobody authored, so the null slot is loud.
            var matchConfig = _matchConfig != null
                ? _matchConfig
                : ScriptableObject.CreateInstance<MatchConfigSO>();
            Container.Bind<MatchConfigSO>().FromInstance(matchConfig).AsSingle();
            if (_matchConfig == null)
            {
                Debug.LogWarning(
                    "[ProjectInstaller] MatchConfig not assigned — falling back to the SO's " +
                    "built-in defaults. Match duration, food target and MaxPlayers are NOT " +
                    "coming from MatchConfig.asset. Assign it here.");
            }
        }
    }
}
