using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The designs and the parallel runner. No rate is asserted: cell counts, determinism for any thread count and the failure message are.
    /// Counting tests use eight synthetic kits (two per class); the thread test runs P0 over the shipped kits.
    /// </summary>
    public sealed class MatchRunnerTests
    {
        private const float Half = 20f;
        private const ulong Seed = 99UL;

        [TearDown]
        public void TearDown() => MatchTestKit.Release();

        private static MatchKit[] EightKits()
        {
            var kits = new MatchKit[8];
            for (int i = 0; i < kits.Length; i++)
            {
                kits[i] = MatchTestKit.Farmer("kit" + i, 6f, 20, 3f, 1f, 0.5f, 5f);
                kits[i].Class = (ChickenClass)(i / 2);
            }
            return kits;
        }

        private MatchContext SyntheticContext()
        {
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Setup(map, new MatchKit[4], FarmOnlyPolicy.Factory, Half);
            return new MatchContext
            {
                Map = map, Nav = setup.Nav, Food = setup.Food, Rules = setup.Rules, Kits = EightKits(), BalanceHash = "synthetic",
            };
        }

        private static OracleInputs Inputs() => new OracleInputs
        {
            Map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath),
            Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
            Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
            Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
        };

        [Test]
        public void Mixed_Has70x6xRCells_EachKitIn35x6xR()
        {
            const int r = 3;
            var kits = EightKits();
            var cells = MatchDesigns.Cells(MatchDesignKind.Mixed, kits, r);

            Assert.AreEqual(70 * 6 * r, cells.Count);
            for (int k = 0; k < kits.Length; k++)
                Assert.AreEqual(35 * 6 * r, cells.Count(c => c.KitOfSeat.Contains(k)), $"kit {k}");
            Assert.IsTrue(cells.All(c => c.KitOfSeat.Distinct().Count() == 4), "Four distinct kits in every match.");
            CollectionAssert.AreEqual(Enumerable.Range(0, cells.Count), cells.Select(c => c.Index), "Cells are numbered in order.");
        }

        [Test]
        public void OnePerClass_Has16x6xRCells_OneKitPerClass()
        {
            const int r = 2;
            var kits = EightKits();
            var cells = MatchDesigns.Cells(MatchDesignKind.OnePerClass, kits, r);

            Assert.AreEqual(16 * 6 * r, cells.Count);
            foreach (var cell in cells)
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, cell.KitOfSeat.Select(k => (int)kits[k].Class), "One kit of every class.");
        }

        [Test]
        public void Seatings_AreTheSixDistinctUpToRotation()
        {
            var kits = EightKits();
            var cells = MatchDesigns.Cells(MatchDesignKind.Mixed, kits, 4).Where(c => c.KitOfSeat.OrderBy(k => k).SequenceEqual(new[] { 0, 1, 2, 3 })).ToList();

            Assert.AreEqual(6 * 4, cells.Count);
            var canonical = cells.Select(Canonical).ToList();
            Assert.AreEqual(6, canonical.Distinct().Count(), "Six seatings that differ up to rotation.");
            foreach (var group in canonical.GroupBy(c => c)) Assert.AreEqual(4, group.Count(), "Each is played once per replicate.");
            Assert.Greater(cells.Select(c => c.KitOfSeat[0]).Distinct().Count(), 1, "Replicates turn the table, so no kit always sits in seat 0.");

            Assert.AreEqual(6, MatchDesigns.Seatings(new[] { 3, 1, 0, 2 }).Count);
        }

        // The seating turned so the lowest kit sits in seat 0.
        private static string Canonical(MatchCell cell)
        {
            var seats = cell.KitOfSeat;
            int at = Array.IndexOf(seats, seats.Min());
            return string.Join(",", Enumerable.Range(0, 4).Select(i => seats[(at + i) % 4]));
        }

        [Test]
        public void SeatCheck_AndForcedKills_BuildTheirCells()
        {
            var kits = EightKits();
            Assert.AreEqual(8 * 5, MatchDesigns.Cells(MatchDesignKind.SeatCheck, kits, 5).Count);
            Assert.IsTrue(MatchDesigns.Cells(MatchDesignKind.SeatCheck, kits, 1).All(c => c.KitOfSeat.Distinct().Count() == 1));

            // No kit holds a Death Mark here, so the forced design has no Spoiler to play.
            Assert.AreEqual(0, MatchDesigns.Cells(MatchDesignKind.ForcedKills, kits, 1).Count);

            // Give two kits (one class) a Death Mark: each plays 8 sets x 6 seatings x 5 kill counts x 2 victim modes.
            kits[6].Abilities = new[] { MatchTestKit.DeathMark(3f, 6f, 4f, 20f, 8f, 4f) };
            kits[7].Abilities = new[] { MatchTestKit.DeathMark(3f, 6f, 4f, 20f, 8f, 4f) };
            var forced = MatchDesigns.Cells(MatchDesignKind.ForcedKills, kits, 1);
            Assert.AreEqual(2 * 8 * 6 * 5 * 2, forced.Count);
            foreach (var c in forced)
            {
                Assert.IsTrue(MatchDesigns.HoldsDeathMark(kits[c.KitOfSeat[c.Forced.Spoiler]]), "The forced seat holds the Death Mark.");
                Assert.GreaterOrEqual(c.Forced.Times.Length, c.Forced.Kills);
            }
        }

        [Test]
        public void Rotation_IsSharedOutEvenly_SoNoKitKeepsTheLowSeats()
        {
            // One set of four kits, ten replicates of each of the six seatings: sixty matches. The lowest kit sits in each seat about as often.
            var kits = EightKits();
            var cells = MatchDesigns.Cells(MatchDesignKind.Mixed, kits, 10).Where(c => c.KitOfSeat.OrderBy(k => k).SequenceEqual(new[] { 0, 1, 2, 3 })).ToList();

            Assert.AreEqual(60, cells.Count);
            for (int seat = 0; seat < 4; seat++)
                Assert.That(cells.Count(c => c.KitOfSeat[seat] == 0), Is.InRange(14, 16), $"kit 0 in seat {seat}");
        }

        [Test]
        public void ReactionJitter_MovesWhenAnIntentLands_IsSeeded_AndZeroDrawsNothing()
        {
            var map = MatchTestKit.OpenMap(Half);
            var kit = MatchTestKit.Farmer("farmer", 6f, 20, 3f, 1f, 0.5f, 5f);
            int pile = OracleMapSO.PersonalIndex(0);

            float FirstPeck(ulong seed, float jitter)
            {
                var setup = MatchTestKit.Duel(map, kit, kit, MatchTestKit.Pecker(pile), MatchTestKit.Stand, Half, 8f);
                setup.Params[0].ReactionDelay = 0.1f;
                setup.Params[0].ReactionJitter = jitter;
                var result = MatchSim.Run(setup, seed);
                return result.Events.First(e => e.Kind == CluckWars.Balance.MatchEventKind.CastStart && e.Seat == 0).Time;
            }

            var withJitter = Enumerable.Range(1, 8).Select(i => FirstPeck((ulong)i, 0.3f)).ToList();
            Assert.Greater(withJitter.Distinct().Count(), 1, "Other seeds draw other extra delays.");
            Assert.IsTrue(withJitter.All(t => t >= FirstPeck(1UL, 0f) - 1e-4f), "The jitter only adds delay.");
            Assert.AreEqual(FirstPeck(3UL, 0.3f), FirstPeck(3UL, 0.3f), "Same seed, same delays.");
            Assert.AreEqual(1, Enumerable.Range(1, 8).Select(i => FirstPeck((ulong)i, 0f)).Distinct().Count(), "No jitter, no draw: every seed plays alike.");
        }

        private static void AssertSame(MatchResult a, MatchResult b, string where)
        {
            Assert.AreEqual(a.Winner, b.Winner, where);
            Assert.AreEqual(a.WinnerWithoutSpoilerBonus, b.WinnerWithoutSpoilerBonus, where);
            Assert.AreEqual(a.EndSeconds, b.EndSeconds, where);
            Assert.AreEqual(a.ReachedTarget, b.ReachedTarget, where);
            Assert.AreEqual(a.RejectedIntents, b.RejectedIntents, where);
            Assert.AreEqual(a.CastsLostToReactionDelay, b.CastsLostToReactionDelay, where);
            Assert.AreEqual(a.Comeback, b.Comeback, where);
            CollectionAssert.AreEqual(a.Banked, b.Banked, where);
            CollectionAssert.AreEqual(a.Kills, b.Kills, where);
            for (int s = 0; s < a.Counters.Length; s++)
            {
                Assert.AreEqual(a.Counters[s].Foraged, b.Counters[s].Foraged, where);
                Assert.AreEqual(a.Counters[s].Stolen, b.Counters[s].Stolen, where);
                Assert.AreEqual(a.Counters[s].Casts, b.Counters[s].Casts, where);
                Assert.AreEqual(a.Counters[s].FirstContact, b.Counters[s].FirstContact, where);
                CollectionAssert.AreEqual(a.Counters[s].CutsByCause, b.Counters[s].CutsByCause, where);
            }
        }

        [Test]
        public void SameSpec_SameResults()
        {
            var context = SyntheticContext();
            var spec = new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, RunSeed = Seed, Threads = 2, CellLimit = 40 };

            var a = MatchRunner.Run(context, spec, FarmOnlyPolicy.Factory);
            var b = MatchRunner.Run(context, spec, FarmOnlyPolicy.Factory);

            Assert.AreEqual(40, a.Results.Length);
            for (int i = 0; i < a.Results.Length; i++) AssertSame(a.Results[i], b.Results[i], $"cell {i}");
        }

        [Test]
        public void OneThreadAndTwelveThreads_GiveTheSameResults()
        {
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            var spec = new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, Population = PolicyPopulation.P0, RunSeed = Seed, CellLimit = 60 };

            spec.Threads = 1;
            var one = MatchRunner.Run(context, spec);
            spec.Threads = 12;
            var twelve = MatchRunner.Run(context, spec);

            Assert.AreEqual(60, one.Results.Length);
            for (int i = 0; i < 60; i++) AssertSame(one.Results[i], twelve.Results[i], $"cell {i}");
        }

        [Test]
        public void EveryMatch_HasExactlyOneWinner()
        {
            var context = MatchContext.From(Inputs(), OracleMapBaker.ArenaHalf(), OracleMenu.BotArrivalRadius());
            var run = MatchRunner.Run(context, new MatchRunSpec
            {
                Design = MatchDesignKind.Mixed, Replicates = 1, Population = PolicyPopulation.P0, RunSeed = Seed, Threads = 4, CellLimit = 100,
            });

            var wins = new int[context.Kits.Length];
            for (int i = 0; i < run.Results.Length; i++)
            {
                int seat = run.Results[i].Winner;
                Assert.That(seat, Is.InRange(0, 3), $"cell {i} has a winner");
                wins[run.Cells[i].KitOfSeat[seat]]++;
            }
            Assert.AreEqual(run.Results.Length, wins.Sum(), "The wins of all kits add up to the matches played.");
        }

        [Test]
        public void PolicyThrows_RunFailsNamingCellAndSeed()
        {
            var context = SyntheticContext();
            const int failingMatch = 2;
            int made = 0;
            PolicyFactory flaky = (seat, p) =>
            {
                // Four seats per match, one thread, cells in order: the third match's first factory call throws.
                if (Interlocked.Increment(ref made) == failingMatch * 4 + 1) throw new InvalidOperationException("boom");
                return FarmOnlyPolicy.Factory(seat, p);
            };
            var spec = new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, RunSeed = Seed, Threads = 1, CellLimit = 5 };

            var e = Assert.Throws<InvalidOperationException>(() => MatchRunner.Run(context, spec, flaky));

            ulong seed = MatchRng.MatchSeed(Seed, failingMatch, 0);
            StringAssert.Contains($"cell {failingMatch}", e.Message);
            StringAssert.Contains(seed.ToString("x16"), e.Message);
            StringAssert.Contains("boom", e.Message);
        }
    }
}
