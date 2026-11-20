using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Services;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The local half of the balance layer (dev-tools balance editor, owner ruling 2026-11-18): the Local origin, the gate as a
    /// pure function, the field list, the delta builder (only values that differ from shipped, enums as names, hash parity with
    /// the publish form), the stored override (write, read, boot apply, reset, window closed).
    /// Real registries and assets, as in BalanceBookTests; TearDown restores and compares every asset to its shipped JSON.
    /// </summary>
    public sealed class BalanceLocalTests
    {
        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Warns = new();
            public readonly List<string> Errors = new();
            public readonly List<string> Infos = new();
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) => Infos.Add(message);
            public void Warn(string source, string message) => Warns.Add(message);
            public void Error(string source, string message, Exception exception = null) => Errors.Add(message);
        }

        private sealed class FakeSource : IBalanceSource
        {
            public BalanceFetchResult Result;
            public string Name => "fake";
            public Task<BalanceFetchResult> FetchAsync(CancellationToken ct) => Task.FromResult(Result);
        }

        private const string SnatchDelta = "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.25}}}";

        private RecordingLog _log;
        private BalanceBook _book;
        private AbilityRegistrySO _registry;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private Dictionary<ScriptableObject, string> _shipped;
        private string _tempDir;
        private AbilityBaseSO Ability(string name) => _registry.All.First(a => a != null && a.name == name);
        private ChickenStatsSO Stats(ChickenClass cls) => _classes.TryGet(cls, out var e) ? e.Stats : null;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLog();
            _registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var all = new List<ScriptableObject>();
            foreach (var a in _registry.All) if (a != null) all.Add(a);
            foreach (ChickenClass c in Enum.GetValues(typeof(ChickenClass))) if (Stats(c) != null) all.Add(Stats(c));
            all.Add(_match);
            _shipped = all.Distinct().ToDictionary(so => so, so => JsonUtility.ToJson(so));
            _book = new BalanceBook(_log, _registry, _classes, _match);
        }

        [TearDown]
        public void TearDown()
        {
            _book?.Dispose();
            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            _tempDir = null;
            foreach (var kv in _shipped)
                Assert.AreEqual(kv.Value, JsonUtility.ToJson(kv.Key), $"{kv.Key.name} was left modified");
        }

        private string TempRoot()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cw-balance-local-" + Guid.NewGuid().ToString("N"));
            return _tempDir;
        }

        // ---- The gate -----------------------------------------------------------------------------------------

        [Test]
        public void LocalAllowed_IsExactlyTheDevToolsAnswer()
        {
            Assert.IsTrue(BalanceBook.LocalAllowed(true));
            Assert.IsFalse(BalanceBook.LocalAllowed(false));
        }

        [Test]
        public void TheLocalOriginIsAppended_AfterTheThreeThatExisted()
        {
            Assert.AreEqual(0, (int)BalanceOrigin.Baseline);
            Assert.AreEqual(1, (int)BalanceOrigin.Cache);
            Assert.AreEqual(2, (int)BalanceOrigin.Remote);
            Assert.AreEqual(3, (int)BalanceOrigin.Local);
        }

        [Test]
        public void TheStore_IsInertWithoutDevTools_AndWorksWithThem()
        {
            var store = new BalanceLocalStore(TempRoot());
            store.Write(SnatchDelta, true);
            Assert.AreEqual(SnatchDelta, store.Read(true));

            Assert.IsNull(store.Read(false), "a production build never reads the file, even when one is on the device");
            Assert.Throws<InvalidOperationException>(() => store.Write(SnatchDelta, false));
            Assert.Throws<InvalidOperationException>(() => store.Clear(false));
            Assert.IsTrue(File.Exists(store.Path), "the refused clear left the file alone");

            store.Clear(true);
            Assert.IsFalse(File.Exists(store.Path));
            Assert.IsNull(store.Read(true));
        }

        [Test]
        public void TheStore_BlankWriteDeletes_AndLeavesNoTempFile()
        {
            var store = new BalanceLocalStore(TempRoot());
            store.Write(SnatchDelta, true);
            store.Write("  ", true);
            Assert.IsFalse(File.Exists(store.Path));
            Assert.IsFalse(File.Exists(store.Path + ".tmp"));
        }

        // ---- The Local origin ---------------------------------------------------------------------------------

        [Test]
        public void TryApplyLocal_AppliesWithTheLocalOrigin_AndRestores()
        {
            var snatch = (SnatchAbilitySO)Ability("Snatch");
            float shipped = snatch.Cooldown;
            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out var verdict), string.Join(" | ", _log.Errors));
            Assert.IsTrue(verdict.IsAccepted);
            Assert.AreEqual(3.25f, snatch.Cooldown);
            Assert.AreEqual(BalanceOrigin.Local, _book.Origin);
            Assert.AreEqual(verdict.Hash, _book.Hash);

            _book.RestoreBaseline();
            Assert.AreEqual(shipped, snatch.Cooldown);
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
        }

        [Test]
        public void TryApplyLocal_UsesTheSameValidation_AsEvaluate()
        {
            Assert.IsFalse(_book.TryApplyLocal("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":-1}}}", out var verdict));
            Assert.AreEqual(BalanceVerdictKind.Refused, verdict.Kind);
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
        }

        [Test]
        public void TryApplyLocal_NeverAppliesMidMatch()
        {
            _book.CloseApplyWindow("test");
            Assert.IsFalse(_book.TryApplyLocal(SnatchDelta, out _));
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
        }

        // ---- The field list and the shipped helper --------------------------------------------------------------

        [Test]
        public void DescribeFields_ListsOnlyWhatADeltaMayOverride()
        {
            var fields = _book.DescribeFields();
            var snatchCooldown = fields.First(f => f.Asset == "Snatch" && f.Field == "Cooldown");
            Assert.AreEqual(BalanceFieldKind.Number, snatchCooldown.Kind);
            Assert.AreEqual("Ability", snatchCooldown.Group);
            Assert.AreEqual(snatchCooldown.Shipped, snatchCooldown.Current);

            Assert.IsTrue(fields.Any(f => f.Asset == "MatchConfig" && f.Field == "FoodTargetToWin" && f.Kind == BalanceFieldKind.Whole));
            var areaKind = fields.First(f => f.Asset == "FeatherAura" && f.Field == "AreaKind");
            Assert.AreEqual(BalanceFieldKind.Enum, areaKind.Kind);
            CollectionAssert.Contains(areaKind.EnumNames, "StaticAoE");

            foreach (var names in BalanceBook.DeniedFields.Values)
                foreach (string denied in names)
                    Assert.IsFalse(fields.Any(f => f.Field == denied), $"{denied} is deny-listed and must not be listed");
        }

        [Test]
        public void DescribeFields_CurrentFollowsTheAppliedDelta_ShippedDoesNot()
        {
            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out _));
            var f = _book.DescribeFields().First(x => x.Asset == "Snatch" && x.Field == "Cooldown");
            Assert.AreEqual("3.25", f.Current);
            Assert.AreNotEqual("3.25", f.Shipped);
        }

        [Test]
        public void TryGetShippedToken_ReadsTheSnapshotWhenTheFieldIsTouched_ElseTheLiveValue()
        {
            var snatch = (SnatchAbilitySO)Ability("Snatch");
            float shipped = snatch.Cooldown;

            Assert.IsTrue(_book.TryGetShippedToken("Snatch", "Cooldown", out string untouched));
            Assert.AreEqual(shipped, float.Parse(untouched, System.Globalization.CultureInfo.InvariantCulture));

            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out _));
            Assert.IsTrue(_book.TryGetShippedToken("Snatch", "Cooldown", out string touched));
            Assert.AreEqual(shipped, float.Parse(touched, System.Globalization.CultureInfo.InvariantCulture), "the shipped value, not 3.25");

            Assert.IsFalse(_book.TryGetShippedToken("Snatch", "NoSuchField", out _));
            Assert.IsFalse(_book.TryGetShippedToken("NoSuchAsset", "Cooldown", out _));
        }

        // ---- The delta builder ----------------------------------------------------------------------------------

        [Test]
        public void TryBuildDeltaJson_KeepsOnlyValuesThatDifferFromShipped()
        {
            Assert.IsTrue(_book.TryGetShippedToken("Snatch", "Cooldown", out string shipped));
            Assert.IsTrue(_book.TryBuildDeltaJson(new[]
            {
                new BalanceEdit("Snatch", "Cooldown", shipped),       // equal to shipped: pruned
                new BalanceEdit("MatchConfig", "FoodTargetToWin", "55"),
            }, out string json, out var problems), string.Join(" | ", problems));
            Assert.AreEqual("{\"assets\":{\"MatchConfig\":{\"FoodTargetToWin\":55}},\"schema\":1}", json);

            var verdict = _book.Evaluate(json, "test");
            Assert.IsTrue(verdict.IsAccepted);
            Assert.AreEqual(1, verdict.Changes.Count);
        }

        [Test]
        public void TryBuildDeltaJson_NothingDifferent_IsTheEmptyDelta()
        {
            Assert.IsTrue(_book.TryBuildDeltaJson(Array.Empty<BalanceEdit>(), out string json, out _));
            Assert.AreEqual("{\"schema\":1}", json);
            Assert.IsTrue(_book.Evaluate(json, "test").IsEmpty);
        }

        [Test]
        public void TryBuildDeltaJson_StartsFromTheDeltaInForce_AndAnEditBackToShippedLeavesIt()
        {
            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out _));

            Assert.IsTrue(_book.TryBuildDeltaJson(new[] { new BalanceEdit("MatchConfig", "FoodTargetToWin", "55") }, out string both, out _));
            StringAssert.Contains("\"Snatch\":{\"Cooldown\":3.25}", both, "an untouched applied value is carried over");
            StringAssert.Contains("\"FoodTargetToWin\":55", both);

            Assert.IsTrue(_book.TryGetShippedToken("Snatch", "Cooldown", out string shipped));
            Assert.IsTrue(_book.TryBuildDeltaJson(new[] { new BalanceEdit("Snatch", "Cooldown", shipped) }, out string back, out _));
            Assert.AreEqual("{\"schema\":1}", back, "editing a value back to shipped removes it from the delta");
        }

        [Test]
        public void TryBuildDeltaJson_ReportsBadTextAndUnknownFields_NeverBuildsAHalfDelta()
        {
            Assert.IsFalse(_book.TryBuildDeltaJson(new[]
            {
                new BalanceEdit("Snatch", "Cooldown", "abc"),
                new BalanceEdit("Snatch", "NoSuchField", "1"),
                new BalanceEdit("MatchConfig", "FoodTargetToWin", "1.5"),
                new BalanceEdit("MatchConfig", "MaxPlayers", "9"),      // deny-listed
                new BalanceEdit("NoSuchAsset", "X", "1"),
            }, out string json, out var problems));
            Assert.IsNull(json);
            Assert.AreEqual(5, problems.Count, string.Join(" | ", problems));
        }

        [Test]
        public void TryBuildDeltaJson_WritesEnumsAsNames_AndThePublishFormHashesLikeTheCanonicalOne()
        {
            Assert.IsTrue(_book.TryBuildDeltaJson(new[]
            {
                new BalanceEdit("FeatherAura", "AreaKind", "StaticAoE"),
                new BalanceEdit("Snatch", "Cooldown", "3.25"),
            }, out string json, out var problems), string.Join(" | ", problems));
            StringAssert.Contains("\"AreaKind\":\"StaticAoE\"", json);

            Assert.IsTrue(_book.TryApplyLocal(json, out var verdict), string.Join(" | ", _log.Errors));
            StringAssert.Contains("\"AreaKind\":1", _book.Canonical, "the app's canonical form carries the number");
            StringAssert.Contains("\"AreaKind\":\"StaticAoE\"", _book.PublishJson, "the publish form carries the name make_delta.py accepts");

            var again = _book.Evaluate(_book.PublishJson, "test");
            Assert.IsTrue(again.IsAccepted);
            Assert.AreEqual(_book.Hash, again.Hash, "bal= is the same whichever form is published");
            Assert.AreEqual(_book.Canonical, again.Canonical);
            Assert.AreEqual(verdict.Hash, _book.Hash);
        }

        [Test]
        public void PublishJson_IsTheCanonicalFormWithoutEnums_AndEmptyIsBraces()
        {
            Assert.AreEqual("{}", _book.PublishJson);
            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out _));
            Assert.AreEqual(_book.Canonical, _book.PublishJson, "no enum field: both forms are the same text");
            Assert.AreEqual("{\"assets\":{\"Snatch\":{\"Cooldown\":3.25}},\"schema\":1}", _book.PublishJson);
        }

        // ---- The stored override ----------------------------------------------------------------------------------

        private BalanceLocalOverride NewOverride(BalanceBook book, BalanceCache cache = null) =>
            new BalanceLocalOverride(book, new BalanceLocalStore(_tempDir ?? TempRoot()), cache, _log);

        [Test]
        public void Override_Apply_StoresThePublishForm_AndAppliesNow()
        {
            var local = NewOverride(_book);
            var result = local.Apply(SnatchDelta);
            Assert.AreEqual(LocalOverrideOutcome.Applied, result.Outcome, result.Reason);
            Assert.AreEqual(BalanceOrigin.Local, _book.Origin);
            Assert.AreEqual(_book.PublishJson, File.ReadAllText(local.StorePath));
            Assert.IsTrue(local.InForce);
        }

        [Test]
        public void Override_BootApply_ReadsTheStoredFile_OnAFreshBook()
        {
            var local = NewOverride(_book);
            Assert.AreEqual(LocalOverrideOutcome.Applied, local.Apply(SnatchDelta).Outcome);
            string hash = _book.Hash;
            _book.RestoreBaseline();

            using (var next = new BalanceBook(_log, _registry, _classes, _match))
            {
                // The new book is now Live, as at a real boot; it restores on dispose.
                NewOverride(next).ApplyAtBoot();
                Assert.AreEqual(BalanceOrigin.Local, next.Origin);
                Assert.AreEqual(hash, next.Hash);
                Assert.AreEqual(3.25f, Ability("Snatch").Cooldown);
            }
        }

        [Test]
        public void Override_BootApply_AnInvalidFile_IsWarnedAndIgnored()
        {
            var store = new BalanceLocalStore(TempRoot());
            store.Write("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":-5}}}", true);
            new BalanceLocalOverride(_book, store, null, _log).ApplyAtBoot();
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
            Assert.IsTrue(_log.Warns.Any(w => w.Contains("not applied")), string.Join(" | ", _log.Warns));
        }

        [Test]
        public void Override_WindowClosed_StoresForNextLaunch_AndDoesNotChangeTheRunningBalance()
        {
            var local = NewOverride(_book);
            _book.CloseApplyWindow("test");
            var result = local.Apply(SnatchDelta);
            Assert.AreEqual(LocalOverrideOutcome.StoredForNextLaunch, result.Outcome);
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
            Assert.IsTrue(File.Exists(local.StorePath), "the file is there for the next launch");
        }

        [Test]
        public void Override_Reset_WindowOpen_PutsTheCachedRemoteDeltaBack_AndDeletesTheFile()
        {
            var cache = new BalanceCache(TempRoot(), "dev");
            cache.Write("{\"schema\":1,\"assets\":{\"MatchConfig\":{\"FoodTargetToWin\":55}}}");
            BalanceRefresher.ApplyCached(_book, cache, _log);
            Assert.AreEqual(BalanceOrigin.Cache, _book.Origin);

            var local = NewOverride(_book, cache);
            Assert.AreEqual(LocalOverrideOutcome.Applied, local.Apply(SnatchDelta).Outcome);
            Assert.AreNotEqual(55, _match.FoodTargetToWin, "the local delta replaced the cached one: deltas never stack");

            var result = local.Reset();
            Assert.AreEqual(LocalOverrideOutcome.Cleared, result.Outcome);
            Assert.AreEqual(BalanceOrigin.Cache, _book.Origin);
            Assert.AreEqual(55, _match.FoodTargetToWin);
            Assert.IsFalse(File.Exists(local.StorePath));
        }

        [Test]
        public void Override_Reset_WindowOpen_RestoresTheShippedAssets_WhenThereIsNoCache()
        {
            var local = NewOverride(_book);
            Assert.AreEqual(LocalOverrideOutcome.Applied, local.Apply(SnatchDelta).Outcome);
            var result = local.Reset();
            Assert.AreEqual(LocalOverrideOutcome.Cleared, result.Outcome);
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
            Assert.IsFalse(File.Exists(local.StorePath));
        }

        [Test]
        public void Override_Reset_WindowClosed_DeletesTheFile_AndKeepsTheRunningBalanceUntilNextLaunch()
        {
            var local = NewOverride(_book);
            Assert.AreEqual(LocalOverrideOutcome.Applied, local.Apply(SnatchDelta).Outcome);
            _book.CloseApplyWindow("test");
            var result = local.Reset();
            Assert.AreEqual(LocalOverrideOutcome.ClearedNextLaunch, result.Outcome);
            Assert.AreEqual(BalanceOrigin.Local, _book.Origin, "never mid-match: the running numbers stay");
            Assert.IsFalse(File.Exists(local.StorePath));
        }

        [Test]
        public void Override_AnEmptyDelta_IsAReset()
        {
            var local = NewOverride(_book);
            Assert.AreEqual(LocalOverrideOutcome.Applied, local.Apply(SnatchDelta).Outcome);
            Assert.AreEqual(LocalOverrideOutcome.Cleared, local.Apply("{\"schema\":1}").Outcome);
            Assert.IsFalse(File.Exists(local.StorePath));
        }

        [Test]
        public void Override_ARefusedDelta_ChangesAndStoresNothing()
        {
            var local = NewOverride(_book);
            var result = local.Apply("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":-1}}}");
            Assert.AreEqual(LocalOverrideOutcome.Refused, result.Outcome);
            Assert.IsFalse(File.Exists(local.StorePath));
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
        }

        // ---- The refresher yields to a local override ---------------------------------------------------------------

        [Test]
        public void Refresher_WhileALocalOverrideIsInForce_CachesTheRemoteDeltaButDoesNotApplyIt()
        {
            var cache = new BalanceCache(TempRoot(), "dev");
            Assert.IsTrue(_book.TryApplyLocal(SnatchDelta, out _));
            string hash = _book.Hash;

            const string remote = "{\"schema\":1,\"assets\":{\"MatchConfig\":{\"FoodTargetToWin\":55}}}";
            var refresher = new BalanceRefresher(_book, new FakeSource { Result = BalanceFetchResult.Delta(remote) }, _log, cache);
            refresher.Initialize();
            refresher.Dispose();

            Assert.AreEqual(BalanceOrigin.Local, _book.Origin);
            Assert.AreEqual(hash, _book.Hash);
            Assert.AreEqual(remote, cache.Read(), "the remote delta is cached for Reset and the next launch");
            Assert.IsTrue(_log.Infos.Any(i => i.Contains("NOT applied") && i.Contains("local override")), string.Join(" | ", _log.Infos));
        }
    }
}
