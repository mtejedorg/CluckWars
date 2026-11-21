using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-206 batch 2: the Peck is an interruptible cast on the state authority. The rules are pure and pinned here; what needs a live
    /// Fusion runner (every cutter really leaving pile and beak unchanged) is covered structurally by the source guards and live in batch 4.
    /// </summary>
    public sealed class PeckCastTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string PeckPath = Scripts + "Abilities/PeckAbilitySO.cs";
        private const string ControllerPath = Scripts + "Gameplay/AbilityController.cs";

        private sealed class SilentLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private sealed class RecordingAbility : AbilityBaseSO
        {
            public bool SawNatural;
            public override void OnActivate(AbilityContext ctx) { }
            public override void OnDeactivate(AbilityContext ctx) => SawNatural = ctx.EndedNaturally;
        }

        private static string Code(string path) =>
            string.Join("\n", File.ReadAllLines(path).Select(l =>
            {
                int slash = l.IndexOf("//", StringComparison.Ordinal);
                return slash >= 0 ? l.Substring(0, slash) : l;
            }));

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        private static List<ChickenStatsSO> Foragers()
        {
            var abilities = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            var peck = abilities.Find(a => a is PeckAbilitySO);
            var passives = abilities.OfType<PassiveAbilitySO>().ToList();
            return TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir)
                .Where(s => Enum.TryParse<ChickenClass>(s.DisplayName, out var cls) &&
                            PreEquippedLoadout.ClassForages(cls, passives, abilities, peck)).ToList();
        }

        private static PeckAbilitySO PeckAsset() =>
            (PeckAbilitySO)TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Find(a => a is PeckAbilitySO);

        // ---- Pure rules ------------------------------------------------------------------------------------------

        [Test]
        public void IsCut_AStunOrAShove_Cuts_NothingElseDoes()
        {
            Assert.IsTrue(PeckCastRules.IsCut(true, false));
            Assert.IsTrue(PeckCastRules.IsCut(false, true));
            Assert.IsTrue(PeckCastRules.IsCut(true, true));
            Assert.IsFalse(PeckCastRules.IsCut(false, false));
        }

        [Test]
        public void LandingTake_IsTheMinimum_NeverNegative()
        {
            Assert.AreEqual(3f, PeckCastRules.LandingTake(3f, 10f, 10f));
            Assert.AreEqual(2f, PeckCastRules.LandingTake(3f, 2f, 10f), "room is the limit");
            Assert.AreEqual(1.5f, PeckCastRules.LandingTake(3f, 10f, 1.5f), "the pile is the limit");
            Assert.AreEqual(0f, PeckCastRules.LandingTake(3f, 0f, 10f));
            Assert.AreEqual(0f, PeckCastRules.LandingTake(3f, 10f, 0f));
            Assert.AreEqual(0f, PeckCastRules.LandingTake(3f, -1f, -2f), "never negative");
        }

        [Test]
        public void CastSeconds_ReadsTheStatsAsset_AndFallsBackWithoutStats()
        {
            var peck = PeckAsset();
            Assert.IsNotNull(peck);
            var foragers = Foragers();
            Assert.IsNotEmpty(foragers);
            foreach (var s in foragers)
                Assert.AreEqual(s.PeckCastSeconds, PeckCastRules.CastSeconds(s, peck.Duration), $"{s.name}");
            Assert.AreEqual(peck.Duration, PeckCastRules.CastSeconds(null, peck.Duration));
        }

        // ---- A delta reaches the cast ----------------------------------------------------------------------------

        [Test]
        public void ADeltaOnPeckCastSeconds_ChangesTheCast_AndLeavesTheAssetShipped()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            var fatty = Foragers().Find(s => s.DisplayName == nameof(ChickenClass.Fatty));
            Assert.IsNotNull(fatty);
            string shipped = JsonUtility.ToJson(fatty);

            var range = typeof(ChickenStatsSO).GetField(nameof(ChickenStatsSO.PeckCastSeconds)).GetCustomAttribute<UnityEngine.RangeAttribute>();
            float v = (fatty.PeckCastSeconds + range.max) * 0.5f;   // one step inside the range, computed

            using var book = new BalanceBook(new SilentLog(), registry, classes, match);
            string json = "{\"schema\":1,\"assets\":{\"" + fatty.name + "\":{\"PeckCastSeconds\":" +
                          v.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}}}";
            var verdict = book.Evaluate(json, "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);

            var copy = book.Preview(fatty, verdict);
            try { Assert.AreEqual(v, PeckCastRules.CastSeconds(copy, 0f), 1e-4f); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            Assert.AreEqual(shipped, JsonUtility.ToJson(fatty), "Preview must not touch the live asset");
        }

        // ---- Natural end vs cut ----------------------------------------------------------------------------------

        [Test]
        public void ReleaseSlot_PassesNatural_OnlyWhenAsked()
        {
            var a = ScriptableObject.CreateInstance<RecordingAbility>();
            try
            {
                var ctx = new AbilityContext(null);
                AbilityRunRules.ReleaseSlot(0, a, ctx, new AbilityEffectStack(), null, natural: true);
                Assert.IsTrue(a.SawNatural);
                Assert.IsFalse(ctx.EndedNaturally, "reset after the call");

                AbilityRunRules.ReleaseSlot(0, a, ctx, new AbilityEffectStack(), null);
                Assert.IsFalse(a.SawNatural, "the default is a cut");
                Assert.IsFalse(ctx.EndedNaturally);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        }

        // ---- Source guards (a live runner is needed to execute these paths) --------------------------------------

        [Test]
        public void Peck_OnActivate_AsksNothing_OnDeactivate_AsksOnlyAtANaturalEnd()
        {
            string activate = Body(PeckPath, "public override void OnActivate(");
            Assert.IsFalse(activate.Contains("RequestPeck("), "food is asked at the cast's end, not its start");
            StringAssert.Contains("SetMovementLock(ctx.Slot)", activate);

            string deactivate = Body(PeckPath, "public override void OnDeactivate(");
            int guard = deactivate.IndexOf("if (!ctx.EndedNaturally) return;", StringComparison.Ordinal);
            int ask = deactivate.IndexOf("cargo.RequestPeck(pile, take)", StringComparison.Ordinal);
            int landed = deactivate.IndexOf("NotePeckLanded()", StringComparison.Ordinal);
            Assert.GreaterOrEqual(guard, 0);
            Assert.Greater(ask, guard, "the natural-end guard comes before the request");
            Assert.Greater(landed, ask, "the landed event follows the request");
        }

        [Test]
        public void AbilityController_CutsThePeck_RightAfterTheNaturalExpiry_ReadingOnlyStunAndShove()
        {
            string code = Code(ControllerPath);
            Assert.AreEqual(1, Regex.Matches(code, Regex.Escape("Deactivate(i, natural: true)")).Count, "one natural call site");
            int natural = code.IndexOf("Deactivate(i, natural: true)", StringComparison.Ordinal);
            int cut = code.IndexOf("CutPeckIfInterrupted();", StringComparison.Ordinal);
            int input = code.IndexOf("GetInput<", StringComparison.Ordinal);
            Assert.Greater(cut, natural, "expiry first: a stun on the completing tick lets the food land");
            Assert.Less(cut, input, "the cut runs whether or not there is input");

            string body = Body(ControllerPath, "private void CutPeckIfInterrupted(");
            StringAssert.Contains("IsStunned", body);
            StringAssert.Contains("KnockbackEventId", body);
            foreach (var forbidden in new[] { "SelfImpulseEventId", "SlowMultiplier", "CurrentControlState", "StealEventId" })
                Assert.IsFalse(body.Contains(forbidden), $"{forbidden} must not cut a Peck (a dodge, a slow and a robbery do not)");
        }

        [Test]
        public void Peck_HasNoPostPeckLock_TheCastIsTheOnlyTime()
        {
            string raw = File.ReadAllText(PeckPath);
            Assert.IsFalse(raw.Contains("0.4 s"), "the old 0.4 s lock wording is gone");

            // Duration is only the stat-less fallback: its one read sits in ResolveActiveDuration (the ctor assignment is a write).
            string code = Code(PeckPath);
            string body = Body(PeckPath, "public override float ResolveActiveDuration(");
            string nl = System.Environment.NewLine;
            string without = code.Replace(nl, "\n").Replace(body.Replace(nl, "\n"), "");
            without = Regex.Replace(without, @"\bDuration\s*=\s*[0-9.]+f?;", "");
            Assert.IsFalse(Regex.IsMatch(without, @"\bDuration\b"), "Duration is read only inside ResolveActiveDuration");
        }
    }
}
