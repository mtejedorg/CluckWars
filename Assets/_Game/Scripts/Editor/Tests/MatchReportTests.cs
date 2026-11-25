using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The match report section: what it prints from results the tests build by hand or play on a synthetic map. No rate is asserted
    /// against a target; the tests pin the structure, the labels' source and the rules the lines follow.
    /// </summary>
    public sealed class MatchReportTests
    {
        private const float Half = 20f;
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            MatchTestKit.Release();
            foreach (var o in _made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _made.Clear();
        }

        private static MatchKit[] EightKits()
        {
            var kits = new MatchKit[8];
            for (int i = 0; i < kits.Length; i++)
            {
                kits[i] = MatchTestKit.Farmer("kit" + i, 6f, 20, 3f, 1f, 0.5f, 5f);
                kits[i].Class = (ChickenClass)(i / 2);
                kits[i].Passive = MatchPassive.Identity();
            }
            return kits;
        }

        private MatchContext Context()
        {
            var map = MatchTestKit.OpenMap(Half);
            var setup = MatchTestKit.Setup(map, new MatchKit[4], FarmOnlyPolicy.Factory, Half);
            return new MatchContext { Map = map, Nav = setup.Nav, Food = setup.Food, Rules = setup.Rules, Kits = EightKits(), BalanceHash = "synthetic" };
        }

        private OracleReferenceSO Reference(params SpoilerKillTarget[] targets)
        {
            var so = ScriptableObject.CreateInstance<OracleReferenceSO>();
            _made.Add(so);
            so.KillTargets = new List<SpoilerKillTarget>(targets);
            return so;
        }

        private static MatchResult Result(int winner, float[] banked = null, int[] kills = null, bool clock = true)
        {
            int causes = Enum.GetValues(typeof(CutCause)).Length;
            var counters = new SeatCounters[4];
            for (int s = 0; s < 4; s++)
                counters[s] = new SeatCounters
                {
                    FirstContact = -1f, StolenFromSeat = new float[4], CutsByCause = new int[causes], ExposedByCause = new int[causes],
                    CutWhileExposedByCause = new int[causes],
                };
            return new MatchResult
            {
                Winner = winner, WinnerWithoutSpoilerBonus = winner, Banked = banked ?? new float[4], Kills = kills ?? new int[4],
                NobodyReachedTarget = clock, ReachedTarget = !clock, EndSeconds = 45f, Counters = counters,
            };
        }

        private static MatchRunOutput Output(MatchDesignKind design, MatchCell[] cells, MatchResult[] results) =>
            new MatchRunOutput { Spec = new MatchRunSpec { Design = design, Replicates = 1, RunSeed = 5UL }, Cells = cells, Results = results };

        private static MatchCell Cell(int index, params int[] kits) => new MatchCell { Index = index, KitOfSeat = kits };

        [Test]
        public void Report_WithOnlyP0_GivesNoCall()
        {
            var context = Context();
            var mixed = MatchRunner.Run(context, new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, RunSeed = 3UL, Threads = 2, CellLimit = 60 }, FarmOnlyPolicy.Factory);

            var report = MatchReports.Build(context, new[] { mixed }, Reference());

            Assert.AreEqual(8, report.Kits.Count);
            foreach (var line in report.Kits)
            {
                Assert.AreEqual(KitCall.NoCall, line.Call, line.Label);
                StringAssert.Contains("P1", line.Reason);
                StringAssert.Contains("mixed population", line.Reason);
            }
            StringAssert.Contains("Only P0", report.ToMarkdown());
            StringAssert.Contains("provisional", report.ToMarkdown());
        }

        [Test]
        public void Report_ListsTheLeftOutColumn()
        {
            var context = Context();
            var mixed = MatchRunner.Run(context, new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, RunSeed = 3UL, CellLimit = 10 }, FarmOnlyPolicy.Factory);

            var report = MatchReports.Build(context, new[] { mixed }, Reference());

            CollectionAssert.AreEqual(MatchReports.LeftOut, report.LeftOut);
            string md = report.ToMarkdown();
            foreach (var row in MatchReports.LeftOut) StringAssert.Contains(row, md);
            Assert.Less(md.IndexOf("What this model leaves out", StringComparison.Ordinal), md.IndexOf("Win rate per kit", StringComparison.Ordinal),
                "The left-out table comes before the numbers.");
        }

        // Forced-kills output with one match per (kills, victims) row: the Spoiler in seat 0 wins when its banked food is highest.
        private static MatchRunOutput ForcedOutput()
        {
            var cells = new List<MatchCell>();
            var results = new List<MatchResult>();
            for (int k = 0; k <= MatchDesigns.MaxForcedKills; k++)
                foreach (var v in new[] { ForcedVictims.Empty, ForcedVictims.Loaded })
                {
                    var cell = Cell(cells.Count, 0, 2, 4, 6);
                    cell.Forced = new ForcedKills { Spoiler = 0, Kills = k, Victims = v, Times = new float[MatchDesigns.MaxForcedKills] };
                    cells.Add(cell);
                    results.Add(Result(k >= 3 ? 0 : 1, new[] { 10f + k, 12f, 8f, 5f }, clock: true));
                }
            return Output(MatchDesignKind.ForcedKills, cells.ToArray(), results.ToArray());
        }

        [Test]
        public void KillsToWin_PrintsRawRatesWithoutLabels_WhenNoTargets()
        {
            var report = MatchReports.Build(Context(), new[] { ForcedOutput() }, Reference());

            Assert.AreEqual(10, report.KillsToWin.Count, "Five kill counts, both victim modes.");
            foreach (var row in report.KillsToWin)
            {
                Assert.IsNull(row.Label, $"{row.Kills} kills: no target, no label");
                Assert.AreEqual(1, row.Win.Trials);
            }
            StringAssert.Contains("no target", report.ToMarkdown());
        }

        [Test]
        public void KillsToWin_LabelsRowsFromTheReferenceAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OracleReferenceSO>(OracleMenu.ReferencePath);
            Assert.IsNotNull(asset, OracleMenu.ReferencePath);

            var report = MatchReports.Build(Context(), new[] { ForcedOutput() }, asset);

            foreach (var row in report.KillsToWin)
            {
                bool covered = asset.KillTargets.Any(t => t.Kills == row.Kills);
                Assert.AreEqual(covered, row.Label != null, $"{row.Kills} kills: labelled exactly when the asset has a target for it");
            }
        }

        [Test]
        public void KillsPlayed_GroupsByTheKillsThePolicyMade_AndLabelsAgainstTheTargets()
        {
            var kits = EightKits();
            kits[2].Abilities = new[] { MatchTestKit.DeathMark(3f, 6f, 4f, 20f, 8f, 4f) };   // the Spoiler: kit 2, class Fatty here
            var context = Context();
            context.Kits = kits;
            // Spoiler in seat 1 with 2 kills wins; in seat 3 with 2 kills loses; in seat 0 with 0 kills loses.
            var cells = new[] { Cell(0, 0, 2, 4, 6), Cell(1, 0, 4, 6, 2), Cell(2, 2, 0, 4, 6) };
            var results = new[]
            {
                Result(1, kills: new[] { 0, 2, 0, 0 }), Result(0, kills: new[] { 0, 0, 0, 2 }), Result(1, kills: new[] { 0, 0, 0, 0 }),
            };
            var target = new SpoilerKillTarget { Kills = 2, WithPassive = false, MinWinRate = 0f, MaxWinRate = 0.4f };

            var report = MatchReports.Build(context, new[] { Output(MatchDesignKind.Mixed, cells, results) }, Reference(target));

            var two = report.KillsPlayed.Single(r => r.Kills == 2);
            Assert.AreEqual(2, two.Win.Trials);
            Assert.AreEqual(1, two.Win.Wins);
            StringAssert.Contains("OUT", two.Label, "50 % is outside [0, 0.4]");
            Assert.IsNull(report.KillsPlayed.Single(r => r.Kills == 0).Label);
        }

        [Test]
        public void SeatCheck_OutsideTheInterval_IsFlaggedNotThrown()
        {
            var lopsided = Enumerable.Range(0, 100).Select(i => Result(0)).ToArray();
            var cells = lopsided.Select((r, i) => Cell(i, 0, 0, 0, 0)).ToArray();

            var report = MatchReports.Build(Context(), new[] { Output(MatchDesignKind.SeatCheck, cells, lopsided) }, Reference());

            Assert.IsTrue(report.SeatCheck[0].Flagged, "Seat 0 won every match.");
            Assert.IsTrue(report.SeatCheck[1].Flagged, "Seat 1 won none.");
            StringAssert.Contains("OUTSIDE", report.ToMarkdown());

            var even = Enumerable.Range(0, 100).Select(i => Result(i % 4)).ToArray();
            var fair = MatchReports.Build(Context(), new[] { Output(MatchDesignKind.SeatCheck, even.Select((r, i) => Cell(i, 0, 0, 0, 0)).ToArray(), even) }, Reference());
            Assert.IsFalse(fair.SeatCheck.Any(s => s.Flagged), "An even split is inside every interval.");
        }

        [Test]
        public void PeckCutHazard_IsCutOverExposed()
        {
            Assert.AreEqual(0.25f, MatchReports.PeckCutHazard(8, 2), 1e-6f);
            Assert.IsNaN(MatchReports.PeckCutHazard(0, 0), "No exposed cast, no hazard.");

            var result = Result(0);
            result.Counters[0].ExposedByCause[(int)CutCause.Stun] = 4;
            result.Counters[0].CutWhileExposedByCause[(int)CutCause.Stun] = 1;
            result.Counters[0].CutsByCause[(int)CutCause.Stun] = 3;   // two more cuts came when no stunner was in range at the start
            var report = MatchReports.Build(Context(), new[] { Output(MatchDesignKind.Mixed, new[] { Cell(0, 0, 2, 4, 6) }, new[] { result }) }, Reference());

            var line = report.PeckCuts.Single(l => l.Class == ChickenClass.Warrior && l.Cause == CutCause.Stun);
            Assert.AreEqual(4, line.Exposed);
            Assert.AreEqual(3, line.Cut, "The raw count includes cuts that were not exposed.");
            Assert.AreEqual(0.25f, line.Hazard, 1e-6f, "The hazard is cut-while-exposed over exposed, not the raw count.");
        }

        [Test]
        public void KitRates_AndClassRates_CountSeatsThatWon()
        {
            // Kit 0 wins the first match, kit 1 the second (both class Warrior), so the class wins both of the two seats it sat in; one-per-class gives the class headline.
            var context = Context();
            var mixed = Output(MatchDesignKind.Mixed, new[] { Cell(0, 0, 2, 4, 6), Cell(1, 2, 1, 4, 6) }, new[] { Result(0), Result(1) });
            var one = Output(MatchDesignKind.OnePerClass, new[] { Cell(0, 0, 2, 4, 6), Cell(1, 1, 3, 5, 7) }, new[] { Result(1), Result(0) });

            var report = MatchReports.Build(context, new[] { mixed, one }, Reference());

            Assert.AreEqual(1, report.Kits.Single(k => k.Label == "kit0").Win.Wins);
            Assert.AreEqual(1, report.Kits.Single(k => k.Label == "kit0").Win.Trials);
            Assert.AreEqual(0, report.Kits.Single(k => k.Label == "kit2").Win.Wins);
            var warrior = report.Classes.Single(c => c.Class == ChickenClass.Warrior);
            Assert.AreEqual(2, warrior.Pooled.Wins, "Both seats of class Warrior that won, over the seats of that class.");
            Assert.AreEqual(2, warrior.Pooled.Trials);
            Assert.AreEqual(1, warrior.Headline.Wins);
            Assert.AreEqual(2, warrior.Headline.Trials);
        }

        [Test]
        public void Json_IsWellFormed_AndTheMarkdownNamesTheRunHeadlines()
        {
            var context = Context();
            var mixed = MatchRunner.Run(context, new MatchRunSpec { Design = MatchDesignKind.Mixed, Replicates = 1, RunSeed = 3UL, CellLimit = 30 }, FarmOnlyPolicy.Factory);

            var report = MatchReports.Build(context, new[] { mixed }, Reference());
            string json = report.ToJson();

            Assert.DoesNotThrow(() => JsonUtility.FromJson<JsonProbe>(json), "Unity's parser accepts it.");
            Assert.AreEqual(1, JsonUtility.FromJson<JsonProbe>(json).schema);
            StringAssert.DoesNotContain("NaN", json);
            string md = report.ToMarkdown();
            StringAssert.Contains("ended on the 40 food target", md);
            StringAssert.Contains("mean match length", md);
        }

        [Test]
        public void WarriorBlock_ShowsEveryCounterOfSection8()
        {
            var context = Context();
            var kit = context.Kits[0];
            kit.Class = ChickenClass.Warrior;
            kit.Passive.HitSteal = new HitStealFacts { Stun = 2f, Shove = 2f, LockSeconds = 3f };
            kit.Passive.Bite = new BiteFacts { FreshSeconds = 1f };
            kit.Passive.Trophy = new TrophyFacts { IsChampion = true, Steal = 2f, Cap = 3, Seconds = 6f };

            var results = new MatchResult[2];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = Result(0);
                results[i].Counters[0] = WarriorCounters(results[i].Counters[0], worst: i == 0 ? 2f : 6f);
            }
            var run = Output(MatchDesignKind.Mixed, new[] { Cell(0, 0, 2, 4, 6), Cell(1, 0, 2, 4, 6) }, results);

            var report = MatchReports.Build(context, new[] { run }, Reference());

            var line = report.Warriors.Single(w => w.Kit == "kit0");
            Assert.AreEqual(2, line.Matches);
            Assert.AreEqual(4f, line.HitTakesPerMatch, 1e-4f);
            Assert.AreEqual(8f, line.HitFoodPerMatch, 1e-4f);
            Assert.AreEqual(1f, line.HitRefusedLockPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.HitRefusedNoFoodPerMatch, 1e-4f);
            Assert.AreEqual(3f, line.HitRefusedRoomPerMatch, 1e-4f);
            Assert.AreEqual(5f, line.HitLostFoodPerMatch, 1e-4f);
            Assert.AreEqual(7f, line.FreshHitsPerMatch, 1e-4f);
            Assert.AreEqual(3f, line.BitesFromCastPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.BitesFromCargoPerMatch, 1e-4f);
            Assert.AreEqual(7f, line.BiteFoodFromCastPerMatch, 1e-4f);
            Assert.AreEqual(5f, line.BiteFoodFromCargoPerMatch, 1e-4f);
            Assert.AreEqual(4f, line.BiteLeftInPilePerMatch, 1e-4f);
            Assert.AreEqual(1f, line.BiteLeftWithVictimPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.BitesSpentPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.PecksBittenPerMatch, 1e-4f);
            Assert.AreEqual(3f, line.BiteLostFoodPerMatch, 1e-4f);
            Assert.AreEqual(6f, line.TrophiesEarnedPerMatch, 1e-4f);
            Assert.AreEqual(4f, line.TrophiesDecayedPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.TrophiesClearedPerMatch, 1e-4f);
            Assert.AreEqual(2f, line.TrophyCashesPerMatch, 1e-4f);
            Assert.AreEqual(2.5f, line.TrophiesPerCash, 1e-4f, "trophies cashed over cashes");
            Assert.AreEqual(6f, line.TrophyBonusFoodPerMatch, 1e-4f);
            Assert.AreEqual(4f, line.StealsPerMatch, 1e-4f);
            Assert.AreEqual(30f / 4f, line.MeanStealSize, 1e-4f, "Steal food over Steal casts");
            Assert.AreEqual(2f, line.MeanHitSize, 1e-4f, "hit food over hit takes");
            Assert.AreEqual(8f + 7f + 5f + 6f, line.IdentityFoodPerMatch, 1e-4f, "hit food, bite food and the trophies' food");
            Assert.AreEqual(4f, line.WorstLossMean, 1e-4f);
            Assert.AreEqual(5.8f, line.WorstLossP95, 1e-3f, "linear interpolation between 2 and 6");

            string md = report.ToMarkdown();
            StringAssert.Contains("## Warrior kits", md);
            foreach (var heading in new[]
            {
                "Hit takes", "Hit food", "Refused: lock / empty / full", "Hit food lost", "Fresh hits", "Bites cast / cargo", "Bite food cast / cargo", "Bites spent", "Pecks bitten",
                "Bite food lost", "Trophies earned", "Decayed", "Cleared", "Cashes", "Trophies per cash", "Trophy food", "Steals", "Mean Steal size", "Identity food",
                "Worst loss mean", "Worst loss p95",
            })
                StringAssert.Contains(heading, md);
            StringAssert.Contains("kit0", md);
            string json = report.ToJson();
            Assert.DoesNotThrow(() => JsonUtility.FromJson<JsonProbe>(json));
            foreach (var key in new[] { "\"warriors\"", "\"hitTakes\"", "\"biteLeftInPile\"", "\"trophiesCleared\"", "\"meanStealSize\"", "\"worstLossP95\"" })
                StringAssert.Contains(key, json);
        }

        // The counters of one Warrior seat in one match: the same numbers every match, but the worst loss.
        private static SeatCounters WarriorCounters(SeatCounters c, float worst)
        {
            c.HitTakes = 4; c.HitFood = 8f; c.HitRefusedLock = 1; c.HitRefusedNoFood = 2; c.HitRefusedRoom = 3; c.HitLostFood = 5f;
            c.FreshHits = 7; c.BitesFromCast = 3; c.BitesFromCargo = 2; c.BiteFoodFromCast = 7f; c.BiteFoodFromCargo = 5f; c.BiteLeftInPile = 4f; c.BiteLeftWithVictim = 1f;
            c.BitesSpent = 2; c.PecksBitten = 2; c.BiteLostFood = 3f;
            c.TrophiesEarned = 6; c.TrophiesDecayed = 4; c.TrophiesCleared = 2; c.TrophyCashes = 2; c.TrophiesCashed = 5; c.TrophyBonusFood = 6f;
            c.Steals = 4; c.StealFood = 30f; c.WorstVictimLoss5s = worst;
            return c;
        }

        [Serializable]
        private sealed class JsonProbe { public int schema; }
    }
}
