using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// P0: the game's bot (<c>BotController.Think</c>) as a match policy. The priority list is the game's, in its order, and every pure
    /// decision is the game's own function, called: <see cref="BotTactics.MustBankNow"/>, <see cref="BotTactics.ReturnThreshold"/>,
    /// <see cref="BotTactics.ResolvePhase"/>, <see cref="BotTactics.NextMarkStep"/>, <see cref="BotTactics.CastPlan"/>,
    /// <see cref="BotTactics.PileCost"/>, <see cref="BotTactics.TargetPriority"/>, <see cref="BotTactics.ShouldStealGuard"/> and
    /// <see cref="BotTactics.ScoreCastCandidate"/>. It reads a rival only from the <see cref="MatchView"/>, so Hide counts through
    /// <see cref="MatchSight"/>. A Thief with the loot mark on also plays <see cref="BotLootRules"/> (BL-217, round 2): chase the marked rival, keep
    /// Steal for it, bank when the next marked steal would not fit.
    /// </summary>
    /// <remarks>
    /// Mirrored, not called (each pinned by a test against the prefab or the source): the hysteresis factor of the state exits and the
    /// stalk standoff fraction (serialized on <c>BotController</c>, private); the walk to a goal stops inside the bot's arrival radius
    /// (<see cref="MatchRules.SightArrivalRadius"/>, the same field the sight rule uses). Differences from the game, all small and listed
    /// in the plan: one cast per think (the intent carries one); no facing (the simulator has no heading, so the game's "turn first, cast
    /// next think" costs nothing here); the walk-home estimate ignores the chicken's own slow; the deposit estimate uses the kit's own rate;
    /// the think interval is the simulator's, not the class profile's; the Route parameter does not apply (the bot's own pile cost decides);
    /// <see cref="PolicyParams.HuntDetour"/> replaces the profile's hunt radius.
    /// </remarks>
    public sealed class ScriptedPolicy : IMatchPolicy
    {
        /// <summary>Mirror of <c>BotController._stateExitRadiusFactor</c> (serialized default; <c>ScriptedPolicyTests</c> compares it with the prefab).</summary>
        public const float StateExitRadiusFactor = 1.35f;
        /// <summary>Mirror of <c>BotController._stalkStandoffFraction</c> (serialized default; compared with the prefab).</summary>
        public const float StalkStandoffFraction = 0.80f;

        private const int Seats = OracleMapSO.SeatCount;
        private const float Eps = 1e-4f;

        public static readonly PolicyFactory Factory = (seat, p) => new ScriptedPolicy(seat, p);

        private enum State { Idle, CollectFood, ReturnToBase, Flee, Hunt, Stalk, Guard, Search }

        private readonly int _seat;
        private readonly PolicyParams _params;
        private BotProfile _profile;
        private bool _ready;
        private State _state = State.Idle;
        private float _shieldUntil;

        // One think's working memory.
        private int _cast;
        private int _nearest;
        private float _nearestDist;
        private int _prey;
        private float _preyDist;
        private float _preyCargo;
        private MatchPhase _phase;
        private bool _leading;
        private bool _holdSteal;

        public ScriptedPolicy(int seat, PolicyParams p)
        {
            _seat = seat;
            _params = p;
        }

        public MatchIntent Think(MatchView v, ref MatchRng rng)
        {
            var me = v.Seats[_seat];
            var kit = v.Kits[_seat];
            if (!_ready)
            {
                _profile = Overridden(kit.Bot, _params);
                _ready = true;
            }
            _cast = -1;
            var abilities = kit.Abilities ?? Array.Empty<MatchAbility>();
            Vector2 home = v.Map.Places[OracleMapSO.BaseIndex(_seat)].Pos;
            float arrival = v.Rules.SightArrivalRadius;

            Perceive(v, me);

            float cargo = me.Cargo;
            float capacity = Mathf.Max(1, kit.Farm.CargoCapacity);
            float cargoFraction = Mathf.Clamp01(cargo / capacity);
            float distHome = Vector2.Distance(me.Pos, home);

            // Priority 0: BANK OR LOSE IT. A Thief with the loot mark banks by its own rule (the clock, the win, a full beak for the next mark).
            float depositRate = Mathf.Max(0.5f, kit.Farm.DepositRate);
            bool lootThief = kit.Passive.Loot.Active;
            if (lootThief)
            {
                if (BotLootRules.BankNow(cargo, capacity, kit.Passive.Loot.Bounty, me.Banked, Mathf.Max(1f, v.Rules.Farm.WinTarget), distHome,
                        kit.Farm.MoveSpeed, depositRate, v.TimeLeft))
                    return GoHome(v, home);
            }
            else if (BotTactics.MustBankNow(cargo, distHome, kit.Farm.MoveSpeed, v.TimeLeft, cargo / depositRate))
                return GoHome(v, home);

            // Overlay (never returns): a loaded bot with a stealer in reach pulls up a steal-proof shell.
            StealGuard(v, me, abilities, cargo);

            // A Death Mark in progress owns the bot until it pays or fizzles. An execute's loot is ordinary cargo (no bag since 2026-11-24): a Spoiler
            // that just killed holds at least the bounty, which is above the return threshold of a 25 beak (0.30 of 25 is 7.5), so the deposit rule goes home.
            if (FollowThroughMark(v, me, kit, abilities,
                    BotTactics.LoadedToBank(cargo, cargoFraction, BotTactics.ReturnThreshold(_profile, _phase, _leading), _state == State.ReturnToBase), arrival, out var markIntent)) return markIntent;

            // Latch ReturnToBase until all cargo is deposited.
            if (_state == State.ReturnToBase && cargo > 0f)
            {
                if (distHome <= v.Map.BaseRadius) TryCast(v, me, abilities, BotSituation.Banking, -1);
                else GuardOverlay(v, me, abilities);
                return Finish(v, home, -1);
            }

            // Hysteresis: while already fleeing, the trigger radius grows.
            float dangerR = _state == State.Flee ? _profile.DangerRadius * StateExitRadiusFactor : _profile.DangerRadius;

            // Priority 1: RETREAT.
            if (cargoFraction >= _profile.ProtectCargoThreshold && _nearest >= 0 && _nearestDist <= dangerR)
            {
                _state = State.Flee;
                TryCast(v, me, abilities, BotSituation.Retreating, _nearest);
                return Finish(v, home, -1);
            }

            // Priority 2: DEPOSIT (a loot Thief decided at priority 0).
            if (!lootThief && cargoFraction >= BotTactics.ReturnThreshold(_profile, _phase, _leading)) return GoHome(v, home);

            // Priority 2b: THE LOOT MARK. Walk to the marked rival at any distance and keep Steal for it.
            if (lootThief && ChaseMarked(v, me, abilities, arrival, out var lootIntent)) return lootIntent;

            // Priority 3: HUNT / STALK.
            if (_prey >= 0 && Engage(v, me, abilities, arrival, out var engageIntent)) return engageIntent;

            // Priority 4: GUARD (the Bunker holds its base).
            if (_profile.Strategy == BotStrategy.Bunker && _profile.GuardRadius > 0f && _nearest >= 0
                && Vector2.Distance(v.Seats[_nearest].Pos, home) <= _profile.GuardRadius)
            {
                _state = State.Guard;
                TryCast(v, me, abilities, BotSituation.Guarding, _nearest);
                return Finish(v, home, -1);
            }

            // Priority 5: SEARCH (the Predator cannot forage: it walks to whoever is nearest).
            if (_profile.Strategy == BotStrategy.Predator)
            {
                if (_nearest >= 0)
                {
                    _state = State.Search;
                    return Finish(v, Reach(me.Pos, v.Seats[_nearest].Pos, arrival), -1);
                }
                _state = State.Idle;
                return Finish(v, me.Pos, -1);
            }

            // Priority 6: COLLECT, the cheapest pile by round-trip cost.
            int pile = SelectPile(v, me.Pos, home, ref rng, out float pileDist);
            if (pile >= 0)
            {
                _state = State.CollectFood;
                bool contested = _nearest >= 0 && SurfaceDistance(v, pile, v.Seats[_nearest].Pos) < arrival;
                if (contested) TryCast(v, me, abilities, BotSituation.Contesting, _nearest);
                else if (!GuardOverlay(v, me, abilities) && pileDist >= BotController.MinSprintWorthDistance)
                    TryCast(v, me, abilities, BotSituation.Transiting, -1);
                return Finish(v, PlaceOf(v, pile).Pos, pile);
            }

            // Priority 7: IDLE.
            _state = State.Idle;
            return Finish(v, me.Pos, -1);
        }

        // ---- the profile ----

        private static BotProfile Overridden(BotProfile b, PolicyParams p) => new BotProfile(
            b.Strategy,
            float.IsNaN(p.ReturnThreshold) ? b.ReturnThreshold : p.ReturnThreshold,
            b.ProtectCargoThreshold,
            float.IsNaN(p.FleeRadius) ? b.DangerRadius : p.FleeRadius,
            float.IsNaN(p.HuntDetour) ? b.HuntRadius : p.HuntDetour,
            b.HuntCargoThreshold, b.EngageRadius, b.GuardRadius, b.RoundTripBias, b.LeaderFocus, b.ThinkInterval);

        // ---- the intents ----

        // A goal behind a wall or outside the arena (a stalk standoff point) is made walkable by the simulator, as the bot's navmesh sampling does.
        private MatchIntent Finish(MatchView v, Vector2 goal, int pile) =>
            new MatchIntent { Goal = goal, PeckPile = pile, CastSlot = _cast, CastTarget = -1 };

        private MatchIntent GoHome(MatchView v, Vector2 home)
        {
            _state = State.ReturnToBase;
            return Finish(v, home, -1);
        }

        // The bot counts as arrived inside its arrival radius: it stands where it is instead of walking to the exact point.
        private static Vector2 Reach(Vector2 from, Vector2 to, float arrival) =>
            Vector2.Distance(from, to) <= arrival ? from : to;

        // ---- perception ----

        private void Perceive(MatchView v, in SeatView me)
        {
            _nearest = _prey = -1;
            _nearestDist = _preyDist = float.MaxValue;
            _preyCargo = 0f;

            float winTarget = Mathf.Max(1f, v.Rules.Farm.WinTarget);
            float preyRadius = Mathf.Max(_profile.HuntRadius, _profile.EngageRadius);
            float bestScore = float.NegativeInfinity;
            var rule = _params.Target;

            // Rivals are looked at in the seat's own order (the next seat first), so a tie in distance or value goes the same way for every
            // seat. The game's order is the spawn order, which a simulated table has no stand-in for; seat 0 first would give the lowest seat an edge.
            for (int k = 1; k < Seats; k++)
            {
                int r = (_seat + k) % Seats;
                var rival = v.Seats[r];
                if (!rival.Present || rival.Removed || !rival.Known) continue;   // a rival the bot gave up is dropped

                float dist = Vector2.Distance(me.Pos, rival.Pos);
                if (dist < _nearestDist) { _nearestDist = dist; _nearest = r; }
                if (preyRadius <= 0f) continue;

                float capacity = Mathf.Max(1, v.Kits[r].Farm.CargoCapacity);
                float frac = Mathf.Clamp01(StealRules.Stealable(rival.Cargo) / capacity);
                float banked = Mathf.Clamp01(rival.Banked / winTarget);
                float priority = BotTactics.TargetPriority(dist, frac, banked, preyRadius, _profile.LeaderFocus);
                if (priority <= 0f) continue;

                float score;
                switch (rule)
                {
                    case TargetRule.Nearest: score = -dist; break;
                    case TargetRule.Richest: score = frac; break;
                    case TargetRule.Leader: score = banked; break;
                    default: score = priority; break;
                }
                if (score > bestScore) { bestScore = score; _prey = r; _preyDist = dist; _preyCargo = frac; }
            }
            if (_nearest < 0) _nearestDist = float.MaxValue;

            _phase = BotTactics.ResolvePhase(v.TimeLeft, v.Rules.Farm.ClockSeconds);
            _leading = IsLeading(v, me);
            _holdSteal = false;
        }

        private bool IsLeading(MatchView v, in SeatView me)
        {
            for (int r = 0; r < Seats; r++)
                if (r != _seat && v.Seats[r].Present && v.Seats[r].Banked > me.Banked) return false;
            return true;
        }

        // ---- casting ----

        // The bot's cast gate: the plan's roles in order, ready, usable, in reach at the commit fraction. One cast per think.
        private bool TryCast(MatchView v, in SeatView me, MatchAbility[] abilities, BotSituation situation, int target)
        {
            if (_cast >= 0 || me.Stunned || abilities.Length == 0) return false;

            var plan = BotTactics.CastPlan(_profile.Strategy, situation, targetIsLoaded: _preyCargo > 0f && target == _prey);
            if (plan.Length == 0) return false;

            bool haveTarget = target >= 0;
            Vector2 targetPos = haveTarget ? v.Seats[target].Pos : Vector2.zero;
            float targetDist = haveTarget ? Vector2.Distance(me.Pos, targetPos) : 0f;

            int best = -1;
            float bestScore = float.MaxValue;
            for (int slot = 0; slot < abilities.Length; slot++)
            {
                var ab = abilities[slot];
                if (ab.Job == AbilityJob.Peck || !Ready(me, slot)) continue;
                if (_holdSteal && ab.Job == AbilityJob.Steal) continue;   // kept for the marked rival
                int priority = Array.IndexOf(plan, ab.Role);
                if (priority < 0) continue;
                if (ab.NeedsEnemyInRange && !AnyRivalInRange(v, me, ab)) continue;

                if (!float.IsPositiveInfinity(ab.Reach))
                {
                    // A reach-having ability with nobody to point it at is a whiff by construction.
                    if (!haveTarget) continue;
                    if (targetDist > ab.Reach * BotTactics.CommitReachFraction) continue;
                }
                float score = BotTactics.ScoreCastCandidate(priority, ab.Cooldown);
                if (score < bestScore) { bestScore = score; best = slot; }
            }
            if (best < 0) return false;

            var chosen = abilities[best];
            // "Would it land": a hidden target is cast at on belief alone, as the game's bot does (its check reads the real position).
            if (chosen.NeedsEnemyInRange && (!haveTarget || (!v.Seats[target].Hidden && !WouldAffect(v, me, chosen, target)))) return false;

            _cast = best;
            return true;
        }

        private static bool Ready(in SeatView me, int slot) => me.CooldownLeft[1 + slot] <= Eps;

        // A rival the cast would hit now (range and the base-wall pair rule; a steal needs something to take).
        private bool WouldAffect(MatchView v, in SeatView me, in MatchAbility ab, int rival)
        {
            var r = v.Seats[rival];
            if (!r.Present || r.Removed || !MatchCombat.InRange(me.Pos, r.Pos, ab.Radius)) return false;
            if (MatchCombat.KeepOutBlocks(me.Pos, r.Pos, BaseCentres(v), v.Rules.KeepOutRadius)) return false;
            return ab.Job != AbilityJob.Steal || StealRules.Stealable(r.Cargo) > 0f || me.LootTarget == rival;   // the Thief's mark needs no food to be taken
        }

        private bool AnyRivalInRange(MatchView v, in SeatView me, in MatchAbility ab)
        {
            for (int r = 0; r < Seats; r++)
                if (r != _seat && v.Seats[r].Known && WouldAffect(v, me, ab, r)) return true;
            return false;
        }

        private static Vector2[] BaseCentres(MatchView v)
        {
            int n = 0;
            for (int s = 0; s < Seats; s++) if (v.Seats[s].Present) n++;
            var centres = new Vector2[n];
            n = 0;
            for (int s = 0; s < Seats; s++)
                if (v.Seats[s].Present) centres[n++] = v.Map.Places[OracleMapSO.BaseIndex(s)].Pos;
            return centres;
        }

        // A Guarding strategy punishes anything that comes near, without abandoning what it is doing.
        private bool GuardOverlay(MatchView v, in SeatView me, MatchAbility[] abilities)
        {
            if (_profile.GuardRadius <= 0f || _nearest < 0 || _nearestDist > _profile.GuardRadius) return false;
            return TryCast(v, me, abilities, BotSituation.Guarding, _nearest);
        }

        // Shell up when a loaded bot has a stealer in reach and a shell ready (a Shield under way is left alone).
        private void StealGuard(MatchView v, in SeatView me, MatchAbility[] abilities, float cargo)
        {
            if (me.Stunned || _cast >= 0) return;

            float nearest = float.MaxValue;
            for (int k = 1; k < Seats; k++)
            {
                int r = (_seat + k) % Seats;
                var rival = v.Seats[r];
                if (!rival.Present || rival.Removed || !rival.Known) continue;
                float dist = Vector2.Distance(me.Pos, rival.Pos);
                if (dist >= nearest || dist > BotTactics.StealGuardRadius) continue;
                if (HasStealAbility(v.Kits[r])) nearest = dist;
            }

            if (v.Time + Eps < _shieldUntil) return;
            if (cargo < BotTactics.StealGuardMinCargo) return;
            if (!BotTactics.ShouldStealGuard(cargo, nearest, me.Shelled)) return;

            for (int slot = 0; slot < abilities.Length; slot++)
            {
                var ab = abilities[slot];
                if (!ab.IsStealShield || !Ready(me, slot)) continue;
                _cast = slot;
                _shieldUntil = v.Time + ab.ShieldWalkSeconds + ab.ShieldEggSeconds;
                return;
            }
        }

        private static bool HasStealAbility(MatchKit kit)
        {
            if (kit.Abilities == null) return false;
            foreach (var ab in kit.Abilities) if (ab.IsStealer) return true;
            return false;
        }

        // ---- the Spoiler's mark ----

        private bool FollowThroughMark(MatchView v, in SeatView me, MatchKit kit, MatchAbility[] abilities, bool loadedToBank, float arrival,
            out MatchIntent intent)
        {
            intent = default;
            int markSlot = -1;
            for (int a = 0; a < abilities.Length; a++) if (abilities[a].Job == AbilityJob.DeathMark) { markSlot = a; break; }
            if (markSlot < 0) return false;

            if (me.MarkTarget < 0)
            {
                // A bot loaded to its return threshold banks first: the loot of a kill is worth more than a new mark.
                if (loadedToBank || me.Stunned || _cast >= 0) return false;
                for (int slot = 0; slot < abilities.Length; slot++)
                    if (abilities[slot].Job == AbilityJob.DeathMark && Ready(me, slot)) { _cast = slot; break; }
                return false;   // the mark shows up next tick; this think goes on as usual
            }

            var mark = v.Seats[me.MarkTarget];
            if (!mark.Present || mark.Removed || !mark.Known) return false;

            _state = State.Hunt;
            Vector2 target = mark.Pos;

            // The longest ready stun that would land now, measured on this target (never a shelled or a hidden one).
            int stunSlot = -1;
            float stunSeconds = 0f;
            bool stunnable = !mark.Shelled && !mark.Hidden && !me.Stunned;
            var victimPassive = v.Kits[me.MarkTarget].Passive;
            for (int slot = 0; stunnable && slot < abilities.Length; slot++)
            {
                var ab = abilities[slot];
                if (ab.Control.Kind != ControlKind.Stun || ab.Control.Seconds <= 0f || !Ready(me, slot) || !WouldAffect(v, me, ab, me.MarkTarget)) continue;
                float onTarget = MatchCombat.ControlSeconds(ab.Control, kit.Passive, victimPassive);
                if (onTarget > stunSeconds) { stunSeconds = onTarget; stunSlot = slot; }
            }

            var mk = abilities[markSlot];
            bool armed = me.ArmLeft <= Eps;
            bool canKill = !me.Stunned && ExecuteRules.KillQualifies(armed, mark.Stunned, mark.Shelled)
                && MatchCombat.InRange(me.Pos, mark.Pos, mk.MarkKillRange)
                && !MatchCombat.KeepOutBlocks(me.Pos, mark.Pos, BaseCentres(v), v.Rules.KeepOutRadius);

            var step = BotTactics.NextMarkStep(true, canKill, me.ArmLeft, me.WindowLeft, stunSlot >= 0, stunSeconds);
            if (step == BotMarkStep.Stun && _cast < 0) _cast = stunSlot;
            else if (step == BotMarkStep.Kill && _cast < 0) _cast = markSlot;

            intent = Finish(v, Reach(me.Pos, target, arrival), -1);
            return true;
        }

        // ---- the loot mark ----

        // True and an intent when the bot goes for the marked rival this think: walk to it (the last seen spot when it is hidden, the simulator's
        // path goes round walls) and cast Steal once it is in reach. False leaves the ordinary rules, including the marked rival being abandoned
        // (home behind its wall, shelled, given up), which lets Steal go elsewhere and the mark move.
        private bool ChaseMarked(MatchView v, in SeatView me, MatchAbility[] abilities, float arrival, out MatchIntent intent)
        {
            intent = default;
            int mark = me.LootTarget;
            if (mark < 0) return false;
            var m = v.Seats[mark];
            bool behind = BotLootRules.BehindWall(m.Pos, v.Map.Places[OracleMapSO.BaseIndex(mark)].Pos, v.Rules.KeepOutRadius);
            if (!BotLootRules.Pursue(m.Present && !m.Removed, m.Known, m.Shelled, behind)) return false;

            int slot = -1;
            for (int a = 0; a < abilities.Length; a++) if (abilities[a].Job == AbilityJob.Steal) { slot = a; break; }
            float dist = Vector2.Distance(me.Pos, m.Pos);
            if (slot >= 0)
            {
                var steal = abilities[slot];
                float speed = Mathf.Max(0.1f, v.Kits[_seat].Farm.MoveSpeed);
                _holdSteal = BotLootRules.HoldSteal(true, dist, speed, steal.Cooldown);
                if (!me.Stunned && _cast < 0 && Ready(me, slot))
                {
                    bool onMarked = dist <= steal.Reach * BotTactics.CommitReachFraction && WouldAffect(v, me, steal, mark);
                    if (onMarked || (!_holdSteal && AnyRivalInRange(v, me, steal))) _cast = slot;
                }
            }
            _state = State.Hunt;
            intent = Finish(v, Reach(me.Pos, m.Pos, arrival), -1);
            return true;
        }

        // ---- hunting ----

        private bool Engage(MatchView v, in SeatView me, MatchAbility[] abilities, float arrival, out MatchIntent intent)
        {
            intent = default;
            bool loaded = _preyCargo >= _profile.HuntCargoThreshold;
            bool hunting = _state == State.Hunt || _state == State.Stalk;
            float exit = hunting ? StateExitRadiusFactor : 1f;

            bool inHuntBand = loaded && _preyDist <= _profile.HuntRadius * exit;
            bool inEngageBand = !loaded && _profile.EngageRadius > 0f && _preyDist <= _profile.EngageRadius * exit;
            if (!inHuntBand && !inEngageBand) return false;

            bool fired = TryCast(v, me, abilities, BotSituation.Engaging, _prey);
            float standoff = _profile.Strategy == BotStrategy.Predator && !fired ? StalkStandoff(abilities) : 0f;
            _state = standoff > 0f ? State.Stalk : State.Hunt;

            Vector2 preyPos = v.Seats[_prey].Pos;
            Vector2 goal = preyPos;
            if (standoff > 0f && _preyDist > 0.01f)
            {
                Vector2 away = me.Pos - preyPos;
                goal = preyPos + away.normalized * standoff;
            }
            intent = Finish(v, Reach(me.Pos, goal, arrival), -1);
            return true;
        }

        // A fraction of the longest steal reach it owns, so the pounce is one step away; 0 when it owns no steal.
        private static float StalkStandoff(MatchAbility[] abilities)
        {
            float best = 0f;
            foreach (var ab in abilities)
                if (ab.Role == BotRole.Steal && !float.IsPositiveInfinity(ab.Reach) && ab.Reach > best) best = ab.Reach;
            return best * StalkStandoffFraction;
        }

        // ---- piles ----

        private static OraclePlace PlaceOf(MatchView v, int pile) =>
            pile == MatchGeometry.GoldenPile
                ? new OraclePlace { Kind = OraclePlaceKind.Centre, Seat = -1, Pos = v.GoldenPos, Footprint = v.Map.GoldenFootprint, Yaw = 0f }
                : v.Map.Places[pile];

        private static float PileAvailable(MatchView v, int pile) =>
            pile == MatchGeometry.GoldenPile ? (v.GoldenActive ? v.GoldenFood : 0f) : v.PileFood[pile];

        // Metres from a point to the pile's surface (the footprint oval, not the collect reach round it).
        private static float SurfaceDistance(MatchView v, int pile, Vector2 from)
        {
            var place = PlaceOf(v, pile);
            Vector2 d = from - place.Pos;
            float surface = PileFootprintShape.RadiusToward(d, place.Yaw, place.Footprint);
            return Mathf.Max(0f, d.magnitude - surface);
        }

        /// <summary>
        /// Two pile costs closer than this are a tie. Ties are exact on this map (a chicken on its base's diagonal is as far from its two
        /// contested piles), so the width only has to cover float noise.
        /// </summary>
        public const float PileTieCost = 1e-3f;

        private readonly int[] _pileOf = new int[OracleMapSO.PileCount + 1];
        private readonly float[] _pileCost = new float[OracleMapSO.PileCount + 1];
        private readonly float[] _pileDist = new float[OracleMapSO.PileCount + 1];

        // The cheapest pile by round-trip cost (the game's BotTactics.PileCost). A tie is drawn at random from the seat's own stream. The game's
        // bot keeps the first in its pile list, and its chickens never stand exactly on a symmetry line once physics moves them; an order
        // relative to the seat instead sent seats 1 and 3 to the contested pile nearer the oval centre island every time (seat tilt, plan
        // "Findings after Task 5").
        private int SelectPile(MatchView v, Vector2 from, Vector2 home, ref MatchRng rng, out float bestSurfaceDist)
        {
            int n = 0;
            float bestCost = float.MaxValue;
            for (int k = 0; k <= OracleMapSO.PileCount; k++)
            {
                int pile = k < OracleMapSO.PileCount ? k : MatchGeometry.GoldenPile;
                float available = PileAvailable(v, pile);
                if (available <= Eps) continue;

                float toPile = SurfaceDistance(v, pile, from);
                float toHome = Vector2.Distance(PlaceOf(v, pile).Pos, home);
                float cost = BotTactics.PileCost(toPile, toHome, available, _profile.RoundTripBias);
                _pileOf[n] = pile; _pileCost[n] = cost; _pileDist[n] = toPile; n++;
                bestCost = Math.Min(bestCost, cost);
            }

            bestSurfaceDist = float.MaxValue;
            int tied = 0;
            for (int i = 0; i < n; i++) if (_pileCost[i] <= bestCost + PileTieCost) tied++;
            if (tied == 0) return -1;
            int pick = tied > 1 ? rng.NextInt(tied) : 0;
            for (int i = 0; i < n; i++)
            {
                if (_pileCost[i] > bestCost + PileTieCost) continue;
                if (pick-- > 0) continue;
                bestSurfaceDist = _pileDist[i];
                return _pileOf[i];
            }
            return -1;
        }
    }
}
