using System;

namespace CluckWars.Balance
{
    /// <summary>
    /// Fixed-tick farming run: one to four kits walking the baked map, pecking the nine shared piles and banking
    /// at their own base. Pure and deterministic. Spec 5.2.
    /// </summary>
    /// <remarks>
    /// Time model: everything happens on tick boundaries. A phase that ends inside a tick is noticed at the next
    /// boundary, so each walk, peck cycle and deposit can be late by up to one tick (the two sub-tick exceptions are
    /// the seat's win time during a deposit and nothing else). Seat order rotates every tick so that two kits
    /// reaching the same pile on the same tick alternate who gets the food.
    /// Pile slow approximation: the game slows a chicken while it stands in reach of a non-empty pile; here each
    /// leg that ends at, or starts from, a non-empty pile has <see cref="OracleMapSO.CollectReach"/> metres walked at
    /// <see cref="FarmRules.PileSlowFactor"/> (the whole leg if it is shorter), judged from the position at the start of each tick.
    /// With <see cref="FarmRules.StopAtTarget"/> each seat stops on its own; the other seats keep farming until the clock ends.
    /// Ties between equally near piles go by an order relative to the chicken's own seat (centre, own personal, next seats' personal,
    /// then the contested edges the same way), so a kit banks the same from every seat on a four-fold symmetric map.
    /// Clock: a chicken carrying food heads home once the time left is no more than the walk home plus the deposit plus one tick, and
    /// does not leave home for a pile when the round trip plus one peck cycle no longer fits.
    /// </remarks>
    public static class FarmSim
    {
        private const float Eps = 1e-4f;
        /// <summary>Two legs within this many metres are a tie; the tie goes to the pile first in the chicken's own seat-relative order.</summary>
        private const float TieMetres = 0.02f;
        private const int Seats = OracleMapSO.SeatCount;

        public static FarmSeatResult[] Run(OracleMapSO map, OracleFood food, FarmSeat[] seats, FarmRules rules)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (seats == null || seats.Length != Seats)
                throw new ArgumentException($"FarmSim needs exactly {Seats} seats (index = seat).", nameof(seats));
            return new Sim(map, food, seats, rules).Run();
        }

        private enum Phase : byte { Idle, Walk, Collect, Deposit, Done }

        private struct Chicken
        {
            public Phase Phase;
            public int At;          // place the chicken stands at (valid when not walking)
            public int From;        // walking: anchor place the path is measured from
            public int To;          // walking: destination place
            public float Lead;      // walking: metres from the walk's start to the anchor (a redirect leaves a lead-in)
            public float Walked;
            public float Total;     // Lead + Leg(From, To)
            public float Carrying;
            public float Banked;
            public float SecondsToTarget;
            public bool Reached;
            public int Trips;
            public bool Pecking;
            public float PeckDoneAt;
            public float NextPeckStart;
            public float BurstReadyAt;
            public float BurstUntil;
        }

        private sealed class Sim
        {
            private readonly OracleMapSO _map;
            private readonly FarmSeat[] _seats;
            private readonly FarmRules _rules;
            private readonly OracleFood _food;
            private readonly float _tick;
            private readonly float _slow;
            private readonly float[] _left = new float[OracleMapSO.PileCount];
            private readonly Chicken[] _c = new Chicken[Seats];
            private readonly int[] _easyContested = new int[Seats];
            private float _time;

            public Sim(OracleMapSO map, OracleFood food, FarmSeat[] seats, FarmRules rules)
            {
                _map = map;
                _food = food;
                _seats = seats;
                _rules = rules;
                _tick = rules.TickSeconds > 0f ? rules.TickSeconds : FarmRules.DefaultTickSeconds;
                _slow = rules.PileSlowFactor > 0f ? rules.PileSlowFactor : 1f;

                _left[OracleMapSO.CentreIndex] = food.Centre;
                for (int s = 0; s < Seats; s++)
                {
                    _left[OracleMapSO.PersonalIndex(s)] = food.Personal;
                    _left[OracleMapSO.ContestedIndex(s)] = food.Contested;
                }

                for (int s = 0; s < Seats; s++)
                {
                    _c[s].At = OracleMapSO.BaseIndex(s);
                    _c[s].SecondsToTarget = -1f;
                    _c[s].Phase = seats[s].Present && seats[s].Kit.Forages ? Phase.Idle : Phase.Done;
                    _easyContested[s] = EasyContested(s);
                }
            }

