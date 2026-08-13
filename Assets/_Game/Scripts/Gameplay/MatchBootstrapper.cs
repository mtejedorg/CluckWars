using System.Linq;
using CluckWars.Abilities;
using CluckWars.Logging;
using CluckWars.Networking;
using CluckWars.Services;
using Fusion;
using UnityEngine;
using Zenject;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Entry point for a match. Lives in the Game scene as a single GameObject with a
    /// serialized chicken prefab. On Start it reads the user's mode + class from
    /// <see cref="ISessionSelectionService"/>, kicks off the matching Fusion session
    /// (Solo / Host / Join), and spawns the local player's chicken when
    /// <c>OnPlayerJoined</c> fires.
    /// </summary>
    /// <remarks>
    /// In Shared Mode, every joined player's <c>OnPlayerJoined</c> fires on every
    /// peer; we filter on <c>player == runner.LocalPlayer</c> so each client only
    /// spawns its own chicken. Remote chickens replicate automatically.
    ///
    /// Spawn positions come from <see cref="MapGenerator.SpawnPoints"/> when the
    /// scene has a <c>MapGenerator</c>; <see cref="_legacySpawnPoints"/> is a
    /// fallback for scenes without one. Players are pinned to corners by
    /// <c>PlayerId % count</c> so peers agree on positions without coordination.
    /// </remarks>
    public sealed class MatchBootstrapper : MonoBehaviour
    {
        private const string Source = "MatchBootstrap";

        [Tooltip("Legacy per-component override. If null, PrefabRegistry.Chicken is used.")]
        [SerializeField] private NetworkObject _chickenPrefab;

        [Tooltip("Optional fallback spawn points. Ignored when a MapGenerator is present in the scene — that becomes the source of truth.")]
        [SerializeField] private Transform[] _legacySpawnPoints;

        [Tooltip("Legacy per-component override. If null, PrefabRegistry.GameManager is used.")]
        [SerializeField] private NetworkObject _gameManagerPrefab;

        [Tooltip("Number of AI bots to spawn in Solo mode (0–3). Bots fill corners 1–3 with classes Speedy / Fatty / Assassin.")]
        [Range(0, 3)]
        [SerializeField] private int _soloBotsToSpawn = 3;

        [Header("Bot Loadout")]
        [Tooltip("Curated loadout presets for bots. Each bot rolls a random preset whose " +
            "AllowedClasses permits its class (BOT-3). Author rows in the inspector — see " +
            "docs/ROADMAP.md Phase R-Bot BOT-3 for the recommended set " +
            "(Bruiser / Skirmisher / Tank / Trickster / Thief).")]
        [SerializeField] private BotLoadoutPreset[] _botLoadouts;

        private MapGenerator _mapGenerator;

        // Corner permutation — a shuffled [0,1,2,3] assigned once per session so
        // every player/bot spawns at a different starting edge each game.
        // Online: seeded from the session name (deterministic → all peers agree).
        // Solo:   seeded randomly (single peer, no coordination needed).
        private int[] _cornerPermutation;

        // Deduplication guards — prevent double-spawning if Fusion somehow fires
        // OnPlayerJoined more than once or if TrySpawnBots is called again.
        private readonly System.Collections.Generic.HashSet<PlayerRef> _spawnedPlayers = new();
        private bool _botsSpawned;

        private INetworkService _networkService;
        private ISessionSelectionService _selection;
        private PrefabRegistrySO _prefabRegistry;
        private ChickenClassRegistrySO _classRegistry;
        private AbilityRegistrySO _abilityRegistry;
        private ILogService _log;

        [Inject]
        public void Construct(INetworkService networkService, ISessionSelectionService selection, PrefabRegistrySO prefabRegistry, ChickenClassRegistrySO classRegistry, AbilityRegistrySO abilityRegistry, ILogService log)
        {
            _networkService = networkService;
            _selection = selection;
            _prefabRegistry = prefabRegistry;
            _classRegistry = classRegistry;
            _abilityRegistry = abilityRegistry;
            _log = log;
        }


        private NetworkObject ResolveChickenPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.Chicken != null) return _prefabRegistry.Chicken;
            return _chickenPrefab;
        }

        private NetworkObject ResolveGameManagerPrefab()
        {
            if (_prefabRegistry != null && _prefabRegistry.GameManager != null) return _prefabRegistry.GameManager;
            return _gameManagerPrefab;
        }

        private async void Start()
        {
            // async void: an unhandled exception here would vanish into Unity's
            // synchronization context without any CluckWars-tagged log — guard it.
            try
            {
                if (ResolveChickenPrefab() == null)
                {
                    _log?.Error(Source, "Chicken prefab not assigned (neither PrefabRegistry.Chicken nor legacy slot).");
                    return;
                }

                var mode = _selection != null ? _selection.Mode : SessionMode.Solo;
                var sessionName = _selection != null ? _selection.SessionName : "cluck-lan";
                var chosenClass = _selection != null ? _selection.SelectedClass : ChickenClass.Warrior;

                _log?.Info(Source, $"Starting session: mode={mode}, session='{sessionName}', class={chosenClass}.");
                InitCornerPermutation(sessionName, isSolo: mode == SessionMode.Solo);
                _networkService.OnPlayerJoined += HandlePlayerJoined;

                switch (mode)
                {
                    case SessionMode.Host:
                        await _networkService.StartHostAsync(sessionName);
                        break;
                    case SessionMode.Join:
                        await _networkService.JoinSessionAsync(sessionName);
                        break;
                    case SessionMode.Solo:
                    default:
                        await _networkService.StartSoloAsync();
                        break;
                }

                _log?.Debug(Source, $"Network start awaited (mode={mode}); runner is up.");

                TrySpawnGameManager();

                if (mode == SessionMode.Solo)
                    TrySpawnBots();
            }
            catch (System.Exception e)
            {
                _log?.Error(Source, $"Session start failed: {e}");
            }
        }

        private void TrySpawnGameManager()
        {
            var gmPrefab = ResolveGameManagerPrefab();
            if (gmPrefab == null)
            {
                _log?.Warn(Source, "GameManager prefab not assigned — match has no timer / win condition.");
                return;
            }

            var runner = _networkService.Runner;
            if (runner == null) return;

            // Only the master client (or solo player) spawns the manager. In Shared
            // Mode, every client's local-player check passes for itself; the master
            // client is the one whose LocalPlayer equals runner.LocalPlayer AND is
            // first to spawn. We additionally guard so we don't end up with two
            // managers if joins race.
            if (runner.GameMode != GameMode.Single && !runner.IsSharedModeMasterClient)
            {
                _log?.Debug(Source, "Not master client; GameManager will replicate from the master.");
                return;
            }
            if (FindFirstObjectByType<GameManager>() != null)
            {
                _log?.Debug(Source, "GameManager already in scene; skipping spawn.");
                return;
            }

            _log?.Info(Source, "Master client spawning GameManager.");
            runner.Spawn(gmPrefab, Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Spawns AI bot chickens to fill the non-player corners in solo mode.
        /// Bots use <see cref="PlayerRef.None"/> as input authority so Fusion never
        /// feeds them player input; <see cref="BotController"/> drives them instead.
        /// </summary>
        private void TrySpawnBots()
        {
            if (_botsSpawned) return;
            _botsSpawned = true;
            if (_soloBotsToSpawn <= 0) return;

            var runner = _networkService.Runner;
            if (runner == null) return;

            var chickenPrefab = ResolveChickenPrefab();
            if (chickenPrefab == null) return;

            // Distribute variety: Speedy → corner 1, Fatty → corner 2, Assassin → corner 3.
            var botClasses = new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };

            var mapGen   = FindFirstObjectByType<MapGenerator>();
            var spawnPts = mapGen != null ? mapGen.SpawnPoints : null;

            int count = Mathf.Min(_soloBotsToSpawn, 3);
            for (int i = 0; i < count; i++)
            {
                // Bots take permutation slots 1, 2, 3; slot 0 belongs to the solo player.
                int     cornerIdx = ShuffledCorner((i + 1) % 4);
                var     botClass  = botClasses[i % botClasses.Length];
                Vector3 pos       = Vector3.zero;

                if (spawnPts != null && cornerIdx < spawnPts.Count)
                    pos = spawnPts[cornerIdx];

                // Safety jitter — mirrors HandlePlayerJoined so bots never stack.
                pos += new Vector3(
                    Mathf.Sin((i + 4) * 1.7f) * 0.25f,
                    0.05f,
                    Mathf.Cos((i + 4) * 1.7f) * 0.25f);

                // Roll a loadout preset OUTSIDE the lambda so the random pick is stable
                // for this spawn. botClass is loop-local, so capturing it directly is safe.
                bool haveLoadout = TryPickBotLoadout(botClass, out var loadout);
                if (!haveLoadout)
                    _log?.Warn(Source, $"No bot loadout preset available for {botClass} — bot " +
                        "will run ability-less. Author rows on MatchBootstrapper._botLoadouts.");

                runner.Spawn(
                    chickenPrefab,
                    pos,
                    Quaternion.identity,
                    inputAuthority: PlayerRef.None,
                    onBeforeSpawned: (_, networkObject) =>
                    {
                        var ctrl = networkObject.GetComponent<ChickenController>();
                        if (ctrl != null)
                        {
                            ctrl.Class = botClass;
                            ctrl.IsBot = true;
                            ctrl.HomeCornerIndex = cornerIdx;
                        }



                        // Bots go through the SAME sanitiser as players. Preset filtering alone
                        // was not enough: a preset whose class flavor allowed Fatty still handed
                        // it Speedy-only Feather Trap, and slot reuse produced the same ability
                        // twice on Speedy (both observed live 2026-07-22). Routing both paths
                        // through one chokepoint is what actually guarantees the invariant.
                        var abilities = networkObject.GetComponent<AbilityController>();
                        if (abilities != null)
                        {
                            ResolveLegalLoadout(botClass,
                                haveLoadout ? loadout.Passive : null,
                                haveLoadout ? loadout.Slot0 : null,
                                haveLoadout ? loadout.Slot1 : null,
                                haveLoadout ? loadout.Slot2 : null,
                                haveLoadout ? loadout.Slot3 : null,
                                out var botPassive, out var b0, out var b1, out var b2, out var b3);
                            abilities.SetSlots(botPassive, b0, b1, b2, b3);
                        }
                    });

                _log?.Info(Source, $"Spawning bot {i}: class={botClass}, corner={cornerIdx}, " +
                    $"loadout={(haveLoadout ? loadout.DisplayLabel : "(none)")}, pos={pos}.");
            }
        }

        /// <summary>
        /// Picks a random loadout preset the given class is allowed to roll (ROADMAP
        /// Phase R-Bot, BOT-3). Solo-only — bots exist only in single-player, so a plain
        /// <see cref="UnityEngine.Random"/> draw is fine (no cross-peer seeding needed,
        /// unlike the corner permutation). Skips presets with no abilities assigned.
        /// Returns false when the pool is empty or nothing is eligible for the class.
        /// </summary>
        private bool TryPickBotLoadout(ChickenClass cls, out BotLoadoutPreset preset)
        {
            preset = default;
            if (_botLoadouts == null || _botLoadouts.Length == 0) return false;

            // Gather eligible presets, then pick one uniformly.
            // A preset qualifies only if its own class flavor permits the class AND every
            // ability it carries is legal for that class under the ADR 0003 Decision 3 mask.
            // Without the second test a preset with an empty (= any class) flavor happily
            // handed Warrior-only Flying Peck to Speedy/Fatty/Assassin bots — observed live
            // 2026-07-22, and it silently voided the whole point of class-gated pools.
            var eligible = new System.Collections.Generic.List<BotLoadoutPreset>(_botLoadouts.Length);
            for (int i = 0; i < _botLoadouts.Length; i++)
            {
                var p = _botLoadouts[i];
                if (!p.HasAnyAbility || !p.AllowsClass(cls)) continue;
                if (!PresetIsClassLegal(p, cls)) continue;
                eligible.Add(p);
            }

            if (eligible.Count == 0)
            {
                // No authored preset is legal for this class — compose one from the pools
                // rather than spawning a bot with an off-class or empty loadout.
                if (_abilityRegistry != null)
                {
                    _abilityRegistry.ComposeDefaultLoadout(cls, out var common, out var c0, out var c1);
                    if (common != null || c0 != null)
                    {
                        preset = new BotLoadoutPreset
                        {
                            Name    = $"Composed({cls})",
                            Slot0   = common,
                            Slot1   = c0,
                            Slot2   = c1,
                            Passive = _abilityRegistry.GetDefaultPassiveForClass(cls),
                        };
                        _log?.Debug(Source, $"No legal bot preset for {cls} — composed 1 Common + 2 Character from the registry.");
                        return true;
                    }
                }
                return false;
            }

            preset = eligible[UnityEngine.Random.Range(0, eligible.Count)];
            return true;
        }

        /// <summary>
        /// Sanitises a chosen loadout into a legal one for <paramref name="cls"/>:
        /// <see cref="AbilityController.SlotCount"/> distinct, class-legal active abilities
        /// plus a class-legal passive, with Peck forced present (or forced absent) according
        /// to whether the class can forage at all.
        /// </summary>
        /// <remarks>
        /// <b>The Peck invariant is the important part.</b> Food only enters a chicken through
        /// Peck now, so a Warrior/Speedy/Fatty that spawns without it cannot score for the
        /// whole match — and nothing would log an error, because an unequipped ability is a
        /// perfectly ordinary state. Conversely the Assassin must never receive it: not being
        /// able to farm is the mechanical basis of the class being a predator.
        /// <para>
        /// Both directions are enforced <b>here</b>, at the single spawn chokepoint both bots
        /// and players pass through, rather than in the picker UI. That is the lesson of the
        /// 2026-07-22 live match that spawned a Warrior holding two Common abilities and an
        /// inert passive because nothing validated the selection on the way in.
        /// </para>
        /// <para>
        /// ⚠️ Legality is now <c>IsAllowedFor</c> for EVERY ability, Common included. The old
        /// rule treated any Common ability as legal for everyone and only consulted
        /// <c>AllowedClasses</c> for Character ones. That was harmless while every Common was
        /// flagged All, and became a bug the moment Peck shipped as a Common ability
        /// restricted to three classes: the backfill would have handed it to the Assassin.
        /// </para>
        /// </remarks>
        private void ResolveLegalLoadout(ChickenClass cls,
            PassiveAbilitySO chosenPassive, AbilityBaseSO a0, AbilityBaseSO a1, AbilityBaseSO a2, AbilityBaseSO a3,
            out PassiveAbilitySO passive, out AbilityBaseSO slot0, out AbilityBaseSO slot1,
            out AbilityBaseSO slot2, out AbilityBaseSO slot3)
        {
            passive = chosenPassive; slot0 = a0; slot1 = a1; slot2 = a2; slot3 = a3;
            if (_abilityRegistry == null)
            {
                _log?.Warn(Source, $"ResolveLegalLoadout: no AbilityRegistrySO bound — passing {cls}'s " +
                    "selection through unsanitised. Assign AbilityRegistrySO in ProjectInstaller.");
                return;
            }

            // Passive must exist and be legal for this class; else fall back to the signature.
            if (passive == null || !AbilityRegistrySO.IsAllowedFor(passive, cls))
                passive = _abilityRegistry.GetDefaultPassiveForClass(cls);

            int activeSlots = AbilityController.SlotCount;

            var peck = _abilityRegistry.ActiveAbilities.FirstOrDefault(a => a is Abilities.PeckAbilitySO);
            bool canForage = peck != null && AbilityRegistrySO.IsAllowedFor(peck, cls);

            // Sanitise: genuine, class-legal, distinct picks, in the order supplied. Order is
            // preserved because Peck's button position is the player's to choose.
            var picked = new System.Collections.Generic.List<AbilityBaseSO>(activeSlots);
            foreach (var a in new[] { a0, a1, a2, a3 })
            {
                if (picked.Count >= activeSlots) break;
                if (a == null || picked.Contains(a)) continue;
                if (!AbilityRegistrySO.IsAllowedFor(a, cls)) continue;
                picked.Add(a);
            }

            if (canForage && !picked.Contains(peck))
            {
                // Forced in at slot 0. The picker normally places it wherever the player
                // wants and this never fires; it is the safety net for bot presets and any
                // malformed selection, so a predictable position beats a clever one.
                if (picked.Count >= activeSlots) picked.RemoveAt(picked.Count - 1);
                picked.Insert(0, peck);
                _log?.Debug(Source, $"ResolveLegalLoadout: {cls} had no Peck equipped — forced into slot 0.");
            }
            else if (!canForage && peck != null && picked.Remove(peck))
            {
                // Belt and braces: IsAllowedFor above already rejects it, so reaching here
                // means the flags and this rule disagree. Worth a line in the log.
                _log?.Warn(Source, $"ResolveLegalLoadout: stripped Peck from {cls}, which cannot forage.");
            }

            // Backfill ONLY what sanitising left missing, from everything legal for this class.
            if (picked.Count < activeSlots)
            {
                var fillPool = _abilityRegistry.ActiveAbilities
                    .Where(a => a != null && AbilityRegistrySO.IsAllowedFor(a, cls));
                foreach (var fill in fillPool)
                {
                    if (picked.Count >= activeSlots) break;
                    if (picked.Contains(fill)) continue;
                    picked.Add(fill);
                }
            }

            if (picked.Count < activeSlots)
            {
                _log?.Warn(Source, $"ResolveLegalLoadout: only resolved {picked.Count}/{activeSlots} legal " +
                    $"abilities for {cls} after sanitising + backfill — AbilityRegistrySO has too few entries " +
                    $"legal for {cls}. This bot/player will spawn under-equipped.");
            }

            slot0 = picked.Count > 0 ? picked[0] : null;
            slot1 = picked.Count > 1 ? picked[1] : null;
            slot2 = picked.Count > 2 ? picked[2] : null;
            slot3 = picked.Count > 3 ? picked[3] : null;
        }

        /// <summary>
        /// Every ability the preset carries must be equippable by <paramref name="cls"/>
        /// (ADR 0003 Decision 3). Empty slots are fine; an off-class ability disqualifies the
        /// whole preset. Slot2 is only equipped on Combo Assassins, but it is validated here
        /// too so a preset can never leak an off-class ability into that slot either.
        /// </summary>
        private static bool PresetIsClassLegal(in BotLoadoutPreset p, ChickenClass cls)
        {
            return SlotIsLegal(p.Slot0, cls) && SlotIsLegal(p.Slot1, cls)
                && SlotIsLegal(p.Slot2, cls) && SlotIsLegal(p.Slot3, cls);

            static bool SlotIsLegal(AbilityBaseSO a, ChickenClass c) =>
                a == null || AbilityRegistrySO.IsAllowedFor(a, c);
        }

        private void OnDestroy()
        {
            if (_networkService != null)
                _networkService.OnPlayerJoined -= HandlePlayerJoined;
        }

        /// <summary>
        /// One bot loadout option. Authored as inspector rows on
        /// <see cref="MatchBootstrapper"/>; a bot rolls a random preset whose
        /// <see cref="AllowedClasses"/> permits its class. <see cref="Slot2"/> is only
        /// equipped on Assassin bots (the Combo 3rd slot).
        /// </summary>
        /// <remarks>
        /// <see cref="AllowedClasses"/> is <b>bot-AI flavor only</b> — it shapes how bots
        /// feel and must NOT be reused to gate the player ability-selection UI. Per GDD
        /// §7.1, players face no class-based ability restrictions.
        /// </remarks>
        [System.Serializable]
        public struct BotLoadoutPreset
        {
            [Tooltip("Display name — inspector readability + logs only.")]
            public string Name;

            [Tooltip("Class passive (optional; falls back to class signature passive if null).")]
            public PassiveAbilitySO Passive;

            [Tooltip("Slot-0 ability (typically Offense).")]
            public AbilityBaseSO Slot0;

            [Tooltip("Slot-1 ability (typically Defense or Escape).")]
            public AbilityBaseSO Slot1;

            [Tooltip("Slot-2 ability. Optional.")]
            public AbilityBaseSO Slot2;

            [Tooltip("Slot-3 ability. Optional. Note ResolveLegalLoadout forces Peck into the "
                + "loadout of any class that can forage, so a preset that leaves this empty "
                + "still ends up with four abilities.")]
            public AbilityBaseSO Slot3;

            [Tooltip("Classes allowed to roll this preset. Empty = all classes. Bot-AI flavor " +
                "only — does NOT affect player ability selection (GDD §7.1).")]
            public ChickenClass[] AllowedClasses;

            /// <summary>True if <paramref name="cls"/> may roll this preset (empty list = all).</summary>
            public bool AllowsClass(ChickenClass cls)
            {
                if (AllowedClasses == null || AllowedClasses.Length == 0) return true;
                for (int i = 0; i < AllowedClasses.Length; i++)
                    if (AllowedClasses[i] == cls) return true;
                return false;
            }

            public bool HasAnyAbility => Slot0 != null || Slot1 != null || Slot2 != null;

            public string DisplayLabel => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;
        }

        private void HandlePlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            // Only the local player spawns their own chicken; remote chickens
            // are spawned by their own peer and replicated here automatically.
            // (The old `GameMode.Single` shortcut was too broad — it treated
            // every PlayerRef as local, which triggered duplicate spawns when
            // Fusion fired additional OnPlayerJoined events.)
            _log?.Debug(Source, $"OnPlayerJoined player={player} local={runner.LocalPlayer} mode={runner.GameMode}.");
            if (player != runner.LocalPlayer) return;
            if (!_spawnedPlayers.Add(player))
            {
                _log?.Warn(Source, $"HandlePlayerJoined: already spawned {player} — ignoring duplicate.");
                return;
            }

            int homeCorner = PickSpawnCorner(runner, player);
            if (homeCorner < 0) return;
            var pos = PickSpawnPosition(runner, player);

            // Defensive: nudge spawn slightly up + per-player horizontally so that
            // even if PickSpawnPosition collapses to the same XZ (degenerate
            // SpawnPoints, all-zero fallback, …), two chickens don't spawn
            // perfectly overlapping. CharacterController vs CharacterController
            // collision otherwise leaves the losing chicken drifting in one
            // direction indefinitely — exactly the "non-hosts can't steer" bug.
            var safetyJitter = new Vector3(
                Mathf.Sin(player.PlayerId * 1.7f) * 0.25f,
                0.05f * (1 + Mathf.Abs(player.PlayerId)),  // tiny per-player vertical stagger
                Mathf.Cos(player.PlayerId * 1.7f) * 0.25f);
            pos += safetyJitter;

            var chosenClass = _selection != null ? _selection.SelectedClass : ChickenClass.Warrior;
            _log?.Info(Source,
                $"Spawning chicken: class={chosenClass}, " +
                $"player={player} (PlayerId={player.PlayerId}), " +
                $"pos={pos}, mapGen={(_mapGenerator != null ? "present" : "missing")}, " +
                $"spawnPoints={(_mapGenerator != null && _mapGenerator.SpawnPoints != null ? _mapGenerator.SpawnPoints.Count : 0)}.");

            runner.Spawn(
                ResolveChickenPrefab(),
                pos,
                Quaternion.identity,
                player,
                onBeforeSpawned: (_, networkObject) =>
                {
                    // Set the Networked Class before Spawned() runs so every peer sees the
                    // chosen class on first read and can resolve stats / tint correctly.
                    var controller = networkObject.GetComponent<ChickenController>();
                    if (controller != null)
                    {
                        controller.Class = chosenClass;
                        controller.HomeCornerIndex = homeCorner;
                    }



                    // Apply player-chosen abilities, sanitised against the class pools so an
                    // off-class or malformed selection can never reach the world: up to N
                    // freely-chosen legal abilities (Common optional, per the 2026-07-27
                    // design directive superseding GDD §7.1-7.2) plus a mandatory class passive.
                    if (_selection != null)
                    {
                        var abilityCtrl = networkObject.GetComponent<AbilityController>();
                        ResolveLegalLoadout(chosenClass,
                            _selection.Passive, _selection.Ability0, _selection.Ability1,
                            _selection.Ability2, _selection.Ability3,
                            out var passive, out var s0, out var s1, out var s2, out var s3);
                        abilityCtrl?.SetSlots(passive, s0, s1, s2, s3);
                    }
                });
        }

        /// <summary>
        /// The shuffled corner index (0..3) for a player — single source of truth
        /// shared by spawn-position selection and <c>HomeCornerIndex</c> stamping.
        /// Single mode: the human always takes permutation slot 0 (bots take 1–3).
        /// Shared mode: prefers the player's sorted-roster slot, then scans forward
        /// to the first corner not already stamped on a spawned chicken — roster
        /// position alone collides after a leave + rejoin (slots shift, stamped
        /// <c>HomeCornerIndex</c> values don't). Returns -1 when all corners are
        /// taken (5th joiner) so the caller refuses the spawn.
        /// </summary>
        private int PickSpawnCorner(NetworkRunner runner, PlayerRef player)
        {
            if (runner.GameMode == GameMode.Single) return ShuffledCorner(0);

            // Preferred slot: index among the PlayerId-sorted roster. In a fresh
            // session this reproduces plain join order (0,1,2,3).
            var activePlayers = new System.Collections.Generic.List<PlayerRef>(runner.ActivePlayers);
            activePlayers.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            int preferred = Mathf.Max(0, activePlayers.IndexOf(player));

            // Corner truth is the replicated HomeCornerIndex stamps on live
            // chickens, not the roster. Two players joining in the same instant
            // can still race to one corner (each hasn't seen the other's chicken
            // yet) — rare, and GameManager's invariant Warn surfaces it.
            for (int offset = 0; offset < 4; offset++)
            {
                int corner = ShuffledCorner((preferred + offset) % 4);
                if (!IsCornerOccupied(corner)) return corner;
            }

            _log?.Error(Source, $"Spawn refused for {player}: all 4 corners are occupied.");
            return -1;
        }

        /// <summary>True when any live, non-decoy chicken has <paramref name="corner"/> stamped.</summary>
        private static bool IsCornerOccupied(int corner)
        {
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (c.IsDecoy) continue;
                if (c.HomeCornerIndex == corner) return true;
            }
            return false;
        }

        private Vector3 PickSpawnPosition(NetworkRunner runner, PlayerRef player)
        {
            // Prefer MapGenerator's computed corners. Lookup is cached; null check
            // re-resolves if the MapGenerator was added after Start ran.
            if (_mapGenerator == null) _mapGenerator = FindFirstObjectByType<MapGenerator>();
            if (_mapGenerator != null && _mapGenerator.SpawnPoints != null && _mapGenerator.SpawnPoints.Count > 0)
            {
                var points = _mapGenerator.SpawnPoints;
                int idx = PickSpawnCorner(runner, player);
                if (idx < 0) return Vector3.zero;
                return points[idx % points.Count];
            }

            // Legacy fallback for scenes without a MapGenerator.
            if (_legacySpawnPoints == null || _legacySpawnPoints.Length == 0)
                return Vector3.zero;

            int legacyIdx = PickSpawnCorner(runner, player);
            if (legacyIdx < 0) return Vector3.zero;
            return _legacySpawnPoints[legacyIdx % _legacySpawnPoints.Length] != null
                ? _legacySpawnPoints[legacyIdx % _legacySpawnPoints.Length].position
                : Vector3.zero;
        }

        // ---- Corner randomisation -----------------------------------------------

        /// <summary>
        /// Returns the shuffled corner index for a given raw slot (0–3).
        /// Falls back to the identity mapping if the permutation hasn't been
        /// initialised yet (safety guard).
        /// </summary>
        private int ShuffledCorner(int rawSlot)
        {
            if (_cornerPermutation == null) return rawSlot;
            return _cornerPermutation[rawSlot % _cornerPermutation.Length];
        }

        /// <summary>
        /// Builds <see cref="_cornerPermutation"/> via a Fisher-Yates shuffle.
        /// <para>
        /// <b>Solo</b>: truly random seed — no coordination needed.
        /// <b>Online</b>: seeded from the session name so every peer computes the
        /// identical shuffle and assigns the same corner to each player.
        /// </para>
        /// </summary>
        private void InitCornerPermutation(string sessionName, bool isSolo)
        {
            _cornerPermutation = new[] { 0, 1, 2, 3 };
            var rng = isSolo
                ? new System.Random()                         // different each solo session
                : new System.Random(SessionNameSeed(sessionName)); // same on all peers

            for (int i = _cornerPermutation.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (int a, int b) = (_cornerPermutation[i], _cornerPermutation[j]);
                _cornerPermutation[i] = b;
                _cornerPermutation[j] = a;
            }

            _log?.Info(Source,
                $"Corner permutation [{string.Join(",", _cornerPermutation)}] " +
                $"(seed={( isSolo ? "random" : sessionName )}).");
        }

        private float _lastPromotionPollTime;
        private bool _wasMasterClientLastCheck;

        private void Update()
        {
            var runner = _networkService != null ? _networkService.Runner : null;
            if (runner == null || !runner.IsRunning)
            {
                _wasMasterClientLastCheck = false;
                return;
            }

            if (Time.time - _lastPromotionPollTime >= 1.0f)
            {
                _lastPromotionPollTime = Time.time;
                bool isMaster = runner.IsSharedModeMasterClient;
                if (isMaster && !_wasMasterClientLastCheck)
                {
                    _log?.Info(Source, "Local peer promoted to Master Client. Re-arming match generators.");
                    TrySpawnGameManager();

                    var mapGen = FindFirstObjectByType<MapGenerator>();
                    if (mapGen != null)
                    {
                        mapGen.ReArmForMasterPromotion();
                    }
                }
                _wasMasterClientLastCheck = isMaster;
            }
        }

        /// <summary>
        /// Stable hash of the session name used as a shared RNG seed in online play.
        /// Using a manual polynomial hash avoids relying on <c>string.GetHashCode()</c>
        /// whose output can vary between .NET versions / platforms.
        /// </summary>
        private static int SessionNameSeed(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }
    }
}
