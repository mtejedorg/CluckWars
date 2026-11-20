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
            Assert.IsNotNull(_reference, $"{OracleMenu.ReferencePath} is missing: run {OracleMenu.AcceptMenuPath}.");
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
                float stored = reference.Entries[at].BestBanked;
                if (Mathf.Abs(line.BestBanked - stored) > DriftTolerance)
                    found.Add($"{line.Label}: measured {line.BestBanked:0.00}, reference {stored:0.00}");
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
                $"numbers moved since {reference.AcceptedOn}: accept the new state (Cluck Wars/Balance/Oracle/Accept as reference) or find what moved. "
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
                OracleReferenceSO.Accept(moved, _measured, "test", "2000-01-01");
                var first = moved.Entries[0];
                first.BestBanked += 2f * DriftTolerance + 1f;
                moved.Entries[0] = first;

                var ex = Assert.Throws<AssertionException>(() => AssertNoDrift(_measured, moved));
                StringAssert.Contains("numbers moved since 2000-01-01", ex.Message);
                StringAssert.Contains("Cluck Wars/Balance/Oracle/Accept as reference", ex.Message);
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
                float mean = _measured.Economy.Where(l => l.Applicable).Average(l => l.BestBanked);
                foreach (var line in applicable)
                    Assert.AreEqual(mean, line.Result, 0.01f, $"{line.Label}: result after accepting");
                Assert.AreEqual("2026-11-20", accepted.AcceptedOn);
                Assert.AreEqual("equalise", accepted.Note);
                Assert.AreEqual(_measured.MapFingerprint, accepted.MapFingerprint);
            }
            finally { Object.DestroyImmediate(accepted); }
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
                Assert.AreEqual(line.BestBanked + line.OffsetSeconds * line.IncomePerSecond, line.Result, 1e-4f, line.Label);
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
