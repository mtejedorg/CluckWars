using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Diagnostics;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The local balance editor's oracle check (Balance Oracle v2, D3a): it measures the proposal with the serialized
    /// <see cref="OracleMapSO"/> the book was handed, reports best food before -> after per kit, and never gates an apply.
    /// </summary>
    public sealed class BalanceCheckTests
    {
        private sealed class QuietLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private const string SnatchDelta = "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.25}}}";
        private const string CheckFile = "Assets/_Game/Scripts/Balance/BalanceSctCheck.cs";
        private const string EditorFile = "Assets/_Game/Scripts/Diagnostics/DevBalanceEditor.cs";
        private const string HookFile = "Assets/_Game/Scripts/Editor/Tests/OracleMapHook.cs";

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private BalanceBook _book;

        private static string Read(string relative) =>
            File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), relative)).Replace("\r\n", "\n");

        [SetUp]
        public void SetUp()
        {
            UiText.Reset();
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _book = new BalanceBook(new QuietLog(), _abilities, _classes, _match, _map);
        }

        [TearDown]
        public void TearDown()
        {
            _book?.Dispose();
            UiText.Reset();
        }

        private BalanceVerdict SpeedyMoveSpeed(float factor)
        {
            Assert.IsTrue(_classes.TryGet(ChickenClass.Speedy, out var entry));
            var stats = entry.Stats;
            string value = (stats.MoveSpeed * factor).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var verdict = _book.Evaluate("{\"schema\":1,\"assets\":{\"" + stats.name + "\":{\"MoveSpeed\":" + value + "}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
            return verdict;
        }

        [Test]
        public void Check_NeverBlocks()
        {
            Assert.IsNull(typeof(OracleCheck).GetMember("Passed", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault(),
                "OracleCheck has no Passed member: nothing can gate on it");
            Assert.IsFalse(Read(CheckFile).Contains("Passed"), "the check never declares a pass or a fail");
            string editor = Read(EditorFile);
            Assert.IsFalse(editor.Contains(".Passed"), "the panel does not read a verdict from the check");
            int at = editor.IndexOf("CheckOracle(", StringComparison.Ordinal);
            Assert.Greater(at, 0);
            string after = editor.Substring(at, 200);
            StringAssert.Contains("_checkedJson = json;", after, "APPLY is armed right after the check, whatever the oracle found");
            Assert.IsFalse(editor.Substring(0, at).Split('\n').Last().Contains("if"), "the oracle call is not under a condition that could skip arming");
        }

        [Test]
        public void Check_RunsWithoutTheEditorHook()
        {
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), HookFile)), "OracleMapHook is gone");
            string src = Read(CheckFile);
            Assert.IsFalse(src.Contains("MapProvider"), "no Editor-registered map seam");
            Assert.IsFalse(src.Contains("Resources.Load"), "the map arrives through the serialized reference");

            var before = _book.Hash;
            var check = _book.CheckOracle(SpeedyMoveSpeed(1.5f));

            Assert.IsTrue(check.Relevant);
            Assert.IsTrue(check.Ran, check.Note);
            var rows = check.Rows();
            Assert.IsNotEmpty(rows);
            var speedy = rows.Where(r => r.Label.StartsWith("Speedy", StringComparison.Ordinal)).ToList();
            Assert.IsNotEmpty(speedy);
            Assert.IsTrue(speedy.Any(r => r.Change > 0f), "a faster Speedy banks more: " + string.Join(", ", speedy.Select(r => $"{r.Label} {r.Before}->{r.After}")));
            foreach (var row in rows.Where(r => !r.Label.StartsWith("Speedy", StringComparison.Ordinal)))
                Assert.AreEqual(row.Before, row.After, 1e-3f, $"{row.Label} is not touched by a Speedy stat change");
            Assert.AreEqual(before, _book.Hash, "the check ran on copies");
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
        }

        [Test]
        public void IrrelevantVerdict_SaysSoAndRunsNothing()
        {
            var check = _book.CheckOracle(_book.Evaluate(SnatchDelta, "test"));

            Assert.IsFalse(check.Relevant);
            Assert.IsFalse(check.Ran);
            Assert.IsNull(check.Before);
            Assert.IsNull(check.After);
            Assert.IsNotEmpty(check.Note);
            StringAssert.Contains("not needed", DevBalanceEditor.OracleText(check));
        }

        [Test]
        public void WithoutAMap_TheCheckSaysSo_AndDoesNotThrow()
        {
            using (var book = new BalanceBook(new QuietLog(), _abilities, _classes, _match))
            {
                var speedy = _classes.TryGet(ChickenClass.Speedy, out var e) ? e.Stats : null;
                var verdict = book.Evaluate("{\"schema\":1,\"assets\":{\"" + speedy.name + "\":{\"MoveSpeed\":8}}}", "test");
                var check = book.CheckOracle(verdict);
                Assert.IsTrue(check.Relevant);
                Assert.IsFalse(check.Ran);
                StringAssert.Contains("map", check.Note);
                StringAssert.Contains("not affected", DevBalanceEditor.OracleText(check));
            }
        }

        [Test]
        public void OracleText_ListsEveryKitBeforeAndAfter_AndWhatIsNotModelled()
        {
            var check = _book.CheckOracle(SpeedyMoveSpeed(1.5f));
            Assert.IsTrue(check.Ran, check.Note);

            string text = DevBalanceEditor.OracleText(check);

            foreach (var row in check.Rows()) StringAssert.Contains(row.Label, text);
            StringAssert.Contains("->", text);
            StringAssert.Contains("does not model", text);
            foreach (string item in check.After.NotModelled) StringAssert.Contains(item, text);
        }
    }
}
