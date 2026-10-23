using System.Collections.Generic;
using System.Text;
using CluckWars.Gameplay;
using CluckWars.Logging;
using Fusion;
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
    /// state there is. <c>cs</c> comes from the replicated <c>ControlFlags</c> (<see cref="SyncControlState"/>). Remote
    /// values are whatever this peer last received: positions of a moving chicken lag its owner's own view by the
    /// interpolation delay plus half the RTT (about 0.2-0.35 s, i.e. 1.5-3 m at 7-10 m/s in the 2026-10-20 runs).
    /// <para>The same lag applies to <c>cs</c>: the owner prints the flags of its newest tick, every other peer the newest
    /// snapshot it has, so a flag that toggled in that window differs for one line, and when it toggles again before the
    /// next line the difference is the same sequence seen later (owner Free/Slowed, remotes Slowed/Free: CW-226, session
    /// 20261021-150533). merge.ps1 therefore only reports a <c>cs</c> pair that repeats with the SAME values. To tell a
    /// toggling flag from one that never arrived, each chicken also carries <c>fl=N</c> (how often this peer saw its
    /// replicated flags change since the last line, sampled every frame on every peer alike, printed when N &gt; 0) and,
    /// on the peer that simulates it, <c>own=1</c> plus <c>src=</c> (its slow sources of the last tick).</para>
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

        // fl=N: replicated control-flag changes seen on this peer since the last line, per chicken (CW-226).
        private struct FlagTrack { public ControlVfx Last; public int Flips; }
        private readonly Dictionary<NetworkId, FlagTrack> _flagTrack = new Dictionary<NetworkId, FlagTrack>(16);

        [Inject]
        public void Construct(ILogService log) => _log = log;

        // Deposit diagnostics (Source Base, every peer, every round): where THIS peer thinks each base is. A base whose
        // position differs between the host's and a client's dump is a client that will bank in the wrong place.
        private int _basesDumpedForRound = -1;

        // Round-reset diagnostics (Source Reset, every peer, dev tools): when a round goes Active (its 3-2-1), where every
        // chicken stands against its home corner's spawn point. After a reset every distance should be ~0.
        private int _respawnCheckedForRound = -1;
        private MapGenerator _map;

        private void CheckRespawnOncePerRound(GameManager gm)
        {
            if (_log == null || gm.State != MatchState.Active || gm.StartRound == _respawnCheckedForRound) return;
            _respawnCheckedForRound = gm.StartRound;
            if (_map == null) _map = FindFirstObjectByType<MapGenerator>();
            var spawns = _map != null ? _map.SpawnPoints : null;
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                var p = c.transform.position;
                int home = c.HomeCornerIndex;
                string dist = spawns != null && home >= 0 && home < spawns.Count
                    ? DepositDiagnostics.PlanarDistance(p, spawns[home]).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    : "n/a";
                _log.Info("Reset", RespawnLine(c.IsBot ? "B" + home : "P" + c.Object.InputAuthority.PlayerId, home, p, dist, gm.StartRound));
            }
        }

        /// <summary><c>Respawn check chicken=P2 home=3 pos=x,z dist_to_corner=0.00 round=2</c></summary>
        public static string RespawnLine(string id, int home, Vector3 pos, string distToCorner, int round) =>
            System.FormattableString.Invariant($"Respawn check chicken={id} home={home} pos={pos.x:0.0},{pos.z:0.0} dist_to_corner={distToCorner} round={round}");

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
            if (gm == null || gm.Object == null || !gm.Object.IsValid) { _boundary.Reset(); _flagTrack.Clear(); return; }
            var runner = gm.Runner;
            if (runner == null || !runner.IsRunning) { _boundary.Reset(); _flagTrack.Clear(); return; }
            DumpBasesOncePerRound(gm);
            CheckRespawnOncePerRound(gm);
            if (!HeavyProbes.Enabled || _log == null || !_log.IsEnabled(LogLevel.Debug)) return;
            TrackControlFlags();

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
                    ControlState = SyncControlState.Name(c.ControlFlags),   // replicated, so every peer agrees
                    Flips = TakeFlips(c.Object.Id),
                    Own = c.HasStateAuthority,
                    Src = c.HasStateAuthority ? SyncControlState.Sources(c.ActiveSlowSources) : null,
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

        private void TrackControlFlags()
        {
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
            {
                var c = chickens[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                var id = c.Object.Id;
                var flags = c.ControlFlags;
                if (!_flagTrack.TryGetValue(id, out var t)) _flagTrack[id] = new FlagTrack { Last = flags };
                else if (t.Last != flags) _flagTrack[id] = new FlagTrack { Last = flags, Flips = t.Flips + 1 };
            }
        }

        private int TakeFlips(NetworkId id)
        {
            if (!_flagTrack.TryGetValue(id, out var t)) return 0;
            _flagTrack[id] = new FlagTrack { Last = t.Last };
            return t.Flips;
        }
    }
}
