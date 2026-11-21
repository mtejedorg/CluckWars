using System;
using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-206 batch 3: one full play of the Peck clip spans the effective cast on every class model, and a cut stops it at once.
    /// The speed rule is pure; the controller wiring and the override binding are pinned against the shipped asset and source.
    /// </summary>
    public sealed class PeckCastAnimationTests
    {
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

        private const string ControllerPath = "Assets/_Game/Art/Animations/Chicken.controller";
        private const string Scripts = "Assets/_Game/Scripts/";

        private static PeckAbilitySO PeckAsset() =>
            (PeckAbilitySO)TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Find(a => a is PeckAbilitySO);

        private static ChickenClassRegistrySO.Entry[] ForagerEntries()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsNotNull(registry, TestAssets.ChickenClassRegistryPath);
            var abilities = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            var passives = abilities.OfType<PassiveAbilitySO>().ToList();
            var peck = abilities.Find(a => a is PeckAbilitySO);
            var result = Enum.GetValues(typeof(ChickenClass)).Cast<ChickenClass>()
                .Where(cls => registry.TryGet(cls, out _) && PreEquippedLoadout.ClassForages(cls, passives, abilities, peck))
                .Select(cls => { registry.TryGet(cls, out var e); return e; })
                .ToArray();
            Assert.IsNotEmpty(result, "no forager class in the registry");
            return result;
        }

        private static void AssertSpansCast(AnimationClip clip, float cast, string label)
        {
            float speed = PeckBeatRules.ClipSpeed(clip.length, cast);
            Assert.AreEqual(cast, clip.length / speed, 1f / 60f, $"{label}: one play must equal the cast time");
        }

        [Test]
        public void PerClass_ScaledPeckClip_SpansTheEffectiveCast()
        {
            var peck = PeckAsset();
            Assert.IsNotNull(peck);
            foreach (var entry in ForagerEntries())
            {
                Assert.IsNotNull(entry.Clips.Peck, $"{entry.Class} has no Peck clip");
                float cast = PeckCastRules.CastSeconds(entry.Stats, peck.Duration);
                AssertSpansCast(entry.Clips.Peck, cast, entry.Class.ToString());

                // A served balance value moves the speed with it, through the same BalanceBook.Preview path the game uses.
                var range = typeof(ChickenStatsSO).GetField(nameof(ChickenStatsSO.PeckCastSeconds))
                    .GetCustomAttributes(typeof(UnityEngine.RangeAttribute), false).Cast<UnityEngine.RangeAttribute>().Single();
                float v = (entry.Stats.PeckCastSeconds + range.max) * 0.5f;
                Assert.AreNotEqual(cast, v);
                string json = "{\"schema\":1,\"assets\":{\"" + entry.Stats.name + "\":{\"PeckCastSeconds\":" +
                              v.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}}}";
                using var book = new BalanceBook(new SilentLog(),
                    TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                    TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                    TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath));
                var verdict = book.Evaluate(json, "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                var changed = book.Preview(entry.Stats, verdict);
                try
                {
                    float served = PeckCastRules.CastSeconds(changed, peck.Duration);
                    Assert.AreEqual(v, served, 1e-4f);
                    AssertSpansCast(entry.Clips.Peck, served, entry.Class + " (changed cast)");
                }
                finally { UnityEngine.Object.DestroyImmediate(changed); }
            }
        }

        [Test]
        public void ClipSpeed_ZeroClipOrZeroCast_IsOne()
        {
            Assert.AreEqual(1f, PeckBeatRules.ClipSpeed(0f, 0.5f));
            Assert.AreEqual(1f, PeckBeatRules.ClipSpeed(0.5f, 0f));
            Assert.AreEqual(1f, PeckBeatRules.ClipSpeed(-1f, 0.5f));
            Assert.AreEqual(1f, PeckBeatRules.ClipSpeed(0.5f, -1f));
        }

        [Test]
        public void Controller_PeckState_IsDrivenByPeckSpeed_AndCutsHard()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(c, ControllerPath);
            var state = c.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state).FirstOrDefault(s => s.name == "Peck");
            Assert.IsNotNull(state, "no Peck state");
            Assert.IsTrue(state.speedParameterActive);
            Assert.AreEqual("PeckSpeed", state.speedParameter);

            var exit = state.transitions.Single(t => t.destinationState != null && t.destinationState.name == "Idle");
            Assert.IsFalse(exit.hasExitTime, "the exit is the Pecking bit, not the clip end");
            Assert.AreEqual(0f, exit.duration, "a cut is not blended");
            Assert.AreEqual(1, exit.conditions.Length);
            Assert.AreEqual("Pecking", exit.conditions[0].parameter);
            Assert.AreEqual(AnimatorConditionMode.IfNot, exit.conditions[0].mode);
        }

        [Test]
        public void WiringMethod_IsIdempotent()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            int parameters = c.parameters.Length;
            Assert.IsTrue(CluckWars.EditorTools.ChickenAnimationSetup.EnsurePeckCastWiring(c));
            Assert.AreEqual(parameters, c.parameters.Length, "a second run adds no parameter");
        }

        [Test]
        public void Override_BindsTheClassPeckClip()
        {
            string src = File.ReadAllText(Scripts + "Gameplay/ChickenController.cs");
            StringAssert.Contains("[ClipKeyPeck]    = entry.Clips.Peck", src);
            StringAssert.Contains("public const string ClipKeyPeck", src);
        }

        [Test]
        public void LandedRecently_InsideAtBeyondTheWindow_AndBeforeTheStamp()
        {
            float w = FeedbackTuning.PeckLandedWindowSeconds;
            Assert.Greater(w, 0f);
            Assert.IsTrue(PeckBeatRules.LandedRecently(10f, 10f));
            Assert.IsTrue(PeckBeatRules.LandedRecently(10f + w * 0.5f, 10f));
            Assert.IsTrue(PeckBeatRules.LandedRecently(10f + w, 10f));
            Assert.IsFalse(PeckBeatRules.LandedRecently(10f + w * 1.01f, 10f));
            Assert.IsFalse(PeckBeatRules.LandedRecently(9.9f, 10f), "before the stamp");
            Assert.IsFalse(PeckBeatRules.LandedRecently(0f, float.PositiveInfinity));
        }

        [Test]
        public void ChickenAnimator_HasNoHardCodedPeckDuration_AndDrivesSpeedFromClipSpeed()
        {
            string src = File.ReadAllText(Scripts + "Visuals/ChickenAnimator.cs");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(System.Text.RegularExpressions.Regex.Replace(src, @"\[Range\([^\]]*\]", ""), @"(?<![\d.])0\.4f?(?!\d)"), "a literal 0.4 (the old clip length) must not return (inspector Range attributes aside)");
            int push = src.IndexOf("private void PushPeckParameters", StringComparison.Ordinal);
            Assert.GreaterOrEqual(push, 0);
            string body = src.Substring(push);
            StringAssert.Contains("SetFloat(PeckSpeedHash, PeckBeatRules.ClipSpeed(", body);
            StringAssert.Contains("SetBool(PeckingHash, pecking)", body);
            int pushParams = src.IndexOf("private void PushAnimatorParameters", StringComparison.Ordinal);
            StringAssert.Contains("PushPeckParameters();", src.Substring(pushParams, 400));
            int trigger = src.IndexOf("public void TriggerPeck()", StringComparison.Ordinal);
            Assert.GreaterOrEqual(trigger, 0);
            string triggerBody = src.Substring(trigger, src.IndexOf('}', trigger) - trigger);
            StringAssert.Contains("_animator.Play(PeckStateHash, 0, 0f)", triggerBody,
                "the Peck clip starts on the cast's first frame, never queued behind a locomotion blend");
            StringAssert.DoesNotContain("SetTrigger(PeckHash)", triggerBody);
        }

        [Test]
        public void TheObserver_RaisesPeckLanded_FromTheCounter_AndStampsTheTime()
        {
            var events = new ChickenLocalEvents();
            int raised = 0;
            events.PeckLanded += () => raised++;
            var s = new ChickenEventSnapshot { HasAbilities = true, PeckLandedEventId = 7 };
            events.Observe(in s);
            Assert.AreEqual(0, raised, "the first observation only seeds");
            Assert.AreEqual(float.NegativeInfinity, events.LastPeckLandedTime);
            s.PeckLandedEventId = 8;
            events.Observe(in s);
            Assert.AreEqual(1, raised);
            Assert.AreNotEqual(float.NegativeInfinity, events.LastPeckLandedTime);
            events.Reset();
            Assert.AreEqual(float.NegativeInfinity, events.LastPeckLandedTime);
        }
    }
}
