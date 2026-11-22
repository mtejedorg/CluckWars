using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    public enum MatchEventKind : byte { Contact, CastStart, PeckCut, Steal, Kill, Bank }

    /// <summary>Why a running Peck was cut. <see cref="OwnMove"/> is the chicken's own doing (it cast another ability).</summary>
    public enum CutCause : byte { None, Stun, Shove, Execution, EmptyPile, OwnMove }

    /// <summary>One thing that happened, for statistics, debugging and the comparison with the real game's telemetry.</summary>
    public struct MatchEvent
    {
        public float Time;
        public MatchEventKind Kind;
        public int Seat;
        /// <summary>The other seat; for a <see cref="MatchEventKind.CastStart"/> of a Peck, the pile index; -1 for a <c>CastStart</c> of an ability (its slot is in <see cref="Amount"/>) and when there is none.</summary>
        public int Other;
        public float Amount;
        public CutCause Cause;

        /// <summary>The cause as the game's telemetry spells it (<see cref="MatchTelemetry"/>), so simulated and real runs compare by name.</summary>
        public static string CauseName(CutCause cause)
        {
            switch (cause)
            {
                case CutCause.Stun: return MatchTelemetry.CauseStun;
                case CutCause.Shove: return MatchTelemetry.CauseShove;
                case CutCause.Execution: return MatchTelemetry.CauseExecution;
                case CutCause.EmptyPile: return MatchTelemetry.CauseEmptyPile;
                case CutCause.OwnMove: return "own-move";
                default: return "none";
            }
        }
    }

    /// <summary>Everything one simulated match needs. The arrays have four entries (index = seat); a null kit is an empty seat.</summary>
    public sealed class MatchSetup
    {
        public OracleMapSO Map;
        public OracleNav Nav;
        public OracleFood Food;
        public MatchRules Rules;
        public MatchKit[] Seats;
        public PolicyFactory[] Policies;
        /// <summary>Per seat; null means <see cref="PolicyParams.Default"/> for every seat.</summary>
        public PolicyParams[] Params;
        public bool RecordEvents;
    }

    public sealed class MatchResult
    {
        /// <summary>The winning seat, or -1 when no seat played.</summary>
        public int Winner;
        public float[] Banked;
        public int[] Kills;
        public float EndSeconds;
        public bool ReachedTarget;
        /// <summary>The clock ended with banked food and kills level and the lowest corner won (the game's last tie-break).</summary>
        public bool CornerDecided;
        /// <summary>Intents a policy asked for that the rules refuse (a cast no seat can make, an unknown pile). Zero for a sound policy.</summary>
        public int RejectedIntents;
        /// <summary>One entry per seat (an empty seat has zeros).</summary>
        public SeatCounters[] Counters;
        /// <summary>The comeback event that fired, or <c>None</c> when it did not (the match ended first, or the event is off).</summary>
        public CluckWars.Gameplay.MatchEventKind Comeback;
        public float ComebackTime;
        /// <summary>Where the Golden Pile stands when <see cref="Comeback"/> is <c>GoldenPile</c>.</summary>
        public Vector2 GoldenSpot;
        /// <summary>Null unless <see cref="MatchSetup.RecordEvents"/> was set.</summary>
        public List<MatchEvent> Events;
    }

    /// <summary>Reach geometry shared by the simulator and its policies: the same rim rule the map bake uses for its legs.</summary>
    public static class MatchGeometry
    {
        private const float Slack = 1e-3f;

        /// <summary>The place index a policy uses to peck the comeback event's Golden Pile (one past the 13 baked places).</summary>
        public const int GoldenPile = OracleMapSO.PlaceCount;

        /// <summary>
        /// Distance from a place's centre at which a chicken is in reach, looking along <paramref name="toward"/> (centre to chicken):
        /// the base deposit radius, or the pile's oval plus <see cref="OracleMapSO.CollectReach"/>.
        /// </summary>
        public static float Rim(OracleMapSO map, int place, Vector2 toward) => Rim(map, map.Places[place], toward);

        /// <summary><see cref="Rim(OracleMapSO, int, Vector2)"/> for a place that is not one of the baked 13 (the Golden Pile).</summary>
        public static float Rim(OracleMapSO map, in OraclePlace p, Vector2 toward)
        {
            if (p.Kind == OraclePlaceKind.Base) return map.BaseRadius;
            return PileFootprintShape.RadiusToward(toward, p.Yaw, p.Footprint) + map.CollectReach;
        }

        /// <summary>True when <paramref name="pos"/> is inside the place's reach (a hair of slack, so a chicken that stopped at the rim counts).</summary>
        public static bool InReach(OracleMapSO map, int place, Vector2 pos) => InReach(map, map.Places[place], pos);

        /// <summary><see cref="InReach(OracleMapSO, int, Vector2)"/> for a place that is not one of the baked 13 (the Golden Pile).</summary>
        public static bool InReach(OracleMapSO map, in OraclePlace p, Vector2 pos)
        {
            Vector2 d = pos - p.Pos;
            float sq = d.sqrMagnitude;
            if (p.Kind == OraclePlaceKind.Base)
            {
                float r = map.BaseRadius + Slack;
                return sq <= r * r;
            }
            float widest = Mathf.Max(p.Footprint.x, p.Footprint.y) * 0.5f + map.CollectReach + Slack;
            if (sq > widest * widest) return false;
            float rim = Rim(map, p, d) + Slack;
            return sq <= rim * rim;
        }
    }

    /// <summary>
    /// One four-player match in the abstract model: fixed ticks, continuous positions on the wall-aware graph of <see cref="OracleNav"/>,
    /// one <see cref="IMatchPolicy"/> per seat. Pure and deterministic for a seed. Spec 2.1 to 3.2.
    /// </summary>
    /// <remarks>
    /// Order inside a tick: (0) every observer looks at every rival through the game's bot sight rule (<see cref="MatchSight"/>), the
    /// comeback event fires when its time has come and first contact is noted; (1) completions, seats in rotating order: a Peck whose
    /// time is up (or whose pile ran dry) lands or is cut, a chicken in its base zone deposits, and a seat that just finished something
    /// is marked to think; (2) every marked or due seat thinks on a snapshot that shows it what it may see; (3) due intents take effect,
    /// then every cast of the tick resolves at once against the state the tick began with (two stuns on one tick both land), then the
    /// slow auras act; (4) each chicken starts a Peck when it may, is pushed, and walks.
    /// A seat thinks every <see cref="MatchRules.ThinkSeconds"/> and at once when it arrived, a cast landed or was cut, a deposit ended,
    /// its target pile emptied, a stun ended, a rival hit it, or its Peck is ready to start. An intent takes effect
    /// <see cref="PolicyParams.ReactionDelay"/> later. Food leaves a pile only when a Peck lands, in rotating seat order.
    /// Pile slow: a chicken in reach of any non-empty pile walks at <see cref="FarmRules.PileSlowFactor"/> unless its kit ignores it.
    /// A kit with a Jump ability walks straight lines and spends a Jump to cross a wall (see <c>Move</c>); a kit that only has the
    /// <c>Jumps</c> flag (a part 1 farmer) crosses walls for free, as part 1's jump table does.
    /// Not modelled: body collision, a chicken's own base keep-out as a movement limit (the walk graph ignores rival bases), the
    /// Death Mark kill (slice S4).
    /// </remarks>
    public static class MatchSim
    {
        private const int Seats = OracleMapSO.SeatCount;
        private const int Slots = 4;
        private const float Eps = 1e-4f;
        private const float StandSlack = 1e-3f;
        /// <summary>The game counts a push as a shove (and so cuts a Peck) only above this impulse (<c>ChickenController</c>: <c>impulse.sqrMagnitude &gt; 1f</c>).</summary>
        private const float ShoveCounts = 1f;
        /// <summary>The game applies a push while its velocity squared is above this (<c>ChickenMovement.Tick</c>).</summary>
        private const float PushFloor = 0.001f;

        public static MatchResult Run(MatchSetup setup, ulong matchSeed)
        {
            Validate(setup);
            return new Core(setup, matchSeed).Run();
        }

        private static void Validate(MatchSetup s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Map == null || s.Map.Places == null || s.Map.Places.Length != OracleMapSO.PlaceCount)
                throw new ArgumentException("MatchSetup.Map needs the 13 baked places.", nameof(s));
            if (s.Nav == null) throw new ArgumentException("MatchSetup.Nav is required (OracleNav.For).", nameof(s));
            if (s.Seats == null || s.Seats.Length != Seats) throw new ArgumentException($"MatchSetup.Seats needs exactly {Seats} entries.", nameof(s));
            if (s.Policies == null || s.Policies.Length != Seats) throw new ArgumentException($"MatchSetup.Policies needs exactly {Seats} entries.", nameof(s));
            if (s.Params != null && s.Params.Length != Seats) throw new ArgumentException($"MatchSetup.Params needs {Seats} entries or null.", nameof(s));
            for (int i = 0; i < Seats; i++)
            {
                var kit = s.Seats[i];
                if (kit == null) continue;
                if (s.Policies[i] == null) throw new ArgumentException($"Seat {i} ({kit.Label}) has a kit but no policy factory.", nameof(s));
                if (kit.Abilities == null) continue;
                if (kit.Abilities.Length > Slots) throw new ArgumentException($"Seat {i} ({kit.Label}) has {kit.Abilities.Length} abilities; the simulator has {Slots} slots.", nameof(s));
                foreach (var a in kit.Abilities)
                    if (a.Job == AbilityJob.SlowArea && !a.AreaAura)
                        throw new InvalidOperationException($"{kit.Label}: '{a.Asset}' places a zone; only the aura form of Slow Area is modelled.");
            }
        }

        /// <summary>
        /// Where a chicken starts: on the edge of its base zone, on the side facing the arena centre. Part 1's legs are measured from the
        /// zone's rim, so a chicken that starts on the rim walks the first leg the bake says; starting at the base centre would add the
        /// zone radius (a quarter second) to every first trip. The one-time difference from the game, where the chicken spawns at the centre, is the bake's own.
        /// </summary>
        public static Vector2 Spawn(OracleMapSO map, int seat)
        {
            Vector2 basePos = map.Places[OracleMapSO.BaseIndex(seat)].Pos;
            Vector2 toCentre = -basePos;
            return toCentre.sqrMagnitude < 1e-6f ? basePos : basePos + toCentre.normalized * map.BaseRadius;
        }

        private struct Pending
        {
            public MatchIntent Intent;
            public int ApplyTick;
        }

        private struct CastRequest
        {
            public int Slot;
            public int Target;
        }

        private sealed class Chick
        {
            public bool Present;
            public Vector2 Pos;
            public float Cargo;
            public float Bag;
            public float Banked;
            public float Visit;
            public bool Pecking;
            public int PeckPile;
            public float PeckDoneAt;
            public float NextPeckAt;
            public int ExposedMask;
            public bool HasIntent;
            public MatchIntent Intent;
            public bool Arrived;
            public bool EmptyNoted;
            public bool ThinkNow = true;
            public int NextThink;
            public bool Thought;
            public Vector2 Waypoint;
            public bool WaypointValid;
            public float BurstUntil;
            public float BurstReadyAt;
            public readonly Queue<Pending> Queue = new Queue<Pending>();
            public readonly List<CastRequest> Casts = new List<CastRequest>(2);

            // abilities
            public MatchAbility[] Abilities = Array.Empty<MatchAbility>();
            public float[] AbReady = Array.Empty<float>();
            public float[] AuraUntil = Array.Empty<float>();
            public float SpeedUntil;
            public float SpeedMult = 1f;
            public bool Dashing;
            public float DashUntil;
            public float DashMult = 1f;
            public float HideUntil;
            public float HideOpacity = 1f;
            public bool HideEndsOnMove;
            public float ShieldWalkUntil;
            public float EggUntil;
            public float ShieldWalkMult = 1f;

            // control
            public float StunUntil;
            public bool WasStunned;
            public float SlowUntil;
            public float SlowFactor = 1f;
            public Vector2 Push;
            public bool Surge;
        }

        private sealed class Core
        {
            private readonly MatchSetup _s;
            private readonly OracleMapSO _map;
            private readonly OracleNav _nav;
            private readonly MatchRules _rules;
            private readonly FarmRules _farm;
            private readonly float _tick;
            private readonly float _clock;
            private readonly float _slow;
            private readonly int _thinkTicks;
            private readonly int[] _delayTicks = new int[Seats];
            private readonly IMatchPolicy[] _policy = new IMatchPolicy[Seats];
            private readonly MatchRng[] _rng = new MatchRng[Seats];
            private MatchRng _comebackRng;
            private readonly Chick[] _c = new Chick[Seats];
            private readonly float[] _pile = new float[OracleMapSO.PileCount];
            private readonly float[] _pileMax = new float[OracleMapSO.PileCount];
            private bool _goldenActive;
            private float _goldenFood;
            private OraclePlace _golden;
            private readonly MatchView _view;
            private readonly MatchResult _result = new MatchResult();
            private readonly RivalSighting?[] _sight = new RivalSighting?[Seats * Seats];
            private readonly Vector2[] _baseCentres;
            private readonly float _contactRadius;
            private readonly bool[] _egg = new bool[Seats];
            private readonly bool[] _walk = new bool[Seats];
            private readonly List<(int seat, CastRequest req)> _valid = new List<(int, CastRequest)>(Seats);
            private float _time;
            private int _i;
            private int _winner = -1;
            private float _winTime;
            private bool _contactSeen;
            private CluckWars.Gameplay.MatchEventKind _activeEvent = CluckWars.Gameplay.MatchEventKind.None;

            public Core(MatchSetup s, ulong matchSeed)
            {
                _s = s;
                _map = s.Map;
                _nav = s.Nav;
                _rules = s.Rules;
                _farm = s.Rules.Farm;
                _tick = _farm.TickSeconds > 0f ? _farm.TickSeconds : FarmRules.DefaultTickSeconds;
                _clock = _farm.ClockSeconds;
                _slow = _farm.PileSlowFactor > 0f ? _farm.PileSlowFactor : 1f;
                float think = s.Rules.ThinkSeconds > 0f ? s.Rules.ThinkSeconds : MatchRules.DefaultThinkSeconds;
                _thinkTicks = Math.Max(1, Mathf.RoundToInt(think / _tick));

                _pile[OracleMapSO.CentreIndex] = _pileMax[OracleMapSO.CentreIndex] = s.Food.Centre;
                for (int seat = 0; seat < Seats; seat++)
                {
                    _pile[OracleMapSO.PersonalIndex(seat)] = _pileMax[OracleMapSO.PersonalIndex(seat)] = s.Food.Personal;
                    _pile[OracleMapSO.ContestedIndex(seat)] = _pileMax[OracleMapSO.ContestedIndex(seat)] = s.Food.Contested;
                }

                _view = new MatchView
                {
                    Seats = new SeatView[Seats],
                    PileFood = _pile,
                    Kits = s.Seats,
                    Map = s.Map,
                    Nav = s.Nav,
                    Rules = s.Rules,
                };
                _result.Banked = new float[Seats];
                _result.Kills = new int[Seats];
                _result.Winner = -1;
                _result.Counters = new SeatCounters[Seats];
                int causes = Enum.GetValues(typeof(CutCause)).Length;
                var centres = new List<Vector2>(Seats);
                float contact = float.PositiveInfinity;
                for (int seat = 0; seat < Seats; seat++)
                {
                    var kit = s.Seats[seat];
                    var ch = _c[seat] = new Chick { Present = kit != null };
                    _result.Counters[seat] = new SeatCounters
                    {
                        FirstContact = -1f,
                        StolenFromSeat = new float[Seats],
                        CutsByCause = new int[causes],
                        ExposedByCause = new int[causes],
                        CutWhileExposedByCause = new int[causes],
                    };
                    var abilities = kit != null && kit.Abilities != null ? kit.Abilities : Array.Empty<MatchAbility>();
                    _view.Seats[seat].CooldownLeft = new float[1 + abilities.Length];
                    if (kit == null) continue;

                    ch.Abilities = abilities;
                    ch.AbReady = new float[abilities.Length];
                    ch.AuraUntil = new float[abilities.Length];
                    for (int a = 0; a < abilities.Length; a++)
                    {
                        ch.AbReady[a] = abilities[a].RoundStartCooldown;
                        contact = Math.Min(contact, OffensiveRange(abilities[a]));
                    }
                    centres.Add(s.Map.Places[OracleMapSO.BaseIndex(seat)].Pos);
                    ch.Pos = Spawn(s.Map, seat);
                    var p = s.Params != null ? s.Params[seat] : PolicyParams.Default;
                    float delay = float.IsNaN(p.ReactionDelay) ? 0f : Math.Max(0f, p.ReactionDelay);
                    _delayTicks[seat] = Mathf.RoundToInt(delay / _tick);
                    _policy[seat] = s.Policies[seat](seat, p);
                    if (_policy[seat] == null) throw new InvalidOperationException($"The policy factory of seat {seat} returned null.");
                    _rng[seat] = MatchRng.Create(matchSeed, (ulong)seat);
                }
                _baseCentres = centres.ToArray();
                _contactRadius = float.IsPositiveInfinity(contact) ? 0f : contact;
                _comebackRng = MatchRng.Create(matchSeed, MatchRng.StreamComeback);
                if (s.RecordEvents) _result.Events = new List<MatchEvent>();
            }

            // The smallest reach of an ability that can hurt a rival; 0 or none counts as no reach.
            private static float OffensiveRange(in MatchAbility a)
            {
                float r;
                switch (a.Job)
                {
                    case AbilityJob.Steal: case AbilityJob.Stun: case AbilityJob.Shove: case AbilityJob.Slow: r = a.Radius; break;
                    case AbilityJob.SlowArea: r = a.AreaRadius; break;
                    default: return float.PositiveInfinity;
                }
                return r > 0f ? r : float.PositiveInfinity;
            }

            public MatchResult Run()
            {
                for (_i = 0; ; _i++)
                {
                    _time = _i * _tick;
                    if (_time >= _clock - Eps) break;

                    Look();
                    TickComeback();
                    NoteContact();

                    for (int k = 0; k < Seats; k++) Complete((_i + k) % Seats);
                    if (_winner >= 0) break;

                    for (int k = 0; k < Seats; k++) Think((_i + k) % Seats);
                    for (int k = 0; k < Seats; k++) Due((_i + k) % Seats);
                    ResolveCasts();
                    AuraTick();
                    for (int k = 0; k < Seats; k++) Act((_i + k) % Seats);
                }
                return Finish();
            }

            // ---- state predicates ----

            private bool Stunned(Chick ch) => _time + Eps < ch.StunUntil;
            private bool WalkPhase(Chick ch) => _time + Eps < ch.ShieldWalkUntil;
            private bool EggPhase(Chick ch) => _time + Eps >= ch.ShieldWalkUntil && _time + Eps < ch.EggUntil;
            private bool Hidden(Chick ch) => _time + Eps < ch.HideUntil;
            private float Opacity(Chick ch) => Hidden(ch) ? ch.HideOpacity : 1f;

            private MatchPassive PassiveOf(int s) => _s.Seats[s].Passive;

            // ---- piles (nine baked, plus the Golden Pile) ----

            private float PileFood(int p) => p == MatchGeometry.GoldenPile ? _goldenFood : _pile[p];

            private void TakeFromPile(int p, float amount)
            {
                if (p == MatchGeometry.GoldenPile) _goldenFood -= amount; else _pile[p] -= amount;
            }

            private OraclePlace PlaceOf(int p) => p == MatchGeometry.GoldenPile ? _golden : _map.Places[p];

            // ---- phase 0: looking, comeback, contact ----

            private void Look()
            {
                for (int obs = 0; obs < Seats; obs++)
                {
                    if (!_c[obs].Present) continue;
                    for (int tgt = 0; tgt < Seats; tgt++)
                    {
                        if (tgt == obs || !_c[tgt].Present) continue;
                        int k = obs * Seats + tgt;
                        _sight[k] = MatchSight.Observe(_sight[k], _c[tgt].Pos, Opacity(_c[tgt]), _c[obs].Pos, _time, _rules.SightArrivalRadius);
                    }
                }
            }

            private void TickComeback()
            {
                var cfg = _rules.Comeback;
                if (!ComebackEventTiming.ShouldFire(MatchState.Active, false, _clock - _time, cfg.SecondsLeft, _activeEvent)) return;

                _activeEvent = ComebackRules.EventForRoll(_comebackRng.NextInt(ComebackRules.Events.Length));
                _result.Comeback = _activeEvent;
                _result.ComebackTime = _time;
                switch (_activeEvent)
                {
                    case CluckWars.Gameplay.MatchEventKind.GoldenPile:
                    {
                        float angle = _comebackRng.NextFloat() * 360f * Mathf.Deg2Rad;
                        float radius = Mathf.Lerp(cfg.GoldenRingMin, cfg.GoldenRingMax, _comebackRng.NextFloat());
                        var centre = _map.Places[OracleMapSO.CentreIndex];
                        _golden = new OraclePlace
                        {
                            Kind = OraclePlaceKind.Centre, Seat = -1,
                            Pos = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius),
                            Footprint = centre.Footprint, Yaw = centre.Yaw,   // the same prefab as every pile
                        };
                        _goldenFood = cfg.GoldenPileFood;
                        _goldenActive = true;
                        _result.GoldenSpot = _golden.Pos;
                        break;
                    }
                    case CluckWars.Gameplay.MatchEventKind.UnderdogSurge:
                    {
                        int under = -1;
                        for (int s = 0; s < Seats; s++)
                            if (_c[s].Present && (under < 0 || _c[s].Banked < _c[under].Banked)) under = s;
                        if (under >= 0) _c[under].Surge = true;
                        break;
                    }
                    case CluckWars.Gameplay.MatchEventKind.Restock:
                        for (int p = 0; p < _pile.Length; p++) _pile[p] = Math.Min(_pileMax[p], _pile[p] + cfg.RestockFood);
                        break;
                }
                for (int s = 0; s < Seats; s++) if (_c[s].Present) _c[s].ThinkNow = true;
            }

            private void NoteContact()
            {
                if (_contactSeen || _contactRadius <= 0f) return;
                for (int a = 0; a < Seats; a++)
                {
                    if (!_c[a].Present) continue;
                    for (int b = a + 1; b < Seats; b++)
                    {
                        if (!_c[b].Present || !MatchCombat.InRange(_c[a].Pos, _c[b].Pos, _contactRadius)) continue;
                        _contactSeen = true;
                        _result.Counters[a].FirstContact = _result.Counters[b].FirstContact = _time;
                        Record(MatchEventKind.Contact, a, b, 0f, CutCause.None);
                        return;
                    }
                }
            }

            // ---- phase 1: completions ----

            private void Complete(int s)
            {
                var ch = _c[s];
                if (!ch.Present) return;
                var kit = _s.Seats[s].Farm;

                if (ch.WasStunned && !Stunned(ch)) { ch.WasStunned = false; ch.ThinkNow = true; }

                if (ch.Pecking)
                {
                    bool gone = PileFood(ch.PeckPile) <= Eps;
                    if (PeckCastRules.IsCut(false, false, gone)) Cut(s, CutCause.EmptyPile);
                    else if (_time + Eps >= ch.PeckDoneAt) Land(s);
                }

                if (ch.Cargo > Eps && kit.DepositRate > 0f && MatchGeometry.InReach(_map, OracleMapSO.BaseIndex(s), ch.Pos)) Deposit(s, kit);

                if (!ch.HasIntent || ch.Intent.PeckPile < 0) return;
                int pile = ch.Intent.PeckPile;
                if (PileFood(pile) <= Eps)
                {
                    if (!ch.EmptyNoted) { ch.EmptyNoted = true; ch.ThinkNow = true; }
                }
                else if (CanStartPeck(s)) ch.ThinkNow = true; // the policy may still call the Peck off
            }

            // A cast ends: the food lands (the cooldown was spent when it began).
            private void Land(int s)
            {
                var ch = _c[s];
                var kit = _s.Seats[s].Farm;
                ch.Pecking = false;
                ch.ThinkNow = true;
                float take = PeckCastRules.LandingTake(kit.PeckAmount, kit.CargoCapacity - ch.Cargo, PileFood(ch.PeckPile));
                if (take <= 0f) return;
                ch.Cargo += take;
                TakeFromPile(ch.PeckPile, take);
                _result.Counters[s].Foraged += take;
            }

            // A running cast is cut: nothing lands, the cooldown stays spent, the chicken thinks again at once.
            private void Cut(int s, CutCause cause)
            {
                var ch = _c[s];
                ch.Pecking = false;
                ch.ThinkNow = true;
                Record(MatchEventKind.PeckCut, s, ch.PeckPile, 0f, cause);
                ref SeatCounters n = ref _result.Counters[s];
                n.CutsByCause[(int)cause]++;
                if ((ch.ExposedMask & (1 << (int)cause)) != 0) n.CutWhileExposedByCause[(int)cause]++;
            }

            private void Deposit(int s, OracleKit kit)
            {
                var ch = _c[s];
                float dep = Math.Min(ch.Cargo, kit.DepositRate * _tick);
                float before = ch.Banked;
                ch.Banked += dep;
                ch.Cargo -= dep;
                ch.Visit += dep;

                float target = _farm.WinTarget;
                if (before < target - Eps && ch.Banked >= target - Eps)
                {
                    // The win lands inside this deposit: time it to the sub tick. Two seats on one tick: the earlier time wins.
                    float at = _time + (target - before) / kit.DepositRate;
                    if (_winner < 0 || at < _winTime - 1e-6f) { _winner = s; _winTime = at; }
                }
                if (ch.Cargo > Eps) return;

                ch.Cargo = 0f;
                ch.ThinkNow = true;
                Record(MatchEventKind.Bank, s, -1, ch.Visit, CutCause.None);
                ch.Visit = 0f;
            }

            private bool CanStartPeck(int s)
            {
                var ch = _c[s];
                if (!ch.HasIntent || ch.Intent.PeckPile < 0 || ch.Pecking) return false;
                // A stunned or egg-shelled chicken cannot cast; a hidden one does not auto-Peck (AbilityController.AnyStealthRunning:
                // the chain would break the stealth), and a dashing one is busy.
                if (Stunned(ch) || EggPhase(ch) || Hidden(ch) || ch.Dashing) return false;
                int pile = ch.Intent.PeckPile;
                var kit = _s.Seats[s].Farm;
                return _time + Eps >= ch.NextPeckAt
                    && PileFood(pile) > Eps
                    && ch.Cargo < kit.CargoCapacity - Eps
                    && MatchGeometry.InReach(_map, PlaceOf(pile), ch.Pos);
            }

            // ---- phase 2: think ----

            // What seat `self` may see: itself as it is, every rival through the sight rule.
            private void BuildView(int self)
            {
                _view.Self = self;
                _view.Time = _time;
                _view.TimeLeft = _clock - _time;
                _view.GoldenActive = _goldenActive;
                _view.GoldenPos = _golden.Pos;
                _view.GoldenFood = _goldenFood;
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    ref SeatView v = ref _view.Seats[s];
                    v.Present = ch.Present;
                    v.Cargo = ch.Cargo;
                    v.Bag = ch.Bag;
                    v.Banked = ch.Banked;
                    v.Pecking = ch.Pecking;
                    v.Stunned = Stunned(ch);
                    v.Removed = false;
                    v.Shelled = EggPhase(ch);
                    if (s == self || !ch.Present)
                    {
                        v.Seen = v.Known = ch.Present;
                        v.Pos = ch.Pos;
                        v.Hidden = false;
                    }
                    else
                    {
                        var sighting = _sight[self * Seats + s].Value;
                        v.Hidden = sighting.Hidden;
                        v.Seen = !sighting.Hidden;
                        v.Known = !sighting.GivenUp;
                        v.Pos = v.Known ? MatchSight.PlanarPosition(sighting) : Vector2.zero;
                    }
                    var cd = v.CooldownLeft;
                    cd[0] = Math.Max(0f, ch.NextPeckAt - _time);
                    for (int a = 0; a < ch.Abilities?.Length; a++) cd[1 + a] = Math.Max(0f, ch.AbReady[a] - _time);
                }
            }

            private void Think(int s)
            {
                var ch = _c[s];
                if (!ch.Present || !(ch.ThinkNow || _i >= ch.NextThink)) return;

                BuildView(s);
                MatchIntent intent = _policy[s].Think(_view, ref _rng[s]);
                ch.Queue.Enqueue(new Pending { Intent = intent, ApplyTick = _i + _delayTicks[s] });
                ch.ThinkNow = false;
                // The first periodic think is staggered by seat so the four do not all think on the same tick.
                ch.NextThink = _i + _thinkTicks + (ch.Thought ? 0 : s * _thinkTicks / Seats);
                ch.Thought = true;
            }

            // ---- phase 3: intents take effect, casts resolve together ----

            private void Due(int s)
            {
                var ch = _c[s];
                if (!ch.Present) return;
                while (ch.Queue.Count > 0 && ch.Queue.Peek().ApplyTick <= _i) Apply(s, ch.Queue.Dequeue().Intent);
            }

            private void Apply(int s, MatchIntent intent)
            {
                var ch = _c[s];
                bool badPile = intent.PeckPile < -1
                    || (intent.PeckPile >= OracleMapSO.PileCount && !(intent.PeckPile == MatchGeometry.GoldenPile && _goldenActive));
                bool badCast = intent.CastSlot >= ch.Abilities.Length || intent.CastSlot < -1
                    || intent.CastTarget < -1 || intent.CastTarget >= Seats
                    || (intent.CastSlot < 0 && intent.CastTarget >= 0);
                if (badPile || badCast || float.IsNaN(intent.Goal.x) || float.IsNaN(intent.Goal.y))
                {
                    _result.RejectedIntents++;
                    return;
                }
                if (intent.CastSlot >= 0) ch.Casts.Add(new CastRequest { Slot = intent.CastSlot, Target = intent.CastTarget });

                // The movement part of the intent; the cast part is a one-off and is not part of what the chicken keeps doing.
                intent.CastSlot = -1;
                intent.CastTarget = -1;
                if (ch.HasIntent && ch.Intent.SameAs(intent)) return;
                ch.Intent = intent;
                ch.HasIntent = true;
                ch.WaypointValid = false;
                ch.Arrived = false;
                ch.EmptyNoted = false;
            }

            private bool CanCast(int s, int slot)
            {
                var ch = _c[s];
                var ab = ch.Abilities[slot];
                if (ab.Job == AbilityJob.Peck) return false;           // pecking is the PeckPile intent
                if (ab.Job == AbilityJob.DeathMark) return false;      // slice S4
                if (_time + Eps < ch.AbReady[slot]) return false;
                return !_stunSnap[s] && !_egg[s];
            }

            private readonly bool[] _stunSnap = new bool[Seats];

            private void ResolveCasts()
            {
                for (int s = 0; s < Seats; s++)
                {
                    _egg[s] = _c[s].Present && EggPhase(_c[s]);
                    _walk[s] = _c[s].Present && WalkPhase(_c[s]);
                    _stunSnap[s] = _c[s].Present && Stunned(_c[s]);
                }

                _valid.Clear();
                for (int k = 0; k < Seats; k++)
                {
                    int s = (_i + k) % Seats;
                    var ch = _c[s];
                    if (!ch.Present) continue;
                    int used = 0;
                    foreach (var req in ch.Casts)
                    {
                        if ((used & (1 << req.Slot)) != 0 || !CanCast(s, req.Slot)) { _result.RejectedIntents++; continue; }
                        used |= 1 << req.Slot;
                        _valid.Add((s, req));
                    }
                    ch.Casts.Clear();
                }
                for (int k = 0; k < _valid.Count; k++) Cast(_valid[k].seat, _valid[k].req);
            }

            // Spends the cooldown, counts the cast, and applies the "own move" rules every cast shares.
            private void BeginCast(int s, int slot)
            {
                var ch = _c[s];
                var ab = ch.Abilities[slot];
                ch.AbReady[slot] = _time + ab.Cooldown;
                _result.Counters[s].Casts++;
                Record(MatchEventKind.CastStart, s, -1, slot, CutCause.None);
                if (ch.Pecking) Cut(s, CutCause.OwnMove);                          // Peck ends on the next move
                if (ab.Job != AbilityJob.Hide && Hidden(ch) && ch.HideEndsOnMove) ch.HideUntil = _time; // using a move ends Hide
            }

            private bool RivalCanBeHit(int caster, int victim, float radius)
            {
                var v = _c[victim];
                return victim != caster && v.Present
                    && MatchCombat.InRange(_c[caster].Pos, v.Pos, radius)
                    && !MatchCombat.KeepOutBlocks(_c[caster].Pos, v.Pos, _baseCentres, _rules.KeepOutRadius);
            }

            private void Cast(int s, CastRequest req)
            {
                var ch = _c[s];
                var ab = ch.Abilities[req.Slot];
                BeginCast(s, req.Slot);
                switch (ab.Job)
                {
                    case AbilityJob.Steal:
                        for (int v = 0; v < Seats; v++)
                        {
                            if (!RivalCanBeHit(s, v, ab.Radius)) continue;
                            var victim = _c[v];
                            if (StealRules.Stealable(victim.Cargo, victim.Bag) <= 0f) continue;     // Snatch only targets a carrier
                            if (MatchCombat.Refused(IncomingEffect.Steal, _egg[v], _walk[v])) continue;
                            DoSteal(s, v, ab);
                            if (ab.Knockback > 0f && !MatchCombat.Refused(IncomingEffect.Shove, _egg[v], _walk[v])) Shove(s, v, ab.Knockback);
                        }
                        break;
                    case AbilityJob.Stun:
                    case AbilityJob.Slow:
                        for (int v = 0; v < Seats; v++)
                        {
                            if (!RivalCanBeHit(s, v, ab.Radius)) continue;
                            Control(s, v, ab.Control);
                        }
                        break;
                    case AbilityJob.Shove:
                        for (int v = 0; v < Seats; v++)
                        {
                            if (!RivalCanBeHit(s, v, ab.Radius)) continue;
                            if (MatchCombat.Refused(IncomingEffect.Shove, _egg[v], _walk[v])) continue;
                            Shove(s, v, ab.Knockback);
                        }
                        break;
                    case AbilityJob.SlowArea:
                        // The run time IS the slow it inflicts, so the caster's outgoing factor lengthens it (ActiveDurationIsOutgoingControl).
                        ch.AuraUntil[req.Slot] = _time + ab.Duration * PassiveOf(s).OutgoingControlFactor;
                        break;
                    case AbilityJob.Speed:
                        ch.SpeedUntil = _time + ab.Duration;
                        ch.SpeedMult = ab.SpeedMultiplier;
                        break;
                    case AbilityJob.Jump:
                        ch.Dashing = true;
                        ch.DashUntil = _time + ab.Duration;
                        ch.DashMult = ab.SpeedMultiplier;
                        ch.WaypointValid = false;
                        break;
                    case AbilityJob.Shield:
                        ch.ShieldWalkUntil = _time + ab.ShieldWalkSeconds;
                        ch.EggUntil = ch.ShieldWalkUntil + ab.ShieldEggSeconds;
                        ch.ShieldWalkMult = ab.ShieldWalkSpeedMultiplier;
                        break;
                    case AbilityJob.Hide:
                        ch.HideUntil = _time + ab.Duration;
                        ch.HideOpacity = ab.HideOpacity;
                        ch.HideEndsOnMove = ab.EndsOnNextMove;
                        break;
                }
            }

            private void DoSteal(int thief, int victim, in MatchAbility ab)
            {
                var t = _c[thief];
                var v = _c[victim];
                float room = _s.Seats[thief].Farm.CargoCapacity - t.Cargo;
                float take = MatchCombat.StealTake(ab.StealAmount, PassiveOf(thief).StealFactor, room, v.Cargo, v.Bag);
                if (take <= 0f) return;
                StealRules.SplitDrain(take, v.Cargo, v.Bag, out float fromCargo, out float fromBag);
                v.Cargo -= fromCargo;
                v.Bag -= fromBag;
                t.Cargo += take;
                v.ThinkNow = t.ThinkNow = true;
                ref SeatCounters tn = ref _result.Counters[thief];
                tn.Stolen += take;
                tn.Steals++;
                tn.StolenFromSeat[victim] += take;
                _result.Counters[victim].LostToSteals += take;
                Record(MatchEventKind.Steal, thief, victim, take, CutCause.None);
            }

            private void Control(int caster, int victim, ControlEmission e)
            {
                var v = _c[victim];
                var victimPassive = PassiveOf(victim);
                if (e.Kind == ControlKind.Stun)
                {
                    if (MatchCombat.Refused(IncomingEffect.Stun, _egg[victim], _walk[victim])) return;
                    float seconds = MatchCombat.ControlSeconds(e, PassiveOf(caster), victimPassive);
                    float remaining = Math.Max(0f, v.StunUntil - _time);
                    if (!ControlReapplyRules.IncomingExtends(remaining, seconds)) return;
                    v.StunUntil = _time + seconds;
                    v.WasStunned = true;
                    v.ThinkNow = true;
                    if (v.Pecking && PeckCastRules.IsCut(true, false, false)) Cut(victim, CutCause.Stun);
                }
                else if (e.Kind == ControlKind.Slow)
                {
                    if (MatchCombat.Refused(IncomingEffect.Slow, _egg[victim], _walk[victim])) return;
                    float seconds = MatchCombat.ControlSeconds(e, PassiveOf(caster), victimPassive);
                    ApplySlow(v, victimPassive.ReceivedSlowFactor(e.SlowFactor), seconds);
                    v.ThinkNow = true;
                }
            }

            private void ApplySlow(Chick v, float factor, float seconds)
            {
                var merged = ControlReapplyRules.MergeSlow(
                    new ControlReapplyRules.TimedSlow(v.SlowFactor, v.SlowUntil),
                    new ControlReapplyRules.TimedSlow(factor, _time + seconds), _time);
                v.SlowFactor = merged.Factor;
                v.SlowUntil = (float)merged.Until;
            }

            private void Shove(int caster, int victim, float knockback)
            {
                var v = _c[victim];
                Vector2 away = v.Pos - _c[caster].Pos;
                away = away.sqrMagnitude < 1e-8f ? Vector2.right : away.normalized;
                Vector2 impulse = away * (knockback * Math.Max(0f, PassiveOf(victim).KnockbackFactor));
                v.Push = impulse;                                  // a shove replaces the velocity, it does not add (ExternalDisplacement = impulse)
                v.WaypointValid = false;
                v.ThinkNow = true;
                bool knocked = impulse.sqrMagnitude > ShoveCounts;
                if (v.Pecking && PeckCastRules.IsCut(false, knocked, false)) Cut(victim, CutCause.Shove);
            }

            // Slow Area: every tick of its run, each rival inside is slowed for one tick (refreshed), through the victim's passive.
            private void AuraTick()
            {
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    if (!ch.Present) continue;
                    for (int a = 0; a < ch.Abilities.Length; a++)
                    {
                        if (ch.Abilities[a].Job != AbilityJob.SlowArea || _time + Eps >= ch.AuraUntil[a]) continue;
                        var ab = ch.Abilities[a];
                        for (int v = 0; v < Seats; v++)
                        {
                            if (!RivalCanBeHit(s, v, ab.AreaRadius)) continue;
                            if (MatchCombat.Refused(IncomingEffect.Slow, EggPhase(_c[v]), WalkPhase(_c[v]))) continue;
                            ApplySlow(_c[v], PassiveOf(v).ReceivedSlowFactor(ab.AreaSlowFactor), _tick);
                        }
                    }
                }
            }

            // ---- phase 4: act ----

            private void Act(int s)
            {
                var ch = _c[s];
                if (!ch.Present) return;

                // The dash of a Jump ends when its seconds are up and the chicken stands clear of every wall.
                if (ch.Dashing && _time + Eps >= ch.DashUntil && _nav.IsClear(ch.Pos, ch.Pos)) { ch.Dashing = false; ch.WaypointValid = false; }

                if (CanStartPeck(s)) StartPeck(s);
                ApplyPush(ch);
                if (!ch.Pecking && !Stunned(ch) && !EggPhase(ch)) Move(s);

                if (ch.HasIntent && !ch.Arrived && Standing(s)) { ch.Arrived = true; ch.ThinkNow = true; }
            }

            private void StartPeck(int s)
            {
                var ch = _c[s];
                var kit = _s.Seats[s].Farm;
                ch.Pecking = true;
                ch.PeckPile = ch.Intent.PeckPile;
                ch.PeckDoneAt = _time + kit.PeckCastSeconds;
                ch.NextPeckAt = _time + Math.Max(kit.PeckCooldown, kit.PeckCastSeconds);
                ch.ExposedMask = 0;
                NoteExposure(s);
                Record(MatchEventKind.CastStart, s, ch.PeckPile, 0f, CutCause.None);
                if (kit.PeckCastSeconds <= 0f) Land(s); // a zero cast lands at once
            }

            // Counts, once per cause, a Peck that starts with a rival holding a ready cutter of that cause in range.
            private void NoteExposure(int s)
            {
                var ch = _c[s];
                for (int r = 0; r < Seats; r++)
                {
                    var rv = _c[r];
                    if (r == s || !rv.Present || Stunned(rv) || EggPhase(rv)) continue;
                    for (int a = 0; a < rv.Abilities.Length; a++)
                    {
                        var ab = rv.Abilities[a];
                        CutCause cause = ab.Job == AbilityJob.Stun ? CutCause.Stun : ab.Job == AbilityJob.Shove ? CutCause.Shove : CutCause.None;
                        if (cause == CutCause.None || (ch.ExposedMask & (1 << (int)cause)) != 0) continue;
                        if (_time + Eps < rv.AbReady[a]) continue;
                        if (!RivalCanBeHit(r, s, ab.Radius)) continue;
                        ch.ExposedMask |= 1 << (int)cause;
                        _result.Counters[s].ExposedByCause[(int)cause]++;
                    }
                }
            }

            // ---- movement ----

            private Vector2 TargetOf(MatchIntent intent) =>
                intent.PeckPile >= 0 ? PlaceOf(intent.PeckPile).Pos : intent.Goal;

            // How close to the target the chicken stops: the rim of the pile it pecks, the base zone when the goal is its own base, else the point itself.
            private float StopDistance(int s, MatchIntent intent, Vector2 pos)
            {
                if (intent.PeckPile >= 0)
                {
                    var place = PlaceOf(intent.PeckPile);
                    return MatchGeometry.Rim(_map, place, pos - place.Pos);
                }
                int home = OracleMapSO.BaseIndex(s);
                return (intent.Goal - _map.Places[home].Pos).sqrMagnitude < 1e-6f ? _map.BaseRadius : 0f;
            }

            private bool Standing(int s)
            {
                var ch = _c[s];
                if (ch.Intent.PeckPile >= 0) return MatchGeometry.InReach(_map, PlaceOf(ch.Intent.PeckPile), ch.Pos);
                return Vector2.Distance(ch.Pos, ch.Intent.Goal) <= StopDistance(s, ch.Intent, ch.Pos) + StandSlack;
            }

            private bool InAnyPileReach(Vector2 pos)
            {
                for (int p = 0; p < OracleMapSO.PileCount; p++)
                    if (_pile[p] > Eps && MatchGeometry.InReach(_map, p, pos)) return true;
                return _goldenActive && _goldenFood > Eps && MatchGeometry.InReach(_map, _golden, pos);
            }

            private Vector2 ClampToArena(Vector2 p)
            {
                float h = _rules.ArenaHalf;
                return h > 0f ? new Vector2(Mathf.Clamp(p.x, -h, h), Mathf.Clamp(p.y, -h, h)) : p;
            }

            // A shove's velocity moves the chicken a tick's worth and decays as ChickenMovement does (velocity, then a lerp to zero).
            private void ApplyPush(Chick ch)
            {
                if (ch.Push.sqrMagnitude <= PushFloor) { ch.Push = Vector2.zero; return; }
                Vector2 to = ClampToArena(ch.Pos + ch.Push * _tick);
                if (_nav.IsClear(ch.Pos, to)) ch.Pos = to; else ch.Push = Vector2.zero;   // a wall stops it
                ch.Push *= 1f - Mathf.Clamp01(_rules.KnockbackDecayRate * _tick);
                ch.WaypointValid = false;
            }

            private bool HasJump(Chick ch, out int slot)
            {
                for (slot = 0; slot < ch.Abilities.Length; slot++)
                    if (ch.Abilities[slot].Job == AbilityJob.Jump) return true;
                slot = -1;
                return false;
            }

            private bool HasSpeedAbility(Chick ch)
            {
                for (int a = 0; a < ch.Abilities.Length; a++) if (ch.Abilities[a].Job == AbilityJob.Speed) return true;
                return false;
            }

            private float SpeedOf(int s)
            {
                var ch = _c[s];
                var kit = _s.Seats[s].Farm;
                float speed = kit.MoveSpeed;
                if (WalkPhase(ch)) speed *= ch.ShieldWalkMult;
                if (_time + Eps < ch.SpeedUntil) speed *= ch.SpeedMult;
                if (ch.Dashing) speed *= ch.DashMult;
                if (ch.Surge) speed *= _rules.SurgeSpeedFactor;
                if (_time + Eps < ch.SlowUntil && !WalkPhase(ch)) speed *= ch.SlowFactor;

                // A part 1 farmer (a kit without the Speed ability) bursts on its own whenever the burst is ready, as FarmSim does.
                if (kit.BurstMultiplier > 1f && kit.BurstSeconds > 0f && !HasSpeedAbility(ch))
                {
                    if (_time >= ch.BurstUntil && _time + Eps >= ch.BurstReadyAt)
                    {
                        ch.BurstUntil = _time + kit.BurstSeconds;
                        ch.BurstReadyAt = _time + kit.BurstCooldown;
                    }
                    if (_time < ch.BurstUntil) speed *= kit.BurstMultiplier;
                }
                if (!kit.IgnoresPileSlow && _slow < 1f && !ch.Dashing && InAnyPileReach(ch.Pos)) speed *= _slow;
                return speed;
            }

            private void Move(int s)
            {
                var ch = _c[s];
                if (!ch.HasIntent || Standing(s)) return;
                var kit = _s.Seats[s].Farm;

                // A kit with a Jump ability walks straight and spends a Jump at a wall; a part 1 farmer that only has the flag crosses for free.
                bool hasJump = HasJump(ch, out int jumpSlot);
                bool straight = ch.Dashing || hasJump || kit.Jumps;

                float budget = SpeedOf(s) * _tick;
                Vector2 target = TargetOf(ch.Intent);
                for (int hop = 0; hop < 8 && budget > 1e-6f; hop++)
                {
                    if (!ch.WaypointValid)
                    {
                        ch.Waypoint = straight ? target : _nav.NextPoint(ch.Pos, target);
                        ch.WaypointValid = true;
                    }
                    Vector2 d = ch.Waypoint - ch.Pos;
                    float len = d.magnitude;
                    bool final = (ch.Waypoint - target).sqrMagnitude < 1e-10f;
                    float allowed = final ? Math.Max(0f, len - StopDistance(s, ch.Intent, ch.Pos)) : len;
                    float step = Math.Min(budget, allowed);

                    if (hasJump && !ch.Dashing && len > 1e-6f && !_nav.IsClear(ch.Pos, ch.Pos + d / len * step))
                    {
                        // A wall is in the way. A ready Jump crosses it; otherwise the chicken waits for the cooldown.
                        if (_time + Eps >= ch.AbReady[jumpSlot]) { BeginCast(s, jumpSlot); StartDash(ch, jumpSlot); Move(s); }
                        return;
                    }
                    budget -= step;

                    if (step < allowed - 1e-6f)
                    {
                        ch.Pos = ClampToArena(ch.Pos + d / len * step);
                        return;
                    }
                    if (final)
                    {
                        if (len > 1e-6f) ch.Pos = ClampToArena(ch.Pos + d / len * allowed);
                        return;
                    }
                    ch.Pos = ClampToArena(ch.Waypoint);
                    ch.WaypointValid = false;
                }
            }

            private void StartDash(Chick ch, int slot)
            {
                ch.Dashing = true;
                ch.DashUntil = _time + ch.Abilities[slot].Duration;
                ch.DashMult = ch.Abilities[slot].SpeedMultiplier;
                ch.WaypointValid = false;
            }

            // ---- end ----

            private MatchResult Finish()
            {
                for (int s = 0; s < Seats; s++) _result.Banked[s] = _c[s].Banked;

                if (_winner >= 0)
                {
                    _result.Winner = _winner;
                    _result.ReachedTarget = true;
                    _result.EndSeconds = _winTime;
                    return _result;
                }

                _result.EndSeconds = _clock;
                var present = new bool[Seats];
                for (int s = 0; s < Seats; s++) present[s] = _c[s].Present;
                _result.Winner = MatchCombat.PickTimerWinner(_result.Banked, _result.Kills, present, out bool corner);
                _result.CornerDecided = corner;
                return _result;
            }

            private void Record(MatchEventKind kind, int seat, int other, float amount, CutCause cause)
            {
                if (_result.Events == null) return;
                _result.Events.Add(new MatchEvent { Time = _time, Kind = kind, Seat = seat, Other = other, Amount = amount, Cause = cause });
            }
        }
    }
}
