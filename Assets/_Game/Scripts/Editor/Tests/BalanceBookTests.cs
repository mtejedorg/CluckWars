using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Logging;
using CluckWars.Services;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The live-ops balance layer (BalanceBook, owner rulings 2026-11-17): a server-published delta overrides numbers on the
    /// REAL assets, all or nothing, never stacking, and always leaves the assets exactly as shipped afterwards. Every test
    /// builds the book over the real registries and disposes it in TearDown, which restores, so a failing test cannot leave
    /// an asset modified; TearDown then checks that against the snapshot taken in SetUp.
    /// </summary>
    public sealed class BalanceBookTests
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

        private RecordingLog _log;
        private BalanceBook _book;
        private AbilityRegistrySO _registry;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private Dictionary<ScriptableObject, string> _shipped;
        private Dictionary<PassiveAbilitySO, AbilityBaseSO[]> _shippedKits;
        private string _tempDir;

        private AbilityBaseSO Ability(string name) => _registry.All.First(a => a != null && a.name == name);
        private PassiveAbilitySO Passive(string name) => (PassiveAbilitySO)Ability(name);
        private ChickenStatsSO Stats(ChickenClass cls) => _classes.TryGet(cls, out var e) ? e.Stats : null;

        private IEnumerable<ScriptableObject> AllAddressable()
        {
            foreach (var a in _registry.All) if (a != null) yield return a;
            foreach (ChickenClass c in Enum.GetValues(typeof(ChickenClass))) if (Stats(c) != null) yield return Stats(c);
            yield return _match;
        }

        [SetUp]
        public void SetUp()
        {
            UiText.Reset();
            _log = new RecordingLog();
            _registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _shipped = AllAddressable().Distinct().ToDictionary(so => so, so => JsonUtility.ToJson(so));
            _shippedKits = _registry.Passives.ToDictionary(p => p, p => (AbilityBaseSO[])p.Kit.Clone());
            _book = new BalanceBook(_log, _registry, _classes, _match);
        }

        [TearDown]
        public void TearDown()
        {
            _book?.Dispose();
            UiText.Reset();
            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            _tempDir = null;
            // Belt and braces: whatever the test did, the real assets read as shipped.
            foreach (var kv in _shipped)
                Assert.AreEqual(kv.Value, JsonUtility.ToJson(kv.Key), $"{kv.Key.name} was left modified");
            foreach (var kv in _shippedKits)
                CollectionAssert.AreEqual(kv.Value, kv.Key.Kit, $"{kv.Key.name}'s kit was left modified");
        }

        private void AssertAllShipped()
        {
            foreach (var kv in _shipped) Assert.AreEqual(kv.Value, JsonUtility.ToJson(kv.Key), $"{kv.Key.name} changed");
            foreach (var kv in _shippedKits) CollectionAssert.AreEqual(kv.Value, kv.Key.Kit, $"{kv.Key.name}'s kit changed");
        }

        private bool Apply(string json) => _book.TryApply(json, BalanceOrigin.Remote);

        // ---- Round trips -----------------------------------------------------------------------------------------

        [Test]
        public void FloatIntEnumAndKit_Override_AndRestore()
        {
            var snatch = Ability("Snatch");
            var slowArea = Ability("FeatherAura"); // the Slow Area: Aura or placed zone, chosen by AreaKind
            var bully = Passive("Bully");
            float cooldown = snatch.Cooldown;
            int target = _match.FoodTargetToWin;

            Assert.IsTrue(Apply(
                "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.25},\"MatchConfig\":{\"FoodTargetToWin\":55}," +
                "\"FeatherAura\":{\"AreaKind\":1}},\"kits\":{\"Bully\":[\"Peck\",\"Snatch\",\"WingSlam\",\"SpeedBurst\"]}}"),
                string.Join(" | ", _log.Errors));

            Assert.AreEqual(3.25f, snatch.Cooldown);
            Assert.AreEqual(55, _match.FoodTargetToWin);
            Assert.AreEqual(AreaEffectKind.StaticAoE, slowArea.AreaKind);
            Assert.AreEqual(Ability("SpeedBurst"), bully.Kit[3]);
            Assert.AreEqual(BalanceOrigin.Remote, _book.Origin);
            Assert.AreNotEqual(BalanceBook.BaselineHash, _book.Hash);
            Assert.AreEqual(4, _book.Applied.Count);

            _book.RestoreBaseline();
            Assert.AreEqual(cooldown, snatch.Cooldown);
            Assert.AreEqual(target, _match.FoodTargetToWin);
            Assert.AreEqual(BalanceBook.BaselineHash, _book.Hash);
            Assert.AreEqual(BalanceBook.BaselineLabel, _book.Label);
            AssertAllShipped();
            Assert.IsEmpty(_log.Errors);
        }

        [Test]
        public void ClassStats_AreAddressedByAssetName_AndAnEnumMayBeNamed()
        {
            var speedy = Stats(ChickenClass.Speedy);
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"" + speedy.name + "\":{\"MoveSpeed\":13.5},\"FeatherAura\":{\"AreaKind\":\"StaticAoE\"}}}"),
                string.Join(" | ", _log.Errors));
            Assert.AreEqual(13.5f, speedy.MoveSpeed);
            Assert.AreEqual(AreaEffectKind.StaticAoE, Ability("FeatherAura").AreaKind);
        }

        [Test]
        public void SnapshotRestore_LeavesTheAssetFileBytesUnchanged_AndNotDirty()
        {
            var slow = Ability("Slow");
            string path = AssetDatabase.GetAssetPath(slow);
            byte[] before = File.ReadAllBytes(path);
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"Slow\":{\"Cooldown\":9,\"SlowFactor\":0.4}}}"));
            Assert.IsFalse(EditorUtility.IsDirty(slow), "applying a delta must not dirty the asset");
            _book.Dispose();
            _book = null;
            Assert.AreEqual(_shipped[slow], JsonUtility.ToJson(slow));
            Assert.IsFalse(EditorUtility.IsDirty(slow));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "the .asset file changed on disk");
        }

        [Test]
        public void TextFollowsTheValue_AndTheDescriptionFieldIsUntouched()
        {
            var slow = (SlowAbilitySO)Ability("Slow");
            string rawDescription = slow.Description;
            string before = slow.DescriptionText;
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"Slow\":{\"SlowSeconds\":4.5}}}"));
            string after = slow.DescriptionText;
            Assert.AreNotEqual(before, after);
            StringAssert.Contains("4.5", after);
            Assert.AreEqual(rawDescription, slow.Description, "the asset's Description field must not be rewritten");
        }

        // ---- Ignored, not thrown ---------------------------------------------------------------------------------

        [Test]
        public void UnknownAssetAndField_AreReportedAndIgnored_TheRestApplies()
        {
            Assert.IsTrue(Apply("{\"schema\":1,\"note\":\"hi\",\"extra\":1,\"assets\":{\"NoSuchAsset\":{\"Cooldown\":1}," +
                                "\"Snatch\":{\"NoSuchField\":2,\"Cooldown\":5.5,\"Description\":\"x\"}}}"));
            Assert.AreEqual(5.5f, Ability("Snatch").Cooldown);
            Assert.AreEqual(4, _book.Ignored.Count, string.Join(" | ", _book.Ignored));
            Assert.AreEqual(4, _log.Warns.Count);
            Assert.IsEmpty(_log.Errors);
        }

        [Test]
        public void DenyListedFields_AreIgnored()
        {
            int maxPlayers = _match.MaxPlayers;
            var mode = _match.TargetingMode;
            var classes = Ability("Snatch").AllowedClasses;
            var verdict = _book.Evaluate("{\"schema\":1,\"assets\":{\"MatchConfig\":{\"MaxPlayers\":8,\"TargetingMode\":1,\"LoadoutMode\":1}," +
                                         "\"Snatch\":{\"AllowedClasses\":1},\"Thief\":{\"Subclass\":1}}}", "t");
            Assert.IsTrue(verdict.IsAccepted);
            Assert.IsTrue(_book.Apply(verdict, BalanceOrigin.Remote));
            Assert.AreEqual(maxPlayers, _match.MaxPlayers);
            Assert.AreEqual(mode, _match.TargetingMode);
            Assert.AreEqual(classes, Ability("Snatch").AllowedClasses);
            Assert.AreEqual(5, verdict.Ignored.Count, string.Join(" | ", verdict.Ignored));
            Assert.AreEqual(BalanceBook.BaselineHash, _book.Hash, "a delta with nothing accepted is the baseline");
        }

        // ---- Whole-delta refusal ---------------------------------------------------------------------------------

        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":NaN}}}", TestName = "Refused_NaNLiteral")]
        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":1e400}}}", TestName = "Refused_Infinite")]
        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":-1}}}", TestName = "Refused_NegativeCooldown")]
        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":4000}}}", TestName = "Refused_AbsurdValue")]
        [TestCase("{\"schema\":1,\"assets\":{\"MatchConfig\":{\"FoodTargetToWin\":2.5}}}", TestName = "Refused_FractionForAnInt")]
        [TestCase("{\"schema\":1,\"assets\":{\"MatchConfig\":{\"MatchDurationSeconds\":10}}}", TestName = "Refused_BelowTheFieldsMin")]
        [TestCase("{\"schema\":1,\"assets\":{\"FeatherAura\":{\"AreaKind\":9}}}", TestName = "Refused_UndefinedEnum")]
        [TestCase("{\"schema\":1,\"assets\":{\"FeatherAura\":{\"AreaKind\":\"1\"}}}", TestName = "Refused_NumericStringForAnEnum")]
        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":\"3\"}}}", TestName = "Refused_StringForAFloat")]
        [TestCase("{\"schema\":1,\"kits\":{\"Bully\":[\"Snatch\",\"WingSlam\",\"Shadowstep\",\"SpeedBurst\"]}}", TestName = "Refused_KitWithoutItsPeck")]
        [TestCase("{\"schema\":1,\"kits\":{\"Bully\":[\"Peck\",\"Snatch\",\"WingSlam\",\"Nope\"]}}", TestName = "Refused_KitUnknownAbility")]
        [TestCase("{\"schema\":1,\"kits\":{\"Bully\":[\"Peck\",\"Snatch\",\"WingSlam\",\"Thief\"]}}", TestName = "Refused_KitHoldsAPassive")]
        [TestCase("{\"schema\":1,\"kits\":{\"Bully\":[\"Peck\",\"Snatch\",\"WingSlam\"]}}", TestName = "Refused_KitWrongLength")]
        [TestCase("{\"schema\":1,\"kits\":{\"Snatch\":[\"Peck\",\"Snatch\",\"WingSlam\",\"SpeedBurst\"]}}", TestName = "Refused_KitOwnerNotAPassive")]
        [TestCase("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":1,\"Cooldown\":2}}}", TestName = "Refused_DuplicateKey")]
        [TestCase("{\"schema\":1.5}", TestName = "Refused_FractionalSchema")]
        [TestCase("[1,2]", TestName = "Refused_NotAnObject")]
        public void ABadValue_RefusesTheWholeDelta_AndKeepsThePreviousState(string bad)
        {
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}"));
            string hash = _book.Hash;

            // A good value next to the bad one must not apply either.
            string json = bad.Contains("\"assets\":{")
                ? bad.Replace("\"assets\":{", "\"assets\":{\"Peck\":{\"Cooldown\":0.9},")
                : bad;
            Assert.IsFalse(Apply(json));
            Assert.AreEqual(hash, _book.Hash);
            Assert.AreEqual(3.5f, Ability("Snatch").Cooldown);
            Assert.AreEqual(_shipped[Ability("Peck")], JsonUtility.ToJson(Ability("Peck")));
            CollectionAssert.AreEqual(_shippedKits[Passive("Bully")], Passive("Bully").Kit);
            Assert.AreEqual(1, _log.Errors.Count, string.Join(" | ", _log.Errors));
            Assert.IsNotNull(_book.LastRefusal);
        }

        // ---- Schema gate -----------------------------------------------------------------------------------------

        [TestCase("{\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}", TestName = "SchemaMissing_IgnoredWhole")]
        [TestCase("{\"schema\":2,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}", TestName = "SchemaNewer_IgnoredWhole")]
        public void SchemaGate_IgnoresTheWholeDeltaWithAWarn(string json)
        {
            float cooldown = Ability("Snatch").Cooldown;
            Assert.IsFalse(Apply(json));
            Assert.AreEqual(cooldown, Ability("Snatch").Cooldown);
            Assert.AreEqual(BalanceBook.BaselineHash, _book.Hash);
            Assert.IsEmpty(_log.Errors);
            Assert.AreEqual(1, _log.Warns.Count);
        }

        // ---- Hash, stacking, null source ------------------------------------------------------------------------

        [Test]
        public void Hash_IsStable_AndIndependentOfKeyOrderAndNumberSpelling()
        {
            var a = _book.Evaluate("{\"schema\":1,\"note\":\"one\",\"assets\":{\"Snatch\":{\"Cooldown\":3.50,\"StealAmount\":6},\"Peck\":{\"Cooldown\":1}}}", "t");
            var b = _book.Evaluate("{\"assets\":{\"Peck\":{\"Cooldown\":1.0}, \"Snatch\":{\"StealAmount\":6.0,\"Cooldown\":3.5}},\"note\":\"two\",\"schema\":1}", "t");
            Assert.IsTrue(a.IsAccepted && b.IsAccepted);
            Assert.AreEqual(a.Canonical, b.Canonical);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual("{\"assets\":{\"Peck\":{\"Cooldown\":1},\"Snatch\":{\"Cooldown\":3.5,\"StealAmount\":6}},\"schema\":1}", a.Canonical);
            Assert.AreEqual(BalanceBook.HashOf(a.Canonical), a.Hash);
            StringAssert.IsMatch("^[0-9a-f]{8}$", a.Hash);
            Assert.AreEqual("5465b825", BalanceBook.BaselineHash, "docs quote this value (Session line bal= of the shipped assets)");
            Assert.AreEqual(BalanceBook.HashOf("{}"), BalanceBook.BaselineHash);
            Assert.AreEqual("811c9dc5", BalanceBook.HashOf(""), "FNV-1a 32 offset basis");
        }

        [Test]
        public void ReApplying_RestoresFirst_SoDeltasNeverStack()
        {
            var snatch = (SnatchAbilitySO)Ability("Snatch");
            float cooldown = snatch.Cooldown;
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}"));
            Assert.IsTrue(Apply("{\"schema\":1,\"assets\":{\"Snatch\":{\"StealAmount\":7}}}"));
            Assert.AreEqual(cooldown, snatch.Cooldown, "the first delta's cooldown must be gone");
            Assert.AreEqual(7f, snatch.StealAmount);
        }

        [Test]
        public void NullBalanceSource_PublishesNothing_AndChangesNothing()
        {
            var result = new NullBalanceSource().FetchAsync(CancellationToken.None).Result;
            Assert.IsTrue(result.Ok);
            Assert.IsFalse(result.HasDelta);
            Assert.IsTrue(_book.TryApply(result.Json, BalanceOrigin.Remote));
            Assert.AreEqual(BalanceBook.BaselineHash, _book.Hash);
            Assert.AreEqual(BalanceOrigin.Baseline, _book.Origin);
            AssertAllShipped();
        }

        [Test]
        public void DuplicateAssetNames_RefuseTheWholeDelta()
        {
            var impostor = ScriptableObject.CreateInstance<SnatchAbilitySO>();
            impostor.name = "Snatch";
            var registry = ScriptableObject.CreateInstance<AbilityRegistrySO>();
            registry.All = new AbilityBaseSO[] { Ability("Snatch"), Ability("Peck"), impostor };
            var book = new BalanceBook(_log, registry, _classes, _match);
            try
            {
                Assert.IsFalse(book.TryApply("{\"schema\":1,\"assets\":{\"Peck\":{\"Cooldown\":0.9}}}", BalanceOrigin.Remote));
                StringAssert.Contains("Snatch", book.LastRefusal);
                AssertAllShipped();
            }
            finally
            {
                book.Dispose();
                UnityEngine.Object.DestroyImmediate(impostor);
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        // ---- Apply window, refresher, cache ---------------------------------------------------------------------

        [Test]
        public void AfterTheWindowCloses_NothingApplies()
        {
            _book.CloseApplyWindow("test");
            Assert.IsFalse(Apply("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}"));
            AssertAllShipped();
        }

        private BalanceCache TempCache()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cw-balance-" + Guid.NewGuid().ToString("N"));
            return new BalanceCache(_tempDir, "dev");
        }

        [Test]
        public void Refresher_AppliesANewDelta_AndCachesIt_ThenTheCacheAppliesAtBoot()
        {
            var cache = TempCache();
            const string json = "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}";
            var source = new FakeSource { Result = BalanceFetchResult.Delta(json) };
            var refresher = new BalanceRefresher(_book, source, _log, cache);
            refresher.Initialize(); // the fake answers synchronously, so the whole refresh runs here
            refresher.Dispose();
            Assert.AreEqual(3.5f, Ability("Snatch").Cooldown);
            Assert.AreEqual(BalanceOrigin.Remote, _book.Origin);
            Assert.AreEqual(json, cache.Read());

            _book.RestoreBaseline();
            BalanceRefresher.ApplyCached(_book, cache, _log);
            Assert.AreEqual(3.5f, Ability("Snatch").Cooldown);
            Assert.AreEqual(BalanceOrigin.Cache, _book.Origin);
        }

        [Test]
        public void Refresher_AfterTheWindowCloses_OnlyCaches()
        {
            var cache = TempCache();
            const string json = "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}";
            _book.CloseApplyWindow("test");
            var refresher = new BalanceRefresher(_book, new FakeSource { Result = BalanceFetchResult.Delta(json) }, _log, cache);
            refresher.Initialize();
            refresher.Dispose();
            AssertAllShipped();
            Assert.AreEqual(json, cache.Read());
            Assert.IsTrue(_log.Infos.Any(m => m.Contains("next launch")));
        }

        [Test]
        public void Refresher_RefusedDeltaOrFailure_KeepsStateAndCache()
        {
            var cache = TempCache();
            const string good = "{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}";
            cache.Write(good);
            BalanceRefresher.ApplyCached(_book, cache, _log);
            string hash = _book.Hash;

            var refused = new BalanceRefresher(_book, new FakeSource { Result = BalanceFetchResult.Delta("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":-3}}}") }, _log, cache);
            refused.Initialize();
            refused.Dispose();
            var failed = new BalanceRefresher(_book, new FakeSource { Result = BalanceFetchResult.Failed("offline") }, _log, cache);
            failed.Initialize();
            failed.Dispose();

            Assert.AreEqual(hash, _book.Hash);
            Assert.AreEqual(BalanceOrigin.Cache, _book.Origin);
            Assert.AreEqual(good, cache.Read());
            Assert.IsTrue(_log.Warns.Any(m => m.Contains("offline")));
        }

        [Test]
        public void Refresher_NoDeltaPublished_RestoresBaseline_AndClearsTheCache()
        {
            var cache = TempCache();
            cache.Write("{\"schema\":1,\"assets\":{\"Snatch\":{\"Cooldown\":3.5}}}");
            BalanceRefresher.ApplyCached(_book, cache, _log);
            var refresher = new BalanceRefresher(_book, new FakeSource { Result = BalanceFetchResult.NoDelta() }, _log, cache);
            refresher.Initialize();
            refresher.Dispose();
            AssertAllShipped();
            Assert.AreEqual(BalanceBook.BaselineHash, _book.Hash);
            Assert.IsNull(cache.Read());
        }

        // ---- UGSService seams (source scans: the UGS singletons have no test seam) --------------------------------

        private static string Method(string src, string signature)
        {
            int start = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, signature);
            int next = src.IndexOf("\n        public ", start + signature.Length, StringComparison.Ordinal);
            return next < 0 ? src.Substring(start) : src.Substring(start, next - start);
        }

        [Test]
        public void UgsInitAndSignIn_AreSingleFlight()
        {
            string src = File.ReadAllText("Assets/_Game/Scripts/App/UGSService.cs");
            string init = Method(src, "public Task InitializeAsync()");
            StringAssert.Contains("_initTask == null || _initTask.IsCompleted", init, "concurrent callers share one init");
            StringAssert.Contains("return _initTask;", init);
            string signIn = Method(src, "public Task SignInAnonymouslyAsync()");
            StringAssert.Contains("_signInTask == null || _signInTask.IsCompleted", signIn, "concurrent callers share one sign-in");
            StringAssert.Contains("return _signInTask;", signIn);
        }

        [Test]
        public void TheLobbyHash_IsReadOnlyAfterTheApplyWindowCloses()
        {
            string src = File.ReadAllText("Assets/_Game/Scripts/App/UGSService.cs");
            string create = Method(src, "public async Task<LobbyInfo> CreateLobbyAsync(");
            int close = create.IndexOf("_balance.CloseApplyWindow(", StringComparison.Ordinal);
            Assert.GreaterOrEqual(close, 0, "create closes the window");
            Assert.Less(close, create.IndexOf("_balance.Hash", StringComparison.Ordinal), "before it reads the hash");
            int compare = src.IndexOf("private void CompareBalance(", StringComparison.Ordinal);
            string body = src.Substring(compare);
            Assert.Less(body.IndexOf("_balance.CloseApplyWindow(", StringComparison.Ordinal), body.IndexOf("_balance.Hash", StringComparison.Ordinal),
                "a join closes the window before it reads the hash");
            foreach (var join in new[] { "public async Task<LobbyInfo> JoinLobbyByCodeAsync(", "public async Task<LobbyInfo> JoinLobbyAsync(" })
                StringAssert.Contains("CompareBalance(", Method(src, join), join);
        }

        // ---- Source choice -------------------------------------------------------------------------------------

        [Test]
        public void SourceChoice_EditorOnlyWithTheToggle_PlayersAlways()
        {
            Assert.IsTrue(BalanceSourceChoice.Decide(isEditor: false, editorToggle: false));
            Assert.IsTrue(BalanceSourceChoice.Decide(isEditor: false, editorToggle: true));
            Assert.IsFalse(BalanceSourceChoice.Decide(isEditor: true, editorToggle: false));
            Assert.IsTrue(BalanceSourceChoice.Decide(isEditor: true, editorToggle: true));
        }

        [Test]
        public void Environment_DevWithDevTools_ProductionOtherwise()
        {
            Assert.AreEqual("dev", BalanceSourceChoice.EnvironmentFor(devToolsAvailable: true));
            Assert.AreEqual("production", BalanceSourceChoice.EnvironmentFor(devToolsAvailable: false));
            StringAssert.EndsWith(Path.Combine("balance", "production.json"), new BalanceCache("root", "production").Path);
        }

        // ---- JSON reader ---------------------------------------------------------------------------------------

        [TestCase("{\"a\":1} x")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{'a':1}")]
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":Infinity}")]
        [TestCase("{\"a\":1 // c\n}")]
        public void JsonReader_IsStrict(string text) => Assert.IsFalse(BalanceJson.TryParse(text, out _, out _));

        [Test]
        public void JsonReader_ReadsEveryKind()
        {
            Assert.IsTrue(BalanceJson.TryParse("{\"s\":\"a\\\"b\\u0041\",\"n\":-1.5e2,\"t\":true,\"f\":false,\"z\":null,\"l\":[1,{}]}", out var v, out var error), error);
            Assert.IsTrue(v.TryGet("s", out var s)); Assert.AreEqual("a\"bA", s.Text);
            Assert.IsTrue(v.TryGet("n", out var n)); Assert.AreEqual(-150d, n.Number);
            Assert.IsTrue(v.TryGet("l", out var l)); Assert.AreEqual(2, l.Items.Count);
        }
    }
}
