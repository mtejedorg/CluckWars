using CluckWars.Audio;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;

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
        [Networked] public float WinnerFoodTotal { get; set; }
        [Networked] public TickTimer RestartCountdown { get; set; }
        [Networked] public TickTimer IntroTimer { get; set; }

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
        private float _winCheckIntervalSeconds = 0.25f;
        private float _nextWinCheckTime;

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
        public void Construct(MatchConfigSO config, ILogService log, IAudioService audio, AudioRegistrySO audioReg)
        {
            _config = config;
            _log = log;
            _audio = audio;
            _audioReg = audioReg;
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

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
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (bases.Length == 0) return;

            foreach (var player in Runner.ActivePlayers)
            {
                if (!player.IsRealPlayer) continue;
                if (PlayerHasBase(bases, player)) continue;

                // Prefer the corner matching the player's PlayerId so spawn corner
                // and assigned base agree (MatchBootstrapper picks SpawnPoints[
                // playerId % count]). Falls back to first-unowned for scene-baked
                // bases that don't carry a CornerIndex.
                int desiredCorner = Mathf.Abs(player.PlayerId) % bases.Length;
                var pairedBase = FindUnownedBaseAtCorner(bases, desiredCorner);
                var freeBase = pairedBase ?? FindUnownedBase(bases);
                if (freeBase == null) break; // no more bases to hand out

                freeBase.Owner = player;
                _log?.Info(Source, $"Assigned {freeBase.name} (corner {freeBase.CornerIndex}) to {player}.");
            }
        }

        private static PlayerBase FindUnownedBaseAtCorner(PlayerBase[] bases, int cornerIndex)
        {
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || b.Owner.IsRealPlayer) continue;
                if (b.CornerIndex == cornerIndex) return b;
            }
            return null;
        }

        private static bool PlayerHasBase(PlayerBase[] bases, PlayerRef player)
        {
            for (int i = 0; i < bases.Length; i++)
            {
                if (bases[i] != null && bases[i].Owner == player) return true;
            }
            return false;
        }

        private static PlayerBase FindUnownedBase(PlayerBase[] bases)
        {
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b != null && !b.Owner.IsRealPlayer) return b;
            }
            return null;
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
            WinnerFoodTotal = 0f;
            _audio?.PlaySFX(_audioReg != null ? _audioReg.MatchStart : null);
            if (_audioReg != null && _audioReg.MatchMusic != null)
            {
                _audio?.PlayMusic(_audioReg.MatchMusic, 0.6f);
            }
            _log?.Info(Source, $"Match started: {MatchDurationSeconds}s playable + {_introSeconds}s intro, target {FoodTargetToWin} food.");
        }

        private void EvaluateWinCondition()
        {
            // Owned bases only — a stray unowned base hitting the target shouldn't
            // trigger a PlayerRef.None winner.
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || !b.Owner.IsRealPlayer) continue;
                if (b.FoodTotal < FoodTargetToWin) continue;
                EndMatch(b.Owner, b.FoodTotal, reason: "food target reached");
                return;
            }
        }

        private void EndOnTimerExpiry()
        {
            // Highest-total OWNED base wins. Ties broken by iteration order; good
            // enough for the demo, refine when scoring rules are revisited.
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            PlayerRef winner = PlayerRef.None;
            float bestTotal = -1f;
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || !b.Owner.IsRealPlayer) continue;
                if (b.FoodTotal > bestTotal)
                {
                    bestTotal = b.FoodTotal;
                    winner = b.Owner;
                }
            }
            EndMatch(winner, Mathf.Max(0f, bestTotal), reason: "timer expired");
        }

        private void EndMatch(PlayerRef winner, float winnerTotal, string reason)
        {
            State = MatchState.Ended;
            WinnerPlayer = winner;
            WinnerFoodTotal = winnerTotal;
            RestartCountdown = TickTimer.CreateFromSeconds(Runner, _restartDelaySeconds);
            _audio?.StopMusic();
            // MatchVictory if anyone actually won, MatchEnd otherwise (timer expiry with no scorer).
            var endCue = winner.IsRealPlayer
                ? (_audioReg != null ? _audioReg.MatchVictory : null)
                : (_audioReg != null ? _audioReg.MatchEnd : null);
            _audio?.PlaySFX(endCue);
            _log?.Info(Source, $"Match ended ({reason}). Winner={winner}, total={winnerTotal:0.0}. Next round in {_restartDelaySeconds}s.");
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
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < bases.Length; i++)
            {
                if (bases[i] != null) bases[i].FoodTotal = 0f;
            }

            var piles = FindObjectsByType<FoodPile>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < piles.Length; i++)
            {
                var p = piles[i];
                if (p == null) continue;
                // Refill to whatever capacity was set at spawn (per-instance via
                // onBeforeSpawned for center vs satellite piles).
                p.Amount = p.MaxAmount;
            }

            // Loose ground-dropped pickups don't belong in the fresh match — kill them.
            var pickups = FindObjectsByType<FoodPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < pickups.Length; i++)
            {
                var pk = pickups[i];
                if (pk != null && pk.Object != null && pk.Object.IsValid)
                    Runner.Despawn(pk.Object);
            }

            // Chickens are owned by each player — cross-authority writes go via RPC.
            // Calling these on every chicken routes to that chicken's state authority.
            var combats = FindObjectsByType<ChickenCombat>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < combats.Length; i++)
            {
                combats[i]?.RPC_ResetForNewMatch();
            }

            var cargos = FindObjectsByType<ChickenCargo>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < cargos.Length; i++)
            {
                cargos[i]?.RPC_ResetForNewMatch();
            }

            // Teleport each chicken back to its corner so the new round opens with
            // everyone where they started. MapGenerator.SpawnPoints is in the same
            // order as PlayerBase.CornerIndex, so the playerId-modulo mapping that
            // MatchBootstrapper uses on join is reproduced here.
            var mapGen = FindFirstObjectByType<MapGenerator>();
            var spawnPoints = mapGen != null ? mapGen.SpawnPoints : null;
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                var controllers = FindObjectsByType<ChickenController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < controllers.Length; i++)
                {
                    var ctrl = controllers[i];
                    if (ctrl == null || ctrl.Object == null) continue;
                    var player = ctrl.Object.InputAuthority;
                    if (!player.IsRealPlayer) continue;
                    int cornerIdx = Mathf.Abs(player.PlayerId) % spawnPoints.Count;
                    ctrl.RPC_TeleportTo(spawnPoints[cornerIdx]);
                }
            }

            // Resume the match — fresh intro countdown + timer, same window as
            // the initial StartMatch so each round opens identically.
            State = MatchState.Active;
            IntroTimer = TickTimer.CreateFromSeconds(Runner, _introSeconds);
            MatchTimer = TickTimer.CreateFromSeconds(Runner, MatchDurationSeconds + _introSeconds);
            WinnerPlayer = PlayerRef.None;
            WinnerFoodTotal = 0f;
            RestartCountdown = default;
            _nextWinCheckTime = 0f;
        }
    }
}