            // Of the two contested piles next to a base (edges s and s-1), the unwalled one: the walk leg closest to the jump leg.
            private int EasyContested(int seat)
            {
                int b = OracleMapSO.BaseIndex(seat);
                int e1 = OracleMapSO.ContestedIndex(seat);
                int e2 = OracleMapSO.ContestedIndex((seat + Seats - 1) % Seats);
                float r1 = _map.Leg(b, e1, false) / Math.Max(_map.Leg(b, e1, true), Eps);
                float r2 = _map.Leg(b, e2, false) / Math.Max(_map.Leg(b, e2, true), Eps);
                return r2 < r1 ? e2 : e1;
            }

            public FarmSeatResult[] Run()
            {
                float clock = _rules.ClockSeconds;
                float eventAt = clock - Math.Max(_rules.EventSecondsLeft, 0f);
                bool eventDone = _rules.Event == FarmFoodEvent.None;

                for (int i = 0; ; i++)
                {
                    _time = i * _tick;
                    if (_time >= clock - Eps) break;

                    if (!eventDone && _time + Eps >= eventAt)
                    {
                        eventDone = true;
                        FireEvent();
                    }

                    for (int k = 0; k < Seats; k++)
                        Step((i + k) % Seats);
                }

                var result = new FarmSeatResult[Seats];
                for (int s = 0; s < Seats; s++)
                    result[s] = new FarmSeatResult { Banked = _c[s].Banked, SecondsToTarget = _c[s].SecondsToTarget, Trips = _c[s].Trips, Carried = _c[s].Carrying };
                return result;
            }

            private void FireEvent()
            {
                if (_rules.Event == FarmFoodEvent.GoldenPile)
                    _left[OracleMapSO.CentreIndex] += _rules.GoldenPileFood;
                else if (_rules.Event == FarmFoodEvent.Restock)
                    for (int p = 0; p < _left.Length; p++) _left[p] += _rules.RestockFood;
            }

            private void Step(int s)
            {
                // A few zero-time transitions can chain inside one tick (peck lands, decide, start walking).
                for (int guard = 0; guard < 6; guard++)
                {
                    switch (_c[s].Phase)
                    {
                        case Phase.Idle:
                            if (!Decide(s)) return;
                            continue;
                        case Phase.Walk:
                            if (!WalkStep(s)) return;
                            continue;
                        case Phase.Collect:
                            if (!CollectStep(s)) return;
                            continue;
                        case Phase.Deposit:
                            if (!DepositStep(s)) return;
                            continue;
                        default:
                            return;
                    }
                }
            }

            // ---- decisions ----

            private bool AnyFood()
            {
                for (int p = 0; p < _left.Length; p++)
                    if (_left[p] > Eps) return true;
                return false;
            }

            // Food the seat still has to bank: unlimited once it has won and keeps farming.
            private float StillNeeded(in Chicken ch) =>
                ch.Banked < _rules.WinTarget - Eps || _rules.StopAtTarget ? _rules.WinTarget - ch.Banked : float.MaxValue;

            // Full, carrying what it still needs, nothing left to take, or the clock leaves just time to walk home and bank.
            private bool ReturnDue(int s)
            {
                ref Chicken ch = ref _c[s];
                if (ch.Carrying <= Eps) return false;
                return ch.Carrying >= _seats[s].Kit.CargoCapacity - Eps
                    || ch.Carrying >= StillNeeded(ch) - Eps
                    || !AnyFood()
                    || TimeIsShort(s, false);
            }

            private float TimeLeft => _rules.ClockSeconds - _time;

