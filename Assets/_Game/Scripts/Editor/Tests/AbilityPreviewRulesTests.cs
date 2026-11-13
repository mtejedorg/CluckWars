using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 1: press always previews (local, from the held bit), "no target" is not illegal,
    /// the in-range pip rule, the stronger range guides, and the invisible-caster wind-up rule.
    /// </summary>
    public sealed class AbilityPreviewRulesTests
    {
        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string Read(string rel) => TestSources.ReadClass(rel);

        // ---- NoTarget is not illegal ---------------------------------------

        [Test]
        public void NoTarget_DoesNotWashTheLegalityColour()
        {
            Assert.IsFalse(AbilityPreviewRules.IsRealRefusal(AbilityRefusal.NoTarget),
                "Pressing with nobody in the shape is a legal aim; it must not wash to the illegal tint.");
            Assert.IsTrue(AbilityPreviewRules.IsNoTargetPreview(AbilityRefusal.NoTarget));
            Assert.IsFalse(AbilityPreviewRules.IsRealRefusal(AbilityRefusal.None));
            Assert.IsFalse(AbilityPreviewRules.IsNoTargetPreview(AbilityRefusal.None));
        }

        [TestCase(AbilityRefusal.Stunned)]
        [TestCase(AbilityRefusal.Cooldown)]
        [TestCase(AbilityRefusal.SlotUnavailable)]
        [TestCase(AbilityRefusal.OtherAbilityActive)]
        public void RealRefusals_StillWashIllegal(AbilityRefusal refusal)
        {
            Assert.IsTrue(AbilityPreviewRules.IsRealRefusal(refusal));
            Assert.IsFalse(AbilityPreviewRules.IsNoTargetPreview(refusal));
        }

        [Test]
        public void EveryRefusalValue_IsClassified_AsExactlyOneOfNothingRealOrNoTarget()
        {
            // A new AbilityRefusal member must be decided here, not silently fall through as "legal".
            foreach (AbilityRefusal r in System.Enum.GetValues(typeof(AbilityRefusal)))
            {
                bool none = r == AbilityRefusal.None;
                bool noTarget = AbilityPreviewRules.IsNoTargetPreview(r);
                bool real = AbilityPreviewRules.IsRealRefusal(r);
                Assert.AreEqual(1, new[] { none, noTarget, real }.Count(b => b),
                    $"{r} must be exactly one of: no refusal, no-target, real refusal.");
            }
        }

        [Test]
        public void Telegraph_UsesThePureLegalityRule_NotAnyRefusalAtAll()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            StringAssert.Contains("AbilityPreviewRules.IsRealRefusal(refusal)", src);
            StringAssert.DoesNotContain("EvaluateRefusal(slot) != AbilityRefusal.None", src,
                "the wash once keyed on ANY refusal, which made NoTarget (a legal aim) wash illegal");
        }

        // ---- In-range pip ----------------------------------------------------

        [TestCase(true, AbilityRefusal.None, true)]
        [TestCase(true, AbilityRefusal.NoTarget, true)]
        [TestCase(true, AbilityRefusal.Cooldown, false)]
        [TestCase(true, AbilityRefusal.Stunned, false)]
        [TestCase(true, AbilityRefusal.SlotUnavailable, false)]
        [TestCase(false, AbilityRefusal.None, false)]
        [TestCase(false, AbilityRefusal.Cooldown, false)]
        public void Pip_IsVisible_OnlyWhenHotAndNoRealRefusal(bool hot, AbilityRefusal refusal, bool expected)
        {
            Assert.AreEqual(expected, AbilityPreviewRules.InRangePipVisible(hot, refusal, rearming: false));
        }

        [Test]
        public void Pip_IsHiddenWhileRearming()
        {
            Assert.IsFalse(AbilityPreviewRules.InRangePipVisible(true, AbilityRefusal.None, rearming: true));
        }

        [Test]
        public void Pip_ReadsTheSameHotVerdictAsTheGroundGuide_AndHonoursReducedMotion()
        {
            string touch = Read("Assets/_Game/Scripts/Input/TouchControlsController.cs");
            StringAssert.Contains("_localOverlay.IsHot(slot)", touch, "one source of truth for 'hot'");
            StringAssert.Contains("PlayerPreferences.ReducedMotionEnabled", touch, "the pip pop is skipped under Reduced Motion");
            StringAssert.Contains("AbilityPreviewRules.InRangePipVisible", touch);

            string overlay = Read("Assets/_Game/Scripts/Visuals/AbilitySlotOverlay.cs");
            StringAssert.Contains("public bool IsHot(int slot)", overlay);
        }

        [Test]
        public void HudMarkup_HasARimFillAndPipPerHex_AndNoNoTargetSlash()
        {
            string uxml = Read("Assets/UI/TouchControls.uxml");
            string uss  = Read("Assets/UI/Styles/TouchControls.uss");
            for (int n = 1; n <= AbilityController.SlotCount; n++)
            {
                StringAssert.Contains($"name=\"Fill{n}\"", uxml);
                StringAssert.Contains($"name=\"Pip{n}\"", uxml);
                StringAssert.DoesNotContain($"StateRing{n}", uxml);
            }
            StringAssert.Contains(".cw-hex-pip", uss);
            StringAssert.Contains("rgb(245, 200, 66)", uss, "pip is gold #f5c842");
            StringAssert.DoesNotContain(".cw-hex--no-target", uss, "no slash is ever drawn over a resting hex");
            StringAssert.DoesNotContain("cw-hex-state-ring", uss);
        }

        [Test]
        public void HeldHex_ScalesAndDimsTheOthers_WithinTheSpec()
        {
            Assert.That(FeedbackTuning.HexHeldScale, Is.EqualTo(1.12f).Within(1e-4f));
            Assert.That(FeedbackTuning.HexHeldTransitionSeconds, Is.EqualTo(0.06f).Within(1e-4f));
            Assert.That(FeedbackTuning.HexOthersWhileHeldAlpha, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(FeedbackTuning.HexPipPopSeconds, Is.EqualTo(0.2f).Within(1e-4f));
        }

        // ---- Local preview slot selection --------------------------------------

        [Test]
        public void LocalPreview_NothingHeld_NoPreview()
        {
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { false, false, false, false }, new[] { true, true, true, true }));
        }

        [Test]
        public void LocalPreview_SingleHeldAvailableSlot_IsThatSlot()
        {
            Assert.AreEqual(2, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { false, false, true, false }, new[] { true, true, true, true }));
        }

        [Test]
        public void LocalPreview_LowestHeldSlotWins()
        {
            Assert.AreEqual(1, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { false, true, false, true }, new[] { true, true, true, true }));
        }

        [Test]
        public void LocalPreview_HeldSlotOnCooldown_NoPreview()
        {
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { false, true, false, false }, new[] { true, false, true, true }));
        }

        [Test]
        public void LocalPreview_LowestHeldUnavailable_DoesNotFallThroughToAHigherSlot()
        {
            // AbilityHoldStateMachine: the lowest held slot claims the gesture even if it is dead, so a
            // higher held slot would never fire. Previewing it would show an aim that cannot happen.
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { true, true, false, false }, new[] { false, true, true, true }));
        }

        [Test]
        public void LocalPreview_NullOrRaggedInputs_AreSafe()
        {
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(null, new[] { true }));
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(new[] { true }, null));
            Assert.AreEqual(AbilityPreviewRules.NoSlot, AbilityPreviewRules.SelectLocalPreviewSlot(
                new[] { false, false, true }, new[] { true }), "a held bit beyond the availability array is not previewed");
        }

        [Test]
        public void Telegraph_PreviewsFromTheLocalHeldBit_NotOnlyTheNetworkedCharge()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/AbilityTelegraph.cs");
            StringAssert.Contains("_input.GetAbilityHeld(i)", src);
            StringAssert.Contains("AbilityPreviewRules.SelectLocalPreviewSlot", src);
            StringAssert.Contains("_abilities.ChargingSlot", src, "the networked charge stays as the fallback");
            StringAssert.Contains("SelfInject.Ensure(this, _log != null);", src, "self-injection per CONVENTIONS");
        }

        // ---- Range guides ------------------------------------------------------

        [Test]
        public void RangeGuides_AreStrongerAndStillOrdered()
        {
            Assert.That(FeedbackTuning.AmbientSlotOutlineWidth, Is.EqualTo(0.06f).Within(1e-5f));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaSuppressed, Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaCooldown));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaCooldown, Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaReady));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaReady, Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaHot));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaHot, Is.LessThan(FeedbackTuning.TelegraphPreviewAlpha));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaReady, Is.GreaterThanOrEqualTo(0.45f - 1e-5f));
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaCooldown, Is.GreaterThanOrEqualTo(0.18f - 1e-5f));
        }

        // ---- Invisible caster --------------------------------------------------

        [Test]
        public void WindupGlow_IsHidden_ForAFadedCaster()
        {
            Assert.IsFalse(ControlStateVFX.IsFadedOpacity(0f), "0 is the networked 'unset' default, not invisible");
            Assert.IsFalse(ControlStateVFX.IsFadedOpacity(1f));
            Assert.IsFalse(ControlStateVFX.IsFadedOpacity(0.995f));
            Assert.IsTrue(ControlStateVFX.IsFadedOpacity(0.2f), "Invisibility's ghostly 0.2");
            Assert.IsTrue(ControlStateVFX.IsFadedOpacity(0.5f), "Smoke Roost's fade");

            string src = Read("Assets/_Game/Scripts/Visuals/ControlStateVFX.cs");
            StringAssert.Contains("if (IsFaded(_controller)) charging = 0;", src);
        }

        // ---- A2: a slow on a slow-immune target previews as no effect ------------
        //
        // IsSlowImmune reads a [Networked] TickTimer against a live Runner, which an EditMode probe never has, so the
        // immune half is pinned by the derivation (which abilities are slow-only, from the shipped assets) plus the
        // one-line routing in IsEffectNullified; the non-immune half is a real call (ControlImmunityFeedbackTests uses
        // the same split for IsControlImmune).

        private static AbilityBaseSO ShippedAbility(string name) =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).First(a => a.name == name);

        [Test]
        public void SlowOnlyAbilities_AreExactlyTheSlowSources_FromTheShippedAssets()
        {
            var slowOnly = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Where(a => a.TargetEffectIsOnlySlow).Select(a => a.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "DustKick", "FeatherAura", "FeatherTrap", "Slow", "SmokeRoost" }, slowOnly,
                "the slow aura, the slow zones and the timed slow are every effect IsSlowImmune refuses");

            Assert.IsFalse(ShippedAbility("WingSlam").TargetEffectIsOnlySlow, "a stun is not a slow: the walker is still stunned");
            Assert.IsFalse(ShippedAbility("RootEgg").TargetEffectIsOnlySlow, "a root zone is not a slow zone");
            Assert.IsFalse(ShippedAbility("Snatch").TargetEffectIsOnlySlow, "a steal is not a slow");
        }

        [Test]
        public void IsEffectNullified_AsksSlowImmunity_ForASlowOnlyEffect_BeforeControlImmunity()
        {
            string src = Read("Assets/_Game/Scripts/Abilities/AbilityBaseSO.cs");
            int start = src.IndexOf("public bool IsEffectNullified(", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "IsEffectNullified moved or was renamed");
            string body = src.Substring(start, 700);

            int slowAt = body.IndexOf("if (TargetEffectIsOnlySlow) return candidate.IsSlowImmune;", System.StringComparison.Ordinal);
            int controlAt = body.IndexOf("if (TargetEffectIsPurelyControl) return candidate.IsControlImmune;", System.StringComparison.Ordinal);
            Assert.Greater(slowAt, 0, "a slow aimed at a slow-immune walker must read NoEffect (grey) in the telegraph");
            Assert.Greater(controlAt, slowAt,
                "the slow check must come first: Dust Kick is also purely control, and the walker is only slow-immune");
        }

        [Test]
        public void IsEffectNullified_IsFalse_ForANonImmuneTarget_OfASlowOrAStun()
        {
            var caster = new GameObject("A2_Caster").AddComponent<ChickenController>();
            var target = new GameObject("A2_Target").AddComponent<ChickenController>();
            try
            {
                Assert.IsFalse(target.IsSlowImmune, "precondition: an unspawned probe is not slow-immune");
                Assert.IsFalse(ShippedAbility("FeatherAura").IsEffectNullified(caster, target), "the Slow lands: a valid mark");
                Assert.IsFalse(ShippedAbility("DustKick").IsEffectNullified(caster, target), "the timed slow lands");
                Assert.IsFalse(ShippedAbility("WingSlam").IsEffectNullified(caster, target), "the Stun lands");
            }
            finally
            {
                Object.DestroyImmediate(caster.gameObject);
                Object.DestroyImmediate(target.gameObject);
            }
        }

        // ---- Art import + prefab wiring -----------------------------------------

        [Test]
        public void DashedRing_ImportsAsATiledWorldTexture()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/_Game/Art/UI/Fx/Fx_DashedRing.png");
            Assert.IsNotNull(ti);
            Assert.AreEqual(TextureImporterType.Default, ti.textureType);
            Assert.AreEqual(TextureWrapMode.Repeat, ti.wrapModeU, "tiles along the line");
            Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapModeV);
            Assert.AreEqual(FilterMode.Bilinear, ti.filterMode);
            Assert.IsTrue(ti.mipmapEnabled);
            Assert.IsTrue(ti.alphaIsTransparency);
            Assert.GreaterOrEqual(ti.maxTextureSize, 256, "the 256 px strip must not be halved by the Fx cap");
        }

        [Test]
        public void WhiffPuff_ImportsAsASingleSprite()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath("Assets/_Game/Art/UI/Fx/Fx_Whiff.png");
            Assert.IsNotNull(ti);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType);
            Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode);
            Assert.AreEqual(100f, ti.spritePixelsPerUnit);
            Assert.AreEqual(FilterMode.Bilinear, ti.filterMode);
            Assert.IsFalse(ti.mipmapEnabled);
            Assert.IsTrue(ti.alphaIsTransparency);
        }

        [Test]
        public void ChickenPrefab_WiresTheDashedRingTexture_OnTheTelegraph()
        {
            // A missing wire is a silent failure surface (the preview falls back to a solid cream line).
            var chicken = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var telegraph = chicken.GetComponentInChildren<AbilityTelegraph>(true);
            Assert.IsNotNull(telegraph, "Chicken.prefab lost AbilityTelegraph");

            var so = new SerializedObject(telegraph);
            var tex = so.FindProperty("_dashedRingTexture");
            Assert.IsNotNull(tex, "AbilityTelegraph._dashedRingTexture was renamed or removed");
            Assert.IsNotNull(tex.objectReferenceValue,
                "AbilityTelegraph._dashedRingTexture is unwired on Chicken.prefab (Fx_DashedRing.png).");
        }
    }
}
