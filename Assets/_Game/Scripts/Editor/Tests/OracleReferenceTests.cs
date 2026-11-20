using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The accepted oracle reference (spec 5.6). The oracle is a guide, not a gate: the only test here that can go red
    /// because of the game's numbers is <see cref="Measured_EqualsTheAcceptedReference"/>, the drift guard.
    /// </summary>
    public sealed class OracleReferenceTests
    {
        private const float DriftTolerance = 0.05f;

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private OracleReferenceSO _reference;
        private OracleReport _measured;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _reference = AssetDatabase.LoadAssetAtPath<OracleReferenceSO>(OracleMenu.ReferencePath);
            Assert.IsNotNull(_reference, $"{OracleMenu.ReferencePath} is missing: run {OracleMenu.RecordMenuPath}.");
            _measured = OracleRunner.Run(Inputs(), _reference.Offsets());
        }

        private OracleInputs Inputs() => new OracleInputs { Map = _map, Abilities = _abilities, Classes = _classes, Match = _match };

        /// <summary>What differs between a fresh measurement and a reference; empty when they agree.</summary>
        private static List<string> Differences(OracleReport report, OracleReferenceSO reference)
        {
            var found = new List<string>();
            var applicable = report.Economy.Where(l => l.Applicable).ToList();
            foreach (var line in applicable)
            {
                int at = reference.Entries.FindIndex(e => e.Label == line.Label);
                if (at < 0) { found.Add($"{line.Label}: the reference has no entry"); continue; }
                float stored = reference.Entries[at].BestSeconds;
                if (Mathf.Abs(line.BestSeconds - stored) > DriftTolerance)
                    found.Add($"{line.Label}: measured {line.BestSeconds:0.00} s, reference {stored:0.00} s");
            }
            foreach (var entry in reference.Entries)
                if (!applicable.Any(l => l.Label == entry.Label))
                    found.Add($"{entry.Label}: in the reference but no longer a foraging kit");
            return found;
        }

        private static void AssertNoDrift(OracleReport report, OracleReferenceSO reference)
        {
            var found = Differences(report, reference);
            Assert.IsEmpty(found,
                $"numbers moved since {reference.AcceptedOn}: record the new state ({OracleMenu.RecordMenuPath}) or find what moved. "
                + string.Join("; ", found));
        }

        [Test]
        public void Measured_EqualsTheAcceptedReference() => AssertNoDrift(_measured, _reference);

        [Test]
        public void Drift_NamesAKitWhoseNumberMoved()
        {
            var moved = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                OracleReferenceSO.Record(moved, _measured, "test", "2000-01-01");
                var first = moved.Entries[0];
                first.BestSeconds += 2f * DriftTolerance + 1f;
                moved.Entries[0] = first;

                var ex = Assert.Throws<AssertionException>(() => AssertNoDrift(_measured, moved));
                StringAssert.Contains("numbers moved since 2000-01-01", ex.Message);
                StringAssert.Contains(OracleMenu.RecordMenuPath, ex.Message);
                StringAssert.Contains(first.Label, ex.Message);
            }
            finally { Object.DestroyImmediate(moved); }
        }

        [Test]
        public void Accept_MakesEveryApplicableResultEqual()
        {
            var accepted = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                OracleReferenceSO.Accept(accepted, _measured, "equalise", "2026-11-20");
                var again = OracleRunner.Run(Inputs(), accepted.Offsets());
                var applicable = again.Economy.Where(l => l.Applicable).ToList();
                Assert.Greater(applicable.Count, 1);
                float mean = _measured.Economy.Where(l => l.Applicable).Average(l => l.BestSeconds);
                foreach (var line in applicable)
                    Assert.AreEqual(mean, line.Result, 0.01f, $"{line.Label}: result after accepting");
                Assert.AreEqual("2026-11-20", accepted.AcceptedOn);
                Assert.AreEqual("equalise", accepted.Note);
                Assert.AreEqual(_measured.MapFingerprint, accepted.MapFingerprint);
            }
            finally { Object.DestroyImmediate(accepted); }
        }

        [Test]
        public void Record_KeepsTheStoredOffsets_AndStoresTheMeasurement()
        {
            var target = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                var foragers = _measured.Economy.Where(l => l.Applicable).Select(l => l.Label).ToList();
                Assert.GreaterOrEqual(foragers.Count, 3);
                // First kit: an offset to keep. Second kit: no entry at all. A label the game no longer has: dropped.
                target.Entries = new List<OracleReferenceSO.Entry>
                {
                    new OracleReferenceSO.Entry { Label = foragers[0], BestSeconds = 1f, OffsetSeconds = 3.25f },
                    new OracleReferenceSO.Entry { Label = foragers[2], BestSeconds = 1f, OffsetSeconds = -1.5f },
                    new OracleReferenceSO.Entry { Label = "Retired_Kit", BestSeconds = 1f, OffsetSeconds = 9f },
                };

                OracleReferenceSO.Record(target, _measured, "re-arm", "2026-11-20");

                var offsets = target.Offsets();
                Assert.AreEqual(3.25f, offsets[foragers[0]], 1e-6f);
                Assert.AreEqual(0f, offsets[foragers[1]], 1e-6f, "a label with no stored offset gets 0");
                Assert.AreEqual(-1.5f, offsets[foragers[2]], 1e-6f);
                Assert.IsFalse(offsets.ContainsKey("Retired_Kit"));
                CollectionAssert.AreEquivalent(foragers, target.Entries.Select(e => e.Label));
                foreach (var entry in target.Entries)
                    Assert.AreEqual(_measured.Economy.Single(l => l.Label == entry.Label).BestSeconds, entry.BestSeconds, 1e-6f, entry.Label);
                Assert.AreEqual(ReferenceOrigin.Recorded, target.Origin);
                Assert.AreEqual("re-arm", target.Note);
                Assert.AreEqual(_measured.MapFingerprint, target.MapFingerprint);
                Assert.IsEmpty(Differences(_measured, target), "a recorded baseline re-arms the drift guard");
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void Accept_ReplacesTheOffsets_Record_DoesNot()
        {
            var target = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                var first = _measured.Economy.First(l => l.Applicable).Label;
                target.Entries = new List<OracleReferenceSO.Entry> { new OracleReferenceSO.Entry { Label = first, OffsetSeconds = 42f } };

                OracleReferenceSO.Record(target, _measured, "a", "2026-11-20");
                Assert.AreEqual(42f, target.Offsets()[first], 1e-6f);

                OracleReferenceSO.Accept(target, _measured, "b", "2026-11-20");
                Assert.AreNotEqual(42f, target.Offsets()[first], "Accept recomputes the offset");
                Assert.AreEqual(ReferenceOrigin.AcceptedAsBalanced, target.Origin);
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void ReportHeader_SaysWhoSetTheOffsets()
        {
            var target = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                OracleReferenceSO.Record(target, _measured, "n", "2026-11-20");
                var recorded = OracleRunner.Run(Inputs(), target.Offsets());
                recorded.PricesNote = target.PricesText();
                StringAssert.Contains("prices set by Maestro", recorded.ToMarkdown());
                StringAssert.Contains(target.PricesText(), recorded.ToJson());

                OracleReferenceSO.Accept(target, _measured, "n", "2026-11-20");
                var accepted = OracleRunner.Run(Inputs(), target.Offsets());
                accepted.PricesNote = target.PricesText();
                StringAssert.Contains("prices recomputed by the oracle when this state was accepted as balanced", accepted.ToMarkdown());
                StringAssert.DoesNotContain("prices set by Maestro", accepted.ToMarkdown());
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test]
        public void Accept_KeepsNonForagersWithoutOffset()
        {
            var accepted = ScriptableObject.CreateInstance<OracleReferenceSO>();
            try
            {
                OracleReferenceSO.Accept(accepted, _measured, "n", "2026-11-20");
                var nonForagers = _measured.Economy.Where(l => !l.Applicable).Select(l => l.Label).ToList();
                Assert.IsNotEmpty(nonForagers, "the roster has kits that never forage (the Assassins)");
                var offsets = accepted.Offsets();
                foreach (var label in nonForagers)
                {
                    Assert.IsFalse(offsets.ContainsKey(label), $"{label} must have no offset");
                    Assert.IsFalse(accepted.Entries.Any(e => e.Label == label), $"{label} must have no entry");
                }
            }
            finally { Object.DestroyImmediate(accepted); }
        }

        [Test]
        public void ShippedReference_OffsetsReachTheReport()
        {
            var offsets = _reference.Offsets();
            foreach (var line in _measured.Economy.Where(l => l.Applicable))
            {
                Assert.AreEqual(offsets[line.Label], line.OffsetSeconds, 1e-5f, line.Label);
                Assert.AreEqual(line.BestSeconds - line.OffsetSeconds, line.Result, 1e-4f, line.Label);
            }
        }

        [Test]
        public void NoV1Type_IsLeft()
        {
            // Built from parts so this file does not name what it forbids.
            string names = string.Join("|", "Sct" + "Targets", "SpecSct" + "Targets", "Sct" + "Target", "Balance" + "Oracle",
                "OracleChick" + "en", "OracleRes" + "ult", "OraclePi" + "le");
            var v1 = new Regex(@"\b(" + names + @")\b|\bstruct OracleMap\b|ShippedMap\.OracleMap\b");
            string scripts = Path.Combine(Application.dataPath, "_Game", "Scripts");
            var hits = new List<string>();
            foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in v1.Matches(File.ReadAllText(file)))
                    hits.Add($"{Path.GetFileName(file)}: {m.Value}");
            Assert.IsEmpty(hits, "the v1 oracle (SCT targets and its simulation) is retired: " + string.Join("; ", hits.Distinct()));
        }
    }
}
