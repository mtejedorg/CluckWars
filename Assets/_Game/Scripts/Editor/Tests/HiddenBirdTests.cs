using System.IO;
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
    }
}
