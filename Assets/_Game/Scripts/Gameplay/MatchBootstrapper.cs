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
        private ILogService _log;

        [Inject]
        public void Construct(INetworkService networkService, ISessionSelectionService selection, PrefabRegistrySO prefabRegistry, ILogService log)
        {
            _networkService = networkService;
            _selection = selection;
            _prefabRegistry = prefabRegistry;
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
                        }

                        if (haveLoadout)
                        {
                            // Slot 2 is the Combo (Assassin) 3rd slot — AbilityController gates
                            // it by passive, so pass null for non-Assassins for tidiness.
                            var abilities = networkObject.GetComponent<AbilityController>();
                            if (abilities != null)
                            {
                                var slot2 = botClass == ChickenClass.Assassin ? loadout.Slot2 : null;
                                abilities.SetSlots(loadout.Slot0, loadout.Slot1, slot2);
                            }
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
            var eligible = new System.Collections.Generic.List<BotLoadoutPreset>(_botLoadouts.Length);
            for (int i = 0; i < _botLoadouts.Length; i++)
            {
                var p = _botLoadouts[i];
                if (p.HasAnyAbility && p.AllowsClass(cls)) eligible.Add(p);
            }
            if (eligible.Count == 0) return false;

            preset = eligible[UnityEngine.Random.Range(0, eligible.Count)];
            return true;
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

            [Tooltip("Slot-0 ability (typically Offense).")]
            public AbilityBaseSO Slot0;

            [Tooltip("Slot-1 ability (typically Defense or Escape).")]
            public AbilityBaseSO Slot1;

            [Tooltip("Slot-2 ability — only equipped on Assassin bots (Combo 3rd slot). Optional.")]
            public AbilityBaseSO Slot2;

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
                    if (controller != null) controller.Class = chosenClass;

                    // Apply player-chosen abilities when the player selected from a pool.
                    // Null means "use the prefab default" — SetSlots ignores null args.
                    if (_selection != null &&
                        (_selection.Ability0 != null || _selection.Ability1 != null || _selection.Ability2 != null))
                    {
                        var abilityCtrl = networkObject.GetComponent<AbilityController>();
                        abilityCtrl?.SetSlots(_selection.Ability0, _selection.Ability1, _selection.Ability2);
                    }
                });
        }

        private Vector3 PickSpawnPosition(NetworkRunner runner, PlayerRef player)
        {
            // Prefer MapGenerator's computed corners. Lookup is cached; null check
            // re-resolves if the MapGenerator was added after Start ran.
            if (_mapGenerator == null) _mapGenerator = FindFirstObjectByType<MapGenerator>();
            if (_mapGenerator != null && _mapGenerator.SpawnPoints != null && _mapGenerator.SpawnPoints.Count > 0)
            {
                var points = _mapGenerator.SpawnPoints;
                // Single mode: always slot 0 for the human player so they never
                // collide with bot slot 1, regardless of what Fusion assigns as PlayerId.
                // Shared mode: PlayerId is sequential (0-based) across up to 4 players.
                int raw = (runner.GameMode == GameMode.Single)
                    ? 0
                    : Mathf.Abs(player.PlayerId) % 4;
                int idx = ShuffledCorner(raw);
                return points[idx % points.Count];
            }

            // Legacy fallback for scenes without a MapGenerator.
            if (_legacySpawnPoints == null || _legacySpawnPoints.Length == 0)
                return Vector3.zero;

            int legacyRaw = Mathf.Abs(player.PlayerId) % _legacySpawnPoints.Length;
            int legacyIdx = ShuffledCorner(legacyRaw) % _legacySpawnPoints.Length;
            return _legacySpawnPoints[legacyIdx] != null
                ? _legacySpawnPoints[legacyIdx].position
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
