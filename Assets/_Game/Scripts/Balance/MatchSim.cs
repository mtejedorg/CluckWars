using System;
using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    public enum MatchEventKind : byte { Contact, CastStart, PeckCut, Steal, Kill, Bank }

    /// <summary>Why a running Peck was cut. <see cref="OwnMove"/> is the chicken's own doing (a Jump or a Hide cast).</summary>
    public enum CutCause : byte { None, Stun, Shove, Execution, EmptyPile, OwnMove }

    /// <summary>One thing that happened, for statistics, debugging and the comparison with the real game's telemetry.</summary>
    public struct MatchEvent
    {
        public float Time;
        public MatchEventKind Kind;
        public int Seat;
        /// <summary>The other seat; for a <see cref="MatchEventKind.CastStart"/> of a Peck, the pile index. -1 when there is none.</summary>
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
        /// <summary>Null unless <see cref="MatchSetup.RecordEvents"/> was set.</summary>
        public List<MatchEvent> Events;
    }

    /// <summary>Reach geometry shared by the simulator and its policies: the same rim rule the map bake uses for its legs.</summary>
    public static class MatchGeometry
    {
        private const float Slack = 1e-3f;

        /// <summary>
        /// Distance from a place's centre at which a chicken is in reach, looking along <paramref name="toward"/> (centre to chicken):
        /// the base deposit radius, or the pile's oval plus <see cref="OracleMapSO.CollectReach"/>.
        /// </summary>
        public static float Rim(OracleMapSO map, int place, Vector2 toward)
        {
            var p = map.Places[place];
            if (p.Kind == OraclePlaceKind.Base) return map.BaseRadius;
            return PileFootprintShape.RadiusToward(toward, p.Yaw, p.Footprint) + map.CollectReach;
        }

        /// <summary>True when <paramref name="pos"/> is inside the place's reach (a hair of slack, so a chicken that stopped at the rim counts).</summary>
        public static bool InReach(OracleMapSO map, int place, Vector2 pos)
        {
            var p = map.Places[place];
            Vector2 d = pos - p.Pos;
            float sq = d.sqrMagnitude;
            if (p.Kind == OraclePlaceKind.Base)
            {
                float r = map.BaseRadius + Slack;
                return sq <= r * r;
            }
            float widest = Mathf.Max(p.Footprint.x, p.Footprint.y) * 0.5f + map.CollectReach + Slack;
            if (sq > widest * widest) return false;
            float rim = Rim(map, place, d) + Slack;
            return sq <= rim * rim;
        }
    }

    /// <summary>
    /// One four-player match in the abstract model: fixed ticks, continuous positions on the wall-aware graph of <see cref="OracleNav"/>,
    /// one <see cref="IMatchPolicy"/> per seat. Pure and deterministic for a seed. Spec 2.1 and 2.2.
    /// </summary>
    /// <remarks>
    /// Order inside a tick (my reading of the spec's three phases): (1) completions, seats in rotating order: a Peck whose time is up
    /// (or whose pile ran dry) lands or is cut, a chicken in its base zone deposits, and a seat that just finished something is marked to
    /// think; (2) every marked or due seat thinks on one snapshot taken after the completions; (3) due intents take effect, a Peck starts
    /// when it may, and the chicken walks. Completions come first so that a chicken that finishes at the start of a tick thinks and moves
    /// in that same tick, which is the alignment part 1's <see cref="FarmSim"/> has.
    /// A seat thinks every <see cref="MatchRules.ThinkSeconds"/> and at once when it arrived, a cast landed or was cut, a deposit ended,
    /// its target pile emptied, or its Peck is ready to start. An intent takes effect <see cref="PolicyParams.ReactionDelay"/> later.
    /// Food leaves a pile only when a Peck lands, in rotating seat order, so two seats on one pile alternate as in part 1.
    /// Pile slow: a chicken in reach of any non-empty pile walks at <see cref="FarmRules.PileSlowFactor"/> (judged at the start of the
    /// tick) unless its kit ignores it. A kit that jumps walks straight lines (part 1's jump table has the same approximation).
    /// Left to slice S3: casts, control, Hide, comeback events, kills.
    /// </remarks>
    public static class MatchSim
    {
        private const int Seats = OracleMapSO.SeatCount;
        private const float Eps = 1e-4f;
        private const float StandSlack = 1e-3f;
        private const float TieBanked = 1e-3f;

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
                if (s.Seats[i] != null && s.Policies[i] == null)
                    throw new ArgumentException($"Seat {i} ({s.Seats[i].Label}) has a kit but no policy factory.", nameof(s));
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

        private sealed class Chick
        {
            public bool Present;
            public Vector2 Pos;
            public float Cargo;
            public float Banked;
            public float Visit;
            public bool Pecking;
            public int PeckPile;
            public float PeckDoneAt;
            public float NextPeckAt;
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
        }

        private sealed class Core
        {
            private readonly MatchSetup _s;
            private readonly OracleMapSO _map;
            private readonly OracleNav _nav;
            private readonly FarmRules _farm;
            private readonly float _tick;
            private readonly float _clock;
            private readonly float _slow;
            private readonly int _thinkTicks;
            private readonly int[] _delayTicks = new int[Seats];
            private readonly IMatchPolicy[] _policy = new IMatchPolicy[Seats];
            private readonly MatchRng[] _rng = new MatchRng[Seats];
            private readonly Chick[] _c = new Chick[Seats];
            private readonly float[] _pile = new float[OracleMapSO.PileCount];
            private readonly MatchView _view;
            private readonly MatchResult _result = new MatchResult();
            private float _time;
            private int _i;
            private int _winner = -1;
            private float _winTime;

            public Core(MatchSetup s, ulong matchSeed)
            {
                _s = s;
                _map = s.Map;
                _nav = s.Nav;
                _farm = s.Rules.Farm;
                _tick = _farm.TickSeconds > 0f ? _farm.TickSeconds : FarmRules.DefaultTickSeconds;
                _clock = _farm.ClockSeconds;
                _slow = _farm.PileSlowFactor > 0f ? _farm.PileSlowFactor : 1f;
                float think = s.Rules.ThinkSeconds > 0f ? s.Rules.ThinkSeconds : MatchRules.DefaultThinkSeconds;
                _thinkTicks = Math.Max(1, Mathf.RoundToInt(think / _tick));

                _pile[OracleMapSO.CentreIndex] = s.Food.Centre;
                for (int seat = 0; seat < Seats; seat++)
                {
                    _pile[OracleMapSO.PersonalIndex(seat)] = s.Food.Personal;
                    _pile[OracleMapSO.ContestedIndex(seat)] = s.Food.Contested;
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
                for (int seat = 0; seat < Seats; seat++)
                {
                    _view.Seats[seat].CooldownLeft = new float[1];
                    var kit = s.Seats[seat];
                    var ch = _c[seat] = new Chick { Present = kit != null };
                    if (kit == null) continue;
                    ch.Pos = Spawn(s.Map, seat);
                    var p = s.Params != null ? s.Params[seat] : PolicyParams.Default;
                    float delay = float.IsNaN(p.ReactionDelay) ? 0f : Math.Max(0f, p.ReactionDelay);
                    _delayTicks[seat] = Mathf.RoundToInt(delay / _tick);
                    _policy[seat] = s.Policies[seat](seat, p);
                    if (_policy[seat] == null) throw new InvalidOperationException($"The policy factory of seat {seat} returned null.");
                    _rng[seat] = MatchRng.Create(matchSeed, (ulong)seat);
                }

                _result.Banked = new float[Seats];
                _result.Kills = new int[Seats];
                _result.Winner = -1;
                if (s.RecordEvents) _result.Events = new List<MatchEvent>();
            }

            public MatchResult Run()
            {
                for (_i = 0; ; _i++)
                {
                    _time = _i * _tick;
                    if (_time >= _clock - Eps) break;

                    for (int k = 0; k < Seats; k++) Complete((_i + k) % Seats);
                    if (_winner >= 0) break;

                    BuildView();
                    for (int k = 0; k < Seats; k++) Think((_i + k) % Seats);
                    for (int k = 0; k < Seats; k++) Act((_i + k) % Seats);
                }
                return Finish();
            }

            // ---- phase 1: completions ----

            private void Complete(int s)
            {
                var ch = _c[s];
                if (!ch.Present) return;
                var kit = _s.Seats[s].Farm;

                if (ch.Pecking)
                {
                    bool gone = _pile[ch.PeckPile] <= Eps;
                    if (gone || _time + Eps >= ch.PeckDoneAt) Land(s, gone);
                }

                if (ch.Cargo > Eps && kit.DepositRate > 0f && MatchGeometry.InReach(_map, OracleMapSO.BaseIndex(s), ch.Pos)) Deposit(s, kit);

                if (!ch.HasIntent || ch.Intent.PeckPile < 0) return;
                int pile = ch.Intent.PeckPile;
                if (_pile[pile] <= Eps)
                {
                    if (!ch.EmptyNoted) { ch.EmptyNoted = true; ch.ThinkNow = true; }
                }
                else if (CanStartPeck(s)) ch.ThinkNow = true; // the policy may still call the Peck off
            }

            // A cast ends: the food lands, or the cast is cut because its pile ran dry (cooldown already spent either way).
            private void Land(int s, bool pileGone)
            {
                var ch = _c[s];
                var kit = _s.Seats[s].Farm;
                ch.Pecking = false;
                ch.ThinkNow = true;
                if (pileGone)
                {
                    Record(MatchEventKind.PeckCut, s, ch.PeckPile, 0f, CutCause.EmptyPile);
                    return;
                }
                float take = PeckCastRules.LandingTake(kit.PeckAmount, kit.CargoCapacity - ch.Cargo, _pile[ch.PeckPile]);
                if (take <= 0f) return;
                ch.Cargo += take;
                _pile[ch.PeckPile] -= take;
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
                int pile = ch.Intent.PeckPile;
                var kit = _s.Seats[s].Farm;
                return _time + Eps >= ch.NextPeckAt
                    && _pile[pile] > Eps
                    && ch.Cargo < kit.CargoCapacity - Eps
                    && MatchGeometry.InReach(_map, pile, ch.Pos);
            }

            // ---- phase 2: think ----

            private void BuildView()
            {
                _view.Time = _time;
                _view.TimeLeft = _clock - _time;
                for (int s = 0; s < Seats; s++)
                {
                    var ch = _c[s];
                    ref SeatView v = ref _view.Seats[s];
                    v.Present = ch.Present;
                    v.Seen = ch.Present;
                    v.Pos = ch.Pos;
                    v.Cargo = ch.Cargo;
                    v.Bag = 0f;
                    v.Banked = ch.Banked;
                    v.Pecking = ch.Pecking;
                    v.Stunned = v.Removed = v.Shelled = v.Hidden = false;
                    v.CooldownLeft[0] = Math.Max(0f, ch.NextPeckAt - _time);
                }
            }

            private void Think(int s)
            {
                var ch = _c[s];
                if (!ch.Present || !(ch.ThinkNow || _i >= ch.NextThink)) return;

                _view.Self = s;
                MatchIntent intent = _policy[s].Think(_view, ref _rng[s]);
                ch.Queue.Enqueue(new Pending { Intent = intent, ApplyTick = _i + _delayTicks[s] });
                ch.ThinkNow = false;
                // The first periodic think is staggered by seat so the four do not all think on the same tick.
                ch.NextThink = _i + _thinkTicks + (ch.Thought ? 0 : s * _thinkTicks / Seats);
                ch.Thought = true;
            }

            // ---- phase 3: act ----

            private void Act(int s)
            {
                var ch = _c[s];
                if (!ch.Present) return;

                while (ch.Queue.Count > 0 && ch.Queue.Peek().ApplyTick <= _i) Apply(s, ch.Queue.Dequeue().Intent);

                if (CanStartPeck(s)) StartPeck(s);
                if (!ch.Pecking) Move(s);

                if (ch.HasIntent && !ch.Arrived && Standing(s)) { ch.Arrived = true; ch.ThinkNow = true; }
            }

            private void Apply(int s, MatchIntent intent)
            {
                if (intent.CastSlot >= 0 || intent.CastTarget >= 0
                    || intent.PeckPile < -1 || intent.PeckPile >= OracleMapSO.PileCount
                    || float.IsNaN(intent.Goal.x) || float.IsNaN(intent.Goal.y))
                {
                    _result.RejectedIntents++;
                    return;
                }
                var ch = _c[s];
                if (ch.HasIntent && ch.Intent.SameAs(intent)) return;
                ch.Intent = intent;
                ch.HasIntent = true;
                ch.WaypointValid = false;
                ch.Arrived = false;
                ch.EmptyNoted = false;
            }

            private void StartPeck(int s)
            {
                var ch = _c[s];
                var kit = _s.Seats[s].Farm;
                ch.Pecking = true;
                ch.PeckPile = ch.Intent.PeckPile;
                ch.PeckDoneAt = _time + kit.PeckCastSeconds;
                ch.NextPeckAt = _time + Math.Max(kit.PeckCooldown, kit.PeckCastSeconds);
                Record(MatchEventKind.CastStart, s, ch.PeckPile, 0f, CutCause.None);
                if (kit.PeckCastSeconds <= 0f) Land(s, false); // a zero cast lands at once
            }

            // ---- movement ----

            private Vector2 TargetOf(MatchIntent intent) =>
                intent.PeckPile >= 0 ? _map.Places[intent.PeckPile].Pos : intent.Goal;

            // How close to the target the chicken stops: the rim of the pile it pecks, the base zone when the goal is its own base, else the point itself.
            private float StopDistance(int s, MatchIntent intent, Vector2 pos)
            {
                if (intent.PeckPile >= 0) return MatchGeometry.Rim(_map, intent.PeckPile, pos - _map.Places[intent.PeckPile].Pos);
                int home = OracleMapSO.BaseIndex(s);
                return (intent.Goal - _map.Places[home].Pos).sqrMagnitude < 1e-6f ? _map.BaseRadius : 0f;
            }

            private bool Standing(int s)
            {
                var ch = _c[s];
                if (ch.Intent.PeckPile >= 0) return MatchGeometry.InReach(_map, ch.Intent.PeckPile, ch.Pos);
                return Vector2.Distance(ch.Pos, ch.Intent.Goal) <= StopDistance(s, ch.Intent, ch.Pos) + StandSlack;
            }

            private bool InAnyPileReach(Vector2 pos)
            {
                for (int p = 0; p < OracleMapSO.PileCount; p++)
                    if (_pile[p] > Eps && MatchGeometry.InReach(_map, p, pos)) return true;
                return false;
            }

            private void Move(int s)
            {
                var ch = _c[s];
                if (!ch.HasIntent || Standing(s)) return;
                var kit = _s.Seats[s].Farm;

                float speed = kit.MoveSpeed;
                if (kit.BurstMultiplier > 1f && kit.BurstSeconds > 0f)
                {
                    if (_time >= ch.BurstUntil && _time + Eps >= ch.BurstReadyAt)
                    {
                        ch.BurstUntil = _time + kit.BurstSeconds;
                        ch.BurstReadyAt = _time + kit.BurstCooldown;
                    }
                    if (_time < ch.BurstUntil) speed *= kit.BurstMultiplier;
                }
                if (!kit.IgnoresPileSlow && _slow < 1f && InAnyPileReach(ch.Pos)) speed *= _slow;

                float budget = speed * _tick;
                Vector2 target = TargetOf(ch.Intent);
                for (int hop = 0; hop < 8 && budget > 1e-6f; hop++)
                {
                    if (!ch.WaypointValid)
                    {
                        ch.Waypoint = kit.Jumps ? target : _nav.NextPoint(ch.Pos, target);
                        ch.WaypointValid = true;
                    }
                    Vector2 d = ch.Waypoint - ch.Pos;
                    float len = d.magnitude;
                    bool final = (ch.Waypoint - target).sqrMagnitude < 1e-10f;
                    float allowed = final ? Math.Max(0f, len - StopDistance(s, ch.Intent, ch.Pos)) : len;
                    float step = Math.Min(budget, allowed);
                    budget -= step;

                    if (step < allowed - 1e-6f)
                    {
                        ch.Pos += d / len * step;
                        return;
                    }
                    if (final)
                    {
                        if (len > 1e-6f) ch.Pos += d / len * allowed;
                        return;
                    }
                    ch.Pos = ch.Waypoint;
                    ch.WaypointValid = false;
                }
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
                int best = -1;
                bool tied = false;
                for (int s = 0; s < Seats; s++)
                {
                    if (!_c[s].Present) continue;
                    if (best < 0) { best = s; continue; }
                    float diff = _c[s].Banked - _c[best].Banked;
                    if (Math.Abs(diff) <= TieBanked) diff = 0f;
                    int kills = _result.Kills[s] - _result.Kills[best];
                    if (diff > 0f || (diff == 0f && kills > 0)) { best = s; tied = false; }
                    else if (diff == 0f && kills == 0) tied = true; // level so far: the lower corner (the earlier seat) stays
                }
                _result.Winner = best;
                _result.CornerDecided = tied;
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
