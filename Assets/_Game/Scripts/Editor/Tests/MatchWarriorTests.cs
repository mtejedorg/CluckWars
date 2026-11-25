using System;
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
    /// The two Warrior kits under test in the match simulator (design 2026-11-25): the Bully's hit-steal and the Trophy Hunter's break and
    /// trophies. Every number is the test's own on synthetic maps and kits; nothing asserts a win rate.
    /// </summary>
    public sealed class MatchWarriorTests
    {
        private const float Half = 40f;
        private const float Big = 1000f;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        // ---------------------------------------------------------------- helpers

        private static HitStealFacts Hits(float stun, float shove, float lockSeconds = 3f, float slow = 0f) =>
            new HitStealFacts { Stun = stun, Shove = shove, Slow = slow, LockSeconds = lockSeconds };

        private static MatchKit Bully(HitStealFacts facts, int capacity = 100, params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter("bully", 100f, abilities, capacity: capacity);
            kit.Passive.HitSteal = facts;
            return kit;
        }

        // A forager that picks one bite of `bite` from its own pile and then holds it (the next Peck is far off).
        private static MatchKit Carrier(string label, float bite = 6f, params MatchAbility[] abilities) =>
            MatchTestKit.Fighter(label, 100f, abilities, peckAmount: bite, peckCooldown: 500f, castSeconds: 0.1f);

        private static PolicyFactory Casts(params (float time, int slot)[] plan)
        {
            int next = 0;
            return MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.StandAt(v, s);
                if (next < plan.Length && v.Time + 1e-4f >= plan[next].time) intent.CastSlot = plan[next++].slot;
                return intent;
            });
        }

        private static MatchSetup Arena(MatchKit[] kits, float clock)
        {
            var setup = MatchTestKit.Setup(MatchTestKit.OpenMap(Half), kits, MatchTestKit.Stand, Half);
            setup.Rules.Farm.ClockSeconds = clock;
            setup.RecordEvents = true;
            return setup;
        }

        // Seats 1.. peck their own personal pile; seat 0 runs `bully`.
        private static MatchSetup Brawl(MatchKit bully, MatchKit[] rivals, PolicyFactory bullyPolicy, float clock)
        {
            var kits = new MatchKit[4];
            kits[0] = bully;
            for (int i = 0; i < rivals.Length; i++) kits[i + 1] = rivals[i];
            var setup = Arena(kits, clock);
            setup.Policies[0] = bullyPolicy;
            for (int i = 1; i < 4; i++)
                if (kits[i] != null) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(i));
            return setup;
        }

        private static int Takes(MatchResult r) => r.Events.Count(e => e.Kind == MatchEventKind.HitSteal);

        // ---------------------------------------------------------------- the Bully's hit-steal

        [Test]
        public void Stun_TakesItsAmount_IntoTheBullysCargo()
        {
            var setup = Brawl(Bully(Hits(2f, 0f), 100, MatchTestKit.Stun(Big, 1f, 5f)), new[] { Carrier("victim") }, Casts((2f, 0)), 4f);
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(1, r.Counters[0].HitTakes);
            Assert.AreEqual(2f, r.Counters[0].HitFood, 1e-4f);
            Assert.AreEqual(2f, r.Counters[0].Stolen, 1e-4f, "hit-steal food is stolen food in the report");
            Assert.AreEqual(0, r.Counters[0].Steals, "but not a Steal cast");
            Assert.AreEqual(2f, r.Counters[1].LostToSteals, 1e-4f);
            Assert.AreEqual(2f, r.Counters[1].HitLostFood, 1e-4f);
            Assert.AreEqual(2f, r.Counters[0].StolenFromSeat[1], 1e-4f);
            Assert.AreEqual(2f, r.Banked[0], 1e-3f, "the take sat in the Bully's ordinary cargo, which banked at the base");
            Assert.AreEqual(1, Takes(r));
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void Shove_OnAnImmovableVictim_TakesNothing()
        {
            var victim = Carrier("victim");
            victim.Passive.KnockbackFactor = 0f;
            var bully = Bully(Hits(2f, 2f), 100, MatchTestKit.Shove(Big, 10f, 1f), MatchTestKit.Stun(Big, 1f, 1f));
            // A shove that moves nobody takes nothing and starts no lock: the stun that follows still takes.
            var r = MatchSim.Run(Brawl(bully, new[] { victim }, Casts((2f, 0), (2.5f, 1)), 5f), 1);
            Assert.AreEqual(1, r.Counters[0].HitTakes, "only the stun took");
            Assert.AreEqual(0, r.Counters[0].HitRefusedLock, "the shove that moved nobody started no lock");
            Assert.AreEqual(2f, r.Counters[0].HitFood, 1e-4f);
        }

        [Test]
        public void TheLock_RefusesTheSecondHitter_UntilItEnds()
        {
            var bully = Bully(Hits(2f, 2f, lockSeconds: 3f), 100, MatchTestKit.Stun(Big, 0.5f, 1f), MatchTestKit.Shove(Big, 5f, 1f));
            var r = MatchSim.Run(Brawl(bully, new[] { Carrier("victim", 10f) }, Casts((2f, 0), (2.5f, 1), (5.2f, 1)), 7f), 1);
            Assert.AreEqual(2, r.Counters[0].HitTakes, "the stun at 2.0 and the shove at 5.2, after the lock ended at 5.0");
            Assert.AreEqual(1, r.Counters[0].HitRefusedLock, "the shove at 2.5 met the lock");
            Assert.AreEqual(4f, r.Counters[0].HitFood, 1e-4f);
            var takes = r.Events.Where(e => e.Kind == MatchEventKind.HitSteal).Select(e => e.Time).ToList();
            Assert.AreEqual(2f, takes[0], 0.1f);
            Assert.AreEqual(5.2f, takes[1], 0.1f);
        }

        [Test]
        public void AStunThatDoesNotExtend_TakesNothing_AndStartsNoLock()
        {
            // Bully A lands a 5 s stun at 1.0 (a take). Bully B's 1 s stun at 1.5 does not extend it: no take, and no lock, so B's shove at 2.0 takes.
            // The two stuns reach the victim (seat 1, 43 m from each corner) but not each other (61 m apart).
            var a = Bully(Hits(2f, 0f), 100, MatchTestKit.Stun(50f, 5f, 100f));
            var b = Bully(Hits(2f, 2f), 100, MatchTestKit.Stun(50f, 1f, 100f), MatchTestKit.Shove(50f, 5f, 1f));
            var kits = new MatchKit[] { a, Carrier("victim"), b, null };
            var setup = Arena(kits, 4f);
            setup.Policies[0] = Casts((1f, 0));
            setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
            setup.Policies[2] = Casts((1.5f, 0), (2f, 1));
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(1, r.Counters[0].HitTakes);
            Assert.AreEqual(0, r.Counters[2].HitRefusedLock);
            Assert.AreEqual(1, r.Counters[2].HitTakes, "only B's shove took: its stun did not extend, so it started no lock");
            Assert.AreEqual(2f, r.Counters[2].HitFood, 1e-4f);
        }

        [Test]
        public void OneCast_ThreeVictims_ThreeTakes_ClampedToTheRoomLeft()
        {
            var rivals = new[] { Carrier("a"), Carrier("b"), Carrier("c") };
            var r = MatchSim.Run(Brawl(Bully(Hits(2f, 0f), 5, MatchTestKit.Stun(Big, 1f, 5f)), rivals, Casts((2f, 0)), 3f), 1);
            Assert.AreEqual(3, r.Counters[0].HitTakes, "each victim through its own lock");
            Assert.AreEqual(5f, r.Counters[0].HitFood, 1e-4f, "2, 2 and then the 1 of room left");
            var amounts = r.Events.Where(e => e.Kind == MatchEventKind.HitSteal).OrderBy(e => e.Other).Select(e => e.Amount).ToArray();
            CollectionAssert.AreEqual(new[] { 2f, 2f, 1f }, amounts, "in seat order");
            Assert.AreEqual(1f, r.Counters[3].LostToSteals, 1e-4f, "the third loses only what the Bully gains");
        }

        [Test]
        public void Egg_RefusesTheHit_AndTheTake()
        {
            var victim = Carrier("victim", 6f, MatchTestKit.Shield(0.1f, 1f, 6f, 100f));
            var bully = Bully(Hits(2f, 2f), 100, MatchTestKit.Stun(Big, 1f, 5f));
            var setup = Brawl(bully, new[] { victim }, Casts((2f, 0)), 3f);
            bool cast = false;
            setup.Policies[1] = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(1));
                if (!cast && v.Time >= 1f) { cast = true; intent.CastSlot = 0; }
                return intent;
            });
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(0, r.Counters[0].HitTakes);
            Assert.AreEqual(0f, r.Counters[1].LostToSteals, "the egg refused the stun, so it refused the take");
            Assert.AreEqual(0, r.Counters[0].HitRefusedLock + r.Counters[0].HitRefusedNoFood + r.Counters[0].HitRefusedRoom, "refused before the take was asked");
        }

        [Test]
        public void EmptyVictimOrFullBully_NoTake_NoLock()
        {
            // Empty victim: the stun at 2.0 finds nothing; it starts no lock, so the shove at 3.0 takes once the victim has pecked.
            var bully = Bully(Hits(2f, 2f), 100, MatchTestKit.Stun(Big, 0.2f, 0.5f), MatchTestKit.Shove(Big, 5f, 0.5f));
            var setup = Brawl(bully, new[] { Carrier("victim") }, Casts((2f, 0), (3f, 1)), 4f);
            setup.Policies[1] = MatchTestKit.Policy((v, s) => v.Time < 2.5f ? MatchTestKit.StandAt(v, s) : MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(1)));
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(1, r.Counters[0].HitRefusedNoFood);
            Assert.AreEqual(0, r.Counters[0].HitRefusedLock, "the empty hit started no lock");
            Assert.AreEqual(1, r.Counters[0].HitTakes);

            // Full Bully: a beak with no room takes nothing and starts no lock.
            var full = Bully(Hits(2f, 2f), 0, MatchTestKit.Stun(Big, 0.2f, 0.5f), MatchTestKit.Shove(Big, 5f, 0.5f));
            r = MatchSim.Run(Brawl(full, new[] { Carrier("victim") }, Casts((2f, 0), (3f, 1)), 4f), 1);
            Assert.AreEqual(2, r.Counters[0].HitRefusedRoom);
            Assert.AreEqual(0, r.Counters[0].HitRefusedLock);
            Assert.AreEqual(0, r.Counters[0].HitTakes);
            Assert.AreEqual(0f, r.Counters[1].LostToSteals, "the victim lost nothing");
        }

        [Test]
        public void Peck_NeverTakes()
        {
            var bully = Bully(Hits(2f, 2f, slow: 2f), 100, MatchTestKit.Jump(2f, 0.3f, 1f));
            var setup = Brawl(bully, new[] { Carrier("victim") }, MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
                if (v.Time >= 2f && v.Seats[s].CooldownLeft[1] <= 1e-4f) intent.CastSlot = 0;
                return intent;
            }), 5f);
            var r = MatchSim.Run(setup, 1);
            Assert.Greater(r.Counters[0].Foraged, 0f, "the Bully pecked");
            Assert.Greater(r.Counters[0].Casts, 0, "and jumped");
            Assert.AreEqual(0, r.Counters[0].HitTakes, "neither Peck nor Jump is a hit that takes");
            Assert.AreEqual(0, Takes(r));
        }

        // ---------------------------------------------------------------- ships off

        private static MatchResult Fight(HitStealFacts facts, ulong seed)
        {
            var bully = Bully(facts, 30, MatchTestKit.Stun(8f, 1f, 3f), MatchTestKit.Shove(8f, 6f, 2f));
            var kits = new[] { bully, Carrier("b", 3f), Carrier("c", 3f), Carrier("d", 3f) };
            foreach (var k in kits.Skip(1)) { k.Farm.PeckCooldown = 1f; k.Farm.CargoCapacity = 30; }
            var setup = Arena(kits, 25f);
            setup.Policies[0] = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
                var cd = v.Seats[s].CooldownLeft;
                if (cd[1] <= 1e-4f) intent.CastSlot = 0; else if (cd[2] <= 1e-4f) intent.CastSlot = 1;
                return intent;
            });
            for (int i = 1; i < 4; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0));
            return MatchSim.Run(setup, seed);
        }

        [Test]
        public void ShippedOff_PlaysExactlyAsBefore()
        {
            // The shipped Bully's facts (every amount 0, the lock authored) against no facts at all.
            var shipped = new HitStealFacts { LockSeconds = 3f };
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                var a = Fight(shipped, seed);
                var b = Fight(HitStealFacts.None, seed);
                Assert.AreEqual(b.Winner, a.Winner);
                Assert.AreEqual(b.EndSeconds, a.EndSeconds);
                CollectionAssert.AreEqual(b.Banked, a.Banked);
                Assert.AreEqual(b.Events.Count, a.Events.Count);
                for (int s = 0; s < 4; s++)
                {
                    Assert.AreEqual(b.Counters[s].Foraged, a.Counters[s].Foraged, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].Stolen, a.Counters[s].Stolen, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].LostToSteals, a.Counters[s].LostToSteals, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].Casts, a.Counters[s].Casts, $"seat {s}");
                    Assert.AreEqual(0, a.Counters[s].HitTakes + a.Counters[s].HitRefusedLock + a.Counters[s].HitRefusedNoFood + a.Counters[s].HitRefusedRoom);
                }
            }
            Assert.Greater(Fight(Hits(2f, 2f), 1).Counters[0].HitTakes, 0, "the same fight with the amounts on does take: the comparison is not vacuous");
        }

        [Test]
        public void Sim_CallsTheSharedRules_NotCopies()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/MatchSim.cs"));
            StringAssert.Contains("class Core", source, "the scan found the simulator");
            foreach (var call in new[] { "HitStealRules.Take(", "HitStealRules.Locked(", "ControlReapplyRules.IncomingExtends(" })
                StringAssert.Contains(call, source, "the simulator must call the game's rule, not restate it.");
        }
    }
}
