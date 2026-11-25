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

        // ================================================================ the Trophy Hunter

        private static MatchKit Hunter(float reach, int room = 0, int cap = 3, bool clearOnBank = false, int capacity = 100, float breakCooldown = 2.5f,
            float immune = 2.5f, float peckCooldown = 500f, params MatchAbility[] abilities)
        {
            var kit = MatchTestKit.Fighter("hunter", 100f, abilities, peckAmount: 3f, peckCooldown: peckCooldown, castSeconds: 0.1f, capacity: capacity);
            kit.Passive.Break = new PeckBreakFacts { Reach = reach, Cooldown = breakCooldown, ImmuneSeconds = immune };
            kit.Passive.Trophy = new TrophyFacts { IsTrophyHunter = true, Room = room, Cap = cap, ClearOnBank = clearOnBank };
            return kit;
        }

        // A forager whose Peck casts for `castSeconds` and then is far from ready again.
        private static MatchKit Pecker(string label, float bite, float castSeconds, float cooldown = 500f) =>
            MatchTestKit.Fighter(label, 100f, new MatchAbility[0], peckAmount: bite, peckCooldown: cooldown, castSeconds: castSeconds);

        // The hunter stands where it is and presses a break on `target` whenever that rival is casting a Peck and the break is ready.
        private static PolicyFactory BreakWhenCasting(int target) =>
            MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.StandAt(v, s);
                if (v.Seats[target].Pecking && v.Seats[s].BreakLeft <= 1e-4f) { intent.Break = true; intent.BreakTarget = target; }
                return intent;
            });

        private static MatchSetup Pair(MatchKit hunter, MatchKit victim, PolicyFactory hunterPolicy, float clock, Action<MatchSetup> tweak = null)
        {
            var setup = MatchTestKit.Duel(MatchTestKit.OpenMap(Half), hunter, victim, hunterPolicy, MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1)), Half, clock);
            tweak?.Invoke(setup);
            return setup;
        }

        [Test]
        public void Break_CutsTheCast_TakesTheBite_RestStaysInThePile()
        {
            // The victim's bite is 6 from a pile of 10; the hunter's beak holds 4: it takes 4, the other 2 of the bite stay in the pile.
            var r = MatchSim.Run(Pair(Hunter(Big, capacity: 4), Pecker("victim", 6f, 1f), BreakWhenCasting(1), 3f), 1);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.Breaks);
            Assert.AreEqual(4f, c.BreakFood, 1e-4f, "held to the free room");
            Assert.AreEqual(2f, c.BreakLeftInPile, 1e-4f, "what does not fit stays in the pile, never the centre");
            Assert.AreEqual(0f, r.Counters[1].Foraged, "nothing landed for the victim");
            Assert.AreEqual(1, r.Counters[1].PecksBroken);
            Assert.AreEqual(1, r.Counters[1].CutsByCause[(int)CutCause.Break]);
            Assert.AreEqual(4f, r.Banked[0], 1e-3f, "the bite sat in the breaker's ordinary cargo and banked");
            Assert.AreEqual(1, r.Events.Count(e => e.Kind == MatchEventKind.PeckBreak));
            Assert.AreEqual(0f, c.OverflowToCentre);
            Assert.AreEqual(0, r.RejectedIntents);
        }

        [Test]
        public void Break_EarnsATrophy_EvenWhenNoFoodFits()
        {
            var r = MatchSim.Run(Pair(Hunter(Big, capacity: 0), Pecker("victim", 6f, 1f), BreakWhenCasting(1), 3f), 1);
            var c = r.Counters[0];
            Assert.AreEqual(1, c.Breaks);
            Assert.AreEqual(0f, c.BreakFood);
            Assert.AreEqual(6f, c.BreakLeftInPile, 1e-4f, "the whole bite stayed in the pile");
            Assert.AreEqual(1, c.TrophiesEarned, "the cut is the feat");
            Assert.AreEqual(0f, r.Counters[1].Foraged);
        }

        [Test]
        public void Break_AfterTheCastEnded_FallsBackToAPilePeck_ElseCancelsAndSpendsNothing()
        {
            // The victim's cast lasts 0.1 s, so it is over at 3.0 when the press resolves.
            PolicyFactory PressAt(Vector2? at, int pile)
            {
                bool pressed = false;
                return MatchTestKit.Policy((v, s) =>
                {
                    var intent = MatchIntent.Idle(at ?? v.Seats[s].Pos);
                    if (!pressed && v.Time >= 3f) { pressed = true; intent.Break = true; intent.BreakTarget = 1; intent.PeckPile = pile; }
                    return intent;
                });
            }

            // At its own pile with a Peck ready: the press is an ordinary Peck.
            var map = MatchTestKit.OpenMap(Half);
            Vector2 pile = map.Places[OracleMapSO.PersonalIndex(0)].Pos;
            var r = MatchSim.Run(Pair(Hunter(Big, peckCooldown: 1f), Pecker("victim", 3f, 0.1f), PressAt(pile, OracleMapSO.PersonalIndex(0)), 5f), 1);
            Assert.AreEqual(0, r.Counters[0].Breaks);
            Assert.AreEqual(1, r.Counters[0].BreakFallbacks);
            Assert.AreEqual(0, r.Counters[0].BreakCancels);
            Assert.Greater(r.Counters[0].Foraged, 0f, "the fallback pecked the pile");

            // Away from every pile: nothing to break, nothing to peck, a visible cancel that spends nothing.
            r = MatchSim.Run(Pair(Hunter(Big), Pecker("victim", 3f, 0.1f), PressAt(null, -1), 5f), 1);
            Assert.AreEqual(1, r.Counters[0].BreakCancels);
            Assert.AreEqual(0, r.Counters[0].BreakFallbacks + r.Counters[0].Breaks);
            Assert.AreEqual(0, r.Counters[0].Casts, "no ability slot was spent");
            Assert.AreEqual(0, r.RejectedIntents);

            // A rival outside the break's reach is no target: the press falls through the same way.
            r = MatchSim.Run(Pair(Hunter(2f), Pecker("victim", 6f, 1f), BreakWhenCasting(1), 3f), 1);
            Assert.AreEqual(0, r.Counters[0].Breaks);
            Assert.Greater(r.Counters[0].BreakCancels, 0);
            Assert.AreEqual(6f, r.Counters[1].Foraged, 1e-4f, "his Peck landed");
        }

        [Test]
        public void Break_SetsBreakCooldown_AndTheVictimsImmunity()
        {
            // The victim pecks every 0.6 s for 0.4 s. Immune for 5 s: only the first break lands, the later presses are refused.
            MatchKit Victim() => Pecker("victim", 1f, 0.4f, 0.6f);
            var r = MatchSim.Run(Pair(Hunter(Big, breakCooldown: 1f, immune: 5f), Victim(), BreakWhenCasting(1), 4f), 1);
            Assert.AreEqual(1, r.Counters[0].Breaks);
            Assert.Greater(r.Counters[0].BreakRefused, 0, "the later presses met the immunity");

            // No immunity: only the break's own cooldown spaces the breaks.
            r = MatchSim.Run(Pair(Hunter(Big, breakCooldown: 1f, immune: 0f), Victim(), BreakWhenCasting(1), 4f), 1);
            var times = r.Events.Where(e => e.Kind == MatchEventKind.PeckBreak).Select(e => e.Time).ToList();
            Assert.GreaterOrEqual(times.Count, 2);
            for (int i = 1; i < times.Count; i++) Assert.GreaterOrEqual(times[i] - times[i - 1], 1f - 1e-3f, "BreakCooldown spaces the breaks");
        }

        [Test]
        public void Break_NeverSpendsTheBreakersOwnPeckCycle()
        {
            // BreakCooldown gates breaks only (Maestro, 2026-11-25): the hunter's own Pecks are the same with and without the break.
            PolicyFactory Own(bool press) => MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
                if (press && v.Seats[1].Pecking && v.Seats[s].BreakLeft <= 1e-4f && !v.Seats[s].Pecking) { intent.Break = true; intent.BreakTarget = 1; }
                return intent;
            });
            MatchResult Play(bool press) => MatchSim.Run(Pair(Hunter(Big, peckCooldown: 0.5f), Pecker("victim", 3f, 0.3f, 0.7f), Own(press), 6f), 1);
            var with = Play(true);
            var without = Play(false);
            Assert.Greater(with.Counters[0].Breaks, 0, "the break landed");
            Assert.AreEqual(without.Counters[0].Foraged, with.Counters[0].Foraged, 1e-4f, "his own Pecks did not change");
        }

        [Test]
        public void Break_WalledOrImmune_IsRefused()
        {
            // The hunter stands inside its own base's wall, the victim outside: a base wall stands between them.
            var r = MatchSim.Run(Pair(Hunter(Big), Pecker("victim", 6f, 1f), BreakWhenCasting(1), 3f, s => s.Rules.KeepOutRadius = 3f), 1);
            Assert.AreEqual(0, r.Counters[0].Breaks);
            Assert.Greater(r.Counters[0].BreakRefused, 0);
            Assert.AreEqual(6f, r.Counters[1].Foraged, 1e-4f, "the victim's Peck landed");
        }

        [Test]
        public void BreakWithoutTheKit_IsARejectedIntent()
        {
            var plain = MatchTestKit.Fighter("plain", 100f, new MatchAbility[0]);
            var r = MatchSim.Run(Pair(plain, Pecker("victim", 6f, 1f), BreakWhenCasting(1), 3f), 1);
            Assert.Greater(r.RejectedIntents, 0, "a kit with no break asked for one: a wiring bug, not a game state");
            Assert.AreEqual(0, r.Counters[0].Breaks);
        }

        [Test]
        public void TwoBreakersOneVictim_TheSecondFallsBack()
        {
            PolicyFactory AtOwnPile = MatchTestKit.Policy((v, s) =>
            {
                int pile = OracleMapSO.PersonalIndex(s);
                Vector2 at = v.Map.Places[pile].Pos;
                var intent = MatchIntent.Idle(at);
                if (v.Seats[1].Pecking && Vector2.Distance(v.Seats[s].Pos, at) < 0.3f) { intent.Break = true; intent.BreakTarget = 1; intent.PeckPile = pile; }
                return intent;
            });
            var kits = new[] { Hunter(Big, peckCooldown: 5f), Pecker("victim", 6f, 1f), Hunter(Big, peckCooldown: 5f), null };
            var setup = Arena(kits, 2f);
            setup.Policies[0] = AtOwnPile;
            setup.Policies[2] = AtOwnPile;
            setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
            var r = MatchSim.Run(setup, 1);
            Assert.AreEqual(1, r.Counters[0].Breaks + r.Counters[2].Breaks, "the first in the rotating order broke the cast");
            Assert.AreEqual(1, r.Counters[0].BreakFallbacks + r.Counters[2].BreakFallbacks, "the second met an ended cast and pecked its pile");
            Assert.AreEqual(1, r.Counters[1].PecksBroken);
        }

        [Test]
        public void Trophies_FromBreaksAndSteals_OnePerCast_CappedAtTheCap()
        {
            // One Steal cast robs three birds: one trophy. Cap 3: the fourth cast earns nothing more.
            var hunter = Hunter(Big, room: 2, cap: 3, abilities: MatchTestKit.Steal(Big, 5f, 1f));
            var rivals = new[] { Pecker("a", 20f, 0.1f), Pecker("b", 20f, 0.1f), Pecker("c", 20f, 0.1f) };
            var kits = new MatchKit[] { hunter, rivals[0], rivals[1], rivals[2] };
            var setup = Arena(kits, 7f);
            setup.Policies[0] = Casts((2f, 0), (3.2f, 0), (4.4f, 0), (5.6f, 0));
            for (int i = 1; i < 4; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(i));
            var r = MatchSim.Run(setup, 1);
            var c = r.Counters[0];
            Assert.AreEqual(12, c.Steals, "three robbed birds on each of four casts");
            Assert.AreEqual(3, c.TrophiesEarned, "a trophy a cast, however many birds, and none past the cap");
            Assert.AreEqual(3, c.TrophyPeak);
            Assert.AreEqual(3, c.TrophiesAtEnd, "banking keeps trophies when the passive says so");
            Assert.AreEqual(0, r.Counters[1].TrophiesEarned + r.Counters[1].TrophiesLost, "a rival that is not a Trophy Hunter holds none");
        }

        private static PolicyFactory Wander(params (float time, int slot)[] plan)
        {
            int next = 0;
            return MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchIntent.Idle(Vector2.zero);
                if (next < plan.Length && v.Time + 1e-4f >= plan[next].time) intent.CastSlot = plan[next++].slot;
                return intent;
            });
        }

        // Seat 1 is a hunter (class capacity 10, room 5, cap 3) that wanders and steals from two rich carriers until it holds 25 food and 3 trophies.
        private static MatchSetup RichHunter(float clock, MatchKit seat0, PolicyFactory policy0)
        {
            var hunter = Hunter(Big, room: 5, cap: 3, capacity: 10, abilities: MatchTestKit.Steal(Big, 8f, 1f));
            var kits = new MatchKit[] { seat0, hunter, Pecker("a", 30f, 0.1f), Pecker("b", 30f, 0.1f) };
            var setup = Arena(kits, clock);
            setup.Policies[0] = policy0;
            setup.Policies[1] = Wander((2f, 0), (3.5f, 0), (5f, 0), (6.2f, 0));
            for (int i = 2; i < 4; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(i));
            return setup;
        }

        [Test]
        public void Robbed_LosesOne_AndTheExcessGoesToTheCentrePile()
        {
            // The hunter holds 25 in a beak of 10 + 3 x 5. A rival's steal of 2 leaves 23 and costs a trophy: the beak is 20 and 3 food go to the centre.
            var thief = MatchTestKit.Fighter("thief", 100f, new[] { MatchTestKit.Steal(Big, 2f, 1f) });
            float centreMax = 0f;
            var setup = RichHunter(8f, thief, Casts((7f, 0)));
            setup.Policies[2] = MatchTestKit.Policy((v, s) =>
            {
                centreMax = Mathf.Max(centreMax, v.PileFood[OracleMapSO.CentreIndex]);
                return MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(2));
            });
            var r = MatchSim.Run(setup, 1);
            var h = r.Counters[1];
            Assert.AreEqual(3, h.TrophyPeak);
            Assert.AreEqual(1, h.TrophiesLost, "the steal took food: one trophy");
            Assert.AreEqual(3f, h.OverflowToCentre, 1e-3f, "23 in a beak of 20");
            Assert.AreEqual(1, r.Events.Count(e => e.Kind == MatchEventKind.Overflow));
            Assert.AreEqual(2, h.TrophiesAtEnd);
            Assert.AreEqual(43f, centreMax, 1e-3f, "the centre pile took the excess (40 + 3)");
        }

        [Test]
        public void Executed_LosesAll_NothingOverflows()
        {
            var spoiler = MatchTestKit.Fighter("spoiler", 100f, new[] { MatchTestKit.DeathMark(1f, 5f, 5f, 3f, 3f, 2f) });
            var setup = RichHunter(9f, spoiler, MatchTestKit.Stand);
            setup.Seats[3] = null;
            setup.Policies[3] = null;
            setup.Forced = new ForcedKills { Spoiler = 0, Kills = 1, Victims = ForcedVictims.Loaded, Times = new[] { 7f } };
            var r = MatchSim.Run(setup, 1);
            var h = r.Counters[1];
            Assert.AreEqual(3, h.TrophyPeak);
            Assert.AreEqual(3, h.TrophiesLost, "an execution takes every trophy");
            Assert.AreEqual(0, h.TrophiesAtEnd);
            Assert.AreEqual(0f, h.OverflowToCentre, "the killer drained the beak first: nothing overflows");
            Assert.AreEqual(1, r.Kills[0]);
        }

        [Test]
        public void Banking_KeepsTrophies()
        {
            var r = MatchSim.Run(BankingHunter(clearOnBank: false), 1);
            var h = r.Counters[0];
            Assert.AreEqual(2, h.TrophiesAtEnd, "kept through banking");
            Assert.AreEqual(0, h.TrophiesCleared);
            Assert.Greater(r.Banked[0], 0f, "he did bank");
            Assert.AreEqual(10f, h.TrophyRoomOffered, 1e-3f, "two trophies of 5 at the trip home");
            Assert.AreEqual(5f, h.TrophyRoomFilled, 1e-3f, "the trip carried 15 in a class beak of 10: 5 of the room was used");
        }

        [Test]
        public void Banking_ClearsTrophies_WhenThePassiveSaysSo()
        {
            var r = MatchSim.Run(BankingHunter(clearOnBank: true), 1);
            var h = r.Counters[0];
            Assert.AreEqual(2, h.TrophiesEarned);
            Assert.AreEqual(2, h.TrophiesCleared, "a streak ends at the bank");
            Assert.AreEqual(0, h.TrophiesAtEnd);
            Assert.AreEqual(10f, h.TrophyRoomOffered, 1e-3f);
            Assert.AreEqual(5f, h.TrophyRoomFilled, 1e-3f);
        }

        // Seat 0: class beak 10, room 5; steals twice from a rich carrier (15 food, 2 trophies), then walks home and banks it.
        private static MatchSetup BankingHunter(bool clearOnBank)
        {
            var hunter = Hunter(Big, room: 5, cap: 3, clearOnBank: clearOnBank, capacity: 10, abilities: MatchTestKit.Steal(Big, 10f, 1f));
            var setup = Arena(new MatchKit[] { hunter, Pecker("a", 30f, 0.1f), null, null }, 11f);
            int step = 0;
            setup.Policies[0] = MatchTestKit.Policy((v, s) =>
            {
                Vector2 home = v.Map.Places[OracleMapSO.BaseIndex(s)].Pos;
                var intent = MatchIntent.Idle(v.Time >= 4.5f ? home : Vector2.zero);
                if (step == 0 && v.Time >= 2f) { step = 1; intent.CastSlot = 0; }
                else if (step == 1 && v.Time >= 3.5f) { step = 2; intent.CastSlot = 0; }
                return intent;
            });
            setup.Policies[1] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(1));
            return setup;
        }

        [Test]
        public void EveryCapacityRead_GoesThroughCapacityOf()
        {
            string sim = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/MatchSim.cs"));
            StringAssert.Contains("class Core", sim, "the scan found the simulator");
            int reads = sim.Split('\n').Count(l => !l.TrimStart().StartsWith("//") && l.Contains("Farm.CargoCapacity"));
            Assert.AreEqual(1, reads, "MatchSim reads the class capacity once, inside CapacityOf");
            StringAssert.Contains("float CapacityOf(int s) =>", sim);
            StringAssert.Contains("Farm.CargoCapacity + TrophyRules.Room(", sim);
            foreach (string policy in new[] { "ScriptedPolicy.cs", "UtilityPolicy.cs" })
            {
                string text = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance", policy));
                StringAssert.Contains("class ", text, "the scan found " + policy);
                StringAssert.DoesNotContain("CargoCapacity", text, policy + " reads SeatView.Capacity, the one capacity the simulator computes");
            }
        }

        [Test]
        public void Sim_CallsTheSharedRules_NotCopies_Trophy()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Scripts/Balance/MatchSim.cs"));
            StringAssert.Contains("class Core", source);
            foreach (var call in new[] { "PeckBreakRules.CanBreak(", "PeckBreakRules.Bite(", "PeckBreakRules.Resolve(", "TrophyRules.Overflow(", "TrophyRules.Earn(",
                "TrophyRules.LoseOne(", "TrophyRules.StealEarns(", "TrophyRules.Room(" })
                StringAssert.Contains(call, source, "the simulator must call the shared rule, not restate it.");
        }

        private static MatchResult TrophyFight(bool shipped, ulong seed)
        {
            var hunter = Hunter(0f, room: 0, cap: 3, capacity: 30, peckCooldown: 1f, abilities: MatchTestKit.Steal(8f, 5f, 3f));
            if (!shipped) { hunter.Passive.Break = default; hunter.Passive.Trophy = TrophyFacts.None; }
            var kits = new[] { hunter, Carrier("b", 3f), Carrier("c", 3f), Carrier("d", 3f) };
            foreach (var k in kits.Skip(1)) { k.Farm.PeckCooldown = 1f; k.Farm.CargoCapacity = 30; }
            var setup = Arena(kits, 25f);
            setup.Policies[0] = MatchTestKit.Policy((v, s) =>
            {
                var intent = MatchTestKit.PeckAt(v, s, OracleMapSO.PersonalIndex(0));
                if (v.Seats[s].CooldownLeft[1] <= 1e-4f) intent.CastSlot = 0;
                return intent;
            });
            for (int i = 1; i < 4; i++) setup.Policies[i] = MatchTestKit.Pecker(OracleMapSO.PersonalIndex(0));
            return MatchSim.Run(setup, seed);
        }

        [Test]
        public void ShippedOff_PlaysExactlyAsBefore_TrophyHalf()
        {
            // The Trophy Hunter as shipped (no reach, room 0, a cap of 3) against no facts at all.
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                var a = TrophyFight(true, seed);
                var b = TrophyFight(false, seed);
                Assert.AreEqual(b.Winner, a.Winner);
                Assert.AreEqual(b.EndSeconds, a.EndSeconds);
                CollectionAssert.AreEqual(b.Banked, a.Banked);
                Assert.AreEqual(b.Events.Count, a.Events.Count(e => e.Kind != MatchEventKind.Trophy), "the only extra events are the trophy counts");
                for (int s = 0; s < 4; s++)
                {
                    Assert.AreEqual(b.Counters[s].Foraged, a.Counters[s].Foraged, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].Stolen, a.Counters[s].Stolen, $"seat {s}");
                    Assert.AreEqual(b.Counters[s].LostToSteals, a.Counters[s].LostToSteals, $"seat {s}");
                    Assert.AreEqual(0f, a.Counters[s].OverflowToCentre);
                    Assert.AreEqual(0, a.Counters[s].Breaks);
                }
            }
            Assert.Greater(TrophyFight(true, 1).Counters[0].Steals, 0, "the hunter did steal: the comparison is not vacuous");
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
