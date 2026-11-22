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
    /// The places where the independent review of the match simulator found it deciding differently from the game (BL-207 review): how slows
    /// and speed boosts combine, the Shield's follow-up, the Golden Pile's footprint and spot, pushes, the reaction-delay counter and the
    /// removal clean-up. Synthetic maps and kits: every number is the test's own, none is read from an asset. Where the game decides inline and
    /// the simulator mirrors it, a source test fails when the game moves.
    /// </summary>
    public sealed class MatchReviewFixTests
    {
        private const float Half = 40f;
        private const float Tick = 1f / 30f;
        private const float Big = 1000f;
        private const float ShoveDecay = 8f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        // ---- helpers ----

        private struct Sample
        {
            public float Time;
            public Vector2 Pos;
            public float Step;
        }

        private sealed class Spy : IMatchPolicy
        {
            private readonly IMatchPolicy _inner;
            private readonly List<(float time, Vector2 pos)> _log;
            public Spy(IMatchPolicy inner, List<(float, Vector2)> log) { _inner = inner; _log = log; }
            public MatchIntent Think(MatchView view, ref MatchRng rng)
            {
                _log.Add((view.Time, view.Seats[view.Self].Pos));
                return _inner.Think(view, ref rng);
            }
        }

        private static PolicyFactory Spying(PolicyFactory inner, List<(float time, Vector2 pos)> log) =>
            (seat, p) => new Spy(inner(seat, p), log);

        private static List<Sample> Samples(List<(float time, Vector2 pos)> log)
        {
            var list = new List<Sample>();
            for (int i = 1; i < log.Count; i++)
                list.Add(new Sample { Time = log[i - 1].time, Pos = log[i - 1].pos, Step = Vector2.Distance(log[i - 1].pos, log[i].pos) });
            return list;
        }

        private static float MeanStep(IEnumerable<Sample> all, Func<Sample, bool> window, string what)
        {
            var picked = all.Where(window).Select(x => x.Step).ToList();
            Assert.GreaterOrEqual(picked.Count, 3, $"too few ticks to judge: {what}");
            return picked.Average();
        }

        private static float MeanStep(IEnumerable<Sample> all, float from, float to, string what) =>
            MeanStep(all, x => x.Time >= from && x.Time < to, what);

        /// <summary>Walk toward <paramref name="goal"/>; cast the listed slots on the first think at or after each time, one per think.</summary>
        private static PolicyFactory WalkAndCast(Vector2 goal, params (float time, int slot)[] casts) =>
            (seat, p) =>
            {
                int next = 0;
                return MatchTestKit.Policy((v, s) =>
                {
                    var intent = new MatchIntent { Goal = goal, PeckPile = -1, CastSlot = -1, CastTarget = -1 };
                    if (next < casts.Length && v.Time + 1e-4f >= casts[next].time) intent.CastSlot = casts[next++].slot;
                    return intent;
                })(seat, p);
            };

        private static readonly Vector2 West = new Vector2(-35f, 0f);

        /// <summary>Stand where the chicken is; cast the listed slots on the first think at or after each time, one per think.</summary>
        private static PolicyFactory CastsAt(params (float time, int slot)[] casts) =>
            (seat, p) =>
            {
                int next = 0;
                return MatchTestKit.Policy((v, s) =>
                {
                    var intent = MatchTestKit.StandAt(v, s);
                    if (next < casts.Length && v.Time + 1e-4f >= casts[next].time) intent.CastSlot = casts[next++].slot;
                    return intent;
                })(seat, p);
            };

        private static MatchResult Run(MatchSetup setup, ulong seed = 7)
        {
            setup.Rules.KnockbackDecayRate = ShoveDecay;
            return MatchSim.Run(setup, seed);
        }

        private static MatchSetup Seats(OracleMapSO map, float clock, MatchKit k0, MatchKit k1, MatchKit k2 = null)
        {
            var setup = MatchTestKit.Setup(map, new[] { k0, k1, k2, null }, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = clock;
            setup.RecordEvents = true;
            return setup;
        }

        // ---- item 1, 6: slows do not stack ----

        [TestCase(0.8f, 0.6f, 1f, TestName = "Slows_PileAndTimed_OnlyTheStrongestApplies")]
        [TestCase(0.8f, 1f, 0.5f, TestName = "Slows_PileAndAura_OnlyTheStrongestApplies")]
        [TestCase(0.8f, 0.6f, 0.5f, TestName = "Slows_PileTimedAndAura_OnlyTheStrongestApplies")]
        [TestCase(0.9f, 0.5f, 0.6f, TestName = "Slows_TimedStrongerThanAura_OnlyTheStrongestApplies")]
        public void Slows_DoNotStack(float pile, float timed, float aura)
        {
            const float speed = 6f;
            var map = MatchTestKit.OpenMap(Half);
            var victim = MatchTestKit.Fighter("victim", speed, new MatchAbility[0], MatchPassive.Identity(timed, aura));
            // Every kit answers both factors: a slow or an aura reaches every rival, whoever cast the other.
            var slower = timed < 1f ? MatchTestKit.Fighter("slower", 1f, new[] { MatchTestKit.Slow(Big, 20f, timed, 99f) }, MatchPassive.Identity(timed, aura)) : null;
            var areaer = aura < 1f ? MatchTestKit.Fighter("areaer", 1f, new[] { MatchTestKit.SlowArea(Big, aura, 20f, 99f) }, MatchPassive.Identity(timed, aura)) : null;
            var setup = Seats(map, 14f, victim, slower, areaer);
            setup.Rules.Farm.PileSlowFactor = pile;
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West), log);
            if (slower != null) setup.Policies[1] = MatchTestKit.CastAt(0.1f, 0);
            if (areaer != null) setup.Policies[2] = MatchTestKit.CastAt(0.1f, 0);
            Run(setup);

            // Standing in the middle pile's reach, with every slow on: only the strongest of the three applies.
            float expected = speed * Tick * Mathf.Min(pile, Mathf.Min(timed, aura));
            float inPile = MeanStep(Samples(log), x => x.Time > 0.5f && Mathf.Abs(x.Pos.x) < 0.4f, "in the pile's reach");
            Assert.AreEqual(expected, inPile, 0.03f * speed * Tick, "pile, timed slow and aura must not multiply");
        }

        [Test]
        public void AuraSlow_EndsWithTheArea_TheTimedSlowKeepsItsOwnFactor()
        {
            const float speed = 6f, timed = 0.6f, aura = 0.5f, areaSeconds = 1f, timedSeconds = 6f;
            var map = MatchTestKit.OpenMap(Half);
            var victim = MatchTestKit.Fighter("victim", speed, new MatchAbility[0], MatchPassive.Identity(timed, aura));
            var slower = MatchTestKit.Fighter("slower", 1f, new[] { MatchTestKit.Slow(Big, timedSeconds, timed, 99f) }, MatchPassive.Identity(timed, aura));
            var areaer = MatchTestKit.Fighter("areaer", 1f, new[] { MatchTestKit.SlowArea(Big, aura, areaSeconds, 99f) }, MatchPassive.Identity(timed, aura));
            var setup = Seats(map, 6f, victim, slower, areaer);
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West), log);
            setup.Policies[1] = MatchTestKit.CastAt(0.1f, 0);
            setup.Policies[2] = MatchTestKit.CastAt(0.1f, 0);
            Run(setup);

            float full = speed * Tick;
            Assert.AreEqual(full * aura, MeanStep(Samples(log), 0.3f, 0.1f + areaSeconds - 0.1f, "inside the aura"), 0.03f * full);
            Assert.AreEqual(full * timed, MeanStep(Samples(log), 0.1f + areaSeconds + 0.3f, timedSeconds - 0.3f, "after the aura"), 0.03f * full,
                "the aura's factor must end with the area, not stay merged into the timed slow");
        }

        // ---- item 3: the Shield's walk refuses new slows only ----

        private static MatchKit ShieldWalker(float walk, float walkMult, float egg, float slowFactor) =>
            MatchTestKit.Fighter("walker", 6f, new[] { MatchTestKit.Shield(walk, walkMult, egg, 99f) }, MatchPassive.Identity(slowFactor));

        [Test]
        public void ShieldWalk_KeepsASlowAlreadyRunning()
        {
            const float walk = 1f, walkMult = 0.8f, egg = 0.5f, factor = 0.5f, shieldAt = 0.6f;
            var map = MatchTestKit.OpenMap(Half);
            var setup = Seats(map, 4f, ShieldWalker(walk, walkMult, egg, factor),
                MatchTestKit.Fighter("slower", 1f, new[] { MatchTestKit.Slow(Big, 3f, factor, 99f) }));
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West, (shieldAt, 0)), log);
            setup.Policies[1] = MatchTestKit.CastAt(0.3f, 0);
            Run(setup);

            float full = 6f * Tick;
            Assert.AreEqual(full * factor, MeanStep(Samples(log), 0.4f, shieldAt - 0.1f, "slowed before the shield"), 0.03f * full);
            Assert.AreEqual(full * walkMult * factor, MeanStep(Samples(log), shieldAt + 0.2f, shieldAt + walk - 0.1f, "slow still running in the walk"), 0.03f * full,
                "the walk refuses new slows only; the one already running goes on (ChickenController: the timed slow is applied every tick)");
        }

        [Test]
        public void ShieldWalk_RefusesANewSlow()
        {
            const float walk = 1f, walkMult = 0.8f, egg = 0.5f, factor = 0.5f;
            var map = MatchTestKit.OpenMap(Half);
            var setup = Seats(map, 4f, ShieldWalker(walk, walkMult, egg, factor),
                MatchTestKit.Fighter("slower", 1f, new[] { MatchTestKit.Slow(Big, 3f, factor, 99f) }));
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West, (0.2f, 0)), log);
            setup.Policies[1] = MatchTestKit.CastAt(0.5f, 0);
            Run(setup);

            float full = 6f * Tick;
            Assert.AreEqual(full * walkMult, MeanStep(Samples(log), 0.6f, 1.1f, "walk, slow cast during it"), 0.03f * full);
            Assert.AreEqual(full, MeanStep(Samples(log), 0.2f + walk + egg + 0.3f, 3.5f, "after the shield"), 0.03f * full, "the refused slow must not land later");
        }

        // ---- item 2: the Shield's follow-up ----

        private static MatchKit ShieldAndSteal(float walk, float walkMult, float egg) =>
            MatchTestKit.Fighter("walker", 6f, new[] { MatchTestKit.Shield(walk, walkMult, egg, 99f), MatchTestKit.Steal(1f, 1f, 99f) });

        [Test]
        public void Shield_AnyOtherCastDuringTheWalk_DropsTheEgg()
        {
            const float walk = 1f, walkMult = 0.5f, egg = 2f, shieldAt = 0.4f, otherAt = 0.7f;
            var setup = Seats(MatchTestKit.OpenMap(Half), 5f, ShieldAndSteal(walk, walkMult, egg), MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West, (shieldAt, 0), (otherAt, 1)), log);
            Run(setup);

            float full = 6f * Tick;
            float walkEnds = shieldAt + walk;
            Assert.AreEqual(full * walkMult, MeanStep(Samples(log), otherAt + 0.2f, walkEnds - 0.1f, "the rest of the walk"), 0.03f * full, "the walk goes on");
            Assert.AreEqual(full, MeanStep(Samples(log), walkEnds + 0.2f, shieldAt + walk + egg - 0.2f, "where the egg would have stood"), 0.03f * full,
                "the pending egg is dropped: the chicken walks on instead of standing still");
        }

        [Test]
        public void Shield_AnyOtherCastDuringTheEgg_EndsTheShield()
        {
            const float walk = 0.5f, walkMult = 0.5f, egg = 2f, shieldAt = 0.4f, otherAt = 1.4f;
            var setup = Seats(MatchTestKit.OpenMap(Half), 5f, ShieldAndSteal(walk, walkMult, egg), MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West, (shieldAt, 0), (otherAt, 1)), log);
            Run(setup);

            float full = 6f * Tick;
            Assert.AreEqual(0f, MeanStep(Samples(log), shieldAt + walk + 0.1f, otherAt - 0.1f, "the egg"), 1e-4f);
            Assert.AreEqual(full, MeanStep(Samples(log), otherAt + 0.2f, shieldAt + walk + egg - 0.2f, "after the cast"), 0.03f * full,
                "a cast during the egg ends it at once: the chicken is on its way again");
        }

        [Test]
        public void ShieldEgg_CanBeCastThrough_NotRefused()
        {
            // The game refuses a cast only while stunned (ControlRules.CanCast); the egg does not stop one.
            var setup = Seats(MatchTestKit.OpenMap(Half), 4f, ShieldAndSteal(0.3f, 0.5f, 2f), MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            setup.Policies[0] = CastsAt((0.2f, 0), (1.0f, 1));
            var r = Run(setup);
            Assert.AreEqual(0, r.RejectedIntents, "a cast during the egg is legal");
            Assert.AreEqual(2, r.Counters[0].Casts);
        }

        private static PolicyFactory PeckerCasting(int pile, params (float time, int slot)[] casts) =>
            (seat, p) =>
            {
                int next = 0;
                return MatchTestKit.Policy((v, s) =>
                {
                    var intent = MatchTestKit.PeckAt(v, s, pile);
                    if (next < casts.Length && v.Time + 1e-4f >= casts[next].time) intent.CastSlot = casts[next++].slot;
                    return intent;
                })(seat, p);
            };

        private static List<float> PeckStarts(MatchResult r) =>
            r.Events.Where(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other >= 0).Select(e => e.Time).ToList();

        [Test]
        public void Shield_StopsTheAutoPeckForTheWholeRun()
        {
            const float walk = 1f, egg = 2f, shieldAt = 2f;
            var kit = MatchTestKit.Fighter("pecker", 50f, new[] { MatchTestKit.Shield(walk, 0.5f, egg, 99f) }, null, 2f, 0.4f, 0.4f);
            var setup = Seats(MatchTestKit.OpenMap(Half), 8f, kit, MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            setup.Food = new OracleFood { Personal = 200f, Contested = 15f, Centre = 40f };
            setup.Policies[0] = PeckerCasting(OracleMapSO.PersonalIndex(0), (shieldAt, 0));
            var starts = PeckStarts(Run(setup));

            Assert.IsTrue(starts.Any(t => t < shieldAt - 0.5f), "the chicken pecked before the shield");
            Assert.IsFalse(starts.Any(t => t > shieldAt + 1e-3f && t < shieldAt + walk + egg - 1e-3f), "no peck starts while the Shield's egg is pending or running");
            Assert.IsTrue(starts.Any(t => t >= shieldAt + walk + egg - 1e-3f), "the peck resumes when the Shield is over");
        }

        [Test]
        public void Shield_PeckResumesAtOnce_WhenAnotherCastDroppedTheEgg()
        {
            const float walk = 1f, egg = 2f, shieldAt = 2f, otherAt = 2.3f;
            var kit = MatchTestKit.Fighter("pecker", 50f, new[] { MatchTestKit.Shield(walk, 0.5f, egg, 99f), MatchTestKit.Steal(1f, 1f, 99f) }, null, 2f, 0.4f, 0.4f);
            var setup = Seats(MatchTestKit.OpenMap(Half), 8f, kit, MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            setup.Food = new OracleFood { Personal = 200f, Contested = 15f, Centre = 40f };
            setup.Policies[0] = PeckerCasting(OracleMapSO.PersonalIndex(0), (shieldAt, 0), (otherAt, 1));
            var after = PeckStarts(Run(setup)).Where(t => t > shieldAt + 1e-3f).OrderBy(t => t).ToList();

            Assert.IsNotEmpty(after);
            Assert.GreaterOrEqual(after[0], otherAt - 1e-3f, "no peck while the egg is still to come");
            Assert.LessOrEqual(after[0], otherAt + 3f * Tick, "the chain starts again as soon as the egg is dropped, inside the rest of the walk");
            Assert.Less(after[0], shieldAt + walk);
        }

        // ---- item 5: speed boosts ----

        [Test]
        public void SpeedBoosts_OnlyTheStrongestApplies_AndTheDashStillMeetsThePileSlow()
        {
            const float speed = 4f, speedMult = 2f, dashMult = 3f, pile = 0.8f;
            var map = MatchTestKit.OpenMap(Half);
            var kit = MatchTestKit.Fighter("both", speed, new[] { MatchTestKit.Speed(speedMult, 20f, 99f), MatchTestKit.Jump(dashMult, 20f, 99f) });
            var setup = Seats(map, 6f, kit, MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            setup.Rules.Farm.PileSlowFactor = pile;
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(WalkAndCast(West, (0.1f, 0), (0.2f, 1)), log);
            Run(setup);

            float full = speed * Tick;
            var samples = Samples(log);
            Assert.AreEqual(full * Mathf.Max(speedMult, dashMult), MeanStep(samples, x => x.Time > 0.5f && x.Pos.x > 4f && x.Pos.x < 14f, "both boosts, no pile"), 0.03f * full,
                "Speed and the Jump's dash are both boosts: the strongest applies, they do not multiply");
            Assert.AreEqual(full * Mathf.Max(speedMult, dashMult) * pile, MeanStep(samples, x => x.Time > 0.5f && Mathf.Abs(x.Pos.x) < 0.9f, "dashing through the pile"), 0.03f * full,
                "the pile slow applies during a dash too");
        }

        [Test]
        public void ShippedKits_SpeedAndJump_ReportWhoHoldsBoth()
        {
            // A report line, never a rule: the simulator takes the strongest boost, which only matters for a kit holding two boosters.
            var inputs = new OracleInputs
            {
                Map = UnityEditor.AssetDatabase.LoadAssetAtPath<OracleMapSO>(CluckWars.EditorTools.OracleMapBaker.AssetPath),
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
            var kits = MatchKits.All(inputs);
            Assert.IsNotEmpty(kits);
            int count = kits.Count();
            var both = kits.Where(k => k.Abilities != null && k.Abilities.Any(a => a.Job == AbilityJob.Speed) && k.Abilities.Any(a => a.Job == AbilityJob.Jump)).Select(k => k.Label).ToList();
            TestContext.Out.WriteLine($"SHIPPED KITS HOLDING BOTH SPEED AND JUMP: {(both.Count == 0 ? "none" : string.Join(", ", both))} (of {count})");
        }

        // ---- item 7: pushes ----

        [Test]
        public void Push_WaitsForTheStunToEnd()
        {
            const float stunSeconds = 1f, knock = 16f, stunAt = 0.3f, shoveAt = 0.5f;
            var victim = MatchTestKit.Fighter("victim", 0f, new MatchAbility[0]);     // speed 0: the whole displacement is the push
            var attacker = MatchTestKit.Fighter("attacker", 1f, new[] { MatchTestKit.Stun(Big, stunSeconds, 99f), MatchTestKit.Shove(Big, knock, 99f) });
            var setup = Seats(MatchTestKit.OpenMap(Half), 4f, victim, attacker);
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = Spying(MatchTestKit.Stand, log);
            setup.Policies[1] = CastsAt((stunAt, 0), (shoveAt, 1));
            Run(setup);

            Vector2 start = log[0].Item2;
            float stunEnds = stunAt + stunSeconds;
            foreach (var (time, pos) in log.Where(x => x.Item1 > shoveAt + 0.1f && x.Item1 < stunEnds - 0.1f))
                Assert.AreEqual(0f, Vector2.Distance(start, pos), 1e-4f, $"a stunned chicken is not pushed (t={time:0.00})");
            Vector2 end = log[log.Count - 1].Item2;
            Assert.AreEqual(MatchCombat.ShoveTravel(knock, 1f, ShoveDecay), Vector2.Distance(start, end), 0.05f, "the whole push lands once the stun is over");
        }

        [Test]
        public void Push_SlidesAlongAWall()
        {
            const float knock = 40f, wallY = 33f, wallHalf = 1f;
            // Seat 0 (east) shoves seat 1 (north): up and to the west, into a wall running along the x axis above it.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(-20f, wallY), new Vector2(20f, wallY), wallHalf);
            var victim = MatchTestKit.Fighter("victim", 0f, new MatchAbility[0]);
            var attacker = MatchTestKit.Fighter("attacker", 1f, new[] { MatchTestKit.Shove(Big, knock, 99f) });
            var setup = Seats(map, 4f, attacker, victim);
            var log = new List<(float, Vector2)>();
            setup.Policies[0] = CastsAt((0.2f, 0));
            setup.Policies[1] = Spying(MatchTestKit.Stand, log);
            Run(setup);

            Vector2 from = MatchSim.Spawn(map, 1);
            Vector2 dir = (from - MatchSim.Spawn(map, 0)).normalized;
            float travel = MatchCombat.ShoveTravel(knock, 1f, ShoveDecay);
            Vector2 end = log[log.Count - 1].Item2;
            Assert.AreEqual(wallY - wallHalf, end.y, 0.02f, "it ends against the wall's clearance edge");
            Assert.AreEqual(from.x + dir.x * travel, end.x, 0.1f, "and slid along it for the rest of the push instead of stopping dead");
        }

        [Test]
        public void Nav_Slide_StaysWhenClear_AndProjectsOntoTheClearanceEdgeAtAWall()
        {
            var map = MatchTestKit.OneWallMap(Half, new Vector2(0f, -10f), new Vector2(0f, 10f), 1f);
            var nav = OracleNav.For(map, Half);
            Assert.AreEqual(new Vector2(5f, 0f), nav.Slide(new Vector2(6f, 0f), new Vector2(5f, 0f)), "a clear way is taken as asked");
            Vector2 slid = nav.Slide(new Vector2(2f, 3f), new Vector2(0.5f, 5f));
            Assert.AreEqual(1f, slid.x, 0.01f, "stopped at the clearance edge");
            Assert.AreEqual(5f, slid.y, 0.01f, "with the movement along the wall kept");
            Assert.IsTrue(nav.IsClear(slid, slid));
        }

        [Test]
        public void Nav_NearestClear_NeverLeavesAPointInsideAWall()
        {
            var map = MatchTestKit.OneWallMap(Half, new Vector2(-10f, 0f), new Vector2(10f, 0f), 1f);
            var nav = OracleNav.For(map, Half);
            foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(3f, 0.2f), new Vector2(-10f, -0.4f), new Vector2(10.3f, 0.1f), new Vector2(20f, 20f) })
                Assert.IsTrue(nav.IsClear(nav.NearestClear(p), nav.NearestClear(p)), $"{p}");
            Assert.AreEqual(new Vector2(20f, 20f), nav.NearestClear(new Vector2(20f, 20f)), "a clear point stays");
        }

        // ---- item 4: the Golden Pile ----

        private static ComebackSettings GoldenOnly => new ComebackSettings
        {
            SecondsLeft = 8f, GoldenPileFood = 20f, RestockFood = 7f, GoldenRingMin = 3f, GoldenRingMax = 5f,
        };

        private static MatchSetup ComebackSetup(OracleMapSO map, MatchKit k0, MatchKit k1)
        {
            var setup = MatchTestKit.Duel(map, k0, k1, MatchTestKit.Stand, MatchTestKit.Stand, Half, 12f);
            setup.Rules.Comeback = GoldenOnly;
            setup.Rules.KnockbackDecayRate = ShoveDecay;
            return setup;
        }

        private static IEnumerable<(ulong seed, MatchResult result)> GoldenRuns(OracleMapSO map, int seeds)
        {
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                var r = MatchSim.Run(ComebackSetup(map, MatchTestKit.Fighter("a", 1f, new MatchAbility[0]), MatchTestKit.Fighter("b", 1f, new MatchAbility[0])), seed);
                if (r.Comeback == CluckWars.Gameplay.MatchEventKind.GoldenPile) yield return (seed, r);
            }
        }

        [Test]
        public void GoldenPile_HasItsOwnFootprint_NotTheCentreIslands()
        {
            var map = MatchTestKit.OpenMap(Half);
            map.Places[OracleMapSO.CentreIndex].Footprint = new Vector2(9f, 7f);     // the island: much larger
            map.GoldenFootprint = new Vector2(3f, 2f);                               // the plain pile prefab
            ulong seed = GoldenRuns(map, 80).First().seed;

            var log = new List<(float, Vector2)>();
            Vector2 spot = default;
            var forager = MatchTestKit.Policy((v, s) =>
            {
                log.Add((v.Time, v.Seats[s].Pos));
                spot = v.GoldenPos;
                return v.GoldenActive ? MatchTestKit.PeckAt(v, s, MatchGeometry.GoldenPile) : MatchTestKit.StandAt(v, s);
            });
            var setup = ComebackSetup(map, MatchTestKit.Fighter("forager", 100f, new MatchAbility[0], null, 2f, 0.4f, 0.2f), MatchTestKit.Fighter("b", 1f, new MatchAbility[0]));
            setup.Policies[0] = forager;
            var r = MatchSim.Run(setup, seed);

            float firstPeck = r.Events.First(e => e.Kind == MatchEventKind.CastStart && e.Seat == 0 && e.Other == MatchGeometry.GoldenPile).Time;
            Vector2 at = log.Last(x => x.Item1 <= firstPeck + 1e-4f).Item2;
            Vector2 toward = at - spot;
            float goldenRim = MatchGeometry.Rim(map, new OraclePlace { Kind = OraclePlaceKind.Centre, Footprint = map.GoldenFootprint }, toward);
            float islandRim = MatchGeometry.Rim(map, new OraclePlace { Kind = OraclePlaceKind.Centre, Footprint = map.Places[OracleMapSO.CentreIndex].Footprint }, toward);
            Assert.Greater(islandRim - goldenRim, 1f, "the test only means something when the two footprints differ");
            Assert.AreEqual(goldenRim, toward.magnitude, 0.1f, "the chicken stops at the plain pile's own rim");
        }

        [Test]
        public void GoldenPile_RedrawsASpotInsideAWall_LikeTheGameDoes()
        {
            // A wall across the ring: a ball of the game's clear radius fits nowhere within the wall's reach, and the game redraws up to 8 times.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(-20f, 0f), new Vector2(20f, 0f), 1f);
            var nav = OracleNav.For(map, Half);
            var goldens = GoldenRuns(map, 150).ToList();
            Assert.GreaterOrEqual(goldens.Count, 30, "too few Golden Piles to judge");
            foreach (var g in goldens)
                Assert.IsTrue(nav.SpotClear(g.result.GoldenSpot, GameManager.GoldenPileClearRadius), $"seed {g.seed}: spot {g.result.GoldenSpot} touches a wall");
        }

        [Test]
        public void GoldenPile_WhenEverySpotIsBlocked_TheLastIsKept_AndNobodyThrowsReachingIt()
        {
            // Every candidate stands inside a thick wall; the game then spawns at the last one. The sim must neither redraw for ever nor throw.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(-1f, -1f), new Vector2(1f, 1f), 6.5f);
            map.GoldenFootprint = new Vector2(10f, 10f);
            var nav = OracleNav.For(map, Half);
            int tried = 0;
            foreach (var (seed, golden) in GoldenRuns(map, 40).Take(6))
            {
                tried++;
                Assert.IsFalse(nav.SpotClear(golden.GoldenSpot, GameManager.GoldenPileClearRadius), "the test needs a blocked ring");
                var setup = ComebackSetup(map, MatchTestKit.Fighter("forager", 20f, new MatchAbility[0], null, 2f, 0.4f, 0.2f), MatchTestKit.Fighter("b", 1f, new MatchAbility[0]));
                setup.Policies[0] = MatchTestKit.Policy((v, s) => v.GoldenActive ? MatchTestKit.PeckAt(v, s, MatchGeometry.GoldenPile) : MatchTestKit.StandAt(v, s));
                MatchResult r = null;
                Assert.DoesNotThrow(() => r = MatchSim.Run(setup, seed), $"seed {seed}");
                Assert.Greater(r.Counters[0].Foraged, 0f, $"seed {seed}: the chicken reaches the pile from the nearest standing point");
            }
            Assert.Greater(tried, 0);
        }

        [Test]
        public void Nav_SpotClear_CountsTheWallsOwnThickness()
        {
            // The bake's clearance is half the thickness plus the chicken radius; a ball must clear the thickness alone.
            var map = MatchTestKit.OneWallMap(Half, new Vector2(-20f, 0f), new Vector2(20f, 0f), 1f);
            var nav = OracleNav.For(map, Half);
            float body = 1f - PinwheelLayout.ChickenRadius;
            Assert.IsFalse(nav.SpotClear(new Vector2(0f, body + 0.69f), 0.7f));
            Assert.IsTrue(nav.SpotClear(new Vector2(0f, body + 0.71f), 0.7f));
        }

        // ---- item 9: the counters ----

        private static MatchResult CastEveryThink(float delay, Func<MatchView, int, bool> castNow, MatchAbility[] abilities, float clock = 2f)
        {
            var setup = Seats(MatchTestKit.OpenMap(Half), clock, MatchTestKit.Fighter("caster", 5f, abilities), MatchTestKit.Fighter("other", 1f, new MatchAbility[0]));
            setup.Params[0].ReactionDelay = delay;
            setup.Policies[0] = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.StandAt(v, s);
                if (castNow(v, s)) intent.CastSlot = 0;
                return intent;
            });
            return Run(setup);
        }

        [Test]
        public void ReactionDelay_CastLegalWhenDecided_IsNotARejectedIntent()
        {
            // A sound policy: it asks for the cast only while its view says the slot is ready. The delay lets several such asks pile up.
            var r = CastEveryThink(0.3f, (v, s) => v.Seats[s].CooldownLeft[1] <= 0f, new[] { MatchTestKit.Stun(1f, 0.5f, 99f) });
            Assert.AreEqual(0, r.RejectedIntents, "a sound policy has no rejected intents, delay or not");
            Assert.Greater(r.CastsLostToReactionDelay, 0, "the asks decided during the delay found the slot spent");
            Assert.AreEqual(1, r.Counters[0].Casts);
        }

        [Test]
        public void RejectedIntents_CountsAPolicyThatAsksForWhatWasNeverLegal()
        {
            // No delay, and the policy casts on every think: after the first cast the slot was on cooldown when it decided.
            var cooling = CastEveryThink(0f, (v, s) => true, new[] { MatchTestKit.Stun(1f, 0.5f, 99f) });
            Assert.Greater(cooling.RejectedIntents, 10);
            Assert.AreEqual(0, cooling.CastsLostToReactionDelay);

            // A Peck slot is no cast: pecking is the PeckPile intent.
            var peck = CastEveryThink(0f, (v, s) => true, new[] { new MatchAbility { Job = AbilityJob.Peck, Asset = "test-peck" } }, clock: 1f);
            Assert.Greater(peck.RejectedIntents, 10);
            Assert.AreEqual(0, peck.Counters[0].Casts);
        }

        // ---- item 10: the removal clean-up ----

        [TestCase("Speed")]
        [TestCase("Slow")]
        [TestCase("Stun")]
        [TestCase("Shield")]
        public void Removal_ClearsWhateverRanBeforeIt(string state)
        {
            const float speed = 6f, killAt = 1.2f, removal = 1f, slowFactor = 0.5f;
            var spoiler = MatchTestKit.Fighter("spoiler", 1f, new[]
            {
                MatchTestKit.DeathMark(0.1f, 5f, 5f, 5f, 5f, removal),
                MatchTestKit.Slow(Big, 30f, slowFactor, 99f),
                MatchTestKit.Stun(Big, 30f, 99f),
            }, MatchPassive.Identity(slowFactor));
            var victimAbilities = new[] { MatchTestKit.Speed(3f, 30f, 99f), MatchTestKit.Shield(0.2f, 0.5f, 30f, 99f) };
            var victim = MatchTestKit.Fighter("victim", speed, victimAbilities, MatchPassive.Identity(slowFactor));
            var setup = Seats(MatchTestKit.OpenMap(Half), 6f, spoiler, victim);
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Empty, Times = new[] { killAt } };
            var log = new List<(float, Vector2)>();
            switch (state)
            {
                case "Speed": setup.Policies[1] = Spying(WalkAndCast(West, (0.3f, 0)), log); setup.Policies[0] = MatchTestKit.Stand; break;
                case "Slow": setup.Policies[1] = Spying(WalkAndCast(West), log); setup.Policies[0] = CastsAt((0.3f, 1)); break;
                case "Stun": setup.Policies[1] = Spying(WalkAndCast(West), log); setup.Policies[0] = CastsAt((0.3f, 2)); break;
                default: setup.Policies[1] = Spying(WalkAndCast(West, (0.3f, 1)), log); setup.Policies[0] = MatchTestKit.Stand; break;
            }
            var r = Run(setup);

            Assert.AreEqual(1, r.Kills[0], "the scheduled kill happened");
            float back = killAt + removal;
            float full = speed * Tick;
            Assert.AreEqual(full, MeanStep(Samples(log), back + 0.3f, back + 1.5f, "after the respawn"), 0.03f * full,
                $"nothing of the old life ({state}) may carry into the respawn (ChickenController.EndEverythingForRemoval)");
        }

        [Test]
        public void Game_EndEverythingForRemoval_StillClearsExactlyWhatTheSimulatorsRemoveMirrors()
        {
            string cc = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            int at = cc.IndexOf("public void EndEverythingForRemoval()", StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0);
            string body = cc.Substring(at, cc.IndexOf("private PendingTeleport _pendingTeleport;", at, StringComparison.Ordinal) - at);
            var statements = body.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"\s+", " "))
                .Where(l => l.Length > 0 && !l.StartsWith("///") && !l.StartsWith("//") && l != "{" && l != "}" && !l.StartsWith("public void")).ToList();
            // Each line and what MatchSim.Remove does for it (every one mirrored; a new line here is a new thing to mirror).
            var expected = new[]
            {
                "if (!HasStateAuthority) return;",
                "_abilities?.EndAllForRemoval();",          // every running ability ends: Speed, dash, Hide, Shield, aura, Peck; cooldowns stay
                "_traversal?.Abort();",                      // a dash ends
                "_abilitySlowUntil = double.MinValue;",     // the timed slow
                "_abilitySlowFactor = 1f;",
                "_inContact = false;",                       // collision slow: not modelled
                "RootTimer = TickTimer.None;",               // roots: not modelled
                "Rooted = false;",
                "IsStunned = false;",                        // the stun
                "StunTimer = TickTimer.None;",
                "StealProofTimer = TickTimer.None;",         // the egg's windows
                "ControlImmuneTimer = TickTimer.None;",
                "SlowImmuneTimer = TickTimer.None;",
                "Effects.Clear();",                          // the speed stack
                "SyncAbilityEffects();",
                "ExternalDisplacement = Vector3.zero;",      // a push in flight
            };
            CollectionAssert.AreEqual(expected, statements,
                "EndEverythingForRemoval changed: bring MatchSim.Remove in line with it, then update this list");
        }

        // ---- the game rules the fixes mirror, pinned ----

        private static string Between(string text, string from, string to)
        {
            int a = text.IndexOf(from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(a, 0, $"'{from}' not found: the game moved, re-read what the simulator mirrors");
            int b = text.IndexOf(to, a + from.Length, StringComparison.Ordinal);
            Assert.GreaterOrEqual(b, 0, $"'{to}' not found after '{from}'");
            return text.Substring(a, b - a);
        }

        [Test]
        public void Game_Slows_StillKeepOnlyTheStrongest_AndTheTimedSlowIgnoresImmunity()
        {
            string cc = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            StringAssert.Contains("SlowMultiplier = Mathf.Min(SlowMultiplier, factor);", cc, "MatchCombat.CombineSlows mirrors this");
            string sources = Between(cc, "// Pile slow: ChickenCargo sets IsPileSlow", "// General stun timer expiry");
            StringAssert.Contains("ApplySlow(SlowSource.Pile, PileSlowFactor);", sources);
            StringAssert.Contains("ApplyAbilitySlow(_abilitySlowFactor);", sources);
            StringAssert.Contains("CheckAbilityZoneSlow();", sources);
            StringAssert.DoesNotContain("IsSlowImmune", Between(sources, "// Timed ability slow", "// Zone slow"), "the timed slow runs on through the Shield's walk; the simulator keeps it");
            StringAssert.Contains("if (IsSlowImmune) return;", Between(cc, "private void CheckAbilityZoneSlow()", "for (int i = 0;"), "the aura is refused during the walk");
        }

        [Test]
        public void MatchCombat_CombineSlows_IsTheMinimum()
        {
            Assert.AreEqual(0.5f, MatchCombat.CombineSlows(0.8f, 0.6f, 0.5f));
            Assert.AreEqual(0.6f, MatchCombat.CombineSlows(0.8f, 0.6f, 1f));
            Assert.AreEqual(1f, MatchCombat.CombineSlows(1f, 1f, 1f));
        }

        [Test]
        public void Game_ShieldFollowUp_AndAutoPeck_StillWorkAsTheSimulatorAssumes()
        {
            string ac = File.ReadAllText("Assets/_Game/Scripts/Gameplay/AbilityController.cs");
            StringAssert.Contains("if (HasLiveFollowUp(i, running)) return true;", Between(ac, "private bool AnyStealthRunning(int peckSlot)", "private byte _followUpCancelled;"),
                "auto-Peck is refused while a Shield follow-up is live");
            string cancel = Between(ac, "private void EndStealthAndPeckForCast(int castSlot)", "private void HandleOwnerDeath(");
            StringAssert.Contains("if (i == castSlot || !HasLiveFollowUp(i, running)) continue;", cancel);
            StringAssert.Contains("next = running.OnOtherCast(_ctx, remaining);", cancel);
            StringAssert.Contains("EndStealthAndPeckForCast(slot);", Between(ac, "// \"Using a move breaks it\"", "ActiveMask = AbilityRunRules.Begin(ActiveMask, slot);"),
                "every successful cast, a Peck's included, runs it");
            // The egg does not stop a cast: only a stun does.
            Assert.IsFalse(ControlRules.CanCast(ControlState.Stunned));
            Assert.IsTrue(ControlRules.CanCast(ControlState.Slowed));
            Assert.IsTrue(ControlRules.CanCast(ControlState.Free));
            Assert.AreEqual(0f, ShieldRules.RunAfterOtherCast(1.5f, 2f, eggRunning: true), "a cast during the egg ends the Shield");
            Assert.AreEqual(1f, ShieldRules.RunAfterOtherCast(3f, 2f, eggRunning: false), 1e-6f, "a cast during the walk leaves the rest of the walk");
        }

        [Test]
        public void Game_StunnedChicken_DoesNotRunItsMovementTick()
        {
            Assert.IsFalse(ControlRules.CanMove(ControlState.Stunned));
            string cc = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenController.cs");
            string tail = cc.Substring(cc.IndexOf("if (!ControlRules.CanMove(CurrentControlState)) return;", StringComparison.Ordinal));
            Assert.Less(tail.IndexOf("if (!ControlRules.CanMove", StringComparison.Ordinal), tail.IndexOf("_movement.Tick(input.Movement", StringComparison.Ordinal),
                "the stun gate sits before the movement tick, which is where a push is applied");
        }

        [Test]
        public void Game_GoldenPile_SpawnsThePlainPrefab_WithTheConstantsTheSimulatorReads()
        {
            string gm = File.ReadAllText("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            string spawn = Between(gm, "private void SpawnGoldenPile()", "private void ApplyUnderdogSurge()");
            StringAssert.Contains("i < GoldenPileSpawnAttempts", spawn);
            StringAssert.Contains("Physics.CheckSphere(pos + Vector3.up * 0.8f, GoldenPileClearRadius)", spawn);
            StringAssert.DoesNotContain("FootprintSize", spawn, "the spawn now stamps a footprint: the baked GoldenFootprint is no longer the pile's size");
            StringAssert.DoesNotContain("pile.Yaw", spawn, "the spawn now stamps a yaw: the simulator draws the Golden Pile unrotated");
        }

        [Test]
        public void Game_UnderdogSurge_TieGoesToTheFirstControllerListed()
        {
            string gm = File.ReadAllText("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            string body = Between(gm, "private void ApplyUnderdogSurge()", "private float GetBaseFoodForCorner(");
            StringAssert.Contains("float lowestFood = float.MaxValue;", body);
            StringAssert.Contains("if (food < lowestFood)", body, "strictly lower: the first of equals keeps it. The simulator takes the lowest seat index (a seat bias on ties, in the design's caveats)");
        }
    }
}
