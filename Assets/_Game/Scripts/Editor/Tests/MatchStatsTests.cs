using System.Collections.Generic;
using System.Linq;
using CluckWars.Balance;
using CluckWars.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The statistics of the match report: the Wilson interval, Kendall's tau and the Spoiler kills-to-win row. Matches here are built by
    /// hand; no test asserts a win rate of the simulator (D7), and the shipped kill targets are only checked for shape.
    /// </summary>
    public sealed class MatchStatsTests
    {
        private const int Spoiler = 2;

        [Test]
        public void Wilson_MatchesTheTextbookInterval()
        {
            var w = WinInterval.Wilson(10, 100);
            Assert.AreEqual(0.1f, w.Rate, 1e-6f);
            Assert.AreEqual(0.0552f, w.Low, 1e-3f);
            Assert.AreEqual(0.1744f, w.High, 1e-3f);
            Assert.AreEqual(10, w.Wins);
            Assert.AreEqual(100, w.Trials);
        }

        [Test]
        public void Wilson_ZeroTrials_IsTheFullInterval()
        {
            var w = WinInterval.Wilson(0, 0);
            Assert.IsTrue(float.IsNaN(w.Rate));
            Assert.AreEqual(0f, w.Low);
            Assert.AreEqual(1f, w.High);
        }

        [Test]
        public void Wilson_AllWinsAndNoWins_StayInsideZeroToOne()
        {
            var all = WinInterval.Wilson(20, 20);
            var none = WinInterval.Wilson(0, 20);
            Assert.AreEqual(1f, all.High, 1e-6f);
            Assert.Less(all.Low, 1f);
            Assert.AreEqual(0f, none.Low, 1e-6f);
            Assert.Greater(none.High, 0f);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => WinInterval.Wilson(5, 4));
        }

        [Test]
        public void KendallTau_MatchesHandCountedPairs()
        {
            var a = new[] { 1f, 2f, 3f, 4f, 5f };
            Assert.AreEqual(0.2f, MatchStats.KendallTau(a, new[] { 3f, 4f, 1f, 2f, 5f }), 1e-6f, "6 concordant, 4 discordant of 10");
            Assert.AreEqual(-1f, MatchStats.KendallTau(a, new[] { 5f, 4f, 3f, 2f, 1f }), 1e-6f);
            Assert.AreEqual(1f, MatchStats.KendallTau(a, a), 1e-6f);
        }

        [Test]
        public void KendallTau_TiesAndDegenerateInputs()
        {
            Assert.IsTrue(float.IsNaN(MatchStats.KendallTau(new[] { 1f }, new[] { 1f })), "one point has no pairs");
            Assert.IsTrue(float.IsNaN(MatchStats.KendallTau(new[] { 1f, 2f, 3f }, new[] { 7f, 7f, 7f })), "a constant series has no order");
            // tau-b: one tie in b leaves 2 comparable pairs of 3, both concordant -> 2 / sqrt(3 * 2)
            Assert.AreEqual(2f / Mathf.Sqrt(6f), MatchStats.KendallTau(new[] { 1f, 2f, 3f }, new[] { 1f, 1f, 2f }), 1e-5f);
            Assert.Throws<System.ArgumentException>(() => MatchStats.KendallTau(new[] { 1f, 2f }, new[] { 1f }));
        }

        // A match seat 'spoilerSeat' banked 'mine' in, the best rival 'rival'; the Spoiler wins with the bonus when 'winnerWith' says so.
        private static MatchResult Match(int winner, int winnerWithout, float mine, float rival, bool clock)
        {
            var banked = new[] { rival, rival / 2f, mine, 0f };
            return new MatchResult
            {
                Winner = winner, WinnerWithoutSpoilerBonus = winnerWithout, Banked = banked, Kills = new int[4],
                NobodyReachedTarget = clock, ReachedTarget = !clock,
            };
        }

        private static List<MatchResult> Four() => new List<MatchResult>
        {
            Match(Spoiler, Spoiler, 30f, 20f, true),   // wins both ways
            Match(Spoiler, 0, 25f, 24f, true),         // wins only with the bonus
            Match(0, 0, 10f, 30f, true),               // loses
            Match(Spoiler, Spoiler, 40f, 12f, false),  // the target ended it
        };

        [Test]
        public void KillsToWin_CountsWinsWithAndWithoutThePassive_AndTheMeans()
        {
            var row = MatchStats.KillsToWin(Four(), Spoiler, 3, ForcedVictims.Loaded, null);
            Assert.AreEqual(2, row.Win.Wins, "without the bonus: matches 1 and 4");
            Assert.AreEqual(3, row.WinWithPassive.Wins, "with it: matches 1, 2 and 4");
            Assert.AreEqual(4, row.Win.Trials);
            Assert.AreEqual((30f + 25f + 10f + 40f) / 4f, row.MeanFinal, 1e-4f);
            Assert.AreEqual((20f + 24f + 30f + 12f) / 4f, row.MeanBestRival, 1e-4f);
            Assert.AreEqual(0.75f, row.NobodyReachedTarget, 1e-6f);
            Assert.AreEqual(3, row.Kills);
            Assert.AreEqual(ForcedVictims.Loaded, row.Victims);
        }

        [Test]
        public void KillsToWin_WithNoTarget_HasNoLabel()
        {
            Assert.IsNull(MatchStats.KillsToWin(Four(), Spoiler, 3, ForcedVictims.Empty, null).Label, "no list");
            Assert.IsNull(MatchStats.KillsToWin(Four(), Spoiler, 3, ForcedVictims.Empty, new List<SpoilerKillTarget>()).Label, "empty list");
            var other = new List<SpoilerKillTarget> { new SpoilerKillTarget { Kills = 4, WithPassive = false, MinWinRate = 0f, MaxWinRate = 1f } };
            Assert.IsNull(MatchStats.KillsToWin(Four(), Spoiler, 3, ForcedVictims.Empty, other).Label, "a target for other kills does not label this row");
        }

        [Test]
        public void KillsToWin_WithATarget_LabelsInOrOut()
        {
            // Four(): 0.50 without the passive, 0.75 with it.
            var inside = new List<SpoilerKillTarget>
            {
                new SpoilerKillTarget { Kills = 3, WithPassive = false, MinWinRate = 0.4f, MaxWinRate = 0.6f },
                new SpoilerKillTarget { Kills = 3, WithPassive = true, MinWinRate = 0.9f, MaxWinRate = 1f },
            };
            string label = MatchStats.KillsToWin(Four(), Spoiler, 3, ForcedVictims.Empty, inside).Label;
            StringAssert.Contains("without passive IN", label);
            StringAssert.Contains("with passive OUT", label);

            var empty = MatchStats.KillsToWin(new List<MatchResult>(), Spoiler, 3, ForcedVictims.Empty, inside);
            StringAssert.Contains("NO DATA", empty.Label, "no matches, no verdict");
            Assert.IsTrue(float.IsNaN(empty.MeanFinal));
        }

        // ---- the shipped reference asset ----

        private static OracleReferenceSO Reference()
        {
            var asset = AssetDatabase.LoadAssetAtPath<OracleReferenceSO>(OracleMenu.ReferencePath);
            Assert.IsNotNull(asset, $"{OracleMenu.ReferencePath} is missing.");
            return asset;
        }

        [Test]
        public void ReferenceAsset_KillTargets_AreWellFormed()
        {
            var targets = Reference().KillTargets;
            Assert.IsNotNull(targets);
            foreach (var t in targets)
            {
                string who = $"{t.Kills} kills, withPassive {t.WithPassive}";
                Assert.That(t.Kills, Is.InRange(0, 4), who);
                Assert.That(t.MinWinRate, Is.InRange(0f, 1f), who);
                Assert.That(t.MaxWinRate, Is.InRange(0f, 1f), who);
                Assert.LessOrEqual(t.MinWinRate, t.MaxWinRate, who);
            }
            var keys = targets.Select(t => (t.Kills, t.WithPassive)).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "two rows for the same (kills, with passive)");
        }

        [Test]
        public void ReferenceAsset_EntriesUnchanged_ByTheNewList()
        {
            var asset = Reference();
            var copy = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(asset), copy);
                Assert.AreEqual(asset.Entries.Count, copy.Entries.Count);
                for (int i = 0; i < asset.Entries.Count; i++)
                {
                    Assert.AreEqual(asset.Entries[i].Label, copy.Entries[i].Label);
                    Assert.AreEqual(asset.Entries[i].BestSeconds, copy.Entries[i].BestSeconds);
                    Assert.AreEqual(asset.Entries[i].OffsetSeconds, copy.Entries[i].OffsetSeconds);
                }
                CollectionAssert.AreEquivalent(asset.Offsets(), copy.Offsets());
                Assert.AreEqual(asset.KillTargets.Count, copy.KillTargets.Count, "the new list survives the round trip");
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void NewListIsEmptyByDefault_SoAnOlderAssetLoadsWithNoTargets()
        {
            var fresh = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                Assert.IsNotNull(fresh.KillTargets);
                Assert.AreEqual(0, fresh.KillTargets.Count);
            }
            finally { Object.DestroyImmediate(fresh); }
        }
    }
}