            // Carrying food, and the time left is no more than the walk home (the redirect's triangle estimate when mid-leg) + deposit + one tick.
            // `extraFood` / `extraSeconds` count a peck that is in flight or about to start (its amount lands after its remaining cast).
            private bool TimeIsShort(int s, bool walking, float extraFood = 0f, float extraSeconds = 0f)
            {
                ref Chicken ch = ref _c[s];
                OracleKit kit = _seats[s].Kit;
                if (ch.Carrying <= Eps || kit.DepositRate <= 0f || kit.MoveSpeed <= 0f) return false;
                int home = OracleMapSO.BaseIndex(s);
                float metres = walking ? RedirectCost(s, home, out _) : _map.Leg(ch.At, home, kit.Jumps);
                return TimeLeft <= metres / kit.MoveSpeed + (ch.Carrying + extraFood) / kit.DepositRate + _tick + extraSeconds;
            }

            // A round trip to pile p from here plus one peck cycle fits the clock.
            private bool RoundTripFits(int s, int p)
            {
                OracleKit kit = _seats[s].Kit;
                if (kit.MoveSpeed <= 0f) return true;
                int home = OracleMapSO.BaseIndex(s);
                float metres = _map.Leg(_c[s].At, p, kit.Jumps) + _map.Leg(p, home, kit.Jumps);
                return metres / kit.MoveSpeed + Math.Max(kit.PeckCooldown, kit.PeckCastSeconds) <= TimeLeft;
            }

            // Idle: pick the next thing. Returns true when the phase changed (keep stepping).
            private bool Decide(int s)
            {
                ref Chicken ch = ref _c[s];
                if (_rules.StopAtTarget && ch.Banked >= _rules.WinTarget - Eps) { ch.Phase = Phase.Done; return false; }

                if (ReturnDue(s)) { StartWalk(s, OracleMapSO.BaseIndex(s)); return true; }

                int p = ChoosePile(s, false);
                if (p < 0) return false; // nothing to take: wait here (a food event may refill the piles)
                if (p == ch.At) { ch.Phase = Phase.Collect; return true; }
                if (!RoundTripFits(s, p)) return false; // too late to go out: stay put (carried food was already sent home by ReturnDue)
                StartWalk(s, p);
                return true;
            }

            private void StartWalk(int s, int to)
            {
                ref Chicken ch = ref _c[s];
                ch.From = ch.At;
                ch.To = to;
                ch.Lead = 0f;
                ch.Walked = 0f;
                ch.Total = _map.Leg(ch.From, to, _seats[s].Kit.Jumps);
                ch.Phase = Phase.Walk;
            }

            // Metres to reach pile `p` from the chicken's current position, as the better of "back to the anchor" or "on to the old target".
            private float RedirectCost(int s, int p, out bool viaOld)
            {
                ref Chicken ch = ref _c[s];
                bool jumps = _seats[s].Kit.Jumps;
                float toAnchor = Math.Max(0f, ch.Lead - ch.Walked);
                float past = Math.Max(0f, ch.Walked - ch.Lead);
                float oldLeg = _map.Leg(ch.From, ch.To, jumps);
                float back = toAnchor + past + _map.Leg(ch.From, p, jumps);
                float onward = toAnchor + Math.Max(0f, oldLeg - past) + _map.Leg(ch.To, p, jumps);
                viaOld = onward < back;
                return viaOld ? onward : back;
            }

            private void Redirect(int s, int p)
            {
                ref Chicken ch = ref _c[s];
                float cost = RedirectCost(s, p, out bool viaOld);
                int anchor = viaOld ? ch.To : ch.From;
                float lead = cost - _map.Leg(anchor, p, _seats[s].Kit.Jumps);
                ch.From = anchor;
                ch.To = p;
                ch.Lead = Math.Max(0f, lead);
                ch.Walked = 0f;
                ch.Total = cost;
            }

            private float CostTo(int s, int p, bool walking)
            {
                if (walking) return RedirectCost(s, p, out _);
                return _map.Leg(_c[s].At, p, _seats[s].Kit.Jumps);
            }

