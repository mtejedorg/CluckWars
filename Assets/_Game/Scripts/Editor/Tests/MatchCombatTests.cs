using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;
using MatchEventKind = CluckWars.Balance.MatchEventKind;

namespace CluckWars.Tests
{
    /// <summary>
    /// The combat rules of the match simulator on synthetic maps and kits: every number here is the test's own, none is read from an
    /// asset, and no test asserts a win rate. Two-seat duels: seat 0 is the victim (often a pecker), seat 1 the attacker.
    /// </summary>
    public sealed class MatchCombatTests
    {
        private const float Half = 40f;
        private const float Tick = 1f / 30f;
        private const float Big = 1000f;
        private const float ShoveDecay = 8f;
        // A victim has to know how the slows it will meet land (MatchPassive answers only for slow factors the kits declare).
        private const float SlowFactor = 0.5f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        private static MatchKit Pecker(MatchAbility[] abilities = null, MatchPassive? passive = null, float amount = 3f, float cooldown = 2f, float cast = 1f) =>
            MatchTestKit.Fighter("victim", 100f, abilities ?? new MatchAbility[0], passive ?? MatchPassive.Identity(SlowFactor), amount, cooldown, cast);

        private static MatchKit Attacker(params MatchAbility[] abilities) => MatchTestKit.Fighter("attacker", 10f, abilities);

        private static MatchResult Duel(MatchKit victim, MatchKit attacker, PolicyFactory pv, PolicyFactory pa, float clock,
            Action<MatchSetup> tweak = null, OracleMapSO map = null, ulong seed = 7)
        {
            map = map ?? MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Duel(map, victim, attacker, pv, pa, Half, clock);
            setup.Rules.KnockbackDecayRate = ShoveDecay;
            tweak?.Invoke(setup);
            return MatchSim.Run(setup, seed);
        }

        private static List<MatchEvent> Of(MatchResult r, MatchEventKind kind, int seat) =>
            r.Events.Where(e => e.Kind == kind && e.Seat == seat).ToList();

        private static float FirstPeckStart(MatchResult r) =>
            r.Events.First(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other >= 0).Time;

