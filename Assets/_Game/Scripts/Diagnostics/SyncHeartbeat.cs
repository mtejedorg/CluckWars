using System.Text;
using CluckWars.Gameplay;
using CluckWars.Logging;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// State heartbeat (test-session item 3, Source <c>Sync</c>, Debug): every 2 s of simulation, at the first frame whose
    /// <c>runner.Tick</c> crosses a <c>2 × TickRate</c> boundary, one line per peer (<see cref="SyncHeartbeatFormat"/>)
    /// with the match state, every chicken, every base and every pile. Peers key the line by the same boundary tick, so
    /// the merge script can diff them for desyncs, ghost chickens and cargo / score drift.
    /// </summary>
    /// <remarks>
    /// Dev tools only. Reads <c>[Networked]</c> values (and the chicken's transform) and writes nothing. Nothing is logged
    /// without a runner and a spawned <see cref="GameManager"/> (the menu). Doppelganger decoys are left out: they share
    /// their caster's PlayerId and are not a player. <c>hp</c> is 1 for a chicken in play and 0 while it is removed
    /// (<see cref="ChickenCombat.IsRemoved"/>): chickens have had no health since v0.4, and this is the only life
    /// state there is. Remote values are whatever this peer last received, so peers agree to within one snapshot.
    /// </remarks>
    public sealed class SyncHeartbeat : MonoBehaviour
    {
        private const string Source = "Sync";
        private const int PeriodSeconds = 2;
        private const int MaxChickens = 16, MaxBases = 8, MaxPiles = 64;

        private ILogService _log;
        private readonly TickBoundary _boundary = new TickBoundary();
        private readonly SyncChicken[] _chickens = new SyncChicken[MaxChickens];
        private readonly SyncBase[] _bases = new SyncBase[MaxBases];
        private readonly SyncPile[] _piles = new SyncPile[MaxPiles];
        private readonly StringBuilder _sb = new StringBuilder(1024);

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // Deposit diagnostics (Source Base, every peer, every round): where THIS peer thinks each base is. A base whose
        // position differs between the host's and a client's dump is a client that will bank in the wrong place.
        private int _basesDumpedForRound = -1;

        private void DumpBasesOncePerRound(GameManager gm)
        {
            if (_log == null || gm.State == MatchState.WaitingForPlayers || gm.StartRound == _basesDumpedForRound) return;
            _basesDumpedForRound = gm.StartRound;
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                _log.Info("Base", DepositDiagnostics.BaseLine(b.CornerIndex, b.Owner.ToString(), b.transform.position, b.DepositRadius,
                    b.HasSpawnPosition) + $" round={gm.StartRound} authority={(b.HasStateAuthority ? 1 : 0)}");
            }
        }

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            if (!HeavyProbes.Enabled)
                _log?.Info(Source, "Heavy probes off in the Editor (no auto-test config): no Sync lines, no automatic Visual scans " +
                    "(F9 still scans). Turn on: Cluck Wars / Diagnostics / Heavy Probes In Editor.");
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Object == null || !gm.Object.IsValid) { _boundary.Reset(); return; }
            var runner = gm.Runner;
            if (runner == null || !runner.IsRunning) { _boundary.Reset(); return; }
            DumpBasesOncePerRound(gm);
            if (!HeavyProbes.Enabled || _log == null || !_log.IsEnabled(LogLevel.Debug)) return;

            int tick = (int)runner.Tick;
            if (!_boundary.TryCross(tick, Mathf.RoundToInt(PeriodSeconds * runner.TickRate), out int boundary)) return;

            int nc = 0;
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count && nc < MaxChickens; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                bool bot = c.IsBot;
                var p = c.transform.position;
                _chickens[nc++] = new SyncChicken
                {
                    Bot = bot,
                    Key = bot ? c.HomeCornerIndex : c.Object.InputAuthority.PlayerId,
                    X = p.x,
                    Z = p.z,
                    Hp = c.Combat != null && c.Combat.IsRemoved ? 0 : 1,
                    Cargo = c.Cargo != null ? Mathf.FloorToInt(c.Cargo.Cargo) : 0,
                    ControlState = c.CurrentControlState.ToString(),
                };
            }

            int nb = 0;
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count && nb < MaxBases; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                _bases[nb++] = new SyncBase { Corner = b.CornerIndex, Food = Mathf.FloorToInt(b.FoodTotal) };
            }

            int np = 0;
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count && np < MaxPiles; i++)
            {
                var pile = piles[i];
                if (pile == null || pile.Object == null || !pile.Object.IsValid) continue;
                var pos = pile.transform.position;
                _piles[np++] = new SyncPile { X = pos.x, Z = pos.z, Amount = pile.Amount };
            }

            _sb.Clear();
            SyncHeartbeatFormat.Build(_sb, boundary, gm.State.ToString(), gm.TimeRemaining,
                _chickens, nc, _bases, nb, _piles, np);
            _log.Debug(Source, _sb.ToString());
        }
    }
}
