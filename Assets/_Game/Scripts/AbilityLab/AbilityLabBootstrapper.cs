using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.AbilityLab
{
    /// <summary>
    /// Entry point for <c>AbilityLab.unity</c>. Starts a solo Fusion runner, lays down a
    /// flat ground plane, spawns one player chicken on a fixed mark and up to three
    /// practice dummies at authored distances, then holds the match open indefinitely so
    /// abilities can be cast and judged without a clock.
    /// </summary>
    /// <remarks>
    /// <b>The lab spawns a GameManager, and it has to.</b> The brief called for a scene with
    /// no match manager, but three shipped systems hard-gate on one and all three sit
    /// directly on the path this tool exists to exercise:
    /// <list type="bullet">
    ///   <item><c>FusionNetworkService.Update</c> blanks every latched ability press while
    ///   <c>GameManager.Instance</c> is null — no manager means no ability input at all.</item>
    ///   <item><c>ChickenController.FixedUpdateNetwork</c> returns before reading movement input.</item>
    ///   <item><c>AbilityController.FixedUpdateNetwork</c> returns before the hold state machine.</item>
    /// </list>
    /// So the manager is spawned and then held in <c>MatchState.Active</c> by re-arming its
    /// <c>MatchTimer</c> — see <see cref="HoldMatchOpen"/>. The rest of the match is absent by
    /// omission rather than by suppression: <b>no <c>PlayerBase</c> is ever spawned</b>, so
    /// banking has no destination, the food win condition has nothing to evaluate, and the
    /// economy never starts — which is exactly the quiet the lab wanted.
    ///
    /// Note that a single <c>FoodPile</c> <i>is</i> spawned (see <see cref="SpawnFoodPile"/>),
    /// because <c>PeckAbilitySO.IsUsable</c> refuses without a pile in range and Peck would
    /// otherwise be unjudgeable. The absent <c>PlayerBase</c>, not the absent pile, is what
    /// keeps the match inert — do not assume the pile is safe to remove, or that adding a
    /// base would be.
    /// </remarks>
    public sealed class AbilityLabBootstrapper : MonoBehaviour
    {
        private const string Source = "AbilityLab";

        /// <summary>
        /// Re-armed match length, in seconds. Comfortably longer than the T-60 comeback-event
        /// trigger so <c>GameManager</c> never fires its final-minute event in the lab.
        /// </summary>
        private const float HeldMatchSeconds = 3600f;

        /// <summary>Re-arm once the held timer drops below this. Any value above 60s works.</summary>
        private const float ReArmBelowSeconds = 300f;

        [Header("Launch loadout")]
        [Tooltip("Class the lab chicken spawns as. Swappable in play mode from the lab HUD.")]
        [SerializeField] private ChickenClass _class = ChickenClass.Warrior;

        [Tooltip("Passive to launch with. Null falls back to the class default passive.")]
        [SerializeField] private PassiveAbilitySO _passive;

        [Tooltip("Abilities to launch with, slots 0-3. Empty entries are backfilled from the " +
                 "class-legal pool. Unlike a real match, nothing is force-inserted ahead of a pick.")]
        [SerializeField] private AbilityBaseSO[] _slots = new AbilityBaseSO[AbilityController.SlotCount];

        [Tooltip("Offer abilities this class cannot legally equip. The point of a lab is testing " +
                 "the combinations the lobby refuses.")]
        [SerializeField] private bool _ignoreLegality;

        [Header("Dummies")]
        [Range(0, 3)]
        [Tooltip("Practice targets to spawn. Capped at 3 - the arena's shared chicken list is " +
                 "sized for four bodies including the player.")]
        [SerializeField] private int _dummyCount = 1;

        [Tooltip("Distance in metres from the player's mark to each dummy, nearest first.")]
        [SerializeField] private float[] _dummyDistances = { 5f, 10f, 16f };

        [Tooltip("Seconds between automatic dummy resets. Live-editable from the lab HUD.")]
        [Min(0.5f)]
        [SerializeField] private float _dummyResetSeconds = 5f;

        [Tooltip("Class the dummies spawn as. Fatty is the roomiest silhouette to read hits against.")]
        [SerializeField] private ChickenClass _dummyClass = ChickenClass.Fatty;

        [Tooltip("Food each dummy is restocked with on every reset, so the steal abilities have " +
                 "something to take. A dummy carrying nothing refuses Snatch / Sneaky Steal / Scrap.")]
        [Min(0f)]
        [SerializeField] private float _dummyCargoStock = 8f;

        [Header("Props")]
        [Tooltip("Spawn one food pile beside the mark. Peck is unusable without a pile in range, " +
                 "so this is what makes the forage abilities testable. It cannot restart the " +
                 "economy on its own — scoring needs a PlayerBase, and the lab has none.")]
        [SerializeField] private bool _spawnFoodPile = true;

        [Tooltip("Where the food pile sits, relative to the player's mark.")]
        [SerializeField] private Vector3 _foodPileOffset = new Vector3(-4f, 0f, 2f);

        [Header("Ground")]
        [Tooltip("Build a flat ground plane at runtime. Off only if the scene supplies its own.")]
        [SerializeField] private bool _buildGround = true;

        /// <summary>The lab chicken, once spawned. Null until the runner reports the local player.</summary>
        public ChickenController Player { get; private set; }

        /// <summary>Live practice dummies, in spawn order.</summary>
        public IReadOnlyList<AbilityLabDummy> Dummies => _dummies;

        /// <summary>Class the lab chicken was spawned as. The HUD needs it to filter the pickers.</summary>
        public ChickenClass SpawnedClass => _class;

        /// <summary>Whether class legality is currently being ignored. Toggled from the HUD.</summary>
        public bool IgnoreLegality { get => _ignoreLegality; set => _ignoreLegality = value; }

        /// <summary>Seconds between automatic dummy resets; writes through to every live dummy.</summary>
        public float DummyResetSeconds
        {
            get => _dummyResetSeconds;
            set
            {
                _dummyResetSeconds = Mathf.Max(0.5f, value);
                for (int i = 0; i < _dummies.Count; i++)
                {
                    if (_dummies[i] != null) _dummies[i].ResetIntervalSeconds = _dummyResetSeconds;
                }
            }
        }

        private readonly List<AbilityLabDummy> _dummies = new();

        private INetworkService _network;
        private ISessionSelectionService _selection;
        private PrefabRegistrySO _prefabRegistry;
        private AbilityRegistrySO _abilityRegistry;
        private ILogService _log;

        private Vector3 _playerMark;

        [Inject]
        public void Construct(INetworkService network, ISessionSelectionService selection,
            PrefabRegistrySO prefabRegistry, AbilityRegistrySO abilityRegistry, ILogService log)
        {
            _network         = network;
            _selection       = selection;
            _prefabRegistry  = prefabRegistry;
            _abilityRegistry = abilityRegistry;
            _log             = log;
        }

        private async void Start()
        {
            // async void: an unhandled exception here would vanish into Unity's
            // synchronization context with nothing CluckWars-tagged to show for it.
            try
            {
                if (_network == null)
                {
                    _log?.Error(Source, "INetworkService did not resolve. AbilityLab.unity needs a " +
                        "SceneContext running GameInstaller - that is the only installer that binds it.");
                    return;
                }

                if (ResolveChickenPrefab() == null)
                {
                    _log?.Error(Source, "PrefabRegistry.Chicken is not assigned - nothing to spawn. " +
                        "Assign it on the PrefabRegistry asset referenced by ProjectInstaller.");
                    return;
                }

                // The lab drives dummies and the held match timer from Unity's own update
                // loops, which is only sound on a single peer. Pin the mode rather than
                // inherit whatever the Bootstrap menu last left behind.
                if (_selection != null) _selection.Mode = SessionMode.Solo;

                if (_buildGround) BuildGround();

                _playerMark = Vector3.zero;

                _network.OnPlayerJoined += HandlePlayerJoined;
                await _network.StartSoloAsync();

                SpawnGameManager();
                SpawnDummies();
                if (_spawnFoodPile) SpawnFoodPile();

                _log?.Info(Source, $"Ability Lab ready: class={_class}, dummies={_dummyCount}, " +
                    $"ignoreLegality={_ignoreLegality}.");
            }
            catch (System.Exception e)
            {
                _log?.Error(Source, $"Ability Lab start failed: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_network != null) _network.OnPlayerJoined -= HandlePlayerJoined;
        }

        private void Update() => HoldMatchOpen();

        /// <summary>
        /// Keeps <c>GameManager</c> in <c>MatchState.Active</c> indefinitely by pushing its
        /// match timer back before it can expire. Without this the lab would run the shipped
        /// 45-second match and then hard-cut to the win screen mid-experiment.
        /// </summary>
        /// <remarks>
        /// Writing a <c>[Networked]</c> property outside <c>FixedUpdateNetwork</c> is normally
        /// wrong. It is sound here for the same reason the dummy patrol is: <c>GameMode.Single</c>
        /// has one peer holding permanent state authority, with no prediction and no
        /// resimulation, so there is no rollback for the write to be lost to.
        /// </remarks>
        private void HoldMatchOpen()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Object == null || !gm.Object.IsValid) return;
            if (!gm.HasStateAuthority) return;
            if (gm.TimeRemaining > ReArmBelowSeconds) return;

            gm.MatchTimer = TickTimer.CreateFromSeconds(gm.Runner, HeldMatchSeconds);
        }

        private NetworkObject ResolveChickenPrefab() =>
            _prefabRegistry != null ? _prefabRegistry.Chicken : null;

        private void SpawnGameManager()
        {
            var prefab = _prefabRegistry != null ? _prefabRegistry.GameManager : null;
            if (prefab == null)
            {
                _log?.Error(Source, "PrefabRegistry.GameManager is not assigned. Ability casting and " +
                    "movement both hard-gate on a live GameManager, so the lab can neither move nor " +
                    "cast without it.");
                return;
            }

            var runner = _network.Runner;
            if (runner == null) return;

            runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
        }

        private void HandlePlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (player != runner.LocalPlayer) return;
            if (Player != null) return;
            SpawnPlayer(runner, player);
        }

        /// <summary>
        /// Hot-swaps the live chicken's passive and four slots, then clears every cooldown so
        /// the new kit is immediately castable. No respawn: the camera, the dummies' relative
        /// positions and the current charge state all survive, which is what makes an A/B feel
        /// comparison possible at all.
        /// </summary>
        /// <remarks>
        /// Sound because <c>AbilityController</c>'s slot fields are plain <c>[SerializeField]</c>s
        /// rather than <c>[Networked]</c> state, so this is a purely local write with nothing to
        /// replicate — and because <c>PassiveAbilitySO.OnActivate</c> is an empty no-op: passives
        /// express themselves through query hooks consulted live at each call site, so swapping
        /// one needs no unapply/reapply pass.
        /// </remarks>
        public void ApplyLoadout(PassiveAbilitySO passive, IReadOnlyList<AbilityBaseSO> picks)
        {
            _passive = passive;
            if (picks != null)
            {
                for (int i = 0; i < _slots.Length && i < picks.Count; i++) _slots[i] = picks[i];
            }

            var abilities = Player != null ? Player.GetComponent<AbilityController>() : null;
            if (abilities == null) return;

            var resolved = new AbilityBaseSO[AbilityController.SlotCount];
            AbilityLabLoadout.ResolveSlots(_abilityRegistry, _class, _ignoreLegality, _slots, resolved);
            var resolvedPassive = AbilityLabLoadout.ResolvePassive(_abilityRegistry, _class, _ignoreLegality, passive);

            abilities.SetSlots(resolvedPassive, resolved[0], resolved[1], resolved[2], resolved[3]);
            for (int i = 0; i < AbilityController.SlotCount; i++) abilities.TriggerCooldown(i, 0f);
        }

        /// <summary>
        /// Despawns and re-spawns the lab chicken as <paramref name="cls"/>, carrying the
        /// current loadout selection across.
        /// </summary>
        /// <remarks>
        /// A class change cannot be hot-swapped the way a loadout can. <c>ChickenController</c>
        /// resolves its <c>ChickenStatsSO</c> exactly once, in <c>Spawned</c>, and nothing
        /// watches the <c>[Networked] Class</c> property for changes — so writing it live would
        /// leave the chicken wearing one class's silhouette and another class's speed, mass and
        /// cargo capacity. Half a swap is worse than none, so the body is rebuilt.
        /// </remarks>
        public void RespawnPlayerAs(ChickenClass cls)
        {
            var runner = _network != null ? _network.Runner : null;
            if (runner == null)
            {
                _log?.Error(Source, "RespawnPlayerAs called with no live NetworkRunner.");
                return;
            }

            _class = cls;

            if (Player != null && Player.Object != null && Player.Object.IsValid)
            {
                runner.Despawn(Player.Object);
            }
            Player = null;

            SpawnPlayer(runner, runner.LocalPlayer);
        }

        private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
        {
            var resolved = new AbilityBaseSO[AbilityController.SlotCount];
            bool full = AbilityLabLoadout.ResolveSlots(_abilityRegistry, _class, _ignoreLegality, _slots, resolved);
            if (!full)
            {
                _log?.Warn(Source, $"Only resolved a partial loadout for {_class} - the registry holds " +
                    $"fewer than {AbilityController.SlotCount} abilities legal for it. Unfilled slots " +
                    "keep the chicken prefab's own defaults.");
            }

            var passive = AbilityLabLoadout.ResolvePassive(_abilityRegistry, _class, _ignoreLegality, _passive);

            var spawned = runner.Spawn(
                ResolveChickenPrefab(),
                _playerMark,
                Quaternion.identity,
                player,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var controller = networkObject.GetComponent<ChickenController>();
                    if (controller != null)
                    {
                        controller.Class = _class;
                        controller.HomeCornerIndex = 0;
                    }
                    networkObject.GetComponent<AbilityController>()
                        ?.SetSlots(passive, resolved[0], resolved[1], resolved[2], resolved[3]);
                });

            Player = spawned != null ? spawned.GetComponent<ChickenController>() : null;
            if (Player == null)
            {
                _log?.Error(Source, "Spawned the lab chicken but found no ChickenController on it - " +
                    "PrefabRegistry.Chicken does not point at a chicken prefab.");
            }
        }

        private void SpawnDummies()
        {
            var runner = _network.Runner;
            var prefab = ResolveChickenPrefab();
            if (runner == null || prefab == null || _dummyCount <= 0) return;

            for (int i = 0; i < _dummyCount; i++)
            {
                float distance = _dummyDistances != null && i < _dummyDistances.Length
                    ? _dummyDistances[i]
                    : 5f * (i + 1);

                // A zero distance would stack the dummy inside the player and hand
                // Quaternion.LookRotation a zero vector, which Unity answers with an error
                // and an identity rotation. Clamp to something castable instead.
                if (distance < 1f)
                {
                    _log?.Warn(Source, $"Dummy {i} distance {distance:0.0}m is too close to be useful; " +
                        "using 1m. Author a larger value in _dummyDistances.");
                    distance = 1f;
                }

                // Fan the dummies out so one cone or capsule cast at the default facing does
                // not catch all three at once and make per-target feedback unreadable.
                float angle = (i - (_dummyCount - 1) * 0.5f) * 20f;
                var offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * distance);
                var home = _playerMark + offset + new Vector3(0f, 0.05f, 0f);

                // IsBot stays FALSE on purpose. BotController is baked onto Chicken.prefab and
                // cannot be removed from a spawned instance, but it self-gates on IsBot, so
                // leaving the flag clear is what actually keeps the AI off. Setting it would
                // hand the dummy the full hunt/engage FSM and let it fight back.
                var spawned = runner.Spawn(prefab, home, Quaternion.identity, inputAuthority: PlayerRef.None,
                    onBeforeSpawned: (_, networkObject) =>
                    {
                        var controller = networkObject.GetComponent<ChickenController>();
                        if (controller != null)
                        {
                            controller.Class = _dummyClass;
                            controller.IsBot = false;
                            controller.HomeCornerIndex = i + 1;
                        }
                    });

                if (spawned == null) continue;

                var dummyController = spawned.GetComponent<ChickenController>();
                var dummy = spawned.gameObject.AddComponent<AbilityLabDummy>();
                dummy.ResetIntervalSeconds = _dummyResetSeconds;
                dummy.CargoStock = _dummyCargoStock;
                dummy.Bind(dummyController, home, Quaternion.LookRotation(-offset.normalized, Vector3.up));
                _dummies.Add(dummy);
            }

            if (_log != null && _log.IsEnabled(LogLevel.Debug))
            {
                _log.Debug(Source, $"Spawned {_dummies.Count} dummy chicken(s) as {_dummyClass}.");
            }
        }

        /// <summary>
        /// Drops a single food pile beside the mark so the forage abilities are castable.
        /// </summary>
        /// <remarks>
        /// <c>PeckAbilitySO.IsUsable</c> requires a pile in range, so without one Peck refuses
        /// every press with <c>NoTarget</c> and cannot be judged at all. One pile does not
        /// bring the economy back: food can only be scored into a <c>PlayerBase</c>, and the
        /// lab spawns none, so there is still nothing to win.
        /// </remarks>
        private void SpawnFoodPile()
        {
            var prefab = _prefabRegistry != null ? _prefabRegistry.FoodPile : null;
            if (prefab == null)
            {
                _log?.Warn(Source, "PrefabRegistry.FoodPile is not assigned — Peck and the other " +
                    "forage abilities will refuse with NoTarget. Everything else still works.");
                return;
            }

            var runner = _network.Runner;
            if (runner == null) return;

            runner.Spawn(prefab, _playerMark + _foodPileOffset, Quaternion.identity);
        }

        /// <summary>
        /// Lays a flat plane sized to <see cref="MapGenerator.ArenaHalfSize"/>. The lab
        /// deliberately does not run <c>MapGenerator</c>: the baked path additively loads the
        /// whole <c>Map</c> scene with its props and bases, which is precisely the noise this
        /// tool exists to remove. Sizing off the same static the jump resolver clamps against
        /// keeps traversal abilities landing on ground rather than in the void.
        /// </summary>
        private void BuildGround()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                _log?.Error(Source, "URP Lit shader not found - the lab ground would render with the " +
                    "pipeline error material. Is the project still on URP? Skipping ground; the " +
                    "chicken will fall.");
                return;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "AbilityLabGround";

            // Unity's plane primitive is 10m across, hence the 0.2 factor against half-size.
            // Overshoot the arena by 60% purely so the camera never frames the plane's edge —
            // gameplay is unaffected, because the jump resolver still clamps to ArenaHalfSize.
            float half = MapGenerator.ArenaHalfSize * 1.6f;
            ground.transform.localScale = new Vector3(half * 0.2f, 1f, half * 0.2f);
            ground.transform.position = Vector3.zero;

            ground.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(shader) { color = new Color(0.32f, 0.36f, 0.30f, 1f) };
        }
    }
}
