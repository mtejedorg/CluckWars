using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A1 (feedback plan 2026-11-07, spec section 3.2 / 4.8): a hidden bird (Hide / Smoke Roost, VisualOpacity 0.2) must not
    /// leak world-space tells to a remote peer, and its own player sees it at the local floor. Pins the one rule
    /// (<see cref="HiddenVisibilityRules"/>), that every tell renderer routes through it, and the floor against the
    /// shipped Hide opacities.
    /// </summary>
    public class HiddenBirdTests
    {
        private const string VisualsDir = "Assets/_Game/Scripts/Visuals/";

        [TestCase(1f, false, 1f)]
        [TestCase(1f, true, 1f)]
        [TestCase(0.2f, false, 0f)]
        [TestCase(0.2f, true, 0.45f)]
        [TestCase(0f, false, 1f)]   // 0 is the [Networked] "unset" default, not invisible
        [TestCase(0f, true, 1f)]
        [TestCase(0.6f, true, 0.6f)] // above the floor: the real opacity
        public void AlphaFor_PinsTheRule(float opacity, bool isLocal, float expected)
        {
            Assert.That(HiddenVisibilityRules.AlphaFor(opacity, isLocal), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void AlphaFor_UsesTheOneFadedPredicate()
        {
            for (float op = 0f; op <= 1.0001f; op += 0.01f)
            {
                bool faded = ControlStateVFX.IsFadedOpacity(op);
                Assert.That(HiddenVisibilityRules.AlphaFor(op, false) <= 0f, Is.EqualTo(faded), "remote alpha at " + op);
            }
        }

        [TestCase("ControlStateVFX.cs")]
        [TestCase("ChickenWorldBars.cs")]
        [TestCase("ChickenNameplate.cs")]
        [TestCase("ChickenStatusBadges.cs")]
        [TestCase("ChickenVFX.cs")]
        [TestCase("HitFeedback.cs")]
        [TestCase("ChickenVisuals.cs")]
        public void EveryTellRenderer_GoesThroughTheRule(string file)
        {
            string src = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), VisualsDir + file));
            Assert.That(src.Contains("HiddenVisibilityRules.") || src.Contains("IsFadedOpacity("), Is.True,
                file + " draws a world tell but never asks whether the bird is hidden (A1)");
        }

        [TestCase("Assets/_Game/Data/Abilities/Invisibility.asset")]
        [TestCase("Assets/_Game/Data/Abilities/SmokeRoost.asset")]
        public void LocalFloor_SitsBetweenTheShippedHideOpacityAndOne(string path)
        {
            var so = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(path);
            Assert.That(so, Is.Not.Null, path);
            float hideOpacity = so switch
            {
                InvisibilityAbilitySO i => i.Opacity,
                SmokeRoostAbilitySO s => s.Opacity,
                _ => float.NaN,
            };
            Assert.That(float.IsNaN(hideOpacity), Is.False, path + " is not a Hide-type ability");
            Assert.That(ControlStateVFX.IsFadedOpacity(hideOpacity), Is.True, "the shipped Hide opacity must count as faded");

            Assert.That(FeedbackTuning.LocalHiddenOpacityFloor, Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(FeedbackTuning.LocalHiddenOpacityFloor, Is.LessThan(1f));
            Assert.That(FeedbackTuning.LocalHiddenOpacityFloor, Is.GreaterThan(hideOpacity));
        }

        // ---- D9 re-verify: every emitter of batch D (shell, streaks, jump beats, skid, steal chunks and lines, aura) -------------

        private static readonly Regex MethodSig = new Regex(
            @"^ {8}(?:(?:public|private|internal|protected)\s+)(?:static\s+)?(?:override\s+)?[\w<>\[\],\.\? ]+?\s+(\w+)\s*\([^)]*\)\s*\r?\n\s{8}\{",
            RegexOptions.Multiline);
        private static readonly Regex Emission = new Regex(
            @"\b(?:EmitDust|EmitFeathers|EmitSparkles|EmitShards)(?:At)?\(|\bEmitStreak\(|\b\w*PS\.Emit\(|\b_ps\.Emit\(");
        private static readonly Regex GateToken = new Regex(
            @"HiddenVisibilityRules|IsFadedOpacity|IsQuiet|IsHiddenFor|Suppressed|\bCanEmit\(|\bquiet\b|\bhidden\b");

        /// <summary>Block-bodied methods of a class file at 8-space indent: name to body.</summary>
        private static List<(string name, string body)> Methods(string src)
        {
            var list = new List<(string, string)>();
            foreach (Match m in MethodSig.Matches(src))
            {
                int i = m.Index + m.Length - 1, depth = 0, j = i;
                for (; j < src.Length; j++)
                {
                    if (src[j] == '{') depth++;
                    else if (src[j] == '}' && --depth == 0) break;
                }
                list.Add((m.Groups[1].Value, src.Substring(i, j - i)));
            }
            return list;
        }

        private static bool GatedOrOnlyCalledGated(string name, List<(string name, string body)> all, int depth, HashSet<string> seen)
        {
            var self = all.First(m => m.name == name);
            if (GateToken.IsMatch(self.body)) return true;
            if (depth == 0 || !seen.Add(name)) return false;
            var callers = all.Where(m => m.name != name && Regex.IsMatch(m.body, @"\b" + name + @"\(")).ToList();
            return callers.Count > 0 && callers.All(c => GatedOrOnlyCalledGated(c.name, all, depth - 1, seen));
        }

        [TestCase("ChickenVFX.cs")]
        [TestCase("ChickenShellVisuals.cs")]
        [TestCase("HitFeedback.cs")]
        [TestCase("SlowAuraFeedback.cs")]
        [TestCase("ControlStateVFX.cs")]
        [TestCase("FoodChunkFlight.cs")]
        public void EveryEmittingMethod_IsGated_OrReachedOnlyThroughGatedCallers(string file)
        {
            string src = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), VisualsDir + file));
            var all = Methods(src);
            var ungated = new List<string>();
            foreach (var m in all)
            {
                // Emit is ChickenVFX's raw dispatcher (every caller gates); EmitHidePoof is the one marked bypass.
                if (m.name == "Emit" || m.name == "EmitHidePoof" || !Emission.IsMatch(m.body)) continue;
                if (!GatedOrOnlyCalledGated(m.name, all, 3, new HashSet<string>())) ungated.Add(m.name);
            }
            Assert.That(ungated, Is.Empty, file + ": methods that emit on a bird without the hidden-bird gate (directly or via every caller): " + string.Join(", ", ungated));
        }

        [Test]
        public void TheShellsCanEmit_AsksTheFadedPredicate_AndTheStealChunksAreQuietOnAFadedBird()
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), VisualsDir);
            string shell = File.ReadAllText(Path.Combine(root, "ChickenShellVisuals.cs"));
            StringAssert.Contains("CanEmit() => _vfx != null && !ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity)", shell);
            string chunks = File.ReadAllText(Path.Combine(root, "FoodChunkFlight.cs"));
            StringAssert.Contains("HiddenVisibilityRules.IsHiddenFor(c) || ControlStateVFX.IsFadedOpacity(c.VisualOpacity)", chunks);
            // The speed streaks and stride dust, jump beats and skid dust all route through the same suppress flags.
            string vfx = File.ReadAllText(Path.Combine(root, "ChickenVFX.cs"));
            StringAssert.Contains("bool suppressed = hidden || ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);", vfx);
            StringAssert.Contains("private bool JumpSuppressed(bool hidden) => hidden || ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity);", vfx);
            StringAssert.Contains("bool quiet = hidden || ControlStateVFX.IsFadedOpacity(_controller.VisualOpacity)", vfx);
            string aura = File.ReadAllText(Path.Combine(root, "SlowAuraFeedback.cs"));
            StringAssert.Contains("SlowAuraRules.ShouldShow(true, removed, hidden, faded)", aura);
            Assert.IsFalse(SlowAuraRules.ShouldShow(true, false, false, true), "a faded caster draws no aura");
            Assert.IsFalse(SlowAuraRules.ShouldShow(true, false, true, false));
        }

        [Test]
        public void TheHidePoofBypass_IsTheOnlyOneAndOnlyObserveHideBeatsCallsIt()
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), VisualsDir);
            var withMarker = Directory.GetFiles(root, "*.cs").Where(f => File.ReadAllText(f).Contains("HIDE-POOF-BYPASS")).Select(Path.GetFileName).ToArray();
            Assert.That(withMarker, Is.EqualTo(new[] { "ChickenVFX.cs" }), "the faded-bird gate has exactly one bypass: the Hide poofs");

            string vfx = File.ReadAllText(Path.Combine(root, "ChickenVFX.cs"));
            Assert.That(Regex.Matches(vfx, "HIDE-POOF-BYPASS").Count, Is.EqualTo(1));
            var all = Methods(vfx);
            var callers = all.Where(m => m.name != "EmitHidePoof" && Regex.IsMatch(m.body, @"\bEmitHidePoof\(")).Select(m => m.name).ToArray();
            Assert.That(callers, Is.EqualTo(new[] { "ObserveHideBeats" }));
            foreach (var other in Directory.GetFiles(root, "*.cs").Where(f => !f.EndsWith("ChickenVFX.cs")))
                Assert.That(File.ReadAllText(other).Contains("EmitHidePoof("), Is.False, Path.GetFileName(other) + " must not reach the bypass");

            // The bypass body itself never emits the other families or anything but the two poofs.
            string poof = all.First(m => m.name == "EmitHidePoof").body;
            Assert.That(Regex.Matches(poof, @"\bEmit(?:Dust|Feathers|Sparkles|Shards)\(").Count, Is.EqualTo(2), "one dust call, one feather call");
            Assert.That(poof.Contains("EmitSparkles("), Is.False);
        }
    }
}