            private bool Has(int p, int exclude) => p != exclude && _left[p] > Eps;

            // The pile at position k of seat s's own tie-break order: centre, personal of s, s+1.., then contested edges s, s+1..
            private static int PileAtRank(int s, int k)
            {
                if (k == 0) return OracleMapSO.CentreIndex;
                if (k <= Seats) return OracleMapSO.PersonalIndex((s + k - 1) % Seats);
                return OracleMapSO.ContestedIndex((s + k - 1 - Seats) % Seats);
            }

            private static int RankOf(int s, int p)
            {
                if (p == OracleMapSO.CentreIndex) return 0;
                if (p < OracleMapSO.ContestedIndex(0))
                    return 1 + (p - OracleMapSO.PersonalIndex(0) - s + Seats) % Seats;
                return 1 + Seats + (p - OracleMapSO.ContestedIndex(0) - s + Seats) % Seats;
            }

            private int Nearest(int s, bool walking, int exclude)
            {
                float min = float.MaxValue;
                for (int p = 0; p < OracleMapSO.PileCount; p++)
                    if (Has(p, exclude)) min = Math.Min(min, CostTo(s, p, walking));
                if (min == float.MaxValue) return -1;

                for (int k = 0; k < OracleMapSO.PileCount; k++)
                {
                    int p = PileAtRank(s, k);
                    if (Has(p, exclude) && CostTo(s, p, walking) <= min + TieMetres) return p;
                }
                return -1;
            }

            private int NearestOf(int s, bool walking, int exclude, int a, int b)
            {
                bool ha = Has(a, exclude), hb = Has(b, exclude);
                if (!ha || !hb) return ha ? a : hb ? b : -1;
                int first = RankOf(s, a) <= RankOf(s, b) ? a : b;
                int second = first == a ? b : a;
                return CostTo(s, second, walking) < CostTo(s, first, walking) - TieMetres ? second : first;
            }

            // Route rule -> the pile to go for, or -1 when no pile has food. `walking` redirects from mid-walk; the empty target is excluded.
            private int ChoosePile(int s, bool walking)
            {
                int exclude = walking ? _c[s].To : -1;
                int personal = OracleMapSO.PersonalIndex(s);
                int p;
                switch (_seats[s].Route)
                {
                    case FarmRoute.SidesFirst:
                        p = NearestOf(s, walking, exclude, OracleMapSO.ContestedIndex(s), OracleMapSO.ContestedIndex((s + Seats - 1) % Seats));
                        if (p >= 0) return p;
                        if (Has(personal, exclude)) return personal;
                        break;
                    case FarmRoute.CentreFirst:
                        if (Has(OracleMapSO.CentreIndex, exclude)) return OracleMapSO.CentreIndex;
                        break;
                    case FarmRoute.EasySideThenCentre:
                        if (Has(personal, exclude)) return personal;
                        if (Has(_easyContested[s], exclude)) return _easyContested[s];
                        if (Has(OracleMapSO.CentreIndex, exclude)) return OracleMapSO.CentreIndex;
                        break;
                }
                return Nearest(s, walking, exclude);
            }

            // ---- phases; each returns true when the phase changed and the chicken can keep acting this tick ----

            private bool WalkStep(int s)
            {
                ref Chicken ch = ref _c[s];
                OracleKit kit = _seats[s].Kit;

                if (ch.To < OracleMapSO.PileCount && TimeIsShort(s, true)) Redirect(s, OracleMapSO.BaseIndex(s));

                if (ch.Walked < ch.Total - Eps && ch.To < OracleMapSO.PileCount && _left[ch.To] <= Eps)
                {
                    // The target pile emptied under the walker: go for another, or give up the trip with nothing in hand.
                    if (ch.Carrying > Eps && !AnyFood()) Redirect(s, OracleMapSO.BaseIndex(s));
                    else
                    {
                        int p = ChoosePile(s, true);
                        if (p >= 0) Redirect(s, p);
                        else if (ch.Carrying > Eps) Redirect(s, OracleMapSO.BaseIndex(s));
                    }
                }

                if (ch.Walked >= ch.Total - Eps)
                {
                    ch.At = ch.To;
                    ch.Phase = ch.To >= OracleMapSO.PileCount ? Phase.Deposit : Phase.Collect;
                    return true;
                }

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
                if (!kit.IgnoresPileSlow && _slow < 1f && InPileReach(ch)) speed *= _slow;

                ch.Walked += speed * _tick;
                return false;
            }

