using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D9 (Hide beats and the floating-text gate, vfx.md 2.8). Rules derive their inputs from the shipped
    /// <c>Invisibility.asset</c> and the tuning constants: the poof edges, the Reduced Motion variants, the local ring and its
    /// end-of-Hide blink, the budget fit, and one test per floating-text gate (PUSH, cargo text, stun text, any spawn).
    /// </summary>
    public sealed class HideBeatTests
    {
        private const string InvisibilityPath = "Assets/_Game/Data/Abilities/Invisibility.asset";
        private const string Scripts = "Assets/_Game/Scripts/";

        private static InvisibilityAbilitySO Hide()
        {
            var so = AssetDatabase.LoadAssetAtPath<InvisibilityAbilitySO>(InvisibilityPath);
            Assert.IsNotNull(so, "Invisibility.asset missing");
            return so;
        }

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), Scripts + rel));

        // ---- poof edges ----------------------------------------------------------------------------

        [Test]
        public void TheShippedHide_FadesAcrossTheEdge_AndRevealsBackAcrossIt()
        {
            bool visible = ControlStateVFX.IsFadedOpacity(1f);
            bool ghost = ControlStateVFX.IsFadedOpacity(Hide().Opacity);
            Assert.IsFalse(visible);
            Assert.IsTrue(ghost, "the shipped Hide opacity is a faded value");
            Assert.AreEqual(HideBeatRules.Edge.Enter, HideBeatRules.PoofEdge(visible, ghost));
            Assert.AreEqual(HideBeatRules.Edge.Break, HideBeatRules.PoofEdge(ghost, visible));
        }

        [TestCase(false, false, HideBeatRules.Edge.None)]
        [TestCase(true, true, HideBeatRules.Edge.None)]
        [TestCase(false, true, HideBeatRules.Edge.Enter)]
        [TestCase(true, false, HideBeatRules.Edge.Break)]
        public void PoofEdge_IsTheFadedPredicateEdge(bool was, bool now, HideBeatRules.Edge expected) =>
            Assert.AreEqual(expected, HideBeatRules.PoofEdge(was, now));

        [Test]
        public void TheUnsetOpacityDefault_IsNotAnEdge()
        {
            // [Networked] VisualOpacity defaults to 0 (unset), which is not faded: a spawn never poofs.
            Assert.IsFalse(ControlStateVFX.IsFadedOpacity(0f));
            Assert.AreEqual(HideBeatRules.Edge.None, HideBeatRules.PoofEdge(ControlStateVFX.IsFadedOpacity(0f), ControlStateVFX.IsFadedOpacity(1f)));
        }

        // ---- poof content and budget -----------------------------------------------------------------

        [Test]
        public void ThePoof_FitsTheSharedFamilies_AndHalvesUnderReducedMotion()
        {
            Assert.LessOrEqual(HideBeatRules.DustCount(false), ChickenVFX.DustMaxParticles, "8 puffs fit the 12-particle Dust family");
            Assert.LessOrEqual(HideBeatRules.FeatherCount(false), ChickenVFX.FeatherMaxParticles);
            Assert.AreEqual(8, HideBeatRules.DustCount(false));
            Assert.AreEqual(3, HideBeatRules.FeatherCount(false));
            Assert.AreEqual(HideBeatRules.DustCount(false) / 2, HideBeatRules.DustCount(true));
            Assert.Less(HideBeatRules.FeatherCount(true), HideBeatRules.FeatherCount(false));
            Assert.AreEqual(0f, HideBeatRules.DustSpeed(true), "Reduced Motion puffs fade in place");
            Assert.AreEqual(0f, HideBeatRules.FeatherSpeed(true));
            Assert.AreEqual(FeedbackTuning.HidePoofSpeed, HideBeatRules.DustSpeed(false));
            Assert.AreEqual(0.35f, FeedbackTuning.HidePoofLifeSeconds, 1e-6f);
            Assert.AreEqual(0.6f, FeedbackTuning.HidePoofHeight, 1e-6f);
        }

        [Test]
        public void TheBreakPoof_UsesTheHideAccentAndTheSelfRingFlash()
        {
            string vfx = Read("Visuals/ChickenVFX.cs");
            StringAssert.Contains("FeedbackPalette.Current.Accent(AccentKey.Hide)", vfx);
            StringAssert.Contains("PlaySelfFlash(accent)", vfx, "the break reuses the existing cast flash, no second ring");
            string indicator = Read("Visuals/AbilityRangeIndicator.cs");
            StringAssert.Contains("FeedbackTuning.SelfRingRadius", indicator.Substring(indicator.IndexOf("public void PlaySelfFlash", System.StringComparison.Ordinal)));
            Assert.AreEqual(0.62f, FeedbackTuning.SelfRingRadius, 1e-6f);
        }

        // ---- the local ring -----------------------------------------------------------------------------

        [TestCase(true, true, false, true)]
        [TestCase(false, true, false, false)]   // a remote peer (or a decoy) never draws it
        [TestCase(true, false, false, false)]   // not hidden
        [TestCase(true, true, true, false)]     // removed
        public void TheRing_IsDrawnOnlyForTheOwnFadedBird(bool self, bool faded, bool removed, bool expected) =>
            Assert.AreEqual(expected, HideBeatRules.ShowRing(self, faded, removed));

        [Test]
        public void TheRing_UsesTheSelfRingRadius_TheOrderZeroBand_AndTheLocalOnlyGate()
        {
            Assert.AreEqual(FeedbackTuning.SelfRingRadius, 0.62f, 1e-6f);
            Assert.AreEqual(0, FeedbackTuning.HideRingSortingOrder);
            string cue = Read("Visuals/HideLocalCue.cs");
            StringAssert.Contains("FloatingCombatText.IsSelfPopTarget(_controller)", cue, "owner only, never a decoy");
            StringAssert.Contains("SlowAuraFeedback.SharedDashedMaterial", cue, "shared dashed material, no per-instance material");
            StringAssert.DoesNotContain("new Material(", cue);
            StringAssert.DoesNotContain(".material", cue.Replace(".sharedMaterial", ""));
        }

        [Test]
        public void TheRingTurnsAQuarterLapPerSecond_AndStandsStillUnderReducedMotion()
        {
            Assert.AreEqual(0.25f, FeedbackTuning.HideRingLapsPerSecond, 1e-6f);
            Assert.AreEqual(90f, HideBeatRules.RingAngle(1f, false), 1e-3f);
            Assert.AreEqual(0f, HideBeatRules.RingAngle(4f, false), 1e-3f, "one lap per 4 s");
            Assert.AreEqual(0f, HideBeatRules.RingAngle(1f, true));
            Assert.AreEqual(0f, HideBeatRules.RingAngle(123.4f, true));
        }

        [Test]
        public void TheBlink_IsTwoPulsesInTheLastQuarterSecond_OfTheShippedHide()
        {
            Assert.AreEqual(2, HideBeatRules.BlinkCount());
            Assert.AreEqual(0.25f, FeedbackTuning.HideBlinkLeadSeconds, 1e-6f);
            Assert.AreEqual(0.125f, FeedbackTuning.HideBlinkPeriodSeconds, 1e-6f);
            float duration = Hide().Duration;
            Assert.Greater(duration, FeedbackTuning.HideBlinkLeadSeconds, "the blink window fits inside the shipped Hide");

            // Lit while more than 0.25 s remain; unknown remaining (no timer) stays lit.
            Assert.AreEqual(1f, HideBeatRules.BlinkAlpha(duration, false));
            Assert.AreEqual(1f, HideBeatRules.BlinkAlpha(FeedbackTuning.HideBlinkLeadSeconds, false));
            Assert.AreEqual(1f, HideBeatRules.BlinkAlpha(-1f, false));

            // Walk the lead window finely: exactly two dim stretches.
            int dimStretches = 0; bool dim = false;
            for (float remaining = FeedbackTuning.HideBlinkLeadSeconds - 0.0005f; remaining > 0f; remaining -= 0.001f)
            {
                bool now = HideBeatRules.BlinkAlpha(remaining, false) < 1f;
                if (now && !dim) dimStretches++;
                dim = now;
            }
            Assert.AreEqual(2, dimStretches);
            Assert.AreEqual(FeedbackTuning.HideBlinkDimAlpha, HideBeatRules.BlinkAlpha(FeedbackTuning.HideBlinkLeadSeconds - 0.01f, false), 1e-6f);
        }

        [Test]
        public void UnderReducedMotion_TheBlinkIsASteadyDip_NotAFlicker()
        {
            for (float remaining = FeedbackTuning.HideBlinkLeadSeconds - 0.001f; remaining > 0f; remaining -= 0.005f)
                Assert.AreEqual(FeedbackTuning.HideBlinkSteadyAlpha, HideBeatRules.BlinkAlpha(remaining, true), 1e-6f);
            Assert.AreEqual(1f, HideBeatRules.BlinkAlpha(1f, true));
        }

        [Test]
        public void TheLocalFloor_IsInPlace_AndThePoofPopComesFromB4Once()
        {
            Assert.AreEqual(0.45f, FeedbackTuning.LocalHiddenOpacityFloor, 1e-6f);
            string visuals = Read("Visuals/ChickenVisuals.cs");
            StringAssert.Contains("HiddenVisibilityRules.AlphaFor(raw, isLocal: true)", visuals, "A1's local floor push");
            string state = Read("Visuals/ControlStateVFX.cs");
            Assert.AreEqual(1, Regex.Matches(state, "UiKeys\\.HudPopPoof").Count, "POOF! is spawned once, by B4");
            StringAssert.DoesNotContain("HudPopPoof", Read("Visuals/ChickenVFX.cs") + Read("Visuals/HideLocalCue.cs"), "D9 must not double the pop");
        }

        [Test]
        public void TheCueIsOnTheChickenPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Chicken.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<HideLocalCue>(), "HideLocalCue is wired on Chicken.prefab");
        }

        // ---- floating-text gate (one test per gate) -------------------------------------------------------

        [TestCase(0.2f, false, true)]    // faded remote: drawn text is dropped
        [TestCase(0.2f, true, false)]    // faded local: own screen keeps its text
        [TestCase(1f, false, false)]
        [TestCase(0f, false, false)]     // unset default is not hidden
        public void FloatingText_RuleDropsTextAtAFadedRemoteBirdOnly(float opacity, bool isLocal, bool suppressed) =>
            Assert.AreEqual(suppressed, FloatingCombatText.IsSuppressed(opacity, isLocal));

        [Test]
        public void FloatingText_RuleIsTheOneHiddenBirdPredicate()
        {
            for (float op = 0f; op <= 1.0001f; op += 0.01f)
                Assert.AreEqual(ControlStateVFX.IsFadedOpacity(op), FloatingCombatText.IsSuppressed(op, false), "remote at " + op);
        }

        [Test]
        public void FloatingText_PushGate()
        {
            var push = Read("Visuals/HitFeedback.cs").Split('\n').Where(l => l.Contains("UiKeys.HudPopPush")).ToArray();
            Assert.AreEqual(1, push.Length);
            StringAssert.Contains("FloatingCombatText.Spawn(_controller,", push[0], "PUSH goes through the gated spawn of the victim");
        }

        [Test]
        public void FloatingText_CargoGate()
        {
            string fct = Read("Visuals/FloatingCombatText.cs");
            int start = fct.IndexOf("public static void SpawnCargoPop", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string cargoPop = fct.Substring(start, fct.IndexOf("// ── Lifecycle", start, System.StringComparison.Ordinal) - start);
            StringAssert.Contains("IsSuppressedAt(controller)", cargoPop);
            Assert.Less(cargoPop.IndexOf("IsSuppressedAt(controller)", System.StringComparison.Ordinal),
                        cargoPop.IndexOf("SpawnSelfPop(", System.StringComparison.Ordinal), "the gate comes before either pop");
            StringAssert.Contains("FloatingCombatText.SpawnCargoPop(", Read("Gameplay/ChickenCargo.cs"));
        }

        [Test]
        public void FloatingText_StunGate()
        {
            string hit = Read("Visuals/HitFeedback.cs");
            int start = hit.IndexOf("private void SpawnControlText", System.StringComparison.Ordinal);
            Assert.Greater(start, 0);
            string body = hit.Substring(start, 600);
            StringAssert.Contains("FloatingCombatText.Spawn(_controller,", body, "STUN / ROOT digits at the bird go through the gate");
            int slow = hit.IndexOf("private void SpawnSlowText", System.StringComparison.Ordinal);
            StringAssert.Contains("FloatingCombatText.Spawn(_controller,", hit.Substring(slow, 900));
        }

        [Test]
        public void FloatingText_EverySpawnAtABirdGoesThroughTheGate()
        {
            string root = Path.Combine(Path.GetDirectoryName(Application.dataPath), Scripts);
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Editor/") && !f.EndsWith("FloatingCombatText.cs")).ToArray();
            var allowedFirstArgs = new[] { "_controller", "victim", "candidate", "_localHunter" };
            int calls = 0;
            foreach (var f in files)
            {
                string src = File.ReadAllText(f);
                StringAssert.DoesNotContain("SpawnUngated(", src, Path.GetFileName(f) + " must not use the ungated spawn");
                StringAssert.DoesNotContain("SpawnCargoDelta(", src, Path.GetFileName(f) + " must not use the direct delta popup");
                foreach (Match m in Regex.Matches(src, @"FloatingCombatText\.Spawn\(\s*([A-Za-z_]+)"))
                {
                    calls++;
                    Assert.Contains(m.Groups[1].Value, allowedFirstArgs, Path.GetFileName(f) + ": a text spawn that is not tied to a bird");
                }
            }
            Assert.GreaterOrEqual(calls, 7, "the call sites were found");
        }
    }
}
