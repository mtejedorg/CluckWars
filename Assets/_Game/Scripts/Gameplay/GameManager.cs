using CluckWars.Audio;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// High-level match lifecycle for a single round. Master-client owned scene
    /// NetworkObject. Drives the state machine
    /// (<see cref="MatchState.WaitingForPlayers"/> → <see cref="MatchState.Active"/>
    /// → <see cref="MatchState.Ended"/>), counts down the match timer, and runs the
    /// win-condition check (first to <c>FoodTargetToWin</c> OR most food when the
    /// timer expires).
    /// </summary>
    /// <remarks>
    /// Phase 7b adds per-player base ownership: the master client polls every
    /// FixedUpdateNetwork tick, finds players without an assigned base, and
    /// stamps them onto the first unowned <see cref="PlayerBase"/>. Win condition
    /// then skips unowned bases so a stray scene-baked base can't trigger a
    /// PlayerRef.None winner. <see cref="ChickenCargo"/> deposits filter by
    /// <see cref="PlayerBase.Owner"/> so only the player's own base accepts
    /// their deposits.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GameManager : NetworkBehaviour
    {
        private const string Source = "GameManager";

        [Networked] public MatchState State { get; set; }
        [Networked] public TickTimer MatchTimer { get; set; }
        [Networked] public PlayerRef WinnerPlayer { get; set; }
        /// <summary>Corner (0..3) of the winning base; -1 = no winner. Carries the
        /// winner's identity when a bot wins (WinnerPlayer stays None for bots).</summary>
        [Networked] public int WinnerCorner { get; set; } = -1;
        [Networked] public float WinnerFoodTotal { get; set; }
        [Networked] public TickTimer RestartCountdown { get; set; }
        [Networked] public TickTimer IntroTimer { get; set; }
        [Networked] public MatchEventKind ActiveEvent { get; set; }

        [Tooltip("Seconds after a match ends before the world resets and a new round starts.")]
        [Min(1f)]
        [SerializeField] private float _restartDelaySeconds = 6f;

        [Tooltip("Pre-match \"3, 2, 1, GO!\" intro window (seconds). Match timer is offset by this so the actual playable duration matches MatchConfigSO.MatchDurationSeconds.")]
        [Min(0f)]
        [SerializeField] private float _introSeconds = 3f;

        private MatchConfigSO _config;
        private ILogService _log;
        private IAudioService _audio;
        private AudioRegistrySO _audioReg;
        private PrefabRegistrySO _prefabRegistry;
        private float _winCheckIntervalSeconds = 0.25f;
        private float _nextWinCheckTime;
        private int _pickupsSpawnedCount;
        private int _pickupsCollectedCount;
        private NetworkObject _eventPile;

        public static void RegisterPickupSpawned()
        {
            if (Instance != null) Instance._pickupsSpawnedCount++;
        }

        public static void RegisterPickupCollected()
        {
            if (Instance != null) Instance._pickupsCollectedCount++;
        }

        public float MatchDurationSeconds => _config != null ? _config.MatchDurationSeconds : 180f;
        public int FoodTargetToWin => _config != null ? _config.FoodTargetToWin : 150;
        public float RestartDelaySeconds => _restartDelaySeconds;

        /// <summary>
        /// Static singleton accessor — there's one <see cref="GameManager"/> per
        /// session and it's the canonical "is the match running" gate that
        /// chicken / combat / ability systems poll each tick. Null in lobby
        /// before the master client has spawned it, and between matches.
        /// </summary>
        public static GameManager Instance { get; private set; }

        /// <summary>
        /// True only when the playable phase of a round is active: <c>State == Active</c>
        /// and we're past the intro countdown. Chickens / combat / abilities gate
        /// on this so the lobby (<see cref="MatchState.WaitingForPlayers"/>),
        /// intro window, and end screen all freeze gameplay.
        /// </summary>
        public bool IsMatchRunning => State == MatchState.Active && !IsIntroActive;

        /// <summary>Seconds remaining until the next match starts, or 0 if not in Ended state.</summary>
        public float RestartRemaining
        {
            get
            {
                if (State != MatchState.Ended) return 0f;
                return RestartCountdown.RemainingTime(Runner) ?? 0f;
            }
        }

        /// <summary>True while the pre-match intro countdown is running.</summary>
        public bool IsIntroActive => IntroTimer.IsRunning && !IntroTimer.Expired(Runner);

        /// <summary>Seconds left on the intro countdown, or 0 if not in intro.</summary>
        public float IntroRemaining => IsIntroActive ? (IntroTimer.RemainingTime(Runner) ?? 0f) : 0f;

        /// <summary>
        /// Playable seconds remaining (excludes the pre-match intro window).
        /// During the intro this freezes at <c>MatchDurationSeconds</c> so the
        /// HUD doesn't tick down before "GO!".
        /// </summary>
        public float TimeRemaining
        {
            get
            {
                if (State != MatchState.Active) return 0f;
                float matchRemain = MatchTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Max(0f, matchRemain - IntroRemaining);
            }
        }

        [Inject]
        public void Construct(MatchConfigSO config, ILogService log, IAudioService audio, AudioRegistrySO audioReg, PrefabRegistrySO prefabRegistry)
        {
            _config = config;
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
            _prefabRegistry = prefabRegistry;
        }

        public override void Spawned()
        {
            if (_log == null)
            {
                // GameManager needs MatchConfigSO which is bound in GameInstaller (scene scope),
                // not in ProjectContext. Use the SceneContext child container so both
                // scene-level and project-level bindings are available.
                var sceneCtx = FindFirstObjectByType<SceneContext>();
                if (sceneCtx != null)
                    sceneCtx.Container.Inject(this);
                else
                    ProjectContext.Instance.Container.Inject(this);
            }

            Instance = this;

            // Master client behavior:
            // - Solo (GameMode.Single): auto-start. There's no one to wait for.
            // - Shared mode: stay in WaitingForPlayers (lobby). Host clicks the
            //   Start button in MatchHud → StartMatchNow() transitions to Active.
            //   Joiners sit in the lobby until then.
            if (HasStateAuthority)
            {
                if (Runner != null && Runner.GameMode == GameMode.Single)
                {
                    StartMatch();
                }
                else
                {
                    State = MatchState.WaitingForPlayers;
                    _log?.Info(Source, "Lobby armed — waiting for host to Start.");
                }
            }
            _log?.Info(Source, $"Spawned. State={State}, target={FoodTargetToWin}, duration={MatchDurationSeconds}s.");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Host-side entry point — called by <c>MatchHud</c>'s Start button.
        /// Idempotent; no-ops if the match is already running or if the caller
        /// doesn't have StateAuthority (i.e., is not the master client). No
        /// minimum-player-count gate: host can start with just themselves on
        /// the line.
        /// </summary>
        public void StartMatchNow()
        {
            if (!HasStateAuthority)
            {
                _log?.Debug(Source, "StartMatchNow ignored — this peer is not the master client.");
                return;
            }
            if (State != MatchState.WaitingForPlayers)
            {
                _log?.Debug(Source, $"StartMatchNow ignored — match is already in state {State}.");
                return;
            }
            _log?.Info(Source, "StartMatchNow accepted — host pressed Start.");
            StartMatch();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            // Base ownership: cheap to poll, runs every tick. Idempotent — players
            // already assigned skip the inner loop, so the cost is O(players × bases)
            // and the upper bound is 4×4 for the demo.
            AssignBasesToPlayers();

            // Match ended — wait out the restart countdown, then reset the world
            // and start a fresh round.
            if (State == MatchState.Ended)
            {
                if (RestartCountdown.Expired(Runner))
                {
                    RestartMatch();
                }
                return;
            }

            if (State != MatchState.Active) return;

            // No win checks during the intro countdown. Bases are all empty
            // anyway, but be explicit so future logic doesn't accidentally end
            // the match during "3, 2, 1, GO!".
            if (IsIntroActive) return;

            // Comeback event check at T-60
            if (State == MatchState.Active && !IsIntroActive && TimeRemaining <= 60f && ActiveEvent == MatchEventKind.None)
            {
                TriggerFinalMinuteEvent();
            }

            // Cheap throttle: 4 wins-checks per second is plenty and keeps Physics /
            // FindObjectsByType pressure low.
            if (Runner.SimulationTime < _nextWinCheckTime) {
                if (MatchTimer.Expired(Runner)) EndOnTimerExpiry();
                return;
            }
            _nextWinCheckTime = (float)Runner.SimulationTime + _winCheckIntervalSeconds;

            EvaluateWinCondition();

            if (State == MatchState.Active && MatchTimer.Expired(Runner))
            {
                EndOnTimerExpiry();
            }
        }

        private void AssignBasesToPlayers()
        {
            var bases = PlayerBase.ActiveBases;
            if (bases.Count == 0) return;

            foreach (var player in Runner.ActivePlayers)
            {
                if (!player.IsRealPlayer) continue;
                if (PlayerHasBase(bases, player)) continue;

                // Prefer the base whose CornerIndex matches the chicken's stamped
                // HomeCornerIndex (exact identity). No fallbacks.
                var freeBase = FindHomeCornerBaseForPlayer(bases, player);
                if (freeBase == null)
                {
                    // If the home corner base is somehow taken by someone else, this is a real
                    // invariant violation. Warn and continue. (FindHomeCornerBaseForPlayer returns
                    // null if the base is taken, or if the chicken hasn't spawned yet).
                    var allChickens = ChickenController.ActiveControllers;
                    bool chickenFound = false;
                    for (int i = 0; i < allChickens.Count; i++)
                    {
                        var c = allChickens[i];
                        if (c != null && c.Object != null && c.Object.InputAuthority == player)
                        {
                            chickenFound = true;
                            if (c.HomeCornerIndex >= 0)
                            {
                                // Chicken has a valid corner but base is null, check if taken
                                for (int j = 0; j < bases.Count; j++)
                                {
                                    var b = bases[j];
                                    if (b != null && b.CornerIndex == c.HomeCornerIndex && (b.Owner.IsRealPlayer || b.BotClaimed))
                                    {
                                        _log?.Warn(Source, $"Invariant violation: base {b.name} (corner {b.CornerIndex}) is already claimed, but player {player} has it as home corner.");
                                    }
                                }
                            }
                            break;
                        }
                    }

                    if (!chickenFound)
                    {
                        // Expected: chicken hasn't spawned yet. Retry next tick.
                        continue;
                    }
                    
                    continue; // don't break — other players may still need bases
                }

                freeBase.Owner = player;
                _log?.Info(Source, $"Assigned '{freeBase.name}' (corner {freeBase.CornerIndex}) " +
                    $"to player {player} (PlayerId={player.PlayerId}).");
            }

            // Bots: claim the base at each bot's home corner so it counts for win
            // checks and tints. Bots share [Player:None] authority, so Owner can't
            // carry their identity — BotClaimed does.
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || !c.IsBot || c.HomeCornerIndex < 0) continue;
                for (int j = 0; j < bases.Count; j++)
                {
                    var b = bases[j];
                    if (b == null || b.BotClaimed || b.Owner.IsRealPlayer) continue;
                    if (b.CornerIndex != c.HomeCornerIndex) continue;
                    b.BotClaimed = true;
                    _log?.Info(Source, $"Bot ({c.Class}) claimed '{b.name}' (corner {b.CornerIndex}).");
                }
            }
        }

        /// <summary>
        /// The unowned base whose CornerIndex equals the player's chicken
        /// <c>HomeCornerIndex</c>. Null if the chicken hasn't spawned, has no
        /// stamped corner, or the matching base is taken.
        /// </summary>
        private PlayerBase FindHomeCornerBaseForPlayer(System.Collections.Generic.List<PlayerBase> bases, PlayerRef player)
        {
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (c.Object.InputAuthority != player) continue;
                if (c.HomeCornerIndex < 0) return null;
                for (int j = 0; j < bases.Count; j++)
                {
                    var b = bases[j];
                    if (b == null || b.Owner.IsRealPlayer || b.BotClaimed) continue;
                    if (b.CornerIndex == c.HomeCornerIndex) return b;
                }
                return null;
            }
            return null;
        }

        /// <summary>
        /// Finds the unowned <see cref="PlayerBase"/> closest to the chicken
        /// controlled by <paramref name="player"/>. Returns null if the chicken
        /// hasn't spawned yet or no unowned base is available.
        /// </summary>


        private static bool PlayerHasBase(System.Collections.Generic.List<PlayerBase> bases, PlayerRef player)
        {
            for (int i = 0; i < bases.Count; i++)
            {
                if (bases[i] != null && bases[i].Owner == player) return true;
            }
            return false;
        }



        private void StartMatch()
        {
            State = MatchState.Active;
            // Match timer is offset by the intro window so the playable phase still
            // equals MatchConfigSO.MatchDurationSeconds. HUD subtracts when intro
            // is active so the displayed time freezes at MM:SS until "GO!".
            IntroTimer = TickTimer.CreateFromSeconds(Runner, _introSeconds);
            MatchTimer = TickTimer.CreateFromSeconds(Runner, MatchDurationSeconds + _introSeconds);
            WinnerPlayer = PlayerRef.None;
            WinnerCorner = -1;
            WinnerFoodTotal = 0f;
            _pickupsSpawnedCount = 0;
            _pickupsCollectedCount = 0;
            _audio?.PlaySFX(_audioReg != null ? _audioReg.MatchStart : null);
            if (_audioReg != null && _audioReg.MatchMusic != null)
            {
                _audio?.PlayMusic(_audioReg.MatchMusic, 0.6f);
            }
            _log?.Info(Source, $"Match started: {MatchDurationSeconds}s playable + {_introSeconds}s intro, target {FoodTargetToWin} food.");
        }

        private void EvaluateWinCondition()
        {
            // Claimed bases only (human-owned or bot-claimed) — a stray unclaimed
            // base hitting the target shouldn't trigger a phantom winner. Bots
            // compete on equal terms: a bot base reaching the target ends the match.
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || !b.IsClaimed) continue;
                if (b.FoodTotal < FoodTargetToWin) continue;
                EndMatch(b.Owner, b.CornerIndex, b.FoodTotal, reason: "food target reached");
                return;
            }
        }

        private void EndOnTimerExpiry()
        {
            var bases = PlayerBase.ActiveBases;
            PlayerBase bestBase = null;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || !b.IsClaimed) continue;
                if (bestBase == null)
                {
                    bestBase = b;
                    continue;
                }
                if (b.FoodTotal > bestBase.FoodTotal)
                {
                    bestBase = b;
                }
                else if (Mathf.Approximately(b.FoodTotal, bestBase.FoodTotal))
                {
                    int bKills = GetKillsForCorner(b.CornerIndex);
                    int bestKills = GetKillsForCorner(bestBase.CornerIndex);
                    if (bKills > bestKills)
                    {
                        bestBase = b;
                    }
                    else if (bKills == bestKills)
                    {
                        if (b.CornerIndex < bestBase.CornerIndex)
                        {
                            bestBase = b;
                        }
                    }
                }
            }
            PlayerRef winner = bestBase != null ? bestBase.Owner : PlayerRef.None;
            int winnerCorner = bestBase != null ? bestBase.CornerIndex : -1;
            float bestTotal = bestBase != null ? bestBase.FoodTotal : 0f;
            EndMatch(winner, winnerCorner, bestTotal, reason: "timer expired");
        }

        private int GetKillsForCorner(int cornerIndex)
        {
            var statsList = ChickenMatchStats.ActiveStats;
            for (int i = 0; i < statsList.Count; i++)
            {
                var s = statsList[i];
                if (s == null || s.Object == null || !s.Object.IsValid) continue;
                var cc = s.GetComponent<ChickenController>();
                if (cc != null && cc.HomeCornerIndex == cornerIndex && !cc.IsDecoy)
                {
                    return s.Kills;
                }
            }
            return 0;
        }

        private void EndMatch(PlayerRef winner, int winnerCorner, float winnerTotal, string reason)
        {
            State = MatchState.Ended;
            WinnerPlayer = winner;
            WinnerCorner = winnerCorner;
            WinnerFoodTotal = winnerTotal;
            RestartCountdown = TickTimer.CreateFromSeconds(Runner, _restartDelaySeconds);
            _audio?.StopMusic();
            // MatchVictory if anyone actually won, MatchEnd otherwise (timer expiry with no scorer).
            var endCue = winner.IsRealPlayer
                ? (_audioReg != null ? _audioReg.MatchVictory : null)
                : (_audioReg != null ? _audioReg.MatchEnd : null);
            _audio?.PlaySFX(endCue);
            _log?.Info(Source, $"Match ended ({reason}). Winner={winner}, total={winnerTotal:0.0}. Next round in {_restartDelaySeconds}s.");

            // KPI match summary instrumentation (IP7)
            if (HasStateAuthority)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine();
                sb.AppendLine("=== MATCH SUMMARY ===");
                sb.AppendLine($"Match Length: {MatchDurationSeconds - TimeRemaining:0.0}s");
                sb.AppendLine($"Winner Corner: {winnerCorner} (Food: {winnerTotal:0.0})");
                sb.AppendLine($"Event Fired: {ActiveEvent}");
                sb.AppendLine($"Pickups: Spawned={_pickupsSpawnedCount}, Collected={_pickupsCollectedCount}");
                sb.AppendLine("Players Stats:");
                var statsList = ChickenMatchStats.ActiveStats;
                for (int i = 0; i < statsList.Count; i++)
                {
                    var s = statsList[i];
                    if (s == null || s.Object == null || !s.Object.IsValid) continue;
                    var cc = s.GetComponent<ChickenController>();
                    if (cc != null && !cc.IsDecoy)
                    {
                        sb.AppendLine($"  P{cc.HomeCornerIndex + 1} ({cc.Class}): Kills={s.Kills}, Deposited={s.FoodDeposited:0.0}");
                    }
                }
                sb.AppendLine("=====================");
                _log?.Info("MatchSummary", sb.ToString());
            }
        }

        /// <summary>
        /// Resets every networked entity master can touch (bases, piles, loose pickups)
        /// and RPCs each chicken's authority to clear its own combat / cargo state.
        /// State then flips back to <see cref="MatchState.Active"/> with a fresh
        /// match timer so a new round begins immediately.
        /// </summary>
        private void RestartMatch()
        {
            _log?.Info(Source, "Restarting match — resetting world.");

            // Master has authority over scene NetworkObjects (bases, piles) — mutate
            // their networked state directly; replication carries the new values to
            // every peer on the next snapshot.
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                if (bases[i] != null) bases[i].FoodTotal = 0f;
            }

            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null) continue;
                // Refill to whatever capacity was set at spawn (per-instance via
                // onBeforeSpawned for center vs satellite piles).
                p.Amount = p.MaxAmount;
            }

            // Despawn the golden pile if it was spawned in the current round
            if (_eventPile != null)
            {
                if (_eventPile.IsValid)
                {
                    Runner.Despawn(_eventPile);
                }
                _eventPile = null;
            }

            // Loose ground-dropped pickups don't belong in the fresh match — kill them.
            var pickups = FoodPickup.ActivePickups;
            // Iterate backwards when despawning to avoid list modification issues
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var pk = pickups[i];
                if (pk != null && pk.Object != null && pk.Object.IsValid)
                    Runner.Despawn(pk.Object);
            }

            // Chickens are owned by each player — cross-authority writes go via RPC.
            // Calling these on every chicken routes to that chicken's state authority.
            var combats = ChickenCombat.ActiveCombats;
            for (int i = 0; i < combats.Count; i++)
            {
                combats[i]?.RPC_ResetForNewMatch();
            }

            var cargos = ChickenCargo.ActiveCargos;
            for (int i = 0; i < cargos.Count; i++)
            {
                cargos[i]?.RPC_ResetForNewMatch();
            }

            // Reset v0.3 control states (slow timers, root, knockback, aura).
            var chickenControllers = ChickenController.ActiveControllers;
            for (int i = 0; i < chickenControllers.Count; i++)
            {
                chickenControllers[i]?.RPC_ResetControlStates();
            }

            // Reset per-chicken match stats so kills and food totals start fresh.
            var matchStats = ChickenMatchStats.ActiveStats;
            for (int i = 0; i < matchStats.Count; i++)
            {
                matchStats[i]?.RPC_ResetStats();
            }

            // Teleport each chicken back to its corner so the new round opens with
            // everyone where they started. MapGenerator.SpawnPoints is in the same
            // order as PlayerBase.CornerIndex, so the playerId-modulo mapping that
            // MatchBootstrapper uses on join is reproduced here.
            var mapGen = FindFirstObjectByType<MapGenerator>();
            var spawnPoints = mapGen != null ? mapGen.SpawnPoints : null;
            _log?.Debug(Source, $"RestartMatch: {bases.Count} bases, {piles.Count} piles reset. " +
                $"spawnPoints={(spawnPoints != null ? spawnPoints.Count.ToString() : "null")}.");
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                var controllers = ChickenController.ActiveControllers;
                // Every chicken carries its spawn corner in HomeCornerIndex — humans
                // and bots alike — so restart teleports are exact (the old PlayerId-
                // modulo mapping ignored the corner-permutation shuffle and could
                // send a player to a rival's corner).
                int fallbackCorner = 0;
                for (int i = 0; i < controllers.Count; i++)
                {
                    var ctrl = controllers[i];
                    if (ctrl == null || ctrl.Object == null) continue;
                    if (!ctrl.Object.InputAuthority.IsRealPlayer && !ctrl.IsBot) continue;
                    int corner = ctrl.HomeCornerIndex;
                    if (corner < 0) corner = fallbackCorner++; // legacy chickens without a stamp
                    ctrl.RPC_TeleportTo(spawnPoints[corner % spawnPoints.Count] + Vector3.up * 0.05f);
                }
            }

            // Resume the match — fresh intro countdown + timer, same window as
            // the initial StartMatch so each round opens identically.
            State = MatchState.Active;
            IntroTimer = TickTimer.CreateFromSeconds(Runner, _introSeconds);
            MatchTimer = TickTimer.CreateFromSeconds(Runner, MatchDurationSeconds + _introSeconds);
            WinnerPlayer = PlayerRef.None;
            WinnerCorner = -1;
            WinnerFoodTotal = 0f;
            RestartCountdown = default;
            _nextWinCheckTime = 0f;
            ActiveEvent = MatchEventKind.None;
            _pickupsSpawnedCount = 0;
            _pickupsCollectedCount = 0;
        }

        private void TriggerFinalMinuteEvent()
        {
            if (!HasStateAuthority) return;

            int rolled = Random.Range(1, 5); // 1..4 inclusive
            ActiveEvent = (MatchEventKind)rolled;

            _log?.Info(Source, $"FINAL MINUTE EVENT FIRED: {ActiveEvent}");

            switch (ActiveEvent)
            {
                case MatchEventKind.GoldenPile:
                    SpawnGoldenPile();
                    break;
                case MatchEventKind.UnderdogSurge:
                    ApplyUnderdogSurge();
                    break;
                case MatchEventKind.LeaderBounty:
                    ApplyLeaderBounty();
                    break;
                case MatchEventKind.Restock:
                    RestockPiles();
                    break;
            }
        }

        private void SpawnGoldenPile()
        {
            if (_prefabRegistry == null || _prefabRegistry.FoodPile == null)
            {
                _log?.Warn(Source, "GoldenPile event: FoodPile prefab not found in registry.");
                return;
            }

            Vector3 pos = Vector3.zero;
            bool foundSpot = false;
            for (int i = 0; i < 8; i++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float radius = Random.Range(4.5f, 6.5f);
                pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                // Sphere bottom sits at y=0.1 so the ground plane collider never
                // rejects a candidate — only walls, pile blockers, and bases do.
                if (!Physics.CheckSphere(pos + Vector3.up * 0.8f, 0.7f))
                {
                    foundSpot = true;
                    break;
                }
            }

            if (!foundSpot)
            {
                _log?.Warn(Source, "GoldenPile event: Failed to find non-overlapping position after 8 attempts. Spawning at last candidate.");
            }

            _eventPile = Runner.Spawn(
                _prefabRegistry.FoodPile,
                pos,
                Quaternion.identity,
                onBeforeSpawned: (_, networkObject) =>
                {
                    var pile = networkObject.GetComponent<FoodPile>();
                    if (pile != null)
                    {
                        pile.Amount = 25f;
                        pile.MaxAmount = 25f;
                    }
                });

            _log?.Info(Source, $"GoldenPile spawned at {pos} with 25 food.");
        }

        private void ApplyUnderdogSurge()
        {
            var controllers = ChickenController.ActiveControllers;
            ChickenController underdog = null;
            float lowestFood = float.MaxValue;

            for (int i = 0; i < controllers.Count; i++)
            {
                var ctrl = controllers[i];
                if (ctrl == null || ctrl.Object == null || !ctrl.Object.IsValid || ctrl.IsDecoy) continue;

                float food = GetBaseFoodForCorner(ctrl.HomeCornerIndex);
                if (food < lowestFood)
                {
                    lowestFood = food;
                    underdog = ctrl;
                }
            }

            if (underdog != null)
            {
                underdog.UnderdogSurgeActive = true;
                _log?.Info(Source, $"UnderdogSurge applied to {underdog.name} (Corner {underdog.HomeCornerIndex}, Food: {lowestFood}).");
            }
        }

        private void ApplyLeaderBounty()
        {
            var controllers = ChickenController.ActiveControllers;
            ChickenController leader = null;
            float highestFood = -1f;

            for (int i = 0; i < controllers.Count; i++)
            {
                var ctrl = controllers[i];
                if (ctrl == null || ctrl.Object == null || !ctrl.Object.IsValid || ctrl.IsDecoy) continue;

                float food = GetBaseFoodForCorner(ctrl.HomeCornerIndex);
                if (food > highestFood)
                {
                    highestFood = food;
                    leader = ctrl;
                }
            }

            if (leader != null)
            {
                leader.LeaderBountyActive = true;
                _log?.Info(Source, $"LeaderBounty applied to {leader.name} (Corner {leader.HomeCornerIndex}, Food: {highestFood}).");
            }
        }

        private float GetBaseFoodForCorner(int corner)
        {
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b != null && b.CornerIndex == corner)
                {
                    return b.FoodTotal;
                }
            }
            return 0f;
        }

        private void RestockPiles()
        {
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null) continue;
                p.Amount = Mathf.Min(p.MaxAmount, p.Amount + 10f);
            }
            _log?.Info(Source, "Restocked all food piles by +10.");
        }
    }
}
