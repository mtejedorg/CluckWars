using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Logging;
using Fusion;
using UnityEngine;
using Zenject;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// In-game invariant probe (Source <c>Invariant</c>, dev tools, every peer for what it can see): logs every match state
    /// transition, checks <see cref="RoundInvariants"/> 0.5 s after a round goes Active (round start) and when it Ends, and
    /// every 2 s of simulation prints a <c>World</c> line with order-independent hashes of where the static objects
    /// (bases, piles, zones) and the chickens stand, keyed by the same tick boundary on every peer so the merge can
    /// compare them (a client with its bases in the wrong place - CW-216 - shows a different <c>static</c> hash at once).
    /// </summary>
    /// <remarks>
    /// Lines: <c>Invariant state A->B round=k</c> (Info), <c>Invariant FAIL &lt;name&gt; expected=.. actual=.. who=..</c>
    /// (Error), <c>Invariant ok round=k n=..</c> (Info), <c>World t=.. static=XXXXXXXX n=.. chickens=XXXXXXXX m=..</c> (Info).
    /// Round-start lines (ok and FAIL) end with <c> zn=.. au=.. dc=..</c>: live placed (World) zones, live auras
    /// (Caster-anchored zones, WP4) and Doppelganger decoys, all of which must be 0 (B10, <see cref="RoundInvariants.StartCounts"/>).
    /// While a Slow aura lives, every peer also samples each rival inside it (<see cref="AuraVictimSampler"/>): the
    /// victim's replicated <c>ControlVfx.Snared</c> must be on (WP4, the CW-217 lesson - state on a REMOTE peer, not logs).
    /// Read-only; it never changes gameplay and never throws (an exception inside a check is logged as an Error).
    /// </remarks>
    public sealed class InvariantProbe : MonoBehaviour
    {
        private const string Source = "Invariant";
        private const float StartCheckDelaySeconds = 0.5f;
        private const int WorldPeriodSeconds = 2;

        private ILogService _log;
        private bool _haveState;
        private MatchState _state;
        private int _round;
        private float _startCheckAt = -1f;
        private MapGenerator _map;
        private readonly TickBoundary _worldBoundary = new TickBoundary();

        private readonly List<InvariantChicken> _chickens = new List<InvariantChicken>(16);
        private readonly List<(int corner, float food)> _bases = new List<(int, float)>(4);
        private readonly List<(float amount, float initial)> _piles = new List<(float, float)>(16);
        private readonly List<InvariantFailure> _fails = new List<InvariantFailure>(16);
        private readonly List<(string type, Vector2 position)> _basesW = new List<(string, Vector2)>(8);
        private readonly List<(string type, Vector2 position)> _pilesW = new List<(string, Vector2)>(32);
        private readonly List<(string type, Vector2 position)> _zones = new List<(string, Vector2)>(16);
        private readonly List<(string type, Vector2 position)> _moving = new List<(string, Vector2)>(16);
        private readonly List<RoundInvariants.WorldZone> _zoneScan = new List<RoundInvariants.WorldZone>(16);
        private readonly List<(uint owner, int radiusBucket, byte effect, byte channels)> _auras =
            new List<(uint, int, byte, byte)>(8);

        // WP4 aura sampling (local to this peer): per live Caster zone, and per (zone, rival) that was ever deep inside it.
        private struct AuraWatch { public string Owner; public bool Unresolved; public float UnresolvedSince; public bool OrphanReported; }
        private struct AuraVictim { public string Victim; public bool Remote; public RoundInvariants.AuraVictimTally Tally; }
        private readonly Dictionary<uint, AuraWatch> _auraWatch = new Dictionary<uint, AuraWatch>(8);
        private readonly Dictionary<(uint zone, uint victim), AuraVictim> _auraVictims = new Dictionary<(uint, uint), AuraVictim>(16);
        private readonly HashSet<uint> _auraSeen = new HashSet<uint>();
        private readonly List<uint> _auraGone = new List<uint>(4);
        private readonly List<(uint zone, uint victim)> _victimKeys = new List<(uint, uint)>(16);

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.Object == null || !gm.Object.IsValid || gm.Runner == null || !gm.Runner.IsRunning)
            {
                _haveState = false;
                _round = 0;
                _startCheckAt = -1f;
                _worldBoundary.Reset();
                _auraWatch.Clear();     // a left session judges nothing: its zones are gone with it
                _auraVictims.Clear();
                return;
            }

            try
            {
                Observe(gm);
                World(gm);
                Auras(gm.Runner);
            }
            catch (Exception e)
            {
                // A probe must never take the game down; say what broke instead.
                _log?.Error(Source, $"Invariant probe failed ({e.GetType().Name}: {e.Message}); continuing.");
            }
        }

        private void Observe(GameManager gm)
        {
            var st = gm.State;
            if (!_haveState || st != _state)
            {
                // round = the ordinal of the match this transition belongs to, counted here the way "Match started" is
                // logged (one per Starting, or per Active reached without a Starting: a late join), not gm.StartRound.
                _round = RoundOrdinal.Next(_round, _haveState, _haveState ? _state : MatchState.WaitingForPlayers, st);
                if (_haveState) _log?.Info(Source, $"Invariant state {_state}->{st} round={_round}");
                _haveState = true;
                _state = st;
                if (st == MatchState.Active) _startCheckAt = Time.realtimeSinceStartup + StartCheckDelaySeconds;
                if (st == MatchState.Ended) CheckEnd(gm);
            }
            if (_startCheckAt >= 0f && Time.realtimeSinceStartup >= _startCheckAt)
            {
                _startCheckAt = -1f;
                if (gm.State == MatchState.Active) CheckStart(gm);
            }
        }

        private void CheckStart(GameManager gm)
        {
            _chickens.Clear();
            var all = ChickenController.ActiveControllers;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy) continue;
                float cd = 0f;
                int active = 0;
                var a = c.Abilities;
                if (a != null)
                    for (int s = 0; s < AbilityController.SlotCount; s++)
                    {
                        cd = Mathf.Max(cd, a.CooldownRemaining(s));
                        if (a.IsSlotActive(s)) active++;
                    }
                var p = c.transform.position;
                _chickens.Add(new InvariantChicken
                {
                    Id = Label(c),
                    Home = c.HomeCornerIndex,
                    Position = new Vector2(p.x, p.z),
                    Cargo = c.Cargo != null ? c.Cargo.Cargo : 0f,
                    MaxCooldown = cd,
                    ControlActive = c.CurrentControlState != ControlState.Free,
                    ActiveSlots = active,
                    IsBot = c.IsBot,
                });
            }
            FillBases();
            _piles.Clear();
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                _piles.Add((p.Amount, p.MaxAmount));
            }
            if (_map == null) _map = FindFirstObjectByType<MapGenerator>();

            // B10 leftovers: what the previous round placed must be gone (the check runs during the intro: nobody can cast).
            int zones = 0, auras = 0, decoys = 0;
            var live = AbilityZone.ActiveZones;
            // A Caster-anchored zone is an aura (the only kind since WP4 deleted the ChickenController triplet).
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].Object != null && live[i].Object.IsValid)
                {
                    if (live[i].Anchor == ZoneAnchor.Caster) auras++;
                    else zones++;
                }
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (c.IsDecoy) decoys++;
            }

            _fails.Clear();
            int n = RoundInvariants.CheckRoundStart(_chickens, _map != null ? _map.SpawnPoints : null, _bases, _piles,
                gm.TimeRemaining, gm.MatchDurationSeconds, gm.Runner.GameMode == GameMode.Single, _fails, zones, auras, decoys);
            Report("start", n, RoundInvariants.StartCounts(zones, auras, decoys));
        }

        private void CheckEnd(GameManager gm)
        {
            FillBases();
            _fails.Clear();
            int n = RoundInvariants.CheckRoundEnd(gm.WinnerCorner, _bases, _fails);
            int carrying = 0;
            var cargos = ChickenController.ActiveControllers;
            for (int i = 0; i < cargos.Count; i++)
            {
                var c = cargos[i];
                if (c != null && c.Object != null && c.Object.IsValid && !c.IsDecoy && c.Cargo != null && c.Cargo.Cargo > 0.01f) carrying++;
            }
            if (carrying > 0) _log?.Info(Source, $"Invariant note round={_round}: {carrying} chicken(s) still carrying at the end (allowed).");
            Report("end", n);
        }

        private void FillBases()
        {
            _bases.Clear();
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.Object == null || !b.Object.IsValid) continue;
                _bases.Add((b.CornerIndex, b.FoodTotal));
            }
        }

        private void Report(string phase, int checks, string counts = "")
        {
            for (int i = 0; i < _fails.Count; i++) _log?.Error(Source, _fails[i].Line() + $" phase={phase} round={_round}" + counts);
            if (_fails.Count == 0) _log?.Info(Source, $"Invariant ok round={_round} phase={phase} n={checks}" + counts);
        }

        private void World(GameManager gm)
        {
            var runner = gm.Runner;
            if (_log == null || !_log.IsEnabled(LogLevel.Info)) return;
            if (!_worldBoundary.TryCross((int)runner.Tick, Mathf.RoundToInt(WorldPeriodSeconds * runner.TickRate), out int t)) return;

            _basesW.Clear();
            _pilesW.Clear();
            _zones.Clear();
            _moving.Clear();
            _zoneScan.Clear();
            _auras.Clear();
            var bases = PlayerBase.ActiveBases;
            for (int i = 0; i < bases.Count; i++)
                if (bases[i] != null && bases[i].Object != null && bases[i].Object.IsValid) _basesW.Add(("PlayerBase", Xz(bases[i].transform)));
            var piles = FoodPile.ActivePiles;
            for (int i = 0; i < piles.Count; i++)
                if (piles[i] != null && piles[i].Object != null && piles[i].Object.IsValid) _pilesW.Add(("FoodPile", Xz(piles[i].transform)));
            // WP3: World zones stay in zones/static (by their transform, so a mis-placed visual is still caught, CW-217);
            // Caster zones follow a chicken that peers see ~3 m apart, so they hash by identity into auras= instead.
            var zones = AbilityZone.ActiveZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z == null || z.Object == null || !z.Object.IsValid) continue;
                var owner = z.OwnerChicken;
                _zoneScan.Add(new RoundInvariants.WorldZone
                {
                    CasterAnchored = z.Anchor == ZoneAnchor.Caster,
                    Position = Xz(z.transform),
                    OwnerRaw = unchecked(owner.Object.Raw * 16777619u ^ (uint)owner.Behaviour),
                    Radius = z.TriggerRadius,
                    Effect = (byte)z.Effect,
                    Channels = (byte)z.Channels,
                });
            }
            RoundInvariants.SplitZones(_zoneScan, _zones, _auras);
            var chickens = ChickenController.ActiveControllers;
            for (int i = 0; i < chickens.Count; i++)
                if (chickens[i] != null && chickens[i].Object != null && chickens[i].Object.IsValid) _moving.Add(("Chicken", Xz(chickens[i].transform)));

            // Per type, so a differing static hash can be attributed (CW-217); static = their sum, as before.
            uint hb = RoundInvariants.WorldHash(_basesW, out int nb);
            uint hp = RoundInvariants.WorldHash(_pilesW, out int np);
            uint hz = RoundInvariants.WorldHash(_zones, out int nz);
            uint hc = RoundInvariants.WorldHash(_moving, out int nc);
            uint ha = RoundInvariants.AuraHash(_auras, out int na);
            uint hs = unchecked(hb + hp + hz);
            _log.Info(Source, $"World t={t} static={hs:X8} n={nb + np + nz} bases={hb:X8}/{nb} piles={hp:X8}/{np} zones={hz:X8}/{nz} " +
                $"auras={ha:X8}/{na} chickens={hc:X8} m={nc} round={_round}");
        }

        private static Vector2 Xz(Transform t)
        {
            var p = t.position;
            return new Vector2(p.x, p.z);
        }

        /// <summary>The probe's chicken id: <c>B&lt;corner&gt;</c> for a bot, <c>P&lt;PlayerId&gt;</c> for a player.</summary>
        private static string Label(ChickenController c) =>
            c.IsBot ? "B" + c.HomeCornerIndex : "P" + c.Object.InputAuthority.PlayerId;

        /// <summary>
        /// WP4, every frame on every peer: for each live Slow aura (Caster zone), every rival deep inside it is sampled for
        /// its replicated <c>ControlVfx.Snared</c> (<see cref="RoundInvariants.SampleAuraVictim"/>); when the aura is gone the
        /// verdict per victim is logged (<see cref="RoundInvariants.AuraVictimLine"/>), and a <c>not-snared</c> one as an
        /// <c>Invariant FAIL aura-victim-snared</c> Error. A Caster zone whose owner does not resolve here for
        /// <see cref="RoundInvariants.AuraOrphanSeconds"/> is an <c>aura-orphan</c> FAIL (a bot's aura must go with its bot
        /// on hostleave; the authority despawns it the tick its owner is gone, B5).
        /// </summary>
        private void Auras(NetworkRunner runner)
        {
            float now = Time.realtimeSinceStartup;
            _auraSeen.Clear();
            var zones = AbilityZone.ActiveZones;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z == null || z.Object == null || !z.Object.IsValid || z.Anchor != ZoneAnchor.Caster) continue;
                uint id = z.Object.Id.Raw;
                _auraSeen.Add(id);

                _auraWatch.TryGetValue(id, out var w);
                bool resolved = z.TryGetCenter(out Vector3 center);
                if (resolved)
                {
                    w.Unresolved = false;
                    if (w.Owner == null && runner.TryFindBehaviour(z.OwnerChicken, out ChickenController owner) && owner != null &&
                        owner.Object != null && owner.Object.IsValid)
                        w.Owner = Label(owner);
                }
                else if (!w.Unresolved) { w.Unresolved = true; w.UnresolvedSince = now; }
                else if (!w.OrphanReported && now - w.UnresolvedSince > RoundInvariants.AuraOrphanSeconds)
                {
                    w.OrphanReported = true;
                    string waited = (now - w.UnresolvedSince).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    _log?.Error(Source, new InvariantFailure("aura-orphan", "owner resolves", "unresolved for " + waited + "s",
                        "aura" + id).Line() + " owner=" + (w.Owner ?? "?") + " round=" + _round);
                }
                _auraWatch[id] = w;

                if (!resolved || z.Effect != ZoneEffect.Slow || !z.HasChannel(AreaEffectChannel.WhileInside)) continue;
                float radius = z.TriggerRadius;
                var all = ChickenController.ActiveControllers;
                for (int k = 0; k < all.Count; k++)
                {
                    var c = all[k];
                    if (c == null || c.Object == null || !c.Object.IsValid || c.IsDecoy || c.Id == z.OwnerChicken) continue;
                    if (c.Combat != null && c.Combat.IsRemoved) continue;
                    var p = c.transform.position;
                    float dx = p.x - center.x, dz = p.z - center.z;
                    bool deep = RoundInvariants.IsDeepInsideAura(Mathf.Sqrt(dx * dx + dz * dz), radius);
                    var key = (id, c.Object.Id.Raw);
                    bool known = _auraVictims.TryGetValue(key, out var v);
                    if (!known && !deep) continue;   // never deep inside: no record
                    if (!known)
                    {
                        v.Victim = Label(c);
                        v.Remote = c.Object.StateAuthority != z.Object.StateAuthority;
                    }
                    v.Tally = RoundInvariants.SampleAuraVictim(v.Tally, deep, (c.ControlFlags & ControlVfx.Snared) != 0, now);
                    _auraVictims[key] = v;
                }
            }

            _auraGone.Clear();
            foreach (var id in _auraWatch.Keys) if (!_auraSeen.Contains(id)) _auraGone.Add(id);
            for (int i = 0; i < _auraGone.Count; i++) FinishAura(_auraGone[i]);
        }

        /// <summary>The aura is gone on this peer: one verdict line per victim that was ever deep inside it.</summary>
        private void FinishAura(uint zoneId)
        {
            string owner = _auraWatch.TryGetValue(zoneId, out var w) && w.Owner != null ? w.Owner : "?";
            _auraWatch.Remove(zoneId);
            _victimKeys.Clear();
            foreach (var key in _auraVictims.Keys) if (key.zone == zoneId) _victimKeys.Add(key);
            for (int i = 0; i < _victimKeys.Count; i++)
            {
                var v = _auraVictims[_victimKeys[i]];
                _auraVictims.Remove(_victimKeys[i]);
                _log?.Info(Source, RoundInvariants.AuraVictimLine(owner, v.Victim, v.Remote, v.Tally, _round));
                if (RoundInvariants.JudgeAuraVictim(v.Tally) == RoundInvariants.AuraVerdict.NotSnared)
                    _log?.Error(Source, new InvariantFailure("aura-victim-snared",
                        ">=" + Mathf.RoundToInt(RoundInvariants.AuraMinSnaredShare * 100f) + "%",
                        v.Tally.Snared + "/" + v.Tally.Deep, v.Victim).Line() +
                        " owner=" + owner + " remote=" + (v.Remote ? 1 : 0) + " round=" + _round);
            }
        }
    }

    /// <summary>The match ordinal the Invariant probe labels its lines with (pure, tested).</summary>
    public static class RoundOrdinal
    {
        /// <summary>
        /// A new match begins on every entry into Starting, or on reaching Active / Ended without a Starting seen first (a
        /// late join, or a probe that attached mid-round). Ended -> WaitingForPlayers (PLAY AGAIN) still belongs to the
        /// match that just ended, so the label always matches the count of "Match started" lines.
        /// </summary>
        public static int Next(int current, bool hadState, MatchState from, MatchState to)
        {
            if (to == MatchState.Starting) return current + 1;
            if ((to == MatchState.Active || to == MatchState.Ended) && (!hadState || current == 0 ||
                (from != MatchState.Starting && from != MatchState.Active))) return current + 1;
            return current;
        }
    }
}
