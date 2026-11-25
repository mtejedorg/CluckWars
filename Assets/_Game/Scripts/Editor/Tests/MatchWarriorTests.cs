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
    /// The two Warrior kits under test in the match simulator (design 2026-11-25): the Bully's hit-steal and the Enforcer's fresh hits and
    /// debts. Every number is the test's own on synthetic maps and kits; nothing asserts a win rate.
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
            setup.Food.Personal = 100f;   // a bite is never held back by a small pile
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

        // ================================================================ the Enforcer (Maestro, 2026-11-25: a hit on a fresh rival takes no food, it stacks a debt)

        // A Enforcer on a fast, big-reach kit. `share` is the passive's FreshBiteShare: 0 as shipped (no food from a fresh hit), 1 the old bite.
        private static MatchKit Champ(int capacity = 100, float fresh = 1f, float share = 0f, float debtSteal = 2f, int cap = 3, float seconds = 20f, params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter("champ", 100f, abilities, peckAmount: 3f, peckCooldown: 500f, castSeconds: 0.1f, capacity: capacity);
            kit.Passive.Bite = new BiteFacts { FreshSeconds = fresh, FoodShare = share };
            kit.Passive.Debt = new DebtFacts { IsEnforcer = true, Steal = debtSteal, Cap = cap, Seconds = seconds };
            return kit;
        }

        // A forager whose Peck casts for `castSeconds` and then is far from ready again.
        private static MatchKit Pecker(string label, float bite, float castSeconds, float cooldown = 500f) =>
            MatchTestKit.Fighter(label, 100f, new MatchAbility[0], peckAmount: bite, peckCooldown: cooldown, castSeconds: castSeconds);

        // Stand and cast the steps in order, one at a time, each on the first think where its condition holds.
        private static PolicyFactory Steps(params (Func<MatchView, bool> when, int slot)[] steps)
        {
            int next = 0;
            return MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.StandAt(v, s);
                if (next < steps.Length && steps[next].when(v)) intent.CastSlot = steps[next++].slot;
                return intent;
            });
        }

        private static Func<MatchView, bool> VictimCasting(int seat = 1) => v => v.Seats[seat].Pecking;
        private static Func<MatchView, bool> VictimLanded(float after, int seat = 1) => v => v.Seats[seat].LandedWithFood && v.Seats[seat].SinceLanded >= after;
        private static Func<MatchView, bool> At(float time) => v => v.Time + 1e-4f >= time;

        private static MatchSetup Duo(MatchKit hunter, MatchKit victim, PolicyFactory hunterPolicy, float clock, float pile = 100f)
        {
            var setup = Arena(new MatchKit[] { hunter, victim, null, null }, clock);
            setup.Food.Personal = pile;
            setup.Policies[0] = hunterPolicy;
            setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
            return setup;
        }

        [Test]
        public void FreshHit_MidCast_TakesNoFood_StacksOneExtraDebt()
        {
            var r = MatchSim.Run(Duo(Champ(abilities: MatchTestKit.Stun(Big, 1f, 5f)), Pecker("victim", 6f, 1f), Steps((VictimCasting(), 0)), 3f, pile: 10f), 1);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.FreshHits);
            Assert.AreEqual(0f, c.BiteFoodFromCast + c.BiteFoodFromCargo, "no food from a fresh hit as shipped");
            Assert.AreEqual(0f, c.Stolen, 1e-4f);
            Assert.AreEqual(0f, r.Counters[1].Foraged, "the Stun cut the cast, as any Stun does");
            Assert.AreEqual(1, r.Counters[1].CutsByCause[(int)CutCause.Stun]);
            Assert.AreEqual(2, c.DebtsEarned, "+1 for the Stun, +1 for the fresh rival");
            Assert.AreEqual(0, r.Events.Count(e => e.Kind == MatchEventKind.Bite), "no bite event without food");
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void FreshHit_JustLanded_TakesNoFood_OnlyStacks()
        {
            var r = MatchSim.Run(Duo(Champ(abilities: MatchTestKit.Stun(Big, 1f, 5f)), Pecker("victim", 3f, 0.1f), Steps((VictimLanded(0.2f), 0)), 3f), 1);
            Assert.AreEqual(1, r.Counters[0].FreshHits);
            Assert.AreEqual(0f, r.Counters[0].Stolen, 1e-4f);
            Assert.AreEqual(0f, r.Counters[1].LostToSteals, "the victim's cargo is untouched");
            Assert.AreEqual(2, r.Counters[0].DebtsEarned);
        }

        [Test]
        public void BiteOn_MidCast_CutsTheCast_TakesFromThePile_RestStaysInThePile()
        {
            // The victim's bite is 6 from a pile of 10; the Enforcer's beak holds 4: it takes 4, the other 2 of the bite stay in the pile.
            var r = MatchSim.Run(Duo(Champ(4, share: 1f, abilities: MatchTestKit.Shove(Big, 0.1f, 5f)), Pecker("victim", 6f, 1f), Steps((VictimCasting(), 0)), 3f, pile: 10f), 1);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.BitesFromCast);
            Assert.AreEqual(4f, c.BiteFoodFromCast, 1e-4f, "held to the free room");
            Assert.AreEqual(2f, c.BiteLeftInPile, 1e-4f);
            Assert.AreEqual(0f, r.Counters[1].Foraged, "nothing landed for the victim");
            Assert.AreEqual(1, r.Counters[1].PecksBitten + r.Counters[1].CutsByCause[(int)CutCause.Shove]);
            Assert.AreEqual(1, r.Events.Count(e => e.Kind == MatchEventKind.Bite));
        }

        [Test]
        public void BiteOn_StunCutsFirst_TheSnapshotStillBites()
        {
            var r = MatchSim.Run(Duo(Champ(share: 1f, abilities: MatchTestKit.Stun(Big, 1f, 5f)), Pecker("victim", 6f, 1f), Steps((VictimCasting(), 0)), 3f, pile: 2f), 1);
            Assert.AreEqual(1, r.Counters[1].CutsByCause[(int)CutCause.Stun], "the Stun's own cut ran first");
            Assert.AreEqual(0, r.Counters[1].CutsByCause[(int)CutCause.Bite]);
            Assert.AreEqual(1, r.Counters[0].BitesFromCast, "and the bite still came from the pile");
            Assert.AreEqual(2f, r.Counters[0].BiteFoodFromCast, 1e-4f);
        }

        [Test]
        public void BiteOn_JustLanded_TakesFromTheCargo_ClampedToRoom()
        {
            var r = MatchSim.Run(Duo(Champ(2, share: 1f, abilities: MatchTestKit.Stun(Big, 1f, 5f)), Pecker("victim", 3f, 0.1f), Steps((VictimLanded(0.2f), 0)), 3f), 1);
            Assert.AreEqual(1, r.Counters[0].BitesFromCargo);
            Assert.AreEqual(2f, r.Counters[0].BiteFoodFromCargo, 1e-4f);
            Assert.AreEqual(1f, r.Counters[0].BiteLeftWithVictim, 1e-4f);
            Assert.AreEqual(2f, r.Counters[1].LostToSteals, 1e-4f);
            Assert.AreEqual(2f, r.Counters[1].BiteLostFood, 1e-4f);
        }

        [Test]
        public void Fresh_AfterTheWindow_IsNoFreshHit()
        {
            var r = MatchSim.Run(Duo(Champ(abilities: MatchTestKit.Stun(Big, 1f, 5f)), Pecker("victim", 3f, 0.1f), Steps((VictimLanded(1.1f), 0)), 4f), 1);
            Assert.AreEqual(0, r.Counters[0].FreshHits);
            Assert.AreEqual(1, r.Counters[0].DebtsEarned, "only the Stun's own debt");
        }

        [Test]
        public void FreshHit_OncePerFreshState_TheSecondHitOnlyEarnsItsDebt()
        {
            var hunter = Champ(abilities: new[] { MatchTestKit.Stun(Big, 1f, 5f), MatchTestKit.Shove(Big, 1f, 5f) });
            var r = MatchSim.Run(Duo(hunter, Pecker("victim", 3f, 0.1f), Steps((VictimLanded(0.1f), 0), (VictimLanded(0.3f), 1)), 3f), 1);
            Assert.AreEqual(1, r.Counters[0].FreshHits);
            Assert.AreEqual(1, r.Counters[0].BitesSpent, "the Shove met a state already spent");
            Assert.AreEqual(3, r.Counters[0].DebtsEarned, "fresh and Stun, then the Shove's own");
        }

        [Test]
        public void FreshHit_EggRefuses_ShieldWalkLands()
        {
            MatchResult Run(float after)
            {
                var victim = Carrier("victim", 3f, MatchTestKit.Shield(0.3f, 1f, 6f, 100f));
                var setup = Duo(Champ(abilities: MatchTestKit.Stun(Big, 1f, 5f)), victim, Steps((VictimLanded(after), 0)), 3f);
                bool shielded = false;
                setup.Policies[1] = MatchTestKit.Policy((v, s) =>
                {
                    var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(1));
                    if (!shielded && v.Seats[s].LandedWithFood) { shielded = true; intent.CastSlot = 0; }
                    return intent;
                });
                return MatchSim.Run(setup, 1);
            }
            var walk = Run(0.15f);
            Assert.AreEqual(1, walk.Counters[0].FreshHits, "the walk refuses only Slow: the hit lands");
            Assert.AreEqual(2, walk.Counters[0].DebtsEarned);
            var egg = Run(0.6f);
            Assert.AreEqual(0, egg.Counters[0].FreshHits, "the egg refuses the hit");
            Assert.AreEqual(0, egg.Counters[0].DebtsEarned);
        }

        // A victim that stands until `from`, then pecks 3 a bite every 0.1 s until it holds `target`.
        private static PolicyFactory FillTo(float from, float target) =>
            MatchTestKit.Policy((v, s) => v.Time >= from && v.Seats[s].Cargo < target ? MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(s)) : MatchTestKit.StandAt(v, s));

        [Test]
        public void Steal_OnAFreshCarrier_StacksThenTakesTheInflatedAmount()
        {
            // Two Stuns while the victim is not fresh: count 2. Then it fills its beak to 15 and the Steal meets it just after its last landing.
            var hunter = Champ(20, abilities: new[] { MatchTestKit.Stun(Big, 0.2f, 0.5f), MatchTestKit.Steal(Big, 5f, 4f) });
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: 3f, peckCooldown: 0.1f, castSeconds: 0.1f, capacity: 30);
            var setup = Duo(hunter, victim, Steps((At(1f), 0), (At(2f), 0), (v => v.Seats[1].LandedWithFood && v.Seats[1].Cargo >= 15f && v.Seats[1].SinceLanded >= 0.2f, 1)), 6f);
            setup.Policies[1] = FillTo(3f, 15f);
            var r = MatchSim.Run(setup, 1);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.FreshHits, "the Steal's hit on the just-landed victim: one more stack, no food");
            Assert.AreEqual(1, c.Steals);
            Assert.AreEqual(11f, c.StealFood, 1e-4f, "5 + 2 x 3, the count already at the cap");
            Assert.AreEqual(1, c.DebtCollects);
            Assert.AreEqual(3, c.DebtsCollected);
            Assert.AreEqual(6f, c.DebtBonusFood, 1e-4f);
            Assert.AreEqual(11f, r.Counters[1].LostToSteals, 1e-4f, "all the food came through the Steal");
        }

        [Test]
        public void Steal_ThatTakesNothing_LeavesTheCount()
        {
            // A full Enforcer: the Steal takes 0, so it cashes nothing and the count stays until it decays on its own.
            var hunter = Champ(0, seconds: 1.5f, abilities: new[] { MatchTestKit.Stun(Big, 0.2f, 0.5f), MatchTestKit.Steal(Big, 5f, 4f) });
            var victim = MatchTestKit.Fighter("victim", 100f, new MatchAbility[0], peckAmount: 3f, peckCooldown: 0.1f, castSeconds: 0.1f, capacity: 30);
            var setup = Duo(hunter, victim, Steps((At(1f), 0), (v => v.Seats[1].LandedWithFood && v.Seats[1].Cargo >= 6f && v.Seats[1].SinceLanded >= 0.2f, 1)), 8f);
            setup.Policies[1] = FillTo(3f, 6f);
            var r = MatchSim.Run(setup, 1);
            var c = r.Counters[0];
            Assert.AreEqual(0, c.Steals);
            Assert.AreEqual(0, c.DebtCollects);
            Assert.AreEqual(2, c.DebtsEarned, "the Stun's, then the fresh stack");
            Assert.AreEqual(2, c.DebtsDecayed, "the count was still there to decay");
        }

        [Test]
        public void Debts_PerEnemy_CappedAndDecayAllAtOnce()
        {
            MatchResult Run(float clock)
            {
                var hunter = Champ(100, 1f, 0f, 2f, 3, 2f, MatchTestKit.Stun(Big, 0.2f, 0.5f));
                var kits = new MatchKit[] { hunter, Carrier("a"), Carrier("b"), null };
                var setup = Arena(kits, clock);
                setup.Policies[0] = Steps((At(1f), 0), (At(1.5f), 0), (At(2f), 0), (At(2.5f), 0));
                setup.Policies[1] = setup.Policies[2] = MatchTestKit.Stand;
                return MatchSim.Run(setup, 1);
            }
            var held = Run(4f);
            Assert.AreEqual(6, held.Counters[0].DebtsEarned, "three on each rival; the fourth gain at the cap adds nothing");
            Assert.AreEqual(0, held.Counters[0].DebtsDecayed, "the gain at the cap restarted the timer: nothing has decayed at 4 s");
            var gone = Run(5.5f);
            Assert.AreEqual(6, gone.Counters[0].DebtsDecayed, "DebtSeconds after the last gain, every count went in one step");
        }

        [Test]
        public void OneStun_ThreeFreshVictims_InSeatOrder_ClampedToTheRoomLeft()
        {
            var hunter = Champ(5, 100f, 1f, abilities: MatchTestKit.Stun(Big, 0.5f, 5f));
            var kits = new MatchKit[] { hunter, Carrier("a", 3f), Carrier("b", 3f), Carrier("c", 3f) };
            var setup = Arena(kits, 3f);
            setup.Policies[0] = Steps((v => v.Seats[1].LandedWithFood && v.Seats[2].LandedWithFood && v.Seats[3].LandedWithFood, 0));
            for (int i = 1; i < 4; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(i));
            var r = MatchSim.Run(setup, 1);
            var amounts = r.Events.Where(e => e.Kind == MatchEventKind.Bite).OrderBy(e => e.Other).Select(e => e.Amount).ToArray();
            CollectionAssert.AreEqual(new[] { 3f, 2f, 0f }, amounts, "in seat order, each held to the room left");
            Assert.AreEqual(3, r.Counters[0].FreshHits);
            Assert.AreEqual(6, r.Counters[0].DebtsEarned, "a count of 2 on each: the fresh stack and the Stun");
        }

        [Test]
        public void TwoEnforcers_OneFreshVictim_TheFirstSpendsTheState_BothEarnTheirHitDebt()
        {
            var a = Champ(abilities: MatchTestKit.Stun(Big, 1f, 5f));
            var b = Champ(abilities: MatchTestKit.Shove(Big, 1f, 5f));
            var setup = Arena(new MatchKit[] { a, Carrier("victim"), b, null }, 3f);
            setup.Policies[0] = Steps((VictimLanded(0.05f), 0));
            setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
            setup.Policies[2] = Steps((VictimLanded(0.05f), 0));
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(1, r.Counters[0].FreshHits + r.Counters[2].FreshHits, "one stack from the fresh state");
            Assert.AreEqual(1, r.Counters[0].BitesSpent + r.Counters[2].BitesSpent, "the other met a state already spent");
            // Each cast also reaches the other Enforcer: 2 on the victim (fresh and hit) plus 1 for the second's hit, and one hit each on the other Enforcer.
            Assert.AreEqual(5, r.Counters[0].DebtsEarned + r.Counters[2].DebtsEarned);
        }

        [Test]
        public void Executed_ClearsEveryCountOnAndOfTheDead()
        {
            MatchResult Run(bool enforcerDies)
            {
                var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { MatchTestKit.DeathMark(1f, 5f, 5f, 3f, 3f, 2f) });
                var enforcer = Champ(abilities: MatchTestKit.Stun(Big, 0.2f, 5f));
                var rival = Carrier("rival");
                var setup = Arena(new MatchKit[] { spoiler, enforcer, rival, null }, 9f);
                setup.Policies[0] = MatchTestKit.Stand;
                // The dead one is the loaded one: either the Enforcer pecks and the rival stands, or the rival pecks late and the Enforcer stands.
                bool stunned = false;
                setup.Policies[1] = MatchTestKit.Policy((v, s) =>
                {
                    var intent = enforcerDies ? MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(s)) : MatchTestKit.StandAt(v, s);
                    if (v.Time >= 1f && !stunned) { stunned = true; intent.CastSlot = 0; }
                    return intent;
                });
                setup.Policies[2] = enforcerDies
                    ? MatchTestKit.Stand
                    : MatchTestKit.Policy((v, s) => v.Time < 2f ? MatchTestKit.StandAt(v, s) : MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(s)));
                setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Loaded, Times = new[] { 5f } };
                return MatchSim.Run(setup, 1);
            }
            var dies = Run(true);
            Assert.AreEqual(1, dies.Kills[0]);
            Assert.AreEqual(2, dies.Counters[1].DebtsCleared, "the Enforcer's own counts on the two rivals went with it");
            var victimDies = Run(false);
            Assert.AreEqual(1, victimDies.Kills[0]);
            Assert.AreEqual(1, victimDies.Counters[1].DebtsCleared, "only the count on the dead rival went; the one on the spoiler decays on its own");
        }

        [Test]
        public void EveryCapacityRead_GoesThroughCapacityOf()
        {
            string sim = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/MatchSim.cs"));
            StringAssert.Contains("class Core", sim, "the scan found the simulator");
            int reads = sim.Split('\n').Count(l => !l.TrimStart().StartsWith("//") && l.Contains("Farm.CargoCapacity"));
            Assert.AreEqual(1, reads, "MatchSim reads the class capacity once, inside CapacityOf");
            StringAssert.Contains("float CapacityOf(int s) =>", sim);
            foreach (string policy in new[] { "ScriptedPolicy.cs", "UtilityPolicy.cs" })
            {
                string text = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance", policy));
                StringAssert.Contains("class ", text, "the scan found " + policy);
                StringAssert.DoesNotContain("CargoCapacity", text, policy + " reads SeatView.Capacity, the one capacity the simulator computes");
            }
        }

        [Test]
        public void Sim_CallsTheSharedRules_NotCopies_Enforcer()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/MatchSim.cs"));
            StringAssert.Contains("class Core", source);
            foreach (var call in new[] { "BiteRules.Fresh(", "BiteRules.FromCast(", "BiteRules.FromCargo(", "DebtRules.Add(", "DebtRules.StealAmount(", "DebtRules.Decayed(" })
                StringAssert.Contains(call, source, "the simulator must call the shared rule, not restate it.");
            foreach (var gone in new[] { "PeckBreakRules", "ResolveBreak", "SpillOverflow" })
                StringAssert.DoesNotContain(gone, source, "the Peck break and the debt room are retired");
        }

        private static MatchResult EnforcerFight(bool shipped, ulong seed)
        {
            var hunter = Champ(30, 0f, 0f, 0f, 3, 6f, MatchTestKit.Steal(8f, 5f, 3f), MatchTestKit.Stun(8f, 1f, 3f));
            hunter.Farm.PeckCooldown = 1f;
            if (!shipped) { hunter.Passive.Bite = BiteFacts.None; hunter.Passive.Debt = DebtFacts.None; }
            var kits = new[] { hunter, Carrier("b", 3f), Carrier("c", 3f), Carrier("d", 3f) };
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
        public void ShippedOff_PlaysExactlyAsBefore_Enforcer()
        {
            // The Enforcer as shipped (no fresh window, debts adding nothing) against no facts at all.
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                var a = EnforcerFight(true, seed);
                var b = EnforcerFight(false, seed);
                Assert.AreEqual(b.Winner, a.Winner);
                Assert.AreEqual(b.EndSeconds, a.EndSeconds);
                CollectionAssert.AreEqual(b.Banked, a.Banked);
                Assert.AreEqual(b.Events.Count, a.Events.Count(e => e.Kind != MatchEventKind.DebtCollect), "the only extra events are the cashes of counts");
                for (int s = 0; s < 4; s++)
                {
                    Assert.AreEqual(b.Counters[s].Foraged, a.Counters[s].Foraged, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].Stolen, a.Counters[s].Stolen, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].LostToSteals, a.Counters[s].LostToSteals, $"seat {s}");
                    Assert.AreEqual(0, a.Counters[s].FreshHits);
                }
            }
            Assert.Greater(EnforcerFight(true, 1).Counters[0].Steals, 0, "the Enforcer did steal: the comparison is not vacuous");
        }

        [Test]
        public void SlidingWindowSum_IsTheMostInAnyWindow()
        {
            var w = new SlidingWindowSum(5f);
            w.Add(0f, 3f);
            w.Add(4f, 2f);
            Assert.AreEqual(5f, w.Max, 1e-6f, "both inside one 5 s window");
            w.Add(5f, 1f);
            Assert.AreEqual(5f, w.Max, 1e-6f, "the amount at 0 left the window at 5: 2 + 1 is less");
            w.Add(9f, 4f);
            Assert.AreEqual(5f, w.Max, 1e-6f, "at 9 the window holds 1 + 4");
            w.Add(9.5f, 3f);
            Assert.AreEqual(8f, w.Max, 1e-6f, "1 + 4 + 3 inside one window");
        }

    }
}
