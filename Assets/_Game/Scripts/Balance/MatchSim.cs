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

    /// <summary>Which rival a forced kill takes: the one holding the least food in its beak, or the one holding the most.</summary>
    public enum ForcedVictims : byte { Empty, Loaded }

    /// <summary>
    /// A scripted design that grants one seat exactly <see cref="Kills"/> kills on schedule, bypassing the mark: at each of
    /// <see cref="Times"/> the simulator executes the chosen victim with the same payout as a real kill. It measures what a kill is worth
    /// without the survivor bias of kills a policy happened to land. <c>Kills == 0</c> means off (<c>Spoiler == -1</c> is the explicit form).
    /// </summary>
    public struct ForcedKills
    {
        public int Spoiler;
        public int Kills;
        public ForcedVictims Victims;
        /// <summary>Seconds into the match, at least <see cref="Kills"/> of them, in ascending order.</summary>
        public float[] Times;

        public static ForcedKills Off => new ForcedKills { Spoiler = -1 };
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
        /// <summary>Scripted kills; the default (no kills) leaves the match to its policies.</summary>
        public ForcedKills Forced;
    }

    public sealed class MatchResult
    {
        /// <summary>The winning seat, or -1 when no seat played.</summary>
        public int Winner;
        public float[] Banked;
        public int[] Kills;
        public float EndSeconds;
        public bool ReachedTarget;
        /// <summary>The match went to the clock, so no seat banked the target first (same as <c>!ReachedTarget</c>; named for the kills-to-win table).</summary>
        public bool NobodyReachedTarget;
        /// <summary>
        /// The seat that would have won had the Spoiler passive's end-of-clock bonus not been paid: the clock's pick over the banked food as
        /// it stood. Equal to <see cref="Winner"/> when the target ended the match (the bonus is paid on the clock only).
        /// </summary>
        public int WinnerWithoutSpoilerBonus;
        /// <summary>The clock ended with banked food and kills level and the lowest corner won (the game's last tie-break).</summary>
        public bool CornerDecided;
        /// <summary>Intents a policy asked for that the rules refuse (a cast no seat can make, an unknown pile). Zero for a sound policy.</summary>
        public int RejectedIntents;
        /// <summary>
        /// Casts that were legal when the policy decided (the slot ready, the chicken free to cast) and were no longer ready when the
        /// reaction delay passed: the slot was spent or a stun landed in between. A sound policy with a delay still produces some; they are the
        /// delay's doing, not a wiring bug, so they are kept out of <see cref="RejectedIntents"/>.
        /// </summary>
        public int CastsLostToReactionDelay;
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
    /// slow auras act; (4) each chicken starts a Peck when it may, is pushed (a stunned one waits: its movement tick does not run) and walks.
    /// A seat thinks every <see cref="MatchRules.ThinkSeconds"/> and at once when it arrived, a cast landed or was cut, a deposit ended,
    /// its target pile emptied, a stun ended, a rival hit it, or its Peck is ready to start. An intent takes effect
    /// <see cref="PolicyParams.ReactionDelay"/> later. Food leaves a pile only when a Peck lands, in rotating seat order.
    /// Slows do not stack: the pile slow (a chicken in reach of any non-empty pile, <see cref="FarmRules.PileSlowFactor"/>, unless its kit ignores it),
    /// the timed ability slow and the Slow Area aura are three sources and only the strongest applies (<see cref="MatchCombat.CombineSlows"/>); the
    /// Shield's walk refuses new slows and auras but leaves a timed slow already running alone. Speed boosts (Speed, a Jump's dash, a part 1 burst) go through
    /// the game's own <see cref="AbilityEffectStack"/>: the strongest boost times the Shield walk's penalty. Any cast other than the Shield's own drops the Shield's
    /// pending egg (or ends a running egg), and while the egg is live nothing auto-Pecks; the egg does not stop a cast, only a stun does.
    /// A kit with a Jump ability walks straight lines and spends a Jump to cross a wall (see <c>Move</c>); a kit that only has the
    /// <c>Jumps</c> flag (a part 1 farmer) crosses walls for free, as part 1's jump table does.
    /// Death Mark (slice S4): a press with no live mark picks a rival through the game's <see cref="MarkPickRules"/> (rolls from the mark
    /// stream), arms for <c>MarkArmSeconds</c> and lives <c>MarkWindowSeconds</c>; a press with a live mark is the Kill, legal only when
    /// <see cref="ExecuteRules.KillQualifies"/> (armed, the target stunned now, not shelled), the target is within <c>MarkKillRange</c>, no
    /// base wall lies between and the Spoiler can cast. A kill empties the victim's beak into the Spoiler's bounty bag plus
    /// <see cref="MatchRules.ExecuteBounty"/>, takes the victim out for <c>MarkRemovalSeconds</c> and sends it back to its base with no beak
    /// food. The bag ignores capacity, banks first at the base and can be stolen. The Spoiler passive's bonus is paid on the clock before
    /// the winner is picked. Not modelled: body collision, a chicken's own base keep-out as a movement limit (the walk graph ignores rival bases).
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
            ValidateForced(s);
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

        private static void ValidateForced(MatchSetup s)
        {
            var f = s.Forced;
            if (f.Kills <= 0) return;
            if (f.Spoiler < 0 || f.Spoiler >= Seats || s.Seats[f.Spoiler] == null)
                throw new ArgumentException($"MatchSetup.Forced names seat {f.Spoiler}, which is not playing.", nameof(s));
            if (DeathMarkSlot(s.Seats[f.Spoiler].Abilities) < 0)
                throw new ArgumentException($"MatchSetup.Forced: seat {f.Spoiler} ({s.Seats[f.Spoiler].Label}) holds no Death Mark ability, so it has no removal seconds to apply.", nameof(s));
            if (f.Times == null || f.Times.Length < f.Kills)
                throw new ArgumentException($"MatchSetup.Forced asks for {f.Kills} kills and needs that many times (has {f.Times?.Length ?? 0}).", nameof(s));
            for (int i = 1; i < f.Kills; i++)
                if (f.Times[i] < f.Times[i - 1]) throw new ArgumentException("MatchSetup.Forced.Times must ascend.", nameof(s));
        }

        private static int DeathMarkSlot(MatchAbility[] abilities)
        {
            if (abilities == null) return -1;
            for (int a = 0; a < abilities.Length; a++) if (abilities[a].Job == AbilityJob.DeathMark) return a;
            return -1;
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
            /// <summary>The intent's cast (if any) was legal on the tick the policy decided it.</summary>
            public bool CastLegalWhenDecided;
        }

        private struct CastRequest
        {
            public int Slot;
            public int Target;
            public bool LegalWhenDecided;
        }

        // The sources the simulator registers on a chicken's AbilityEffectStack. The game keys them by ability slot; only distinctness matters here.
        private const int SrcSpeed = 0, SrcDash = 1, SrcWalk = 2, SrcBurst = 3;

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
            public float ShieldEgg;
            /// <summary>Another cast ended the Shield's pending egg (or the egg itself): the follow-up phase is no longer live.</summary>
            public bool ShieldCancelled;
            /// <summary>The game's own resolver of move-speed modifiers: the strongest boost times the strongest penalty.</summary>
            public readonly AbilityEffectStack Effects = new AbilityEffectStack();

            // control
            public float StunUntil;
            public bool WasStunned;
            public float SlowUntil;
            public float SlowFactor = 1f;
            /// <summary>The factor of the Slow Area aura the chicken stands in this tick (1 when none); recomputed every tick, never merged into the timed slow.</summary>
            public float AuraFactor = 1f;
            public Vector2 Push;
            public bool Surge;

            // death mark and removal
            public bool Removed;
            public float RemovedUntil;
            public int MarkTarget = -1;
            public int MarkSlot = -1;
            public float ArmEnd;
            public float WindowEnd;
            public bool HasLastMark;
            public int LastMark;
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
            private readonly int[] _jitterTicks = new int[Seats];
            private readonly IMatchPolicy[] _policy = new IMatchPolicy[Seats];
            private readonly MatchRng[] _rng = new MatchRng[Seats];
            private MatchRng _comebackRng;
            private MatchRng _markRng;
            private readonly float _baseSpan;
            private int _forcedNext;
            private readonly List<int> _markCandidates = new List<int>(Seats);
            private readonly List<float> _markHome = new List<float>(Seats);
            private readonly List<float> _markNear = new List<float>(Seats);
            private readonly List<float> _markLeader = new List<float>(Seats);
            private readonly List<float> _markRolls = new List<float>(Seats);
            private readonly Chick[] _c = new Chick[Seats];
            private readonly float[] _pile = new float[OracleMapSO.PileCount];
            private readonly float[] _pileMax = new float[OracleMapSO.PileCount];
            private bool _goldenActive;
            private float _goldenFood;
            private OraclePlace _golden;
            private Vector2 _goldenWalk;
            private bool _goldenOffWall;
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
                    ch.MarkSlot = DeathMarkSlot(abilities);
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
                    float jitter = float.IsNaN(p.ReactionJitter) ? 0f : Math.Max(0f, p.ReactionJitter);
                    _jitterTicks[seat] = Mathf.RoundToInt(jitter / _tick);
                    _policy[seat] = s.Policies[seat](seat, p);
                    if (_policy[seat] == null) throw new InvalidOperationException($"The policy factory of seat {seat} returned null.");
                    _rng[seat] = MatchRng.Create(matchSeed, (ulong)seat);
                }
                _baseCentres = centres.ToArray();
                _contactRadius = float.IsPositiveInfinity(contact) ? 0f : contact;
                _comebackRng = MatchRng.Create(matchSeed, MatchRng.StreamComeback);
                _markRng = MatchRng.Create(matchSeed, MatchRng.StreamMark);
                // The game weighs the mark by the longest distance between two live bases (PlayerBase.BaseSpan); 0 with fewer than two.
                float span = 0f;
                for (int a = 0; a < centres.Count; a++)
                    for (int b = a + 1; b < centres.Count; b++) span = Mathf.Max(span, Vector2.Distance(centres[a], centres[b]));
                _baseSpan = span;
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

                    Respawn();
                    Look();
                    TickComeback();
                    NoteContact();
                    ForceKills();
                    MarkUpkeep();

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
                        // GameManager.SpawnGoldenPile: up to GoldenPileSpawnAttempts spots on the ring, the first one a ball of
                        // GoldenPileClearRadius fits at wins, and when none does the last one is used anyway (it may stand inside a wall).
                        var spot = Vector2.zero;
                        for (int attempt = 0; attempt < GameManager.GoldenPileSpawnAttempts; attempt++)
                        {
                            float angle = _comebackRng.NextFloat() * 360f * Mathf.Deg2Rad;
                            float radius = Mathf.Lerp(cfg.GoldenRingMin, cfg.GoldenRingMax, _comebackRng.NextFloat());
                            spot = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                            if (_nav.SpotClear(spot, GameManager.GoldenPileClearRadius)) break;
                        }
                        // The plain pile prefab: its own footprint (baked), no yaw, not an island's size.
                        _golden = new OraclePlace { Kind = OraclePlaceKind.Centre, Seat = -1, Pos = spot, Footprint = _map.GoldenFootprint, Yaw = 0f };
                        // A fallback spot inside a wall cannot be walked to; chickens walk to the nearest standing point instead.
                        _goldenOffWall = !_nav.IsClear(spot, spot);
                        _goldenWalk = _goldenOffWall ? _nav.NearestClear(spot) : spot;
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
                        if (!_c[b].Present || _c[a].Removed || _c[b].Removed || !MatchCombat.InRange(_c[a].Pos, _c[b].Pos, _contactRadius)) continue;
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
                if (!ch.Present || ch.Removed) return;
                var kit = _s.Seats[s].Farm;

                if (ch.WasStunned && !Stunned(ch)) { ch.WasStunned = false; ch.ThinkNow = true; }

                if (ch.Pecking)
                {
                    bool gone = PileFood(ch.PeckPile) <= Eps;
                    if (PeckCastRules.IsCut(false, false, gone)) Cut(s, CutCause.EmptyPile);
                    else if (_time + Eps >= ch.PeckDoneAt) Land(s);
                }

                if (ch.Cargo + ch.Bag > Eps && kit.DepositRate > 0f && MatchGeometry.InReach(_map, OracleMapSO.BaseIndex(s), ch.Pos)) Deposit(s, kit);

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
                // The bounty bag banks first, then the beak (ChickenCargo.TryDeposit).
                float dep = Math.Min(ch.Cargo + ch.Bag, kit.DepositRate * _tick);
                float before = ch.Banked;
                float fromBag = Math.Min(ch.Bag, dep);
                ch.Bag -= fromBag;
                ch.Cargo -= dep - fromBag;
                ch.Banked += dep;
                ch.Visit += dep;

                float target = _farm.WinTarget;
                if (before < target - Eps && ch.Banked >= target - Eps)
                {
                    // The win lands inside this deposit: time it to the sub tick. Two seats on one tick: the earlier time wins.
                    float at = _time + (target - before) / kit.DepositRate;
                    if (_winner < 0 || at < _winTime - 1e-6f) { _winner = s; _winTime = at; }
                }
                if (ch.Cargo + ch.Bag > Eps) return;

                ch.Cargo = 0f;
                ch.Bag = 0f;
                ch.ThinkNow = true;
                Record(MatchEventKind.Bank, s, -1, ch.Visit, CutCause.None);
                ch.Visit = 0f;
            }

            private bool CanStartPeck(int s)
            {
                var ch = _c[s];
                if (!ch.HasIntent || ch.Intent.PeckPile < 0 || ch.Pecking) return false;
                // A stunned chicken cannot cast; a hidden one does not auto-Peck, nor one whose Shield still has its egg to come or running
                // (AbilityController.AnyStealthRunning: the chain would break it, and the Peck would be a cast that cancels the egg), and a
                // dashing one is busy.
                if (Stunned(ch) || ShieldLive(ch) || Hidden(ch) || ch.Dashing) return false;
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
                    v.Removed = ch.Removed;
                    v.Shelled = EggPhase(ch);
                    v.MarkTarget = ch.MarkTarget;
                    v.ArmLeft = ch.MarkTarget >= 0 ? Math.Max(0f, ch.ArmEnd - _time) : 0f;
                    v.WindowLeft = ch.MarkTarget >= 0 ? Math.Max(0f, ch.WindowEnd - _time) : 0f;
                    v.MarkedBy = -1;
                    for (int m = 0; m < Seats; m++)
                        if (m != s && _c[m].MarkTarget == s) { v.MarkedBy = m; break; }
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
                if (!ch.Present || ch.Removed || !(ch.ThinkNow || _i >= ch.NextThink)) return;

                BuildView(s);
                MatchIntent intent = _policy[s].Think(_view, ref _rng[s]);
                // A random extra delay, drawn only when there is one, so a seat without jitter draws nothing and plays as it always did.
                int jitter = _jitterTicks[s] > 0 ? _rng[s].NextInt(_jitterTicks[s] + 1) : 0;
                ch.Queue.Enqueue(new Pending
                {
                    Intent = intent, ApplyTick = _i + _delayTicks[s] + jitter,
                    CastLegalWhenDecided = intent.CastSlot >= 0 && CastReadyNow(s, intent.CastSlot),
                });
                ch.ThinkNow = false;
                // The first periodic think is staggered by seat so the four do not all think on the same tick.
                ch.NextThink = _i + _thinkTicks + (ch.Thought ? 0 : s * _thinkTicks / Seats);
                ch.Thought = true;
            }

            // ---- phase 3: intents take effect, casts resolve together ----

            private void Due(int s)
            {
                var ch = _c[s];
                if (!ch.Present || ch.Removed) return;
                while (ch.Queue.Count > 0 && ch.Queue.Peek().ApplyTick <= _i)
                {
                    var due = ch.Queue.Dequeue();
                    Apply(s, due.Intent, due.CastLegalWhenDecided);
                }
            }

            private void Apply(int s, MatchIntent intent, bool castLegalWhenDecided)
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
                if (intent.CastSlot >= 0) ch.Casts.Add(new CastRequest { Slot = intent.CastSlot, Target = intent.CastTarget, LegalWhenDecided = castLegalWhenDecided });

                // The movement part of the intent; the cast part is a one-off and is not part of what the chicken keeps doing.
                intent.CastSlot = -1;
                intent.CastTarget = -1;
                // A goal inside a wall's clearance or outside the arena cannot be walked to: the chicken goes to the nearest standing point instead.
                // A pile intent walks to its pile, so its goal is not read.
                if (intent.PeckPile < 0) intent.Goal = _nav.NearestClear(intent.Goal);
                if (ch.HasIntent && ch.Intent.SameAs(intent)) return;
                ch.Intent = intent;
                ch.HasIntent = true;
                ch.WaypointValid = false;
                ch.Arrived = false;
                ch.EmptyNoted = false;
            }

            // The game refuses a cast only while stunned (ControlRules.CanCast); the egg does not stop one, it ends the Shield (BeginCast).
            private bool CanCast(int s, int slot)
            {
                var ch = _c[s];
                var ab = ch.Abilities[slot];
                if (ab.Job == AbilityJob.Peck) return false;           // pecking is the PeckPile intent
                if (ch.Removed) return false;
                if (_time + Eps < ch.AbReady[slot]) return false;
                return ControlRules.CanCast(_stunSnap[s] ? ControlState.Stunned : ControlState.Free);
            }

            // Could this cast be made on the tick the policy decides it? The same test as CanCast, on live state, for a slot that exists.
            private bool CastReadyNow(int s, int slot)
            {
                var ch = _c[s];
                if (slot < 0 || slot >= ch.Abilities.Length || ch.Abilities[slot].Job == AbilityJob.Peck || ch.Removed) return false;
                return _time + Eps >= ch.AbReady[slot] && ControlRules.CanCast(Stunned(ch) ? ControlState.Stunned : ControlState.Free);
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
                        if ((used & (1 << req.Slot)) != 0 || !CanCast(s, req.Slot))
                        {
                            // Legal when decided: the delay spent the slot or let a stun land. Anything else is a policy asking for the impossible.
                            if (req.LegalWhenDecided) _result.CastsLostToReactionDelay++; else _result.RejectedIntents++;
                            continue;
                        }
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
                if (ab.Job != AbilityJob.Shield) CancelShieldFollowUp(ch);                              // any other cast cancels the egg
            }

            // The Shield's egg is live from the cast until it ends, unless another cast cancelled it (AbilityController.HasLiveFollowUp).
            private bool ShieldLive(Chick ch) => _time + Eps < ch.EggUntil && !ch.ShieldCancelled;

            // AbilityController.EndStealthAndPeckForCast with TurtleModeAbilitySO.OnOtherCast: during the walk the pending egg is dropped and the
            // walk goes on to its end; during the egg the whole Shield ends now.
            private void CancelShieldFollowUp(Chick ch)
            {
                if (!ShieldLive(ch)) return;
                ch.ShieldCancelled = true;
                bool eggRunning = _time + Eps >= ch.ShieldWalkUntil;
                float next = ShieldRules.RunAfterOtherCast(ch.EggUntil - _time, ch.ShieldEgg, eggRunning);
                if (next > 0f) ch.EggUntil = ch.ShieldWalkUntil;
                else ch.ShieldWalkUntil = ch.EggUntil = _time;
            }

            private bool RivalCanBeHit(int caster, int victim, float radius)
            {
                var v = _c[victim];
                return victim != caster && v.Present && !v.Removed && !_c[caster].Removed
                    && MatchCombat.InRange(_c[caster].Pos, v.Pos, radius)
                    && !MatchCombat.KeepOutBlocks(_c[caster].Pos, v.Pos, _baseCentres, _rules.KeepOutRadius);
            }

            private void Cast(int s, CastRequest req)
            {
                var ch = _c[s];
                var ab = ch.Abilities[req.Slot];
                if (ch.Removed) return;                                // executed earlier this tick: nothing of its own lands
                if (ab.Job == AbilityJob.DeathMark) { CastDeathMark(s, req.Slot); return; }
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
                        ch.ShieldEgg = ab.ShieldEggSeconds;
                        ch.ShieldCancelled = false;
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

            // Slow Area: every tick of its run, each rival inside gets the aura's factor for that tick, through the victim's passive. The aura
            // is its own slow source (CheckAbilityZoneSlow), not part of the timed slow, so it ends with the area or the victim leaving it.
            private void AuraTick()
            {
                for (int s = 0; s < Seats; s++) _c[s].AuraFactor = 1f;
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    if (!ch.Present || ch.Removed) continue;
                    for (int a = 0; a < ch.Abilities.Length; a++)
                    {
                        if (ch.Abilities[a].Job != AbilityJob.SlowArea || _time + Eps >= ch.AuraUntil[a]) continue;
                        var ab = ch.Abilities[a];
                        for (int v = 0; v < Seats; v++)
                        {
                            if (!RivalCanBeHit(s, v, ab.AreaRadius)) continue;
                            if (MatchCombat.Refused(IncomingEffect.Slow, EggPhase(_c[v]), WalkPhase(_c[v]))) continue;
                            _c[v].AuraFactor = Math.Min(_c[v].AuraFactor, PassiveOf(v).ReceivedSlowFactor(ab.AreaSlowFactor));
                        }
                    }
                }
            }

            // ---- death mark, execution, removal ----

            // The press of Death Mark. With no live mark it picks and arms one; with a live mark it is the Kill. Neither refusal is a
            // wiring bug (the game's Press returns false and changes nothing), so neither is counted in RejectedIntents.
            private void CastDeathMark(int s, int slot)
            {
                var ch = _c[s];
                var ab = ch.Abilities[slot];
                if (ch.MarkTarget < 0)
                {
                    int pick = PickMark(s, ab);
                    if (pick < 0) return;                              // "no rival to mark"
                    BeginCast(s, slot);
                    ch.AbReady[slot] = _time;                          // the slot is free while a mark runs; the outcome sets the cooldown
                    ch.MarkTarget = pick;
                    ch.ArmEnd = _time + ab.MarkArmSeconds;
                    ch.WindowEnd = _time + ab.MarkWindowSeconds;
                    _c[pick].ThinkNow = true;                          // everyone sees the replicated mark
                    return;
                }

                int victim = ch.MarkTarget;
                if (!KillLands(s, victim, ab)) return;                 // "Kill refused": nothing changes, the mark runs on
                BeginCast(s, slot);
                Execute(s, victim, ab.MarkRemovalSeconds);
                ch.AbReady[slot] = _time + ab.MarkSuccessCooldown;
                EndMark(ch, victim);
            }

            // The game's pick (AssassinExecute.GatherMarkCandidates and PickWeighted): living rivals, the last mark filtered out, one roll each
            // in seat order then a fallback roll, all from the mark stream. Positions and banked food are true values: it is the authority's pick.
            private int PickMark(int s, in MatchAbility ab)
            {
                var ch = _c[s];
                _markCandidates.Clear();
                int rivals = 0;
                float bestBanked = 0f;
                for (int t = 0; t < Seats; t++)
                {
                    if (t == s || !_c[t].Present) continue;
                    rivals++;
                    bestBanked = Math.Max(bestBanked, _c[t].Banked);
                    if (!_c[t].Removed) _markCandidates.Add(t);
                }
                MarkPickRules.Filter(_markCandidates, ch.HasLastMark, ch.LastMark, rivals);

                _markHome.Clear(); _markNear.Clear(); _markLeader.Clear(); _markRolls.Clear();
                foreach (int t in _markCandidates)
                {
                    Vector2 at = _c[t].Pos;
                    _markHome.Add(MarkPickRules.HomeFraction(Vector2.Distance(at, _map.Places[OracleMapSO.BaseIndex(t)].Pos), _baseSpan));
                    _markNear.Add(MarkPickRules.AssassinFraction(Vector2.Distance(ch.Pos, at), _baseSpan));
                    _markLeader.Add(MarkPickRules.LeaderWeight(_c[t].Banked, bestBanked, ab.MarkLeaderPickWeight));
                    _markRolls.Add(_markRng.NextFloat());
                }
                float fallback = _markRng.NextFloat();
                int pick = MarkPickRules.PickWeighted(_markHome, _markNear, _markLeader, _markRolls, fallback);
                return pick < 0 ? -1 : _markCandidates[pick];
            }

            // Would a Kill press land now? The game's CanKillNow over the tick's snapshot (the state the tick began with).
            private bool KillLands(int s, int victim, in MatchAbility ab)
            {
                var sp = _c[s];
                var t = _c[victim];
                if (!t.Present || t.Removed) return false;
                bool shelled = MatchCombat.Refused(IncomingEffect.Execute, _egg[victim], _walk[victim]);
                bool armed = _time + Eps >= sp.ArmEnd;
                return ExecuteRules.KillQualifies(armed, _stunSnap[victim], shelled)
                    && MatchCombat.InRange(sp.Pos, t.Pos, ab.MarkKillRange)
                    && !MatchCombat.KeepOutBlocks(sp.Pos, t.Pos, _baseCentres, _rules.KeepOutRadius);
            }

            // One execution: the victim's beak and the flat bounty go into the Spoiler's bag (capacity does not apply), the kill is scored,
            // the victim is taken out. A shelled victim keeps its beak (AssassinExecute: StealRules.CanStealFrom), a forced kill can hit one.
            private void Execute(int s, int victim, float removalSeconds)
            {
                var sp = _c[s];
                var v = _c[victim];
                float loot = EggPhase(v) ? 0f : v.Cargo;
                float pay = loot + _rules.ExecuteBounty;
                v.Cargo -= loot;
                sp.Bag += pay;
                sp.ThinkNow = true;
                _result.Kills[s]++;
                _result.Counters[s].Bounty += pay;
                Record(MatchEventKind.Kill, s, victim, pay, CutCause.None);
                Remove(victim, removalSeconds);
            }

            // EndAllForRemoval and the removal itself: nothing running survives, the queue is dropped, cooldowns keep running.
            private void Remove(int victim, float seconds)
            {
                var v = _c[victim];
                if (v.Pecking) Cut(victim, CutCause.Execution);
                v.Removed = true;
                v.RemovedUntil = _time + seconds;
                v.StunUntil = 0f; v.WasStunned = false;
                v.SlowUntil = 0f; v.SlowFactor = 1f;
                v.Push = Vector2.zero;
                v.SpeedUntil = 0f;
                v.Dashing = false;
                v.HideUntil = 0f;
                v.ShieldWalkUntil = 0f; v.EggUntil = 0f; v.ShieldCancelled = false;
                v.AuraFactor = 1f;
                v.Effects.Clear();
                for (int a = 0; a < v.AuraUntil.Length; a++) v.AuraUntil[a] = 0f;
                v.Queue.Clear();
                v.Casts.Clear();
                v.HasIntent = false;
                v.Arrived = false;
                v.WaypointValid = false;
                v.ThinkNow = true;
                if (v.MarkTarget >= 0) EndMark(v, v.MarkTarget);        // a mark ends with its holder; no cooldown change
            }

            // A mark ended, paid or failed: its target is the one the next press cannot pick.
            private static void EndMark(Chick ch, int target)
            {
                ch.HasLastMark = true;
                ch.LastMark = target;
                ch.MarkTarget = -1;
            }

            // Phase 0: a removed chicken whose time is up stands at its base spawn with an empty beak (its bag stays: it is its own food).
            // Done before the look, so every observer sees it at its base this very tick.
            private void Respawn()
            {
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    if (!ch.Present || !ch.Removed || _time + Eps < ch.RemovedUntil) continue;
                    ch.Removed = false;
                    ch.Pos = Spawn(_map, s);
                    ch.Cargo = 0f;
                    ch.Visit = 0f;
                    ch.Push = Vector2.zero;
                    ch.ThinkNow = true;
                }
            }

            // A live mark ends without a kill when its target is out of play or the window is over; the fail cooldown follows.
            private void MarkUpkeep()
            {
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    if (!ch.Present || ch.MarkTarget < 0) continue;
                    var t = _c[ch.MarkTarget];
                    if (t.Present && !t.Removed && _time + Eps < ch.WindowEnd) continue;
                    ch.AbReady[ch.MarkSlot] = _time + ch.Abilities[ch.MarkSlot].MarkFailCooldown;
                    EndMark(ch, ch.MarkTarget);
                    ch.ThinkNow = true;
                }
            }

            // The forced-kills design: at each scheduled time the chosen rival is executed with the same payout, the mark bypassed.
            private void ForceKills()
            {
                var f = _s.Forced;
                while (f.Kills > 0 && _forcedNext < f.Kills && _time + Eps >= f.Times[_forcedNext])
                {
                    int victim = -1;
                    for (int t = 0; t < Seats; t++)
                    {
                        if (t == f.Spoiler || !_c[t].Present || _c[t].Removed) continue;
                        bool better = victim < 0
                            || (f.Victims == ForcedVictims.Loaded ? _c[t].Cargo > _c[victim].Cargo : _c[t].Cargo < _c[victim].Cargo);
                        if (better) victim = t;
                    }
                    if (victim < 0)
                        throw new InvalidOperationException($"Forced kill {_forcedNext + 1} at {f.Times[_forcedNext]:0.##} s found no living rival to execute; space the times by more than the removal seconds.");
                    var ab = _c[f.Spoiler].Abilities[_c[f.Spoiler].MarkSlot];
                    Execute(f.Spoiler, victim, ab.MarkRemovalSeconds);
                    _forcedNext++;
                }
            }

            // ---- phase 4: act ----

            private void Act(int s)
            {
                var ch = _c[s];
                if (!ch.Present || ch.Removed) return;

                // The dash of a Jump ends when its seconds are up and the chicken stands clear of every wall.
                if (ch.Dashing && _time + Eps >= ch.DashUntil && _nav.IsClear(ch.Pos, ch.Pos)) { ch.Dashing = false; ch.WaypointValid = false; }

                if (CanStartPeck(s)) StartPeck(s);
                // A stunned chicken's movement tick does not run (ChickenController: ControlRules.CanMove), so its push waits for the stun to end.
                if (ControlRules.CanMove(Stunned(ch) ? ControlState.Stunned : ControlState.Free)) ApplyPush(ch);
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
                    if (r == s || !rv.Present || rv.Removed || Stunned(rv) || EggPhase(rv)) continue;
                    // A live mark on this chicken exposes its Peck to an execution (once per cast).
                    if (rv.MarkTarget == s && (ch.ExposedMask & (1 << (int)CutCause.Execution)) == 0)
                    {
                        ch.ExposedMask |= 1 << (int)CutCause.Execution;
                        _result.Counters[s].ExposedByCause[(int)CutCause.Execution]++;
                    }
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
                intent.PeckPile >= 0 ? WalkTarget(intent.PeckPile) : intent.Goal;

            // Where a chicken walks to for a pile: its centre, except for a Golden Pile that fell inside a wall (nobody can stand there).
            private Vector2 WalkTarget(int pile) =>
                pile == MatchGeometry.GoldenPile && _goldenOffWall ? _goldenWalk : PlaceOf(pile).Pos;

            // How close to the target the chicken stops: the rim of the pile it pecks, the base zone when the goal is its own base, else the point itself.
            private float StopDistance(int s, MatchIntent intent, Vector2 pos)
            {
                if (intent.PeckPile >= 0)
                {
                    var place = PlaceOf(intent.PeckPile);
                    float rim = MatchGeometry.Rim(_map, place, pos - place.Pos);
                    // Walking to a stand-in point beside the pile: stopping that much nearer keeps the chicken inside the pile's reach.
                    return intent.PeckPile == MatchGeometry.GoldenPile && _goldenOffWall ? Math.Max(0f, rim - Vector2.Distance(_goldenWalk, place.Pos)) : rim;
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
                ch.Pos = _nav.Slide(ch.Pos, to);                  // a wall does not stop it: it slides along the wall (CharacterController)
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
                // A part 1 farmer (a kit without the Speed ability) bursts on its own whenever the burst is ready, as FarmSim does.
                float burst = 1f;
                if (kit.BurstMultiplier > 1f && kit.BurstSeconds > 0f && !HasSpeedAbility(ch))
                {
                    if (_time >= ch.BurstUntil && _time + Eps >= ch.BurstReadyAt)
                    {
                        ch.BurstUntil = _time + kit.BurstSeconds;
                        ch.BurstReadyAt = _time + kit.BurstCooldown;
                    }
                    if (_time < ch.BurstUntil) burst = kit.BurstMultiplier;
                }

                // Speed modifiers go through the game's own stack: the strongest boost (Speed, a Jump's dash, the burst) times the Shield walk's
                // penalty. A multiplier of exactly 1 registers nothing.
                var fx = ch.Effects;
                fx.SetMoveSpeed(SrcSpeed, _time + Eps < ch.SpeedUntil ? ch.SpeedMult : 1f);
                fx.SetMoveSpeed(SrcDash, ch.Dashing ? ch.DashMult : 1f);
                fx.SetMoveSpeed(SrcWalk, WalkPhase(ch) ? ch.ShieldWalkMult : 1f);
                fx.SetMoveSpeed(SrcBurst, burst);
                float speed = kit.MoveSpeed * fx.MoveSpeedMultiplier;
                if (ch.Surge) speed *= _rules.SurgeSpeedFactor;

                // Slows do not stack: only the strongest of the pile, the timed ability slow and the aura applies. The timed slow keeps running
                // through the Shield's walk, which refuses only new ones.
                float pile = !kit.IgnoresPileSlow && _slow < 1f && InAnyPileReach(ch.Pos) ? _slow : 1f;
                float timed = _time + Eps < ch.SlowUntil ? ch.SlowFactor : 1f;
                return speed * MatchCombat.CombineSlows(pile, timed, ch.AuraFactor);
            }

            private void Move(int s)
            {
                var ch = _c[s];
                if (!ch.HasIntent || Standing(s)) return;
                var kit = _s.Seats[s].Farm;

                // A chicken that stands inside a wall's clearance without dashing (a clamp at the arena edge) steps out first, as the game's
                // CharacterController depenetrates, so the path search always starts from a standing point.
                if (!ch.Dashing && !_nav.IsClear(ch.Pos, ch.Pos)) { ch.Pos = _nav.NearestClear(ch.Pos); ch.WaypointValid = false; }

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
                    _result.Winner = _result.WinnerWithoutSpoilerBonus = _winner;
                    _result.ReachedTarget = true;
                    _result.EndSeconds = _winTime;
                    return _result;
                }

                _result.EndSeconds = _clock;
                _result.NobodyReachedTarget = true;
                var present = new bool[Seats];
                for (int s = 0; s < Seats; s++) present[s] = _c[s].Present;
                _result.WinnerWithoutSpoilerBonus = MatchCombat.PickTimerWinner(_result.Banked, _result.Kills, present, out _);

                // The Spoiler pays out BEFORE the winner is picked (GameManager.EndOnTimerExpiry: AwardMatchEndBonuses first).
                for (int s = 0; s < Seats; s++)
                    if (present[s]) _result.Banked[s] += _s.Seats[s].Passive.MatchEndBonusFood;
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
