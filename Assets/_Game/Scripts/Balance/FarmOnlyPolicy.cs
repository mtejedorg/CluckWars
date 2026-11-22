using System;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// The farming decisions of part 1's <see cref="FarmSim"/> as a match policy: the route rule picks the pile, the chicken
    /// goes home when it is full, holds what it still needs, nothing is left or the clock leaves just time to bank; it picks
    /// another pile when its target empties, and it neither leaves for a pile nor starts a peck that cannot be banked in time.
    /// It never fights. Used for the equivalence test against <see cref="FarmSim"/> and for the timing run.
    /// </summary>
    /// <remarks>
    /// Costs: standing at a place (inside a pile's reach or the base zone) the cost to a pile is the baked leg, exactly what
    /// part 1 uses; between places it is <see cref="OracleNav.PathLength"/> to the pile centre minus the pile's reach (a straight line
    /// for a jumping kit), which is how the bake builds a leg. Ties go by the seat-relative pile order of part 1.
    /// The decisions are the same however often the policy thinks; the reaction delay only shifts when they take effect.
    /// </remarks>
    public sealed class FarmOnlyPolicy : IMatchPolicy
    {
        private const float Eps = 1e-4f;
        /// <summary>Two costs within this many metres are a tie; the tie goes to the pile first in the seat's own order (as in <see cref="FarmSim"/>).</summary>
        private const float TieMetres = 0.02f;
        private const int Seats = OracleMapSO.SeatCount;

        public static readonly PolicyFactory Factory = (seat, p) => new FarmOnlyPolicy(seat, p);

        private readonly int _seat;
        private readonly FarmRoute _route;
        private int _target = -1;
        private bool _homing;
        private int _easyContested = -1;

        public FarmOnlyPolicy(int seat, PolicyParams p)
        {
            _seat = seat;
            _route = p.Route;
        }

        public MatchIntent Think(MatchView v, ref MatchRng rng)
        {
            var me = v.Seats[_seat];
            var kit = v.Kits[_seat].Farm;
            if (!kit.Forages) return MatchIntent.Idle(me.Pos);

            int home = OracleMapSO.BaseIndex(_seat);
            if (_easyContested < 0) _easyContested = EasyContested(v.Map, _seat);
            int at = PlaceAt(v, me.Pos);

            if (_homing)
            {
                if (at == home && me.Cargo <= Eps) _homing = false;
                else return GoHome(v);
            }

            if (_target >= 0 && v.PileFood[_target] <= Eps) _target = -1; // emptied under the walker or the pecker

            // Between places: a chicken heading for its target that passes through another place's reach is still walking.
            int here = _target >= 0 && at != _target ? -1 : at;
            if (me.Cargo > Eps && ReturnDue(v, kit, me, here, ReadyToPeck(me, here)))
            {
                _homing = true;
                _target = -1;
                return GoHome(v);
            }

            if (_target < 0)
            {
                int p = ChoosePile(v, kit, me.Pos, here);
                if (p < 0) return MatchIntent.Idle(me.Pos); // nothing to take: wait here (an event may refill the piles)
                // Too late to go out and come back: stay put. Carried food was already sent home above.
                if (here >= 0 && here != p && !RoundTripFits(v, kit, here, p)) return MatchIntent.Idle(me.Pos);
                _target = p;
            }
            return Peck(v, _target);
        }

        private MatchIntent GoHome(MatchView v) =>
            new MatchIntent { Goal = v.Map.Places[OracleMapSO.BaseIndex(_seat)].Pos, PeckPile = -1, CastSlot = -1, CastTarget = -1 };

        private static MatchIntent Peck(MatchView v, int pile) =>
            new MatchIntent { Goal = v.Map.Places[pile].Pos, PeckPile = pile, CastSlot = -1, CastTarget = -1 };

        // The place the chicken stands at (its target pile first, then its base, then any pile in reach), -1 between places.
        private int PlaceAt(MatchView v, Vector2 pos)
        {
            int home = OracleMapSO.BaseIndex(_seat);
            if (_target >= 0 && MatchGeometry.InReach(v.Map, _target, pos)) return _target;
            if (MatchGeometry.InReach(v.Map, home, pos)) return home;
            for (int p = 0; p < OracleMapSO.PileCount; p++)
                if (MatchGeometry.InReach(v.Map, p, pos)) return p;
            return -1;
        }

        private static bool AnyFood(MatchView v)
        {
            for (int p = 0; p < v.PileFood.Length; p++)
                if (v.PileFood[p] > Eps) return true;
            return false;
        }

        private static float StillNeeded(MatchView v, SeatView me) =>
            me.Banked < v.Rules.Farm.WinTarget - Eps ? v.Rules.Farm.WinTarget - me.Banked : float.MaxValue;

        // About to start a peck at its target: standing there, not casting, the Peck ready.
        private bool ReadyToPeck(SeatView me, int here) => here == _target && _target >= 0 && !me.Pecking && me.CooldownLeft[0] <= Eps;

        // Full, carrying what it still needs, nothing left to take, or the clock leaves just time to walk home and bank
        // (counting the peck it is about to start, when it is about to).
        private bool ReturnDue(MatchView v, OracleKit kit, SeatView me, int here, bool aboutToPeck)
        {
            if (me.Cargo >= kit.CargoCapacity - Eps
                || me.Cargo >= StillNeeded(v, me) - Eps
                || !AnyFood(v)
                || TimeIsShort(v, kit, me, here, 0f, 0f)) return true;
            if (!aboutToPeck) return false;
            float bite = Math.Min(kit.PeckAmount, v.PileFood[_target]);
            return TimeIsShort(v, kit, me, here, bite, kit.PeckCastSeconds);
        }

        // Metres from here to the pile's reach (or the base zone), as a bake leg when standing at a place.
        private float Cost(MatchView v, OracleKit kit, Vector2 pos, int at, int place)
        {
            if (at >= 0) return v.Map.Leg(at, place, kit.Jumps);
            Vector2 centre = v.Map.Places[place].Pos;
            float path = kit.Jumps ? Vector2.Distance(pos, centre) : v.Nav.PathLength(pos, centre);
            return Mathf.Max(0f, path - MatchGeometry.Rim(v.Map, place, pos - centre));
        }

        // Carrying food, and the time left is no more than the walk home + the deposit + one tick. `extraFood` / `extraSeconds` count a peck about to start.
        private bool TimeIsShort(MatchView v, OracleKit kit, SeatView me, int here, float extraFood, float extraSeconds)
        {
            if (me.Cargo <= Eps || kit.DepositRate <= 0f || kit.MoveSpeed <= 0f) return false;
            int home = OracleMapSO.BaseIndex(_seat);
            float metres = Cost(v, kit, me.Pos, here, home);
            float food = me.Cargo + (extraFood > 0f ? Math.Min(extraFood, Math.Max(0f, kit.CargoCapacity - me.Cargo)) : 0f);
            return v.TimeLeft <= metres / kit.MoveSpeed + food / kit.DepositRate + v.Rules.Farm.TickSeconds + extraSeconds;
        }

        // A round trip to pile p from here plus one peck cycle fits the clock.
        private bool RoundTripFits(MatchView v, OracleKit kit, int at, int p)
        {
            if (kit.MoveSpeed <= 0f) return true;
            int home = OracleMapSO.BaseIndex(_seat);
            float metres = v.Map.Leg(at, p, kit.Jumps) + v.Map.Leg(p, home, kit.Jumps);
            return metres / kit.MoveSpeed + Math.Max(kit.PeckCooldown, kit.PeckCastSeconds) <= v.TimeLeft;
        }

        // ---- the route rules of FarmSim ----

        private static int EasyContested(OracleMapSO map, int seat)
        {
            int b = OracleMapSO.BaseIndex(seat);
            int e1 = OracleMapSO.ContestedIndex(seat);
            int e2 = OracleMapSO.ContestedIndex((seat + Seats - 1) % Seats);
            float r1 = map.Leg(b, e1, false) / Math.Max(map.Leg(b, e1, true), Eps);
            float r2 = map.Leg(b, e2, false) / Math.Max(map.Leg(b, e2, true), Eps);
            return r2 < r1 ? e2 : e1;
        }

        private static bool Has(MatchView v, int p) => v.PileFood[p] > Eps;

        private static int PileAtRank(int s, int k)
        {
            if (k == 0) return OracleMapSO.CentreIndex;
            if (k <= Seats) return OracleMapSO.PersonalIndex((s + k - 1) % Seats);
            return OracleMapSO.ContestedIndex((s + k - 1 - Seats) % Seats);
        }

        private static int RankOf(int s, int p)
        {
            if (p == OracleMapSO.CentreIndex) return 0;
            if (p < OracleMapSO.ContestedIndex(0)) return 1 + (p - OracleMapSO.PersonalIndex(0) - s + Seats) % Seats;
            return 1 + Seats + (p - OracleMapSO.ContestedIndex(0) - s + Seats) % Seats;
        }

        private int Nearest(MatchView v, OracleKit kit, Vector2 pos, int at)
        {
            float min = float.MaxValue;
            for (int p = 0; p < OracleMapSO.PileCount; p++)
                if (Has(v, p)) min = Math.Min(min, Cost(v, kit, pos, at, p));
            if (min == float.MaxValue) return -1;

            for (int k = 0; k < OracleMapSO.PileCount; k++)
            {
                int p = PileAtRank(_seat, k);
                if (Has(v, p) && Cost(v, kit, pos, at, p) <= min + TieMetres) return p;
            }
            return -1;
        }

        private int NearestOf(MatchView v, OracleKit kit, Vector2 pos, int at, int a, int b)
        {
            bool ha = Has(v, a), hb = Has(v, b);
            if (!ha || !hb) return ha ? a : hb ? b : -1;
            int first = RankOf(_seat, a) <= RankOf(_seat, b) ? a : b;
            int second = first == a ? b : a;
            return Cost(v, kit, pos, at, second) < Cost(v, kit, pos, at, first) - TieMetres ? second : first;
        }

        private int ChoosePile(MatchView v, OracleKit kit, Vector2 pos, int at)
        {
            int personal = OracleMapSO.PersonalIndex(_seat);
            int p;
            switch (_route)
            {
                case FarmRoute.SidesFirst:
                    p = NearestOf(v, kit, pos, at, OracleMapSO.ContestedIndex(_seat), OracleMapSO.ContestedIndex((_seat + Seats - 1) % Seats));
                    if (p >= 0) return p;
                    if (Has(v, personal)) return personal;
                    break;
                case FarmRoute.CentreFirst:
                    if (Has(v, OracleMapSO.CentreIndex)) return OracleMapSO.CentreIndex;
                    break;
                case FarmRoute.EasySideThenCentre:
                    if (Has(v, personal)) return personal;
                    if (Has(v, _easyContested)) return _easyContested;
                    if (Has(v, OracleMapSO.CentreIndex)) return OracleMapSO.CentreIndex;
                    break;
            }
            return Nearest(v, kit, pos, at);
        }
    }
}