            private bool InPileReach(in Chicken ch)
            {
                float reach = _map.CollectReach;
                float s = ch.Walked - ch.Lead;
                bool leaving = ch.From < OracleMapSO.PileCount && _left[ch.From] > Eps && s >= 0f && s < reach;
                bool arriving = ch.To < OracleMapSO.PileCount && _left[ch.To] > Eps && ch.Total - ch.Walked <= reach;
                return leaving || arriving;
            }

            private bool CollectStep(int s)
            {
                ref Chicken ch = ref _c[s];
                OracleKit kit = _seats[s].Kit;
                int p = ch.At;

                // Out of time with food in hand: drop the peck in flight when its food could no longer be banked, and walk home.
                if (ch.Pecking && TimeIsShort(s, false, PeckTake(s, p), ch.PeckDoneAt - _time))
                {
                    ch.Pecking = false;
                    StartWalk(s, OracleMapSO.BaseIndex(s));
                    return true;
                }

                if (ch.Pecking)
                {
                    if (_time + Eps < ch.PeckDoneAt) return false;
                    ch.Pecking = false;
                    float take = PeckTake(s, p);
                    if (take > 0f)
                    {
                        ch.Carrying += take;
                        _left[p] -= take;
                    }
                }

                if (ReturnDue(s)) { StartWalk(s, OracleMapSO.BaseIndex(s)); return true; }
                if (_left[p] <= Eps) { ch.Phase = Phase.Idle; return true; }

                if (_time + Eps < ch.NextPeckStart) return false;
                // Another peck would land too late to bank its food: walk home with what is in hand.
                if (TimeIsShort(s, false, PeckTake(s, p), kit.PeckCastSeconds)) { StartWalk(s, OracleMapSO.BaseIndex(s)); return true; }
                ch.Pecking = true;
                ch.PeckDoneAt = _time + kit.PeckCastSeconds;
                ch.NextPeckStart = _time + Math.Max(kit.PeckCooldown, kit.PeckCastSeconds);
                // A zero cast lands the food at once: loop again to apply it.
                return kit.PeckCastSeconds <= 0f;
            }

            // What a peck at pile p would take now.
            private float PeckTake(int s, int p) =>
                Math.Max(0f, Math.Min(Math.Min(_seats[s].Kit.PeckAmount, _left[p]), _seats[s].Kit.CargoCapacity - _c[s].Carrying));

            private bool DepositStep(int s)
            {
                ref Chicken ch = ref _c[s];
                float rate = _seats[s].Kit.DepositRate;
                if (rate <= 0f) return false; // a kit that cannot deposit stays at its base, banking nothing
                float dep = Math.Min(ch.Carrying, rate * _tick);
                float target = _rules.WinTarget;

                if (!ch.Reached && ch.Banked + dep >= target - Eps)
                {
                    // The win lands inside this deposit: time it to the sub-tick and, when stopping, bank only what was needed.
                    ch.Reached = true;
                    ch.SecondsToTarget = _time + (target - ch.Banked) / rate;
                    if (_rules.StopAtTarget)
                    {
                        ch.Banked = target;
                        ch.Carrying = 0f;
                        ch.Trips++;
                        ch.Phase = Phase.Done;
                        return false;
                    }
                }

                ch.Banked += dep;
                ch.Carrying -= dep;
                if (ch.Carrying > Eps) return false;

                ch.Carrying = 0f;
                ch.Trips++;
                ch.Phase = Phase.Idle;
                return true;
            }
        }
    }
}
