using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// P1: a utility chooser that plays the kit it was given. It has no per-class profile (it never reads the kit's bot profile or its
    /// class) and no hidden state: rivals come from the <see cref="MatchView"/> through the game's bot sight rule, casts obey the same
    /// legality as every policy, and no tie between equal options is settled by seat order or index order (each is drawn from the seat's own
    /// stream, only when there is one).
    /// </summary>
    /// <remarks>
    /// Every think it scores its options in food banked per second (a cycle's food, carried food included, over the seconds the cycle takes:
    /// walk to the pile, peck it, walk home, deposit):
    /// farm (the pile with the best cycle, rivals standing there thin it and a rival who can steal puts a risk on it), steal (what the Snatch
    /// would take over the seconds to get in reach and walk it home, with a bonus for taking from whoever is nearest to winning),
    /// bank (carried food at risk, the clock, the win). On top it plays what the kit allows: Stun then Steal on a loaded rival; Egg Shell on
    /// an incoming stealer; Stun, Shove, Slow, Hide or Speed against a chaser when loaded; Shove (or Stun) on a rival in mid-Peck; Hide to
    /// walk up to a steal; and for a Death Mark it holds the stun for the kill window (it presses the mark only when the stun will be back by
    /// the arm), chases the marked rival and stuns so it still runs at the arm (the kill window, the last 2 s today).
    /// Left out, on purpose: waiting out a fight between two others (the sim's rivals do not fight each other much; P1 does not model it),
    /// and any chase for a kit without a steal (a stun with nothing to take is no reason to leave a pile).
    /// </remarks>
    public sealed class UtilityPolicy : IMatchPolicy
    {
        public static readonly PolicyFactory Factory = (seat, p) => new UtilityPolicy(seat, p);

        private const int Seats = OracleMapSO.SeatCount;
        private const float Eps = 1e-4f;
        /// <summary>The bank-it-or-lose-it margin on the walk home, plus the deposit and a little more (P1's own, not the bot's safety factor).</summary>
        private const float BankTimeFactor = 1.2f;
        private const float BankTimeSlack = 0.6f;
        /// <summary>A beak at this share of its capacity has no room for another pile's food worth the walk.</summary>
        private const float FullShare = 0.98f;
        /// <summary>Carried food below this is not worth a shell, a hide or a flight.</summary>
        private const float WorthGuarding = 5f;
        /// <summary>A rival this many seconds from using a ready attack counts as a threat (one think plus a reaction).</summary>
        private const float ThreatLookahead = 0.5f;
        /// <summary>A pile with a rival's Peck at it gives this share of its food.</summary>
        private const float SharedPileShare = 0.75f;
        /// <summary>A pile a stealer can reach is worth this share.</summary>
        private const float StealerRiskShare = 0.85f;
        /// <summary>Preferred route piles are worth this much more.</summary>
        private const float RouteBonus = 1.2f;
        /// <summary>The pile the chicken is working keeps this much more, so it does not flip between two near equals.</summary>
        private const float Stickiness = 1.1f;
        /// <summary>A steal from the leader is worth this much more at 100 % of the win target (and nothing at 0 %).</summary>
        private const float DenyWeight = 0.75f;
        /// <summary>Two rates within this share of each other are a tie and are drawn.</summary>
        private const float TieShare = 0.01f;
        /// <summary>Stun first, steal next, when the rival is further than this share of the steal reach.</summary>
        private const float StunFirstShare = 0.6f;
        /// <summary>The wall-aware walk is computed for this many nearest piles; the rest are taken at their straight line times <see cref="DetourFactor"/>.</summary>
        private const int ExactPiles = 4;
        private const float DetourFactor = 1.25f;
        /// <summary>Walk to a far pile with a Speed burst when it is at least this many metres.</summary>
        private const float SprintMetres = 6f;

        private readonly int _seat;
        private readonly PolicyParams _p;

        private bool _banking;
        private int _target = -1;
        private int _lastVictim = -1;
        private float _shieldUntil;
        private float _hideUntil;

        // One think's working memory.
        private SeatView _me;
        private MatchKit _kit;
        private MatchAbility[] _ab;
        private Vector2 _home;
        private float _speed, _dep, _capacity, _all, _tHome;
        private int _cast;
        private int _break;
        private Vector2[] _baseCentres;
        private readonly bool[] _ok = new bool[Seats];
        private readonly float[] _dist = new float[Seats];
        private readonly float[] _score = new float[Seats];
        private readonly float[] _stealReach = new float[Seats];
        private readonly float[] _value = new float[Seats];
        private readonly int[] _tied = new int[OracleMapSO.PileCount + 1];
        private readonly float[] _line = new float[OracleMapSO.PileCount + 1];
        private readonly float[] _sort = new float[OracleMapSO.PileCount + 1];
        private readonly float[] _rate = new float[OracleMapSO.PileCount + 1];

        public UtilityPolicy(int seat, PolicyParams p)
        {
            _seat = seat;
            _p = p;
        }

        public MatchIntent Think(MatchView v, ref MatchRng rng)
        {
            _me = v.Seats[_seat];
            _kit = v.Kits[_seat];
            _ab = _kit.Abilities ?? Array.Empty<MatchAbility>();
            _cast = -1;
            _break = -1;
            _home = v.Map.Places[OracleMapSO.BaseIndex(_seat)].Pos;
            _speed = Mathf.Max(0.1f, _kit.Farm.MoveSpeed);
            _dep = Mathf.Max(0.5f, _kit.Farm.DepositRate);
            _capacity = Mathf.Max(1f, _me.Capacity);
            _all = _me.Cargo;
            _baseCentres = null;
            float distHome = WalkMetres(v, _me.Pos, _home);
            _tHome = Mathf.Max(0f, distHome - v.Map.BaseRadius) / _speed;
            float arrival = v.Rules.SightArrivalRadius;

            Scan(v);

            // Bank or lose it, then bank the win.
            if (_all > Eps && v.TimeLeft <= _tHome * BankTimeFactor + _all / _dep + BankTimeSlack) _banking = true;
            if (_all > Eps && _me.Banked + _all >= v.Rules.Farm.WinTarget - Eps) _banking = true;
            if (_banking && _all <= Eps) _banking = false;

            // A Death Mark in play owns the think until it pays or fizzles.
            if (MarkLoop(v, arrival, out var markIntent)) return markIntent;

            // A Thief whose beak cannot take another marked steal's bounty banks first (Maestro, 2026-11-24: the loot is ordinary cargo and
            // what does not fit is lost). Only while there is something to bank.
            var lootFacts = _kit.Passive.Loot;
            if (lootFacts.Active && _all > Eps && BotLootRules.NextMarkedStealWouldNotFit(_all, _capacity, lootFacts.Bounty)) _banking = true;

            // Threats and the answer to them (shell, stun, shove, slow, hide, speed); a loaded chicken under threat heads home.
            Defend(v);
            if (_banking) return Finish(_home, -1);

            // The beak is full, or the sweep's return threshold says home.
            float share = _me.Cargo / _capacity;
            float returnAt = float.IsNaN(_p.ReturnThreshold) ? FullShare : _p.ReturnThreshold;
            if (_all > Eps && share >= returnAt)
            {
                _banking = true;
                return Finish(_home, -1);
            }

            // Options: steal, farm.
            bool canSteal = BestSteal(v, ref rng, out int victim, out float stealRate, out Vector2 stealGoal);
            float farmRate = 0f;
            int pile = _kit.Farm.Forages ? ChoosePile(v, ref rng, out farmRate) : -1;
            if (pile < 0) farmRate = 0f;

            if (canSteal && stealRate > farmRate)
            {
                _lastVictim = victim;
                return Strike(v, victim, stealGoal, arrival);
            }
            _lastVictim = -1;

            // The Bully's hit-steal: walk to the rival whose hit pays best per second, when that beats the farm.
            if (BestHit(v, ref rng, out int hitVictim, out float hitRate, out Vector2 hitGoal) && hitRate > farmRate)
            {
                HitForFood(v);
                bool inReach = _cast >= 0 || Vector2.Distance(_me.Pos, hitGoal) <= HitReach() * BotTactics.CommitReachFraction;
                if (!inReach) SprintTo(v, hitGoal);
                return Finish(inReach ? _me.Pos : Reach(_me.Pos, hitGoal, arrival), -1);
            }

            if (pile >= 0)
            {
                _target = pile;
                // A free hit on a rival in mid-Peck beside me costs nothing, so long as it does not cost my own Peck. A hit that takes food comes first.
                if (!HitForFood(v)) PeckCut(v);
                SprintTo(v, PlaceOf(v, pile).Pos);
                return Finish(PlaceOf(v, pile).Pos, pile);
            }

            // Nothing to farm and nothing to steal: bank what is carried (a kit that cannot forage waits to fill half its beak), or walk
            // toward the richest rival.
            _target = -1;
            if (_all > Eps && (_kit.Farm.Forages || _all >= _capacity * 0.5f))
            {
                _banking = true;
                return Finish(_home, -1);
            }
            return Finish(Prowl(v, arrival), -1);
        }

        // The wall-aware walk between two points; the straight line when the graph finds none (a chicken on a wall's edge).
        private static float WalkMetres(MatchView v, Vector2 from, Vector2 to)
        {
            float path = v.Nav.PathLength(from, to);
            return float.IsInfinity(path) ? Vector2.Distance(from, to) : path;
        }

        // ---- perception ----

        private void Scan(MatchView v)
        {
            for (int r = 0; r < Seats; r++)
            {
                var s = v.Seats[r];
                _ok[r] = r != _seat && s.Present && !s.Removed && s.Known;
                _dist[r] = _ok[r] ? Vector2.Distance(_me.Pos, s.Pos) : float.MaxValue;
                _stealReach[r] = _ok[r] ? RivalReach(v, r, true, false) : 0f;
            }
        }

        private Vector2[] BaseCentres(MatchView v)
        {
            if (_baseCentres != null) return _baseCentres;
            int n = 0;
            for (int s = 0; s < Seats; s++) if (v.Seats[s].Present) n++;
            var centres = new Vector2[n];
            n = 0;
            for (int s = 0; s < Seats; s++)
                if (v.Seats[s].Present) centres[n++] = v.Map.Places[OracleMapSO.BaseIndex(s)].Pos;
            return _baseCentres = centres;
        }

        // A cast of this radius at that rival would land now: in range and no base wall between.
        private bool Hits(MatchView v, int rival, float radius)
        {
            var r = v.Seats[rival];
            if (!r.Present || r.Removed || !MatchCombat.InRange(_me.Pos, r.Pos, radius)) return false;
            return !MatchCombat.KeepOutBlocks(_me.Pos, r.Pos, BaseCentres(v), v.Rules.KeepOutRadius);
        }

        private bool Ready(int slot) => _me.CooldownLeft[1 + slot] <= Eps;

        private int Slot(AbilityJob job, bool readyOnly)
        {
            for (int s = 0; s < _ab.Length; s++)
                if (_ab[s].Job == job && (!readyOnly || Ready(s))) return s;
            return -1;
        }


        // The longest reach, among a rival's attacks of this kind that are ready or will be within the look-ahead.
        private float RivalReach(MatchView v, int r, bool stealers, bool stuns)
        {
            var kit = v.Kits[r];
            if (kit.Abilities == null) return 0f;
            float reach = 0f;
            for (int s = 0; s < kit.Abilities.Length; s++)
            {
                var a = kit.Abilities[s];
                bool wanted = stealers && a.IsStealer && a.Job != AbilityJob.DeathMark || stuns && a.Job == AbilityJob.Stun;
                if (!wanted || v.Seats[r].CooldownLeft[1 + s] > ThreatLookahead) continue;
                reach = Mathf.Max(reach, a.Radius);
            }
            return reach;
        }

        // ---- intents ----

        private MatchIntent Finish(Vector2 goal, int pile) =>
            new MatchIntent { Goal = goal, PeckPile = pile, CastSlot = _cast, CastTarget = -1, Break = _break >= 0, BreakTarget = _break };

        // The bot counts as arrived inside its arrival radius: stand where it is instead of walking to the exact point.
        private static Vector2 Reach(Vector2 from, Vector2 to, float arrival) =>
            Vector2.Distance(from, to) <= arrival ? from : to;

        private static OraclePlace PlaceOf(MatchView v, int pile) =>
            pile == MatchGeometry.GoldenPile
                ? new OraclePlace { Kind = OraclePlaceKind.Centre, Seat = -1, Pos = v.GoldenPos, Footprint = v.Map.GoldenFootprint, Yaw = 0f }
                : v.Map.Places[pile];

        // ---- defence ----

        // Loaded and a rival about to hit: answer with the one cast that stops it, then go home. One cast per think.
        private void Defend(MatchView v)
        {
            if (_me.Stunned || _cast >= 0) return;

            // The nearest rival who can steal (or stun) within a think's reach of me.
            int threat = -1;
            float threatDist = float.MaxValue;
            bool threatSteals = false;
            for (int r = 0; r < Seats; r++)
            {
                if (!_ok[r]) continue;
                float reach = RivalReach(v, r, true, true);
                if (reach <= 0f) continue;
                float rivalSpeed = Mathf.Max(0.1f, v.Kits[r].Farm.MoveSpeed);
                float trigger = float.IsNaN(_p.FleeRadius) ? reach + rivalSpeed * ThreatLookahead : _p.FleeRadius;
                if (_dist[r] > trigger) continue;
                if (_dist[r] < threatDist)
                {
                    threatDist = _dist[r];
                    threat = r;
                    threatSteals = RivalReach(v, r, true, false) > 0f;
                }
            }
            if (threat < 0) return;
            bool loaded = _all >= WorthGuarding && (threatSteals || _me.Cargo > Eps);
            if (!loaded) return;
            // Running home pays even when the rival is faster: inside the base zone no cast crosses the wall, a thief has to wait for its next
            // think and its delay, and (measured, 2026-11-23) a loaded Fatty that stays put loses twice the food to P1's thieves.
            bool worthRunning = _all >= _capacity * 0.4f || _all >= WorthGuarding * 2f;

            // 1. Shell up (a Shield under way is left alone).
            if (threatSteals && !_me.Shelled && v.Time + Eps >= _shieldUntil)
            {
                for (int s = 0; s < _ab.Length; s++)
                {
                    if (!_ab[s].IsStealShield || !Ready(s)) continue;
                    _cast = s;
                    _shieldUntil = v.Time + _ab[s].ShieldWalkSeconds + _ab[s].ShieldEggSeconds;
                    _banking = worthRunning;
                    return;
                }
            }
            // 2. Stop the thief where he stands: a stun (it also keeps him from shelling), a shove, a slow.
            if (CastOn(v, threat, AbilityJob.Stun, true) || CastOn(v, threat, AbilityJob.Shove, false) || CastOn(v, threat, AbilityJob.Slow, false)
                || CastSelf(AbilityJob.SlowArea, v, threat))
            {
                if (worthRunning) _banking = true;
                return;
            }
            // 3. Walk away unseen or fast.
            if (worthRunning)
            {
                int hide = Slot(AbilityJob.Hide, true);
                if (hide >= 0 && v.Time + Eps >= _hideUntil) { _cast = hide; _hideUntil = v.Time + _ab[hide].Duration; _banking = true; return; }
                int speed = Slot(AbilityJob.Speed, true);
                if (speed >= 0) { _cast = speed; _banking = true; return; }
                _banking = true;
            }
        }

        // Cast a ready attack of this job at a rival who is in range. A reserved stun (a Death Mark is coming) is never spent here.
        private bool CastOn(MatchView v, int rival, AbilityJob job, bool isStun)
        {
            for (int s = 0; s < _ab.Length; s++)
            {
                var a = _ab[s];
                if (a.Job != job || !Ready(s)) continue;
                if (isStun && StunReserved(s)) continue;
                if (v.Seats[rival].Shelled && job != AbilityJob.Slow) continue;
                if (!v.Seats[rival].Hidden && !Hits(v, rival, a.Radius * BotTactics.CommitReachFraction)) continue;
                if (v.Seats[rival].Hidden) continue;
                _cast = s;
                return true;
            }
            return false;
        }

        private bool CastSelf(AbilityJob job, MatchView v, int rival)
        {
            for (int s = 0; s < _ab.Length; s++)
            {
                var a = _ab[s];
                if (a.Job != job || !Ready(s)) continue;
                float radius = a.AreaAura ? a.AreaRadius : a.Radius;
                if (radius <= 0f || _dist[rival] > radius) continue;
                _cast = s;
                return true;
            }
            return false;
        }

        // The stun is kept for a Death Mark kill when the mark is live or will be ready before the stun would be back.
        private bool StunReserved(int stunSlot)
        {
            int mark = Slot(AbilityJob.DeathMark, false);
            if (mark < 0) return false;
            if (_me.MarkTarget >= 0) return true;
            return _me.CooldownLeft[1 + mark] <= _ab[stunSlot].Cooldown;
        }

        // A rival in mid-Peck within reach of a shove (or stun): cut it. Only right after my own Peck (a cast cuts the running one).
        private void PeckCut(MatchView v)
        {
            if (_cast >= 0 || _me.Stunned || _me.Pecking) return;
            if (BreakFor(v)) return;
            for (int r = 0; r < Seats; r++)
            {
                if (!_ok[r] || !v.Seats[r].Pecking || v.Seats[r].Hidden || v.Seats[r].Shelled) continue;
                if (!v.Kits[r].Farm.Forages) continue;
                if (CastOn(v, r, AbilityJob.Shove, false) || CastOn(v, r, AbilityJob.Stun, true)) return;
            }
        }

        // The Trophy Hunter's break: the press is either a break or a pile Peck, so it is made when what it gains (the bite, the bite the rival does
        // not get, and the trophy's room while a trophy is still to win) is more than the Peck the press would have been.
        private bool BreakFor(MatchView v)
        {
            var facts = _kit.Passive.Break;
            if (!facts.Active || _me.BreakLeft > Eps) return false;
            var trophy = _kit.Passive.Trophy;
            float reaction = float.IsNaN(_p.ReactionDelay) ? 0f : Mathf.Max(0f, _p.ReactionDelay);
            float reach = facts.Reach * BotTactics.CommitReachFraction;
            float free = Mathf.Max(0f, _capacity - _me.Cargo);
            int best = -1;
            float bestValue = 0f;
            for (int r = 0; r < Seats; r++)
            {
                var rival = v.Seats[r];
                if (!_ok[r] || rival.Hidden || rival.Shelled) continue;
                bool walled = MatchCombat.KeepOutBlocks(_me.Pos, rival.Pos, BaseCentres(v), v.Rules.KeepOutRadius);
                if (!BotBreakRules.Candidate(rival.Pecking, rival.PeckCastLeft, _dist[r], reach, walled, rival.BreakImmuneLeft > 0f, reaction)) continue;
                float bite = v.Kits[r].Farm.PeckAmount;
                float value = PeckBreakRules.Bite(bite, bite, free) + bite + (_me.Trophies < trophy.Cap ? trophy.Room : 0);
                if (value > bestValue) { bestValue = value; best = r; }
            }
            if (best < 0 || bestValue <= v.OwnPileBite(_seat)) return false;
            _break = best;
            return true;
        }

        // ---- the Bully's hit-steal ----

        // The longest reach of a hitter of the kit.
        private float HitReach()
        {
            float reach = 0f;
            var facts = _kit.Passive.HitSteal;
            foreach (var a in _ab)
                if ((a.Job == AbilityJob.Stun || a.Job == AbilityJob.Shove) && facts.AmountFor(a.Job) > 0f && a.Radius > reach) reach = a.Radius;
            return reach;
        }

        // What one cast of a ready hitter would take over the rivals it reaches (each held to the room left), and the cast to make: the larger gain,
        // the shorter cooldown on a tie. A free overlay, so it never moves the chicken.
        private bool HitForFood(MatchView v)
        {
            var facts = _kit.Passive.HitSteal;
            if (!facts.Active || _cast >= 0 || _me.Stunned) return false;
            int stun = -1, shove = -1;
            float stunGain = 0f, shoveGain = 0f;
            for (int s = 0; s < _ab.Length; s++)
            {
                var a = _ab[s];
                if ((a.Job != AbilityJob.Stun && a.Job != AbilityJob.Shove) || !Ready(s)) continue;
                float amount = facts.AmountFor(a.Job);
                if (amount <= 0f) continue;
                float left = Mathf.Max(0f, _capacity - _me.Cargo);
                float total = 0f;
                for (int r = 0; r < Seats; r++)
                {
                    if (!_ok[r] || v.Seats[r].Shelled || v.Seats[r].Hidden || !Hits(v, r, a.Radius * BotTactics.CommitReachFraction)) continue;
                    float gain = BotHitStealRules.Gain(amount, StealRules.Stealable(v.Seats[r].Cargo), left, _me.LockLeftOn(r) > Eps);
                    left -= gain;
                    total += gain;
                }
                if (a.Job == AbilityJob.Stun) { stun = s; stunGain = total; } else { shove = s; shoveGain = total; }
            }
            if (!BotHitStealRules.CastForFood(Mathf.Max(stunGain, shoveGain))) return false;
            int pick = BotHitStealRules.PickHitter(stunGain, shoveGain,
                stun >= 0 ? _ab[stun].Cooldown : float.MaxValue, shove >= 0 ? _ab[shove].Cooldown : float.MaxValue);
            if (pick == BotHitStealRules.Stun) _cast = stun;
            else if (pick == BotHitStealRules.Shove) _cast = shove;
            return _cast >= 0;
        }

        // The rival whose hit pays best per second: its food over the seconds to get in reach (or for the hitter to be ready), walk it home and bank it.
        private bool BestHit(MatchView v, ref MatchRng rng, out int victim, out float rate, out Vector2 goal)
        {
            victim = -1;
            rate = 0f;
            goal = _me.Pos;
            var facts = _kit.Passive.HitSteal;
            if (!facts.Active || _me.Stunned) return false;
            float free = Mathf.Max(0f, _capacity - _me.Cargo);
            float winTarget = Mathf.Max(1f, v.Rules.Farm.WinTarget);
            for (int r = 0; r < Seats; r++)
            {
                _score[r] = float.NegativeInfinity;
                _value[r] = 0f;
                if (!_ok[r] || v.Seats[r].Shelled) continue;
                float best = 0f;
                for (int s = 0; s < _ab.Length; s++)
                {
                    var a = _ab[s];
                    float amount = (a.Job == AbilityJob.Stun || a.Job == AbilityJob.Shove) ? facts.AmountFor(a.Job) : 0f;
                    if (amount <= 0f) continue;
                    float gain = BotHitStealRules.Gain(amount, StealRules.Stealable(v.Seats[r].Cargo), free, false);
                    if (!BotHitStealRules.CastForFood(gain)) continue;
                    float wait = Mathf.Max(_me.CooldownLeft[1 + s], Mathf.Max(0f, _dist[r] - a.Radius * BotTactics.CommitReachFraction) / _speed);
                    wait = Mathf.Max(wait, _me.LockLeftOn(r));   // a locked rival gives nothing until the lock ends
                    float homeAfter = Mathf.Max(0f, Vector2.Distance(v.Seats[r].Pos, _home) - v.Map.BaseRadius) / _speed;
                    float carried = _all + gain;
                    float seconds = wait + homeAfter + carried / _dep;
                    if (seconds > v.TimeLeft) continue;
                    float lead = Mathf.Clamp01((v.Seats[r].Banked + v.Seats[r].Cargo) / winTarget);
                    best = Mathf.Max(best, carried / Mathf.Max(Eps, seconds) * (1f + DenyWeight * lead));
                }
                if (best <= 0f) continue;
                _value[r] = best;
                _score[r] = best;
            }
            victim = PickBest(_score, Seats, ref rng);
            if (victim < 0) return false;
            rate = _value[victim];
            goal = v.Seats[victim].Pos;
            return true;
        }

        private void SprintTo(MatchView v, Vector2 goal)
        {
            if (_cast >= 0 || _me.Stunned || Vector2.Distance(_me.Pos, goal) < SprintMetres) return;
            int speed = Slot(AbilityJob.Speed, true);
            if (speed >= 0) _cast = speed;
        }

        // ---- the steal ----

        private bool BestSteal(MatchView v, ref MatchRng rng, out int victim, out float rate, out Vector2 goal)
        {
            victim = -1;
            rate = 0f;
            goal = _me.Pos;
            int steal = Slot(AbilityJob.Steal, false);
            if (steal < 0 || _me.Stunned) return false;
            var a = _ab[steal];
            float free = Mathf.Max(0f, _capacity - _me.Cargo);
            float winTarget = Mathf.Max(1f, v.Rules.Farm.WinTarget);
            float chase = float.IsNaN(_p.HuntDetour) ? float.MaxValue : Mathf.Max(_p.HuntDetour, a.Radius);

            int found = 0;
            for (int r = 0; r < Seats; r++)
            {
                _score[r] = float.NegativeInfinity;
                _value[r] = 0f;
                if (!_ok[r] || v.Seats[r].Shelled || _dist[r] > chase) continue;
                // The loot mark (BL-217): the marked rival is worth what the cast really gains, the stolen food and then the bounty, both held to
                // the free room (ordinary cargo). The take < 1 skip does not apply to it: the bounty pays on an empty bird. A full beak gains nothing.
                bool marked = _me.LootTarget == r;
                var loot = _kit.Passive.Loot;
                float take, bounty = 0f;
                if (marked)
                {
                    LootRules.MarkedGain(a.StealAmount, _kit.Passive.StealFactor, free, v.Seats[r].Cargo, loot.Bounty, out take, out bounty, out _);
                    if (take + bounty < 1f) continue;
                }
                else
                {
                    take = MatchCombat.StealTake(a.StealAmount, _kit.Passive.StealFactor, free, v.Seats[r].Cargo);
                    if (take < 1f) continue;
                }

                float wait = Mathf.Max(_me.CooldownLeft[1 + steal], Mathf.Max(0f, _dist[r] - a.Radius * BotTactics.CommitReachFraction) / _speed);
                float homeAfter = Mathf.Max(0f, Vector2.Distance(v.Seats[r].Pos, _home) - v.Map.BaseRadius) / _speed;
                float carried = _all + take + bounty;
                float seconds = wait + homeAfter + carried / _dep;
                if (seconds > v.TimeLeft) continue;     // no time to bank it
                float lead = Mathf.Clamp01((v.Seats[r].Banked + v.Seats[r].Cargo) / winTarget);
                float value = carried / Mathf.Max(Eps, seconds) * (1f + DenyWeight * lead);
                if (r == _lastVictim && !marked) value *= Stickiness;   // it is the loot that pulls a Thief to the marked bird, not the habit
                _value[r] = value;
                switch (_p.Target)
                {
                    case TargetRule.Nearest: _score[r] = -_dist[r]; break;
                    case TargetRule.Richest: _score[r] = take; break;
                    case TargetRule.Leader: _score[r] = v.Seats[r].Banked + v.Seats[r].Cargo; break;
                    default: _score[r] = value; break;
                }
                found++;
            }
            if (found == 0) return false;

            victim = PickBest(_score, Seats, ref rng);
            if (victim < 0) return false;
            rate = _value[victim];
            goal = v.Seats[victim].Pos;
            return true;
        }

        // Walk up to a victim and hit: Hide on the way when it will carry me there unseen, Stun first when the rival is near the edge of the
        // reach, Steal when it is in reach. One cast per think.
        private MatchIntent Strike(MatchView v, int victim, Vector2 goal, float arrival)
        {
            int steal = Slot(AbilityJob.Steal, false);
            var a = _ab[steal];
            var rival = v.Seats[victim];
            float reach = a.Radius * BotTactics.CommitReachFraction;
            bool inReach = Hits(v, victim, reach) || (rival.Hidden && _dist[victim] <= reach);

            if (inReach && Ready(steal))
            {
                int stun = Slot(AbilityJob.Stun, true);
                bool rivalCanShell = false;
                var rk = v.Kits[victim];
                if (rk.Abilities != null)
                    for (int s = 0; s < rk.Abilities.Length; s++)
                        if (rk.Abilities[s].IsStealShield && rival.CooldownLeft[1 + s] <= Eps) rivalCanShell = true;
                bool stunFirst = stun >= 0 && !StunReserved(stun) && !rivalCanShell && !rival.Stunned && !rival.Hidden
                    && _dist[victim] > reach * StunFirstShare && Hits(v, victim, _ab[stun].Radius * BotTactics.CommitReachFraction);
                if (_cast < 0) _cast = stunFirst ? stun : steal;
            }
            else if (!inReach)
            {
                int hide = Slot(AbilityJob.Hide, true);
                if (_cast < 0 && hide >= 0 && v.Time + Eps >= _hideUntil && !rival.Stunned
                    && _dist[victim] > reach + 1f && _dist[victim] <= _ab[hide].Duration * _speed + reach)
                {
                    _cast = hide;
                    _hideUntil = v.Time + _ab[hide].Duration;
                }
                else SprintTo(v, goal);
            }
            return Finish(inReach ? _me.Pos : Reach(_me.Pos, goal, arrival), -1);
        }

        // ---- the Death Mark ----

        private bool MarkLoop(MatchView v, float arrival, out MatchIntent intent)
        {
            intent = default;
            int markSlot = Slot(AbilityJob.DeathMark, false);
            if (markSlot < 0) return false;
            var mk = _ab[markSlot];

            if (_me.MarkTarget < 0)
            {
                // Press the mark only when the stun will be back by the arm, and not while loaded to the return threshold (bank first: a kill's
                // loot is ordinary cargo, so a Spoiler holding some food is no reason to stop marking, a full one is).
                int stun = Slot(AbilityJob.Stun, false);
                bool stunBack = stun >= 0 && _me.CooldownLeft[1 + stun] <= mk.MarkArmSeconds;
                float returnAt = float.IsNaN(_p.ReturnThreshold) ? FullShare : _p.ReturnThreshold;
                bool loadedToBank = _all > Eps && BotTactics.LoadedToBank(_me.Cargo, _me.Cargo / _capacity, returnAt, _banking);
                if (!loadedToBank && !_me.Stunned && Ready(markSlot) && stunBack) _cast = markSlot;
                return false;
            }

            var mark = v.Seats[_me.MarkTarget];
            if (!mark.Present || mark.Removed || !mark.Known) return false;

            // The longest ready stun that would land now on this target.
            int stunSlot = -1;
            float stunSeconds = 0f;
            bool stunnable = !mark.Shelled && !mark.Hidden && !_me.Stunned;
            var victimPassive = v.Kits[_me.MarkTarget].Passive;
            for (int s = 0; stunnable && s < _ab.Length; s++)
            {
                var a = _ab[s];
                if (a.Control.Kind != ControlKind.Stun || a.Control.Seconds <= 0f || !Ready(s) || !Hits(v, _me.MarkTarget, a.Radius * BotTactics.CommitReachFraction)) continue;
                float onTarget = MatchCombat.ControlSeconds(a.Control, _kit.Passive, victimPassive);
                if (onTarget > stunSeconds) { stunSeconds = onTarget; stunSlot = s; }
            }

            bool armed = _me.ArmLeft <= Eps;
            bool canKill = !_me.Stunned && ExecuteRules.KillQualifies(armed, mark.Stunned, mark.Shelled)
                && MatchCombat.InRange(_me.Pos, mark.Pos, mk.MarkKillRange)
                && !MatchCombat.KeepOutBlocks(_me.Pos, mark.Pos, BaseCentres(v), v.Rules.KeepOutRadius);
            var step = BotTactics.NextMarkStep(true, canKill, _me.ArmLeft, _me.WindowLeft, stunSlot >= 0, stunSeconds);
            if (step == BotMarkStep.Stun && _cast < 0) _cast = stunSlot;
            else if (step == BotMarkStep.Kill && _cast < 0) _cast = markSlot;
            else if (step == BotMarkStep.Hunt) SprintTo(v, mark.Pos);
            intent = Finish(Reach(_me.Pos, mark.Pos, arrival), -1);
            return true;
        }

        // ---- farming ----

        // The pile with the best cycle (carried food over walk, peck, walk home and deposit), thinned by rivals at it. -1 when nothing is left.
        private int ChoosePile(MatchView v, ref MatchRng rng, out float best)
        {
            best = 0f;
            int n = 0;
            float free = Mathf.Max(0f, _capacity - _me.Cargo);
            float period = Mathf.Max(0.05f, Mathf.Max(_kit.Farm.PeckCooldown, _kit.Farm.PeckCastSeconds));
            float peck = Mathf.Max(0.1f, _kit.Farm.PeckAmount);

            // The wall-aware walk is the dear part of a think (about 30 microseconds a pile), so it is asked for the nearest few piles only; a
            // further pile is taken at its straight line times a detour factor (the walls bend a path by up to 1.5 on this map), which only
            // keeps it from winning on a distance that is too short.
            int near = 0;
            for (int k = 0; k <= OracleMapSO.PileCount; k++)
            {
                int pile = k < OracleMapSO.PileCount ? k : MatchGeometry.GoldenPile;
                float available = pile == MatchGeometry.GoldenPile ? (v.GoldenActive ? v.GoldenFood : 0f) : v.PileFood[pile];
                if (available <= Eps) { _line[k] = float.MaxValue; continue; }
                _line[k] = Vector2.Distance(_me.Pos, PlaceOf(v, pile).Pos);
                near++;
            }
            float exactWithin = float.MaxValue;
            if (near > ExactPiles)
            {
                Array.Copy(_line, _sort, _line.Length);
                Array.Sort(_sort);
                exactWithin = _sort[ExactPiles - 1];
            }

            for (int k = 0; k <= OracleMapSO.PileCount; k++)
            {
                int pile = k < OracleMapSO.PileCount ? k : MatchGeometry.GoldenPile;
                float available = pile == MatchGeometry.GoldenPile ? (v.GoldenActive ? v.GoldenFood : 0f) : v.PileFood[pile];
                if (available <= Eps) continue;
                var place = PlaceOf(v, pile);

                float food = Mathf.Min(available, free);
                if (food <= Eps) continue;
                bool shared = false, risky = false;
                for (int r = 0; r < Seats; r++)
                {
                    if (!_ok[r]) continue;
                    var s = v.Seats[r];
                    if (MatchGeometry.InReach(v.Map, place, s.Pos) && s.Pecking) shared = true;
                    if (_stealReach[r] > 0f && Vector2.Distance(s.Pos, place.Pos) <= _stealReach[r] + 1f) risky = true;
                }
                if (shared) food *= SharedPileShare;

                Vector2 toward = _me.Pos - place.Pos;
                float path = pile == MatchGeometry.GoldenPile ? toward.magnitude
                    : _line[k] <= exactWithin ? WalkMetres(v, _me.Pos, place.Pos) : _line[k] * DetourFactor;
                float toPile = Mathf.Max(0f, path - MatchGeometry.Rim(v.Map, place, toward));
                float fromPile = pile == MatchGeometry.GoldenPile
                    ? Mathf.Max(0f, Vector2.Distance(place.Pos, _home) - v.Map.BaseRadius)
                    : v.Map.Leg(pile, OracleMapSO.BaseIndex(_seat), _kit.Farm.Jumps);
                float pecks = Mathf.Ceil(food / peck);
                float seconds = toPile / _speed + pecks * period + fromPile / _speed + (_me.Cargo + food) / _dep;
                if (seconds > v.TimeLeft) continue;     // a cycle that does not fit the clock
                float rate = (_me.Cargo + food) / Mathf.Max(Eps, seconds);
                if (risky) rate *= StealerRiskShare;
                if (RoutePrefers(pile)) rate *= RouteBonus;
                if (pile == _target) rate *= Stickiness;

                _tied[n] = pile; _rate[n] = rate; n++;
                if (rate > best) best = rate;
            }
            if (n == 0) return -1;

            int ties = 0;
            for (int i = 0; i < n; i++) if (_rate[i] >= best * (1f - TieShare)) ties++;
            int pick = ties > 1 ? rng.NextInt(ties) : 0;
            for (int i = 0; i < n; i++)
            {
                if (_rate[i] < best * (1f - TieShare)) continue;
                if (pick-- > 0) continue;
                return _tied[i];
            }
            return -1;
        }

        // The route axis as a preference over piles; Nearest prefers none.
        private bool RoutePrefers(int pile)
        {
            switch (_p.Route)
            {
                case FarmRoute.CentreFirst: return pile == OracleMapSO.CentreIndex;
                case FarmRoute.SidesFirst: return pile == OracleMapSO.ContestedIndex(_seat) || pile == OracleMapSO.ContestedIndex((_seat + Seats - 1) % Seats);
                case FarmRoute.EasySideThenCentre: return pile == OracleMapSO.PersonalIndex(_seat);
                default: return false;
            }
        }

        // A chicken with nothing to farm and nothing in reach to take walks toward the richest rival it knows (a stalker), else the middle.
        private Vector2 Prowl(MatchView v, float arrival)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int r = 0; r < Seats; r++)
            {
                if (!_ok[r]) continue;
                float score = v.Seats[r].Cargo + v.Seats[r].Banked * 0.5f - _dist[r] * 0.1f;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            return best >= 0 ? Reach(_me.Pos, v.Seats[best].Pos, arrival) : Vector2.zero;
        }

        // The index of the highest score, a tie (within a small share) drawn from the seat's stream; -1 when every score is -infinity.
        private int PickBest(float[] scores, int n, ref MatchRng rng)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++) best = Mathf.Max(best, scores[i]);
            if (float.IsNegativeInfinity(best)) return -1;
            float band = Mathf.Abs(best) * TieShare;
            int ties = 0;
            for (int i = 0; i < n; i++) if (scores[i] >= best - band && !float.IsNegativeInfinity(scores[i])) ties++;
            int pick = ties > 1 ? rng.NextInt(ties) : 0;
            for (int i = 0; i < n; i++)
            {
                if (float.IsNegativeInfinity(scores[i]) || scores[i] < best - band) continue;
                if (pick-- > 0) continue;
                return i;
            }
            return -1;
        }
    }
}
