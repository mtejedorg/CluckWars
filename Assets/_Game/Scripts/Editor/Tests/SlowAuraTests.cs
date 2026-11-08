using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// D8 (Slow aura feedback, vfx.md 2.4). Rules derive every input from the shipped <c>FeatherAura.asset</c> and the tuning
    /// constants: the radius, the Reduced Motion variants, the opacity gate, and the particle arithmetic that lets the dedicated
    /// <c>VFX_AuraFeather</c> system sit outside the five ChickenVFX families.
    /// </summary>
    public sealed class SlowAuraTests
    {
        private const string AuraPath = "Assets/_Game/Data/Abilities/FeatherAura.asset";

        private static FeatherAuraAbilitySO Aura()
        {
            var a = AssetDatabase.LoadAssetAtPath<FeatherAuraAbilitySO>(AuraPath);
            Assert.IsNotNull(a, "FeatherAura.asset missing or not a FeatherAuraAbilitySO (the trigger type)");
            return a;
        }

        // ---- radius ------------------------------------------------------------------------------

        [Test]
        public void TheRadius_IsTheAssetsAuraRadius_AndTheZoneSpecs()
        {
            var a = Aura();
            Assert.AreEqual(a.AuraRadius, SlowAuraRules.Radius(a));
            Assert.IsTrue(a.TryGetAreaEffect(out var spec));
            Assert.AreEqual(spec.Radius, SlowAuraRules.Radius(a), 1e-5f, "the outline must be the real trigger edge");
        }

        [Test]
        public void TheRadius_FollowsTheAsset_NotALiteral()
        {
            var a = Object.Instantiate(Aura());
            try
            {
                a.AuraRadius = 3.25f;
                Assert.AreEqual(3.25f, SlowAuraRules.Radius(a));
                Assert.AreEqual(3.25f - FeedbackTuning.SlowAuraFeatherInsetMetres, SlowAuraRules.FeatherDiscRadius(SlowAuraRules.Radius(a)), 1e-5f);
            }
            finally { Object.DestroyImmediate(a); }
            Assert.AreEqual(0f, SlowAuraRules.Radius(null));
        }

        [Test]
        public void TheFeatherDisc_StaysInsideTheEdgeBand()
        {
            float r = SlowAuraRules.Radius(Aura());
            float disc = SlowAuraRules.FeatherDiscRadius(r);
            Assert.Less(disc, r, "feathers spawn inside the aura edge");
            Assert.Greater(disc, 0.5f * r);
            Assert.AreEqual(0.1f, SlowAuraRules.FeatherDiscRadius(0.05f), "never a zero or negative disc");
        }

        // ---- Reduced Motion ----------------------------------------------------------------------

        [Test]
        public void ReducedMotion_FreezesTheOutline_AndStillesTheFeathers()
        {
            Assert.AreEqual(0f, SlowAuraRules.OutlineLapsPerSecond(true));
            Assert.AreEqual(0f, SlowAuraRules.OutlineAngle(7.3f, true), "static outline");
            Assert.AreEqual(FeedbackTuning.SlowAuraOutlineLapsPerSecond, SlowAuraRules.OutlineLapsPerSecond(false));
            Assert.AreEqual(0f, SlowAuraRules.FeatherNoiseStrength(true), "feathers fall straight");
            Assert.AreEqual(FeedbackTuning.SlowAuraFeatherNoiseStrength, SlowAuraRules.FeatherNoiseStrength(false));
            Assert.AreEqual(6f, SlowAuraRules.FeatherRate(true));
            Assert.AreEqual(10f, SlowAuraRules.FeatherRate(false));
            Assert.AreEqual(0.14f, SlowAuraRules.BandAlpha(true), 1e-5f);
            Assert.AreEqual(0.20f, SlowAuraRules.BandAlpha(false), 1e-5f);
            Assert.AreEqual(2, SlowAuraRules.PuffCount(true));
            Assert.AreEqual(4, SlowAuraRules.PuffCount(false));
        }

        [Test]
        public void TheOutline_TurnsOneLapPerTenSeconds()
        {
            Assert.AreEqual(0f, SlowAuraRules.OutlineAngle(0f, false), 1e-3f);
            Assert.AreEqual(36f, SlowAuraRules.OutlineAngle(1f, false), 1e-3f);
            Assert.AreEqual(0f, SlowAuraRules.OutlineAngle(10f, false), 1e-2f, "one lap in 1 / 0.1 s, then wraps");
        }

        // ---- gate --------------------------------------------------------------------------------

        [Test]
        public void TheAura_DrawsOnlyForALiveVisibleCaster()
        {
            Assert.IsTrue(SlowAuraRules.ShouldShow(slotActive: true, removed: false, hiddenForViewer: false, faded: false));
            Assert.IsFalse(SlowAuraRules.ShouldShow(false, false, false, false), "slot not active");
            Assert.IsFalse(SlowAuraRules.ShouldShow(true, true, false, false), "removed caster");
            Assert.IsFalse(SlowAuraRules.ShouldShow(true, false, true, false), "hidden for this viewer: the aura would reveal the caster");
            Assert.IsFalse(SlowAuraRules.ShouldShow(true, false, false, true), "faded caster (Hide): nothing at all");
        }

        // ---- budget ------------------------------------------------------------------------------

        [Test]
        public void TheAuraSystem_IsSizedFromItsOwnSteadyState()
        {
            Assert.AreEqual(24, FeedbackTuning.SlowAuraFeatherMaxParticles, "spec cap");
            Assert.LessOrEqual(SlowAuraRules.SteadyStateLive(false), FeedbackTuning.SlowAuraFeatherMaxParticles, "rate x lifetime fits the cap");
            Assert.LessOrEqual(SlowAuraRules.SteadyStateLive(true), SlowAuraRules.SteadyStateLive(false), "Reduced Motion never adds");
        }

        [Test]
        public void FeatherFall_IsTheF4SizeAndAlpha_WithRateAndCapUnchanged()
        {
            Assert.AreEqual(0.30f, FeedbackTuning.SlowAuraFeatherSizeMin, "F4 feather-fall size min");
            Assert.AreEqual(0.40f, FeedbackTuning.SlowAuraFeatherSizeMax, "F4 feather-fall size max");
            Assert.AreEqual(0.95f, FeedbackTuning.SlowAuraFeatherAlpha, "F4 feather-fall alpha");
            Assert.AreEqual(0.95f, FeedbackTuning.SlowAuraFeatherColor.a, 1e-5f, "the feather colour carries the alpha");
            Assert.AreEqual(10f, FeedbackTuning.SlowAuraFeatherRate, "rate unchanged");
            Assert.AreEqual(24, FeedbackTuning.SlowAuraFeatherMaxParticles, "max unchanged");
            Assert.Less(FeedbackTuning.SlowAuraPuffSizeMax, FeedbackTuning.SlowAuraFeatherSizeMin, "the caster foot puffs keep their smaller size");
        }

        [Test]
        public void TheCasterWorstCoincidence_StaysWithinTheChickenCap()
        {
            // The aura system is counted outside the five ChickenVFX families, so the per-chicken live peak is proven from the
            // caps and the shipped data instead of the sum of caps (which no real moment reaches). Two realistic coincidences
            // for a caster whose kit pairs Slow with Speed (the Speedy kits do), the worst a Slow caster can have:
            //  1. sustained: Slow running, then Speed running: aura steady state + foot puffs + the streak cap + stride dust at
            //     the fastest boosted class speed, plus one hit burst on top;
            //  2. the Speed cast landing inside the Slow window: aura + puffs + the whole Sparkle family (cast burst and the Speed
            //     edge sparkles, capped), with the streaks still ramping from zero.
            var registry = AssetDatabase.LoadAssetAtPath<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            Assert.IsNotNull(registry);
            bool kitHoldsBoth = registry.Passives.Any(p => p.Kit != null && p.Kit.Any(k => k is FeatherAuraAbilitySO) && p.Kit.Any(k => k is SpeedBurstAbilitySO));
            Assert.IsTrue(kitHoldsBoth, "no shipped kit pairs Slow with Speed any more: this test can drop the Speed coincidence");

            float fastest = AssetDatabase.FindAssets("t:ChickenStatsSO", new[] { "Assets/_Game/Data/Classes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<ChickenStatsSO>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).Max(c => c.MoveSpeed) * 1.3f;
            int strideDust = Mathf.Min(ChickenVFX.DustMaxParticles,
                Mathf.CeilToInt(fastest * FeedbackTuning.SpeedDustPerMetre * FeedbackTuning.SpeedDustLifeSeconds));
            var vfx = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath).GetComponent<ChickenVFX>());
            int hitBurst = vfx.FindProperty("_hitCount").intValue;

            int aura = SlowAuraRules.SteadyStateLive(false);
            int puffs = System.Math.Min(SlowAuraRules.PuffPeakLive(false), ChickenVFX.FeatherMaxParticles);

            int sustained = aura + puffs + ChickenVFX.StreakMaxParticles + strideDust + hitBurst;
            int speedCastInsideSlow = aura + puffs + ChickenVFX.SparkleMaxParticles;
            Assert.LessOrEqual(sustained, FeedbackTuning.ParticlesPerChickenCap,
                "sustained Slow + Speed + a hit: aura " + aura + " + puffs " + puffs + " + streaks + stride dust " + strideDust + " + hit " + hitBurst + " = " + sustained);
            Assert.LessOrEqual(speedCastInsideSlow, FeedbackTuning.ParticlesPerChickenCap, "Speed cast inside the Slow window = " + speedCastInsideSlow);
        }

        // ---- prefab wiring and the built system --------------------------------------------------

        private static GameObject Instance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            Assert.IsNotNull(prefab);
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            foreach (var t in new[] { typeof(ChickenVFX), typeof(SlowAuraFeedback) })
                t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(go.GetComponent(t), null);
            return go;
        }

        [Test]
        public void TheChickenPrefab_CarriesTheAuraFeedback_WithItsArtWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var c = prefab.GetComponent<SlowAuraFeedback>();
            Assert.IsNotNull(c, "SlowAuraFeedback is not on Chicken.prefab");
            var so = new SerializedObject(c);
            StringAssert.EndsWith("Fx_AuraEdge.png", AssetDatabase.GetAssetPath(so.FindProperty("_auraEdgeSprite").objectReferenceValue));
            StringAssert.EndsWith("Fx_DashedRing.png", AssetDatabase.GetAssetPath(so.FindProperty("_dashedRingTexture").objectReferenceValue));
        }

        [Test]
        public void TheFeatherFallSystem_MatchesTheSpec()
        {
            var go = Instance();
            try
            {
                var ps = go.GetComponent<SlowAuraFeedback>().FeatherSystem;
                Assert.IsNotNull(ps);
                Assert.AreEqual(SlowAuraFeedback.SystemName, ps.name);
                var main = ps.main;
                Assert.AreEqual(24, main.maxParticles);
                Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace, "feathers already in the air stay put when the caster moves");
                Assert.AreEqual(0.03f, main.gravityModifier.constant, 1e-5f);
                Assert.AreEqual(1.6f, main.startLifetime.constant, 1e-5f);
                Assert.AreEqual(0f, main.startSpeed.constant, 1e-5f);
                Assert.IsFalse(main.prewarm);
                Assert.IsFalse(ps.emission.enabled, "off until the slot is active");
                Assert.AreEqual(10f, ps.emission.rateOverTime.constant, 1e-5f);
                var sh = ps.shape;
                Assert.AreEqual(ParticleSystemShapeType.Circle, sh.shapeType);
                Assert.AreEqual(1f, sh.radiusThickness, "emit from the volume");
                Assert.AreEqual(1.6f, sh.position.y, 1e-5f, "spawn height");
                var n = ps.noise;
                Assert.IsTrue(n.enabled);
                Assert.LessOrEqual(n.strength.constant, 0.3f + 1e-5f, "noise cap");
                Assert.AreEqual(0.4f, n.frequency, 1e-5f);
                Assert.AreEqual(ParticleSystemNoiseQuality.Low, n.quality);
                var rend = ps.GetComponent<ParticleSystemRenderer>();
                Assert.AreEqual(ChickenVFX.BodyFxSortingOrder, rend.sortingOrder);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Fx_Particles.mat"), rend.sharedMaterial, "the shared particle material");
                Assert.IsTrue(ps.colorOverLifetime.enabled, "fades in the last quarter");
                StringAssert.Contains("Fx_Feather", AssetDatabase.GetAssetPath(ps.textureSheetAnimation.GetSprite(0)));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheGroundLayers_AreOrderedAndShared()
        {
            var go = Instance();
            try
            {
                var outline = go.GetComponentsInChildren<LineRenderer>(true).Single(l => l.name == "SlowAuraOutline");
                var band = go.GetComponentsInChildren<SpriteRenderer>(true).Single(s => s.name == "SlowAuraBand");
                Assert.AreEqual(0, outline.sortingOrder, "outline: order 0");
                Assert.AreEqual(1, band.sortingOrder, "edge band: order 1");
                Assert.AreEqual(FeedbackTuning.SlowAuraBandY, band.transform.localPosition.y, 1e-5f);
                Assert.AreEqual(LineTextureMode.Tile, outline.textureMode);
                Assert.IsTrue(outline.loop);
                var dashed = outline.sharedMaterial.mainTexture;
                StringAssert.EndsWith("Fx_DashedRing.png", AssetDatabase.GetAssetPath(dashed));
                var second = Instance();
                try
                {
                    var other = second.GetComponentsInChildren<LineRenderer>(true).Single(l => l.name == "SlowAuraOutline");
                    Assert.AreSame(outline.sharedMaterial, other.sharedMaterial, "one dashed material for every aura on the peer");
                }
                finally { Object.DestroyImmediate(second); }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheFeatherColour_IsALavenderTintedCream()
        {
            Color c = FeedbackTuning.SlowAuraFeatherColor;
            Assert.AreEqual(FeedbackTuning.SlowAuraFeatherAlpha, c.a, 1e-5f, "the colour carries the feather alpha (F4 0.95)");
            Assert.Greater(c.b, c.g, "pulled toward the Slow accent (blue above green)");
            Assert.Greater(c.r, 0.75f, "still a cream, not a saturated purple");
        }
    }
}
