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
    /// Scope-trimmed for Phase 7: scans every <c>PlayerBase</c> in the scene and
    /// picks the one with the highest <c>FoodTotal</c>. Per-player base ownership
    /// (one base per joined PlayerRef, deposit only into your own) is Phase 7b —
    /// it requires hooking player-join to base assignment, which we'll do once
    /// the lifecycle itself is validated.
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GameManager : NetworkBehaviour
    {
        private const string Source = "GameManager";

        [Networked] public MatchState State { get; set; }
        [Networked] public TickTimer MatchTimer { get; set; }
        [Networked] public PlayerRef WinnerPlayer { get; set; }
        [Networked] public float WinnerFoodTotal { get; set; }

        private MatchConfigSO _config;
        private ILogService _log;
        private float _winCheckIntervalSeconds = 0.25f;
        private float _nextWinCheckTime;

        public float MatchDurationSeconds => _config != null ? _config.MatchDurationSeconds : 180f;
        public int FoodTargetToWin => _config != null ? _config.FoodTargetToWin : 150;

        /// <summary>Seconds remaining on the match timer, or 0 once Ended.</summary>
        public float TimeRemaining
        {
            get
            {
                if (State != MatchState.Active) return 0f;
                return MatchTimer.RemainingTime(Runner) ?? 0f;
            }
        }

        [Inject]
        public void Construct(MatchConfigSO config, ILogService log)
        {
            _config = config;
            _log = log;
        }

        public override void Spawned()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);

            // Only the master client (StateAuthority for scene NetworkObjects) starts
            // the match. The GDD has a "WaitingForPlayers" lobby state; for the demo
            // we go straight into Active so a solo dev session starts the timer
            // immediately.
            if (HasStateAuthority)
            {
                StartMatch();
            }
            _log?.Info(Source, $"Spawned. State={State}, target={FoodTargetToWin}, duration={MatchDurationSeconds}s.");
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            if (State != MatchState.Active) return;

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

        private void StartMatch()
        {
            State = MatchState.Active;
            MatchTimer = TickTimer.CreateFromSeconds(Runner, MatchDurationSeconds);
            WinnerPlayer = PlayerRef.None;
            WinnerFoodTotal = 0f;
            _log?.Info(Source, $"Match started: {MatchDurationSeconds}s, target {FoodTargetToWin} food.");
        }

        private void EvaluateWinCondition()
        {
            // Phase 7 simplification: any base reaching the food target ends the match.
            // Phase 7b will scope this to bases owned by a real PlayerRef.
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null || b.FoodTotal < FoodTargetToWin) continue;
                EndMatch(b.Owner, b.FoodTotal, reason: "food target reached");
                return;
            }
        }

        private void EndOnTimerExpiry()
        {
            // Find the highest-total base. Ties are broken by whoever the iterator
            // hits first; good enough for the demo, can be refined when scoring rules
            // are revisited.
            var bases = FindObjectsByType<PlayerBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            PlayerRef winner = PlayerRef.None;
            float bestTotal = -1f;
            for (int i = 0; i < bases.Length; i++)
            {
                var b = bases[i];
                if (b == null) continue;
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
            _log?.Info(Source, $"Match ended ({reason}). Winner={winner}, total={winnerTotal:0.0}.");
        }
    }
}