        // The attacker casts slot 0 on the first think that finds the victim in the middle of a Peck, and tells what the distance was.
        private static PolicyFactory CastWhenPecking(Action<float> distance)
        {
            bool cast = false;
            return MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.StandAt(v, s);
                if (!cast && v.Seats[0].Pecking)
                {
                    cast = true;
                    distance?.Invoke(Vector2.Distance(v.Seats[0].Pos, v.Seats[s].Pos));
                    intent.CastSlot = 0;
                }
                return intent;
            });
        }

        // ---- reach ----

        [Test]
        public void Stun_AtRadiusMinusEpsilon_Lands_AtRadiusPlusEpsilon_DoesNot()
        {
            float dist = 0f;
            Duel(Pecker(), Attacker(MatchTestKit.Stun(Big, 1f, 5f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(d => dist = d), 3f);
            Assert.Greater(dist, 1f, "the probe run never saw the victim pecking");

            const float eps = 0.01f;
            var near = Duel(Pecker(), Attacker(MatchTestKit.Stun(dist + eps, 1f, 5f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 3f);
            var far = Duel(Pecker(), Attacker(MatchTestKit.Stun(dist - eps, 1f, 5f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 3f);
            Assert.AreEqual(1, Of(near, MatchEventKind.PeckCut, 0).Count(e => e.Cause == CutCause.Stun), "a stun that reaches must cut the Peck");
            Assert.AreEqual(0, Of(far, MatchEventKind.PeckCut, 0).Count, "a stun that falls short must leave the Peck alone");
        }

        [Test]
        public void Stun_MidCast_LandsNoFood_SpendsTheCooldown()
        {
            const float cooldown = 2f;
            var r = Duel(Pecker(cooldown: cooldown), Attacker(MatchTestKit.Stun(Big, 1f, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)),
                CastWhenPecking(null), 2.6f);
            float t0 = FirstPeckStart(r);

            var cuts = Of(r, MatchEventKind.PeckCut, 0);
            Assert.AreEqual(1, cuts.Count);
            Assert.AreEqual(CutCause.Stun, cuts[0].Cause);
            Assert.AreEqual(0f, r.Counters[0].Foraged, 1e-4f, "a cut cast lands nothing");
            var starts = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other >= 0).Select(e => e.Time).ToList();
            Assert.AreEqual(2, starts.Count, "the Peck starts again once its cooldown is over");
            Assert.GreaterOrEqual(starts[1], t0 + cooldown - 1e-3f, "the cooldown of the cut cast is spent, not refunded");
        }

        [Test]
        public void Slow_MidCast_DoesNotCut()
        {
            var r = Duel(Pecker(), Attacker(MatchTestKit.Slow(Big, 1f, SlowFactor, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 1.4f);
            Assert.AreEqual(0, Of(r, MatchEventKind.PeckCut, 0).Count);
            Assert.Greater(r.Counters[0].Foraged, 0f, "the food lands through a slow");
        }

        [Test]
        public void Stun_OnTheLandingTick_LetsTheFoodLand()
        {
            const float cast = 1f;
            var probe = Duel(Pecker(cast: cast), Attacker(), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), MatchTestKit.Stand, 1.2f);
            float landing = FirstPeckStart(probe) + cast;

            var r = Duel(Pecker(cast: cast), Attacker(MatchTestKit.Stun(Big, 1f, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)),
                MatchTestKit.CastAt(landing, 0), 1.4f);
            Assert.AreEqual(0, Of(r, MatchEventKind.PeckCut, 0).Count, "a stun on the landing tick comes after the landing");
            Assert.Greater(r.Counters[0].Foraged, 0f);
        }

        [Test]
        public void Shove_MidCast_Cuts_AndTravelsKnockbackOverDecay()
        {
            const float knock = 24f;
            var cut = Duel(Pecker(), Attacker(MatchTestKit.Shove(Big, knock, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 1.4f);
            var cuts = Of(cut, MatchEventKind.PeckCut, 0);
            Assert.AreEqual(1, cuts.Count);
            Assert.AreEqual(CutCause.Shove, cuts[0].Cause);

            float travelled = ShoveTravelled(1f, knock);
            Assert.AreEqual(MatchCombat.ShoveTravel(knock, 1f, ShoveDecay), travelled, 0.05f * MatchCombat.ShoveTravel(knock, 1f, ShoveDecay));
        }

        [Test]
        public void Shove_VictimFactor_ScalesTheTravel()
        {
            const float knock = 24f;
            float full = ShoveTravelled(1f, knock);
            float half = ShoveTravelled(0.5f, knock);
            Assert.AreEqual(0.5f, half / full, 0.05f);
            Assert.AreEqual(MatchCombat.ShoveTravel(knock, 0.5f, ShoveDecay), half, 0.05f * MatchCombat.ShoveTravel(knock, 0.5f, ShoveDecay));
        }

        // An idle victim is shoved once; how far did it end up from where it stood?
        private static float ShoveTravelled(float victimFactor, float knockback)
        {
            var passive = MatchPassive.Identity();
            passive.KnockbackFactor = victimFactor;
            Vector2? first = null, last = null;
            // Speed 0: the victim does not steer back to where it stood, so the whole displacement is the push.
            var victim = MatchTestKit.Policy((v, s) =>
            {
                first = first ?? v.Seats[s].Pos;
                last = v.Seats[s].Pos;
                return MatchTestKit.StandAt(v, s);
            });
            var kit = MatchTestKit.Fighter("victim", 0f, new MatchAbility[0], passive);
            Duel(kit, Attacker(MatchTestKit.Shove(Big, knockback, 9f)), victim, MatchTestKit.CastAt(0.2f, 0), 2.5f);
            return Vector2.Distance(first.Value, last.Value);
        }

        // ---- steal ----

        private static MatchResult StealRun(int thiefCapacity, float factor, float nominal)
        {
            var passive = MatchPassive.Identity();
            passive.StealFactor = factor;
            var thief = MatchTestKit.Fighter("thief", 10f, new[] { MatchTestKit.Steal(Big, nominal, 20f) }, passive, capacity: thiefCapacity);
            var victim = Pecker(amount: 6f, cooldown: 50f, cast: 0.2f);
            return Duel(victim, thief, MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), MatchTestKit.CastAt(1.2f, 0), 2f);
        }

        [Test]
        public void Steal_IsTakenFromTheCargo_CappedByFreeRoom()
        {
            // In the simulation: the victim holds 6 (one Peck of 6), the thief has room for 4, wants 5.
            var r = StealRun(thiefCapacity: 4, factor: 1f, nominal: 5f);
            float expected = StealMath.Clamp(5f, 4f, 6f);
            Assert.AreEqual(expected, r.Counters[1].Stolen, 1e-4f);
            Assert.AreEqual(expected, r.Counters[0].LostToSteals, 1e-4f);
            Assert.AreEqual(expected, r.Counters[1].StolenFromSeat[0], 1e-4f);

            // There is no second store (the capacity-free bag is gone): a steal takes at most what the victim's cargo holds, the game's own rule.
            Assert.AreEqual(2f, StealRules.DrainAmount(5f, 2f), 1e-5f);
            Assert.AreEqual(2f, MatchCombat.StealTake(5f, 1f, 100f, 2f), 1e-5f);
        }

        [Test]
        public void Steal_IsScaledByTheThiefPassive_AndNeverExceedsWhatTheVictimHolds()
        {
            var r = StealRun(thiefCapacity: 100, factor: 1.6f, nominal: 5f);
            Assert.AreEqual(StealMath.Clamp(5f * 1.6f, 100f, 6f), r.Counters[1].Stolen, 1e-4f);
        }

        // ---- the egg ----

        [Test]
        public void Refused_EggRefusesEverything_ShieldWalkRefusesOnlySlows()
        {
            foreach (IncomingEffect e in Enum.GetValues(typeof(IncomingEffect)))
            {
                Assert.IsTrue(MatchCombat.Refused(e, egg: true, shieldWalk: false), $"the egg refuses {e}");
                Assert.AreEqual(e == IncomingEffect.Slow, MatchCombat.Refused(e, egg: false, shieldWalk: true), $"the walk and {e}");
                Assert.IsFalse(MatchCombat.Refused(e, egg: false, shieldWalk: false), $"nothing refuses {e}");
            }
        }

        [TestCase(AbilityJob.Stun)]
        [TestCase(AbilityJob.Shove)]
        [TestCase(AbilityJob.Steal)]
        public void Egg_RefusesStunShoveStealAndExecute(AbilityJob job)
        {
            MatchAbility ability = job == AbilityJob.Stun ? MatchTestKit.Stun(Big, 1f, 9f)
                : job == AbilityJob.Shove ? MatchTestKit.Shove(Big, 24f, 9f)
                : MatchTestKit.Steal(Big, 5f, 9f);

            bool everStunned = false;
            Vector2? atCast = null, atEnd = null;
            float cargoAtCast = -1f, cargoAtEnd = -1f;
            bool shelled = false;
            var victim = MatchTestKit.Policy((v, s) =>
            {
                everStunned |= v.Seats[s].Stunned;
                if (v.Time >= 1.4f && !atCast.HasValue) { atCast = v.Seats[s].Pos; cargoAtCast = v.Seats[s].Cargo; }
                atEnd = v.Seats[s].Pos;
                cargoAtEnd = v.Seats[s].Cargo;
                if (shelled) return MatchTestKit.StandAt(v, s);
                if (v.Seats[s].Cargo > 0f)
                {
                    shelled = true;
                    var intent = MatchTestKit.StandAt(v, s);
                    intent.CastSlot = 0;
                    return intent;
                }
                return MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
            });

            var kit = Pecker(new[] { MatchTestKit.Shield(0.1f, 0.5f, 10f, 99f) }, amount: 6f, cooldown: 99f, cast: 0.2f);
            var r = Duel(kit, Attacker(ability), victim, MatchTestKit.CastAt(1.5f, 0), 2.5f);

            Assert.IsTrue(shelled, "the victim never reached the egg");
            Assert.AreEqual(1, Of(r, MatchEventKind.CastStart, 1).Count, "the attacker did cast");
            Assert.IsFalse(everStunned, "an egg cannot be stunned");
            Assert.AreEqual(0f, Vector2.Distance(atCast.Value, atEnd.Value), 1e-4f, "an egg cannot be shoved");
            Assert.AreEqual(cargoAtCast, cargoAtEnd, 1e-4f, "an egg cannot be robbed");
            Assert.AreEqual(0f, r.Counters[1].Stolen, 1e-4f);
        }

        // ---- the base wall, the interior arms ----

        [TestCase(AbilityJob.Stun)]
        [TestCase(AbilityJob.Slow)]
        [TestCase(AbilityJob.Shove)]
        [TestCase(AbilityJob.SlowArea)]
        public void NoAbility_CrossesTheKeepOutPair(AbilityJob job)
        {
            MatchAbility ability = job == AbilityJob.Stun ? MatchTestKit.Stun(Big, 1f, 9f)
                : job == AbilityJob.Slow ? MatchTestKit.Slow(Big, 1f, SlowFactor, 9f)
                : job == AbilityJob.Shove ? MatchTestKit.Shove(Big, 24f, 9f)
                : MatchTestKit.SlowArea(Big, SlowFactor, 1f, 9f);

            Vector2 Final(float castAt, float keepOut)
            {
                Vector2 last = default;
                var victim = MatchTestKit.Policy((v, s) =>
                {
                    last = v.Seats[s].Pos;
                    return new MatchIntent { Goal = new Vector2(-30f, 0f), PeckPile = -1, CastSlot = -1, CastTarget = -1 };
                });
                var kit = MatchTestKit.Fighter("victim", 10f, new MatchAbility[0], MatchPassive.Identity(SlowFactor));
                Duel(kit, Attacker(ability), victim, MatchTestKit.CastAt(castAt, 0), 1.5f, setup => setup.Rules.KeepOutRadius = keepOut);
                return last;
            }

            Vector2 uncast = Final(100f, 3f);
            Vector2 blocked = Final(0.2f, 3f);
            Vector2 open = Final(0.2f, 0f);
            Assert.AreEqual(0f, Vector2.Distance(uncast, blocked), 1e-4f, $"a {job} must not cross a base wall");
            Assert.Greater(Vector2.Distance(uncast, open), 0.05f, $"with no base wall the {job} lands, so the test can tell");
        }

        [Test]
        public void KeepOutBlocks_IsTheGamesPairRule()
        {
            var centres = new[] { new Vector2(10f, 0f) };
            Assert.IsTrue(MatchCombat.KeepOutBlocks(new Vector2(10f, 0f), new Vector2(0f, 0f), centres, 3f), "one inside, one outside");
            Assert.IsTrue(MatchCombat.KeepOutBlocks(new Vector2(0f, 0f), new Vector2(10f, 1f), centres, 3f), "either direction");
            Assert.IsFalse(MatchCombat.KeepOutBlocks(new Vector2(10f, 0f), new Vector2(11f, 1f), centres, 3f), "both inside the same base");
            Assert.IsFalse(MatchCombat.KeepOutBlocks(new Vector2(0f, 0f), new Vector2(-5f, 0f), centres, 3f), "both outside");
            Assert.IsFalse(MatchCombat.KeepOutBlocks(new Vector2(10f, 0f), new Vector2(0f, 0f), centres, 0f), "no radius, no wall");
        }

        [Test]
        public void Abilities_PassThroughInteriorWallArms()
        {
            // The game asks only the base wall (AbilityBaseSO.WouldAffect -> PlayerBase.WallBetween) after the shape; an arm of the
            // pinwheel is a physics collider nobody raycasts against, so a stun crosses it.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(5.25f, 5.25f), new Vector2(25.25f, 25.25f), 1f);
            var nav = OracleNav.For(map, Half);
            Assert.IsFalse(nav.IsClear(MatchSim.Spawn(map, 0), MatchSim.Spawn(map, 1)), "the wall must stand between the two seats");

            var r = Duel(Pecker(), Attacker(MatchTestKit.Stun(Big, 1f, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 1.4f, map: map);
            Assert.AreEqual(1, Of(r, MatchEventKind.PeckCut, 0).Count(e => e.Cause == CutCause.Stun));
        }

        [Test]
        public void ArmWalls_GameCode_AsksOnlyTheBaseWall()
        {
            string src = File.ReadAllText("Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs");
            int from = src.IndexOf("public bool WouldAffect(Gameplay.ChickenController caster, Gameplay.ChickenController candidate, Vector3 forward)", StringComparison.Ordinal);
            int to = src.IndexOf("return ExtraTargetFilter(caster, candidate);", from, StringComparison.Ordinal);
            Assert.Greater(from, 0, "WouldAffect moved: re-read how a cast picks its victims, and record the answer in spec 2.2");
            Assert.Greater(to, from);
            string body = src.Substring(from, to - from);
            StringAssert.Contains("PlayerBase.WallBetween", body);
            Assert.IsFalse(body.Contains("Physics."), "an ability now asks the physics world for walls: the simulator's MatchCombat.InRange needs a wall test (spec 2.2)");
            Assert.IsFalse(body.Contains("Raycast"), "an ability now raycasts for walls (spec 2.2)");
        }

        // ---- area and self abilities ----

        // Distance the walker covers on each tick of a run, indexed by the tick's start time.
        private static List<(float time, float step)> Steps(MatchKit walker, MatchKit other, PolicyFactory walkerCasts, PolicyFactory otherCasts, float clock)
        {
            var log = new List<(float, Vector2)>();
            var inner = walkerCasts;
            PolicyFactory logging = (seat, p) =>
            {
                var policy = inner(seat, p);
                return new Wrapper(policy, (v, s) => log.Add((v.Time, v.Seats[s].Pos)));
            };
            Duel(walker, other, logging, otherCasts, clock);
            var steps = new List<(float, float)>();
            for (int i = 1; i < log.Count; i++) steps.Add((log[i - 1].Item1, Vector2.Distance(log[i - 1].Item2, log[i].Item2)));
            return steps;
        }

        private sealed class Wrapper : IMatchPolicy
        {
            private readonly IMatchPolicy _inner;
            private readonly Action<MatchView, int> _log;
            public Wrapper(IMatchPolicy inner, Action<MatchView, int> log) { _inner = inner; _log = log; }
            public MatchIntent Think(MatchView view, ref MatchRng rng) { _log(view, view.Self); return _inner.Think(view, ref rng); }
        }

        private static PolicyFactory WalkWest(float castAt, int slot)
        {
            return (seat, p) =>
            {
                bool done = false;
                return MatchTestKit.Policy((v, s) =>
                {
                    var intent = new MatchIntent { Goal = new Vector2(-35f, 0f), PeckPile = -1, CastSlot = -1, CastTarget = -1 };
                    if (!done && slot >= 0 && v.Time + 1e-4f >= castAt) { done = true; intent.CastSlot = slot; }
                    return intent;
                })(seat, p);
            };
        }

        private static float MeanStep(List<(float time, float step)> steps, float from, float to)
        {
            var window = steps.Where(x => x.time >= from && x.time < to).Select(x => x.step).ToList();
            Assert.IsNotEmpty(window, $"no ticks between {from} and {to}");
            return window.Average();
        }

        [Test]
        public void SlowArea_SlowsRivalsInsideEachTick_UntilItsDuration()
        {
            const float speed = 6f, factor = 0.5f, duration = 1f, castAt = 0.5f;
            var victim = MatchTestKit.Fighter("victim", speed, new MatchAbility[0], MatchPassive.Identity(factor));
            var caster = MatchTestKit.Fighter("caster", 1f, new[] { MatchTestKit.SlowArea(Big, factor, duration, 99f) });
            var steps = Steps(victim, caster, WalkWest(100f, -1), MatchTestKit.CastAt(castAt, 0), 3f);

            float full = speed * Tick;
            Assert.AreEqual(full, MeanStep(steps, 0.1f, castAt - 0.1f), 0.02f * full, "before the aura");
            Assert.AreEqual(full * factor, MeanStep(steps, castAt + 0.1f, castAt + duration - 0.1f), 0.02f * full, "inside the aura, every tick");
            Assert.AreEqual(full, MeanStep(steps, castAt + duration + 0.2f, 2.8f), 0.02f * full, "after the aura's duration");
        }

        [Test]
        public void Speed_MultipliesMoveForItsSeconds()
        {
            const float speed = 4f, mult = 2f, seconds = 0.6f, castAt = 0.5f;
            var walker = MatchTestKit.Fighter("walker", speed, new[] { MatchTestKit.Speed(mult, seconds, 99f) });
            var steps = Steps(walker, Attacker(), WalkWest(castAt, 0), MatchTestKit.Stand, 2.5f);

            float basic = speed * Tick;
            Assert.AreEqual(basic, MeanStep(steps, 0.1f, castAt - 0.1f), 0.02f * basic);
            Assert.AreEqual(basic * mult, MeanStep(steps, castAt + 0.1f, castAt + seconds - 0.1f), 0.02f * basic);
            Assert.AreEqual(basic, MeanStep(steps, castAt + seconds + 0.2f, 2.3f), 0.02f * basic);
        }

        [Test]
        public void Shield_WalkIsSlowedThenTheEggStandsStill()
        {
            const float speed = 6f, walkMult = 0.5f, castAt = 0.4f, walk = 0.5f, egg = 0.8f;
            var walker = MatchTestKit.Fighter("walker", speed, new[] { MatchTestKit.Shield(walk, walkMult, egg, 99f) });
            var steps = Steps(walker, Attacker(), WalkWest(castAt, 0), MatchTestKit.Stand, 2.4f);

            float basic = speed * Tick;
            Assert.AreEqual(basic * walkMult, MeanStep(steps, castAt + 0.1f, castAt + walk - 0.1f), 0.03f * basic, "the walk phase");
            Assert.AreEqual(0f, MeanStep(steps, castAt + walk + 0.1f, castAt + walk + egg - 0.1f), 1e-4f, "the egg");
            Assert.AreEqual(basic, MeanStep(steps, castAt + walk + egg + 0.2f, 2.2f), 0.03f * basic, "after the egg");
        }

        // ---- jump ----

        [Test]
        public void Jump_CrossingAWall_CostsOneJump_AndWaitsForTheCooldown()
        {
            const float cooldown = 20f, speed = 4f;
            // A wall across the x axis between seat 0's base (+x) and the goal on the far side.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(0f, -10f), new Vector2(0f, 10f), 1f);
            bool goingWest = true;
            var jumper = MatchTestKit.Policy((v, s) =>
            {
                float x = v.Seats[s].Pos.x;
                if (goingWest && x < -15f) goingWest = false;
                return new MatchIntent { Goal = new Vector2(goingWest ? -20f : 20f, 0f), PeckPile = -1, CastSlot = -1, CastTarget = -1 };
            });
            var kit = MatchTestKit.Fighter("jumper", speed, new[] { MatchTestKit.Jump(3f, 0.3f, cooldown) });
            var r = Duel(kit, Attacker(), jumper, MatchTestKit.Stand, 45f, map: map);

            var jumps = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other == -1).Select(e => e.Time).ToList();
            Assert.AreEqual(2, jumps.Count, "one Jump per crossing, none for the free walking in between");
            Assert.GreaterOrEqual(jumps[1], jumps[0] + cooldown - 1e-3f, "the second crossing waits for the cooldown");
        }

        [Test]
        public void Jump_NeverLeavesTheChickenInsideTheWall()
        {
            const float dash = 0.3f;
            var map = MatchTestKit.OneWallMap(Half, new Vector2(0f, -10f), new Vector2(0f, 10f), 1f);
            var nav = OracleNav.For(map, Half);
            float jumpTime = -1f;
            var inside = new List<float>();
            Vector2 last = default;
            var jumper = MatchTestKit.Policy((v, s) =>
            {
                if (jumpTime < 0f && v.Seats[s].CooldownLeft[1] > 0f) jumpTime = v.Time;
                if (!nav.IsClear(v.Seats[s].Pos, v.Seats[s].Pos)) inside.Add(v.Time);
                last = v.Seats[s].Pos;
                return new MatchIntent { Goal = new Vector2(-20f, 0f), PeckPile = -1, CastSlot = -1, CastTarget = -1 };
            });
            var kit = MatchTestKit.Fighter("jumper", 4f, new[] { MatchTestKit.Jump(3f, dash, 5f) });
            var r = Duel(kit, Attacker(), jumper, MatchTestKit.Stand, 20f, map: map);

            Assert.Greater(jumpTime, 0f, "the jumper never jumped");
            Assert.IsNotEmpty(inside, "the dash passes through the wall, so some tick finds the chicken inside it");
            Assert.That(inside, Has.All.GreaterThanOrEqualTo(jumpTime - Tick).And.LessThanOrEqualTo(jumpTime + dash + 0.2f), "the chicken is inside the wall only while it dashes");
            Assert.Less(last.x, -2f, "it came out on the far side");
            Assert.AreEqual(1, r.Events.Count(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other == -1));
        }

        // ---- control seconds ----

        [Test]
        public void Control_DurationPassesThroughCasterThenVictim()
        {
            const float seconds = 1.2f, outgoing = 1.5f, incoming = 0.5f;
            var casterPassive = MatchPassive.Identity();
            casterPassive.OutgoingControlFactor = outgoing;
            var victimPassive = MatchPassive.Identity();
            victimPassive.StunInFactor = incoming;

            float? from = null, until = null;
            var victim = MatchTestKit.Policy((v, s) =>
            {
                if (v.Seats[s].Stunned && !from.HasValue) from = v.Time;
                if (from.HasValue && !v.Seats[s].Stunned && !until.HasValue) until = v.Time;
                return MatchTestKit.StandAt(v, s);
            });
            var kit = MatchTestKit.Fighter("victim", 5f, new MatchAbility[0], victimPassive);
            var stunner = MatchTestKit.Fighter("stunner", 5f, new[] { MatchTestKit.Stun(Big, seconds, 99f) }, casterPassive);
            Duel(kit, stunner, victim, MatchTestKit.CastAt(0.3f, 0), 3f);

            var emission = new ControlEmission(ControlKind.Stun, seconds);
            float expected = MatchCombat.ControlSeconds(emission, casterPassive, victimPassive);
            Assert.AreEqual(seconds * outgoing * incoming, expected, 1e-5f);
            Assert.IsTrue(from.HasValue && until.HasValue);
            Assert.AreEqual(expected, until.Value - from.Value, 2f * Tick + 1e-3f);
        }

        // ---- the Peck ----

        [Test]
        public void PileEmptiedMidCast_CutsWithEmptyPile_AndRethinksAtOnce()
        {
            const float cooldown = 2f;
            var map = MatchTestKit.OpenMap(Half);
            var thinks = new List<float>[] { new List<float>(), new List<float>() };
            // Both go for the centre pile, which holds one bite; whoever finds it gone goes for its own personal pile.
            var policy = MatchTestKit.Policy((v, s) =>
            {
                thinks[s].Add(v.Time);
                bool centreLeft = v.PileFood[OracleMapSO.CentreIndex] > 1e-3f;
                return MatchTestKit.PeckAt(v, s, centreLeft ? OracleMapSO.CentreIndex : OracleMapSO.PersonalIndex(s));
            });
            var setup = MatchTestKit.Duel(map, Pecker(amount: 3f, cooldown: cooldown, cast: 1f), Pecker(amount: 3f, cooldown: cooldown, cast: 1f), policy, policy, Half, 3f);
            setup.Food = new OracleFood { Personal = 10f, Contested = 10f, Centre = 3f };
            setup.Rules.ThinkSeconds = 5f;   // only an event makes a seat think again within the first seconds
            var r = MatchSim.Run(setup, 5);

            var cuts = r.Events.Where(e => e.Kind == MatchEventKind.PeckCut && e.Cause == CutCause.EmptyPile).ToList();
            Assert.AreEqual(1, cuts.Count, "exactly one peck loses the single bite");
            var cut = cuts[0];
            Assert.AreEqual(3f, r.Counters[0].Foraged + r.Counters[1].Foraged, 1e-4f, "the pile's one bite went to the winner");
            Assert.IsTrue(thinks[cut.Seat].Any(t => Mathf.Abs(t - cut.Time) < 1e-4f), "the loser re-plans on the tick of the cut");

            float start = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == cut.Seat && e.Other >= 0).Min(e => e.Time);
            var later = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == cut.Seat && e.Other >= 0 && e.Time > cut.Time).ToList();
            Assert.IsNotEmpty(later, "the loser pecks again at its own pile");
            Assert.GreaterOrEqual(later[0].Time, start + cooldown - 1e-3f, "the cut cast's cooldown stays spent");
        }

        [Test]
        public void ExposedCast_CountsOncePerCause()
        {
            var r = Duel(Pecker(cast: 0.5f, cooldown: 5f), Attacker(MatchTestKit.Stun(Big, 1f, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), MatchTestKit.Stand, 1f);
            Assert.AreEqual(1, r.Counters[0].ExposedByCause[(int)CutCause.Stun]);
            Assert.AreEqual(0, r.Counters[0].ExposedByCause[(int)CutCause.Shove]);
            Assert.AreEqual(0, r.Counters[0].CutsByCause[(int)CutCause.Stun]);

            var cut = Duel(Pecker(), Attacker(MatchTestKit.Stun(Big, 1f, 9f)), MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0)), CastWhenPecking(null), 1.4f);
            Assert.AreEqual(1, cut.Counters[0].CutsByCause[(int)CutCause.Stun]);
            Assert.AreEqual(1, cut.Counters[0].CutWhileExposedByCause[(int)CutCause.Stun]);
        }

        [Test]
        public void CutEvents_UseTheTelemetryCauseNames()
        {
            Assert.AreEqual(MatchTelemetry.CauseStun, MatchEvent.CauseName(CutCause.Stun));
            Assert.AreEqual(MatchTelemetry.CauseShove, MatchEvent.CauseName(CutCause.Shove));
            Assert.AreEqual(MatchTelemetry.CauseExecution, MatchEvent.CauseName(CutCause.Execution));
            Assert.AreEqual(MatchTelemetry.CauseEmptyPile, MatchEvent.CauseName(CutCause.EmptyPile));
        }

        // ---- hide ----

        [Test]
        public void Hide_EndsOnOwnMove_NotOnAutoPeck()
        {
            const float hideAt = 0.3f, moveAt = 1.6f;
            var seen = new List<(float time, bool hidden)>();
            var observer = MatchTestKit.Policy((v, s) =>
            {
                seen.Add((v.Time, v.Seats[0].Hidden));
                return MatchTestKit.StandAt(v, s);
            });
            bool hidden = false, moved = false;
            var hider = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
                if (!hidden && v.Time + 1e-4f >= hideAt) { hidden = true; intent.CastSlot = 0; }
                else if (!moved && v.Time + 1e-4f >= moveAt) { moved = true; intent.CastSlot = 1; }
                return intent;
            });
            var kit = MatchTestKit.Fighter("hider", 100f, new[] { MatchTestKit.Hide(0.2f, 10f, 99f), MatchTestKit.Speed(2f, 0.5f, 99f) },
                peckAmount: 3f, peckCooldown: 1f, castSeconds: 0.5f);
            var r = Duel(kit, Attacker(), hider, observer, 3f);

            Assert.That(seen.Where(x => x.time >= hideAt + 0.15f && x.time <= moveAt - 0.1f).Select(x => x.hidden), Has.All.True, "Hide holds while the chicken stands in reach of a pile");
            Assert.That(seen.Where(x => x.time >= moveAt + 0.15f).Select(x => x.hidden), Has.All.False, "casting another move ends it");

            var pecks = r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other >= 0).Select(e => e.Time).ToList();
            Assert.IsFalse(pecks.Any(t => t > hideAt + 0.05f && t < moveAt - 0.01f), "no auto-Peck starts while Hide runs");
            Assert.IsTrue(pecks.Any(t => t >= moveAt - 1e-3f), "the Peck starts again once Hide is over");
            Assert.AreEqual(1, Of(r, MatchEventKind.PeckCut, 0).Count(e => e.Cause == CutCause.OwnMove), "casting Hide cuts the Peck that was running");
        }

        // ---- the timer's tie-break ----

        [Test]
        public void TimerTieBreak_MostBanked_ThenKills_ThenLowestCorner()
        {
            var all = new[] { true, true, true, true };

            Assert.AreEqual(2, MatchCombat.PickTimerWinner(new[] { 5f, 5f, 9f, 1f }, new[] { 3, 3, 0, 0 }, all, out bool corner));
            Assert.IsFalse(corner, "most banked decides");

            Assert.AreEqual(3, MatchCombat.PickTimerWinner(new[] { 5f, 9f, 9f, 9f }, new[] { 0, 1, 1, 2 }, all, out corner));
            Assert.IsFalse(corner, "level on banked: most kills decides");

            Assert.AreEqual(1, MatchCombat.PickTimerWinner(new[] { 1f, 9f, 9f, 2f }, new[] { 0, 1, 1, 0 }, all, out corner));
            Assert.IsTrue(corner, "level on both: only the corner decides");

            Assert.AreEqual(2, MatchCombat.PickTimerWinner(new[] { 9f, 9f, 1f, 1f }, new[] { 0, 0, 0, 0 }, new[] { false, false, true, true }, out corner));
            Assert.IsTrue(corner, "an empty seat does not play");
            Assert.AreEqual(-1, MatchCombat.PickTimerWinner(new float[4], new int[4], new bool[4], out corner));

            // End to end: nobody banks anything, so seat 0 wins on its corner and the run says so.
            var r = Duel(Pecker(), Attacker(), MatchTestKit.Stand, MatchTestKit.Stand, 1f);
            Assert.AreEqual(0, r.Winner);
            Assert.IsTrue(r.CornerDecided);
            Assert.IsFalse(r.ReachedTarget);
        }

        [Test]
        public void TimerTieBreak_MirrorsTheGamesComparisons()
        {
            // The game decides this inline over PlayerBase objects, so MatchCombat.PickTimerWinner is a copy. If the game's rule moves, this fails.
            string src = File.ReadAllText("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            int from = src.IndexOf("private void EndOnTimerExpiry()", StringComparison.Ordinal);
            Assert.Greater(from, 0, "EndOnTimerExpiry moved or was renamed");
            int to = src.IndexOf("EndMatch(winner, winnerCorner", from, StringComparison.Ordinal);
            string body = src.Substring(from, to - from);

            int last = -1;
            foreach (var step in new[]
            {
                "AwardMatchEndBonuses()",
                "b.FoodTotal > bestBase.FoodTotal",
                "Mathf.Approximately(b.FoodTotal, bestBase.FoodTotal)",
                "bKills > bestKills",
                "bKills == bestKills",
                "b.CornerIndex < bestBase.CornerIndex",
            })
            {
                int at = body.IndexOf(step, StringComparison.Ordinal);
                Assert.Greater(at, last, $"'{step}' must appear, in this order, in the game's timer tie-break");
                last = at;
            }
        }

        // ---- the comeback event ----

        private static ComebackSettings Comeback => new ComebackSettings
        {
            SecondsLeft = 8f, GoldenPileFood = 20f, RestockFood = 7f, GoldenRingMin = 3f, GoldenRingMax = 5f,
        };

        private const float SurgeFactor = 1.25f;
        private const float ComebackClock = 12f;

        private static MatchResult ComebackRun(ulong seed, PolicyFactory p0, PolicyFactory p1, MatchKit k0 = null, MatchKit k1 = null) =>
            Duel(k0 ?? Pecker(), k1 ?? Attacker(), p0, p1, ComebackClock, setup =>
            {
                setup.Rules.Comeback = Comeback;
                setup.Rules.SurgeSpeedFactor = SurgeFactor;
            }, seed: seed);

        private sealed class Drawer : IMatchPolicy
        {
            public MatchIntent Think(MatchView view, ref MatchRng rng)
            {
                for (int i = 0; i < 50; i++) rng.NextUInt();
                return MatchTestKit.StandAt(view, view.Self);
            }
        }

        private static List<(ulong seed, MatchResult result)> Seeds(int count)
        {
            var list = new List<(ulong, MatchResult)>();
            for (ulong seed = 1; seed <= (ulong)count; seed++)
                list.Add((seed, ComebackRun(seed, MatchTestKit.Stand, MatchTestKit.Stand)));
            return list;
        }

        [Test]
        public void ComebackDraw_UsesOnlyTheComebackStream()
        {
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var calm = ComebackRun(seed, MatchTestKit.Stand, MatchTestKit.Stand);
                var noisy = ComebackRun(seed, (s, p) => new Drawer(), (s, p) => new Drawer());
                Assert.AreEqual(calm.Comeback, noisy.Comeback, $"seed {seed}");
                Assert.AreEqual(calm.ComebackTime, noisy.ComebackTime, 1e-6f, $"seed {seed}");
                Assert.AreEqual(calm.GoldenSpot, noisy.GoldenSpot, $"seed {seed}");
            }
        }

        [Test]
        public void GoldenPile_SpotIsOnTheRing_AndCanBePecked()
        {
            var goldens = Seeds(60).Where(x => x.result.Comeback == CluckWars.Gameplay.MatchEventKind.GoldenPile).ToList();
            Assert.GreaterOrEqual(goldens.Count, 5, "too few Golden Piles in 60 seeds to judge the ring");
            foreach (var g in goldens)
                Assert.That(g.result.GoldenSpot.magnitude, Is.InRange(Comeback.GoldenRingMin - 1e-3f, Comeback.GoldenRingMax + 1e-3f), $"seed {g.seed}");
            Assert.Greater(goldens.Max(g => g.result.GoldenSpot.x) - goldens.Min(g => g.result.GoldenSpot.x), 1f, "the spot is drawn, not fixed");

            ulong seed = goldens[0].seed;
            float lastFood = -1f;
            var forager = MatchTestKit.Policy((v, s) =>
            {
                lastFood = v.GoldenFood;
                return v.GoldenActive ? MatchTestKit.PeckAt(v, s, MatchGeometry.GoldenPile) : MatchTestKit.StandAt(v, s);
            });
            var r = ComebackRun(seed, forager, MatchTestKit.Stand, MatchTestKit.Fighter("forager", 100f, new MatchAbility[0], null, 2f, 0.4f, 0.2f));
            Assert.AreEqual(CluckWars.Gameplay.MatchEventKind.GoldenPile, r.Comeback);
            Assert.Greater(r.Counters[0].Foraged, 0f, "the Golden Pile feeds a chicken");
            Assert.AreEqual(Comeback.GoldenPileFood - r.Counters[0].Foraged, lastFood, 1e-3f);
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void UnderdogSurge_SpeedsTheLowestBankedSeat()
        {
            ulong seed = Seeds(80).First(x => x.result.Comeback == CluckWars.Gameplay.MatchEventKind.UnderdogSurge).seed;
            float eventTime = ComebackClock - Comeback.SecondsLeft;

            List<(float time, float step)> StepsOf(int seat)
            {
                var log = new List<(float, Vector2)>();
                var walk = MatchTestKit.Policy((v, s) =>
                {
                    if (s == seat) log.Add((v.Time, v.Seats[s].Pos));
                    return new MatchIntent { Goal = new Vector2(-35f, v.Seats[s].Pos.y), PeckPile = -1, CastSlot = -1, CastTarget = -1 };
                });
                ComebackRun(seed, walk, walk, MatchTestKit.Fighter("a", 4f, new MatchAbility[0]), MatchTestKit.Fighter("b", 4f, new MatchAbility[0]));
                var steps = new List<(float, float)>();
                for (int i = 1; i < log.Count; i++) steps.Add((log[i - 1].Item1, Vector2.Distance(log[i - 1].Item2, log[i].Item2)));
                return steps;
            }

            float under = MeanStep(StepsOf(0), eventTime + 0.3f, eventTime + 1.3f);   // both bank 0: the first seat is the underdog
            float other = MeanStep(StepsOf(1), eventTime + 0.3f, eventTime + 1.3f);
            Assert.AreEqual(SurgeFactor, under / other, 0.03f);
        }

        [Test]
        public void Restock_AddsFoodToEveryPile_UpToItsMaximum()
        {
            ulong seed = Seeds(80).First(x => x.result.Comeback == CluckWars.Gameplay.MatchEventKind.Restock).seed;
            var levels = new List<float>();
            var forager = MatchTestKit.Policy((v, s) =>
            {
                levels.Add(v.PileFood[OracleMapSO.CentreIndex]);
                return MatchTestKit.PeckAt(v, s, OracleMapSO.CentreIndex);
            });
            const float bite = 2f;
            ComebackRun(seed, forager, MatchTestKit.Stand, MatchTestKit.Fighter("forager", 100f, new MatchAbility[0], null, bite, 0.4f, 0.2f));

            var jumps = new List<float>();
            for (int i = 1; i < levels.Count; i++) if (levels[i] - levels[i - 1] > 1e-3f) jumps.Add(levels[i] - levels[i - 1]);
            Assert.AreEqual(1, jumps.Count, "one restock, and pecks only take food away");
            Assert.That(jumps[0], Is.InRange(Comeback.RestockFood - bite - 1e-3f, Comeback.RestockFood + 1e-3f), "the centre pile gained the restock (less a bite that landed the same tick)");
        }

        [Test]
        public void ThreeComebackEvents_AreAllReachable()
        {
            var kinds = Seeds(80).Select(x => x.result.Comeback).Distinct().ToList();
            CollectionAssert.IsSubsetOf(ComebackRules.Events, kinds, "every comeback event the game can roll comes up in 80 seeds");
            CollectionAssert.DoesNotContain(kinds, CluckWars.Gameplay.MatchEventKind.None, "the event fires in every run that lasts to its time");
        }

        [Test]
        public void ComebackEvent_FiresOnlyThroughTheGamesTimingRule()
        {
            var off = Duel(Pecker(), Attacker(), MatchTestKit.Stand, MatchTestKit.Stand, ComebackClock, setup => setup.Rules.Comeback = default);
            Assert.AreEqual(CluckWars.Gameplay.MatchEventKind.None, off.Comeback, "zero seconds switches it off");

            var r = ComebackRun(3, MatchTestKit.Stand, MatchTestKit.Stand);
            Assert.AreEqual(ComebackClock - Comeback.SecondsLeft, r.ComebackTime, Tick + 1e-3f);
        }

        // ---- what the simulator assumes of the game, pinned ----

        private static AbilityBaseSO ByJob(AbilityJob job)
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var found = registry.All.FirstOrDefault(a => a != null && a.Job == job);
            Assert.IsNotNull(found, $"the registry holds no ability with job {job}");
            return found;
        }

        [Test]
        public void Game_PeckEndsOnTheNextMove_AndHideIsStealth()
        {
            // The sim cuts a running Peck (cause own-move) on any other cast, and ends Hide on a move only when the ability says so.
            Assert.IsTrue(ByJob(AbilityJob.Peck).EndsOnNextMove, "Peck no longer ends on the next move: MatchSim.BeginCast cuts it with OwnMove");
            Assert.IsTrue(ByJob(AbilityJob.Hide).EndsOnNextMove, "Hide no longer ends on a move");
        }

        [Test]
        public void Game_JumpTraversal_ClearsEveryInteriorObstacleClass()
        {
            // The sim lets a Jump cross any wall of the baked map (arms, rocks), and never the arena boundary (the arena clamp).
            Assert.AreEqual(TerrainTraversal.Blink, ByJob(AbilityJob.Jump).TerrainTraversal, "Jump no longer blinks: re-read what it clears (ChickenTraversal.IsClearable)");
            foreach (ObstacleClass obstacle in Enum.GetValues(typeof(ObstacleClass)))
                Assert.IsTrue(TraversalRules.Clears(TerrainTraversal.Blink, obstacle), $"Blink no longer clears {obstacle}");
        }

        [Test]
        public void Game_ShoveThresholds_StillMatchTheSimulatorsCopies()
        {
            // MatchSim mirrors two literals the game writes inline: a push counts as a shove (and cuts a Peck) above an impulse of 1,
            // and a push is applied while its velocity squared is above 0.001.
            StringAssert.Contains("if (impulse.sqrMagnitude > 1f)", File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs"));
            StringAssert.Contains("ExternalDisplacement.sqrMagnitude > 0.001f", File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenMovement.cs"));
            StringAssert.Contains("ExternalDisplacement, Vector3.zero, KnockbackDecayRate * deltaTime", File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenMovement.cs"));
        }
    }
}
