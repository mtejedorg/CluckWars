using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Match records on the reference (spec 7, D5 (b)) and the drift line (spec 6): a record is data, it leaves part 1's entries, prices and drift
    /// guard alone, and the drift is a report line, never a test (D7). No rate is asserted against a target.
    /// </summary>
    public sealed class MatchReferenceTests
    {
        private static WinInterval W(int wins, int trials) => WinInterval.Wilson(wins, trials);

        private static KitWinLine Kit(string label, ChickenClass cls, int wins, int trials = 1000) =>
            new KitWinLine { Label = label, Class = cls, Win = W(wins, trials), WinP1 = W(wins, trials), WinMixed = W(wins, trials), Call = KitCall.InBand };

        private static MatchReportSection Section(int firstKitWins, string hash = "abcd1234", ulong seed = 0x1234UL)
        {
            var s = new MatchReportSection { BalanceHash = hash, RunSeed = seed };
            s.Kits.Add(Kit("Alpha", ChickenClass.Warrior, firstKitWins));
            s.Kits.Add(Kit("Beta", ChickenClass.Fatty, 250));
            s.Classes.Add(new ClassWinLine { Class = ChickenClass.Warrior, Headline = W(0, 0), Pooled = W(260, 1000) });
            s.Classes.Add(new ClassWinLine { Class = ChickenClass.Fatty, Headline = W(240, 1000), Pooled = W(250, 1000) });
            return s;
        }

        private static OracleReferenceSO Reference()
        {
            var r = ScriptableObject.CreateInstance<OracleReferenceSO>();
            r.AcceptedOn = "2026-01-01";
            r.Note = "part 1 note";
            r.MapFingerprint = "fp";
            r.Origin = ReferenceOrigin.Recorded;
            r.Entries = new List<OracleReferenceSO.Entry>
            {
                new OracleReferenceSO.Entry { Label = "Kit_A", BestSeconds = 31.5f, OffsetSeconds = 5f },
                new OracleReferenceSO.Entry { Label = "Kit_B", BestSeconds = 30.25f, OffsetSeconds = 0f },
            };
            return r;
        }

        [Test]
        public void OldReferenceAsset_LoadsWithEmptyMatchLists_AndSameOffsets()
        {
            // An asset written before match records existed has no such key; it must load with the lists empty and every price intact.
            const string old = "{\"AcceptedOn\":\"2026-01-01\",\"Origin\":1,\"Entries\":[{\"Label\":\"Kit_A\",\"BestSeconds\":31.5,\"OffsetSeconds\":5}]}";
            var r = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                JsonUtility.FromJsonOverwrite(old, r);
                Assert.IsNotNull(r.MatchRecords);
                Assert.AreEqual(0, r.MatchRecords.Count);
                Assert.AreEqual(1, r.Entries.Count);
                Assert.AreEqual(5f, r.Offsets()["Kit_A"]);
            }
            finally { UnityEngine.Object.DestroyImmediate(r); }
        }

        [Test]
        public void AcceptMatch_AppendsARecord_AndLeavesEntriesUnchanged()
        {
            var r = Reference();
            try
            {
                string entriesBefore = JsonUtility.ToJson(new EntryList { Items = r.Entries });
                var offsetsBefore = r.Offsets().OrderBy(p => p.Key).ToList();

                OracleReferenceSO.AcceptMatch(r, Section(300), "first record", "2026-02-03");

                Assert.AreEqual(1, r.MatchRecords.Count);
                var rec = r.MatchRecords[0];
                Assert.AreEqual("2026-02-03", rec.AcceptedOn);
                Assert.AreEqual("first record", rec.Note);
                Assert.AreEqual("abcd1234", rec.BalanceHash);
                Assert.AreEqual("0x0000000000001234", rec.RunSeed);
                var lines = MatchReports.RecordLines(Section(300));
                Assert.AreEqual(lines.Count, rec.Lines.Count);
                var alpha = rec.Lines.Single(l => l.Label == "Alpha");
                Assert.AreEqual(0.3f, alpha.Rate, 1e-6f);
                Assert.AreEqual(1000, alpha.Trials);
                Assert.IsFalse(alpha.IsClass);
                Assert.IsTrue(rec.Lines.Any(l => l.IsClass && l.Label == "Fatty (one per class)"), "a class with a headline keeps it next to the pooled rate");
                Assert.IsFalse(rec.Lines.Any(l => l.Label == "Warrior (one per class)"), "a headline that did not run is not recorded");

                Assert.AreEqual(entriesBefore, JsonUtility.ToJson(new EntryList { Items = r.Entries }), "part 1's entries are not touched");
                CollectionAssert.AreEqual(offsetsBefore, r.Offsets().OrderBy(p => p.Key).ToList(), "nor are the prices");
                Assert.AreEqual(ReferenceOrigin.Recorded, r.Origin);
                Assert.AreEqual("2026-01-01", r.AcceptedOn);
                Assert.AreEqual("part 1 note", r.Note);
            }
            finally { UnityEngine.Object.DestroyImmediate(r); }
        }

        [Serializable]
        private sealed class EntryList { public List<OracleReferenceSO.Entry> Items; }

        [Test]
        public void AcceptMatch_Twice_KeepsBothInOrder()
        {
            var r = Reference();
            try
            {
                OracleReferenceSO.AcceptMatch(r, Section(300, "11111111"), "one", "2026-02-03");
                OracleReferenceSO.AcceptMatch(r, Section(200, "22222222"), "two", "2026-03-04");

                Assert.AreEqual(2, r.MatchRecords.Count);
                Assert.AreEqual("one", r.MatchRecords[0].Note);
                Assert.AreEqual("two", r.MatchRecords[1].Note);
                Assert.AreEqual(0.3f, r.MatchRecords[0].Lines.Single(l => l.Label == "Alpha").Rate, 1e-6f);
                Assert.AreEqual(0.2f, r.MatchRecords[1].Lines.Single(l => l.Label == "Alpha").Rate, 1e-6f);
                Assert.AreEqual(2, r.Entries.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(r); }
        }

        [Test]
        public void AcceptMatch_RefusesAReportWithNoKitAndABlankDate()
        {
            var r = Reference();
            try
            {
                Assert.Throws<InvalidOperationException>(() => OracleReferenceSO.AcceptMatch(r, new MatchReportSection(), "x", "2026-02-03"));
                Assert.Throws<ArgumentException>(() => OracleReferenceSO.AcceptMatch(r, Section(250), "x", " "));
                Assert.AreEqual(0, r.MatchRecords.Count, "a refused record leaves nothing behind");
            }
            finally { UnityEngine.Object.DestroyImmediate(r); }
        }

        [Test]
        public void Drift_ComparesWithTheLastRecord()
        {
            var r = Reference();
            try
            {
                OracleReferenceSO.AcceptMatch(r, Section(200), "older", "2026-02-03");
                OracleReferenceSO.AcceptMatch(r, Section(300), "last", "2026-03-04");

                var drift = MatchReports.DriftAgainst(Section(350), r.MatchRecords[r.MatchRecords.Count - 1]);

                var alpha = drift.Single(d => d.Label.StartsWith("Alpha, P0"));
                Assert.AreEqual(5f, alpha.Points, 1e-3f, "35 % now against 30 % in the last record, not against the older 20 %");
                StringAssert.Contains("2026-03-04", alpha.Label, "the line names the record it compares with");
                Assert.AreEqual(0f, drift.Single(d => d.Label.StartsWith("Beta, P0")).Points, 1e-3f);
                Assert.AreEqual(0f, drift.Single(d => d.Label.StartsWith("Fatty, P0")).Points, 1e-3f);
                Assert.IsFalse(drift.Any(d => d.Label.StartsWith("Warrior (one per class)")), "a line only one side holds has no drift");
            }
            finally { UnityEngine.Object.DestroyImmediate(r); }
        }

        [Test]
        public void Drift_WithNoMatchingLines_IsEmpty()
        {
            var drift = MatchReports.DriftAgainst(Section(300), new MatchRecord { Lines = null });
            Assert.AreEqual(0, drift.Count);
            drift = MatchReports.DriftAgainst(Section(300), new MatchRecord { Lines = new List<MatchRecordLine> { new MatchRecordLine { Label = "Nobody", Population = "P0" } } });
            Assert.AreEqual(0, drift.Count);
        }

        // ---- the report: no match section unless asked for ----

        private static OracleReport PartOneReport()
        {
            var inputs = new OracleInputs
            {
                Map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath),
                Abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                Classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                Match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath),
            };
            Assert.IsNotNull(inputs.Map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            return OracleRunner.Run(inputs, TestAssets.Load<OracleReferenceSO>(OracleMenu.ReferencePath).Offsets());
        }

        [Test]
        public void Report_WithoutMatch_HasNoMatchKeyOrHeading()
        {
            var report = PartOneReport();
            Assert.IsNull(report.Match);
            StringAssert.DoesNotContain("\"match\"", report.ToJson());
            StringAssert.DoesNotContain("Match simulator report", report.ToMarkdown());

            report.Match = Section(250);
            StringAssert.Contains("\"match\":{", report.ToJson(), "with a match section the key appears");
        }
    }
}
