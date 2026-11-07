using System.Collections.Generic;
using System.IO;
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
    /// D11 (Peck chip, vfx.md 2.7): the beat plays only when the pile's replicated Amount dropped, belongs to the bird whose Peck window is
    /// open (nearest when several, nobody when none), the kernels fly mound top to beak and fade at the beak under Reduced Motion, the squash
    /// composes with the fill height as one write, and the pile-owned kernel system stays inside its cap on the shared Fx material.
    /// Inputs come from the shipped Peck.asset, the shipped class stats and the shipped tuning constants.
    /// </summary>
    public sealed class PeckBeatTests
    {
        private const string PeckPath = "Assets/_Game/Data/Abilities/Peck.asset";
        private const string PilePrefab = "Assets/_Game/Prefabs/FoodPile.prefab";
        private static string Src(string file) => File.ReadAllText("Assets/_Game/Scripts/Visuals/" + file);

        private static PeckAbilitySO Peck() => AssetDatabase.LoadAssetAtPath<PeckAbilitySO>(PeckPath);

        [Test]
        public void SpecConstants_ArePinned()
        {
            Assert.AreEqual(5, FeedbackTuning.PeckKernelCount);
            Assert.AreEqual(0.25f, FeedbackTuning.PeckKernelSeconds);
            Assert.AreEqual(3f, FeedbackTuning.PeckKernelSpeed);
            Assert.AreEqual(0.5f, FeedbackTuning.PeckKernelGravity);
            Assert.AreEqual(0.12f, FeedbackTuning.PeckKernelSize);
            Assert.AreEqual(5, FeedbackTuning.PeckKernelSortingOrder);
            Assert.AreEqual(8, FeedbackTuning.PeckKernelMaxPerPile);
            Assert.AreEqual(0.9f, FeedbackTuning.PeckBeakDistance);
            Assert.AreEqual(3, FeedbackTuning.PeckPuffCount);
            Assert.AreEqual(0.25f, FeedbackTuning.PeckPuffSeconds);
            Assert.AreEqual(new Color(0.9f, 0.85f, 0.65f, 1f), FeedbackTuning.PeckPuffColor);
            Assert.AreEqual(0.94f, FeedbackTuning.PeckSquashScaleY);
            Assert.AreEqual(0.12f, FeedbackTuning.PeckSquashSeconds);
        }

        [Test]
        public void TheBeat_FitsInsideTheShippedPeckWindow()
        {
            var peck = Peck();
            Assert.IsNotNull(peck, PeckPath);
            Assert.Greater(peck.Duration, FeedbackTuning.PeckKernelSeconds);
            Assert.Greater(peck.Duration, FeedbackTuning.PeckSquashSeconds);
            // Four piles of kernels (the 4-pile arena) take a small, fixed slice of the arena budget (vfx.md 0.2).
            Assert.LessOrEqual(4 * FeedbackTuning.PeckKernelMaxPerPile, FeedbackTuning.ParticlesPerArenaCap / 10);
            // One bite never overflows the pile system: the shipped Peck can bite again after its cooldown only.
            Assert.LessOrEqual(FeedbackTuning.PeckKernelCount, FeedbackTuning.PeckKernelMaxPerPile);
        }

        // ---- Emits only when Amount dropped ----

        [TestCase("Fatty")]
        [TestCase("Speedy")]
        [TestCase("Warrior")]
        public void ABite_OfTheShippedClassAmount_IsADrop(string cls)
        {
            var stats = AssetDatabase.LoadAssetAtPath<ChickenStatsSO>("Assets/_Game/Data/Classes/" + cls + ".asset");
            Assert.IsNotNull(stats, cls);
            Assert.Greater(stats.PeckAmount, PeckBeatRules.MinDrop);
            Assert.IsTrue(PeckBeatRules.AmountDropped(40f, 40f - stats.PeckAmount));
        }

        [Test]
        public void NoDrop_NoBeat_AndTheFirstObservationOnlySeedsTheBaseline()
        {
            Assert.IsFalse(PeckBeatRules.AmountDropped(float.NaN, 12f), "late join: baseline, not a bite");
            Assert.IsFalse(PeckBeatRules.AmountDropped(12f, 12f), "empty-pile press: Amount did not change");
            Assert.IsFalse(PeckBeatRules.AmountDropped(12f, 15f), "a refill is not a bite");
            Assert.IsFalse(PeckBeatRules.AmountDropped(12f, 12f - PeckBeatRules.MinDrop * 0.5f), "float noise");
        }

        [Test]
        public void ThePileBeat_IsOnlyReachedThroughTheAmountDropEdge_AndAPecker()
        {
            string src = Src("FoodPileVisuals.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"\bPlayPeckBeat\(").Count - 1, "one caller besides the definition");
            StringAssert.Contains("bool dropped = PeckBeatRules.AmountDropped(_lastAmount, amount);", src);
            StringAssert.Contains("if (!dropped) return;", src);
            StringAssert.Contains("if (pecker != null) PlayPeckBeat(pecker);", src);
            StringAssert.Contains("ObservePeck(_pile.Amount);", src);
        }

        // ---- Attribution ----

        [Test]
        public void Attribution_GoesToTheOpenWindow_NearestWhenSeveral()
        {
            Assert.AreEqual(1, PeckBeatRules.PickPecker(new[] { 0.9f, 0.4f, 0.2f }, new[] { true, true, false }), "nearest of the open windows");
            Assert.AreEqual(0, PeckBeatRules.PickPecker(new[] { 0.1f, 0.9f }, new[] { true, true }));
            Assert.AreEqual(1, PeckBeatRules.PickPecker(new[] { 0.1f, 0.9f }, new[] { false, true }), "a nearer bird without a window is not the pecker");
        }

        [Test]
        public void NoPecker_NoKernels()
        {
            Assert.AreEqual(-1, PeckBeatRules.PickPecker(new[] { 0.5f, 0.2f }, new[] { false, false }));
            Assert.AreEqual(-1, PeckBeatRules.PickPecker(new float[0], new bool[0]));
        }

        [Test]
        public void TheWindowIsTheShippedPeckSlot()
        {
            string src = Src("FoodPileVisuals.cs");
            StringAssert.Contains("abilities.IsSlotActive(i) && abilities.GetSlot(i) is PeckAbilitySO", src);
            Assert.IsInstanceOf<PeckAbilitySO>(Peck());
        }

        // ---- Flight, beak, Reduced Motion ----

        [Test]
        public void Kernels_FlyFromTheMoundTopTowardTheBeak_ArrivingWhereTheBeakIs()
        {
            Vector3 top = new Vector3(0f, 0.9f, 0f);
            Vector3 beak = PeckBeatRules.BeakPoint(new Vector3(0.5f, 0.88f, 0f), Vector3.left, 0.08f);
            Assert.AreEqual(0.5f - FeedbackTuning.PeckBeakDistance, beak.x, 1e-5f, "0.9 m ahead of the pecker");
            Assert.AreEqual(0.08f + FeedbackTuning.PeckBeakHeight, beak.y, 1e-5f, "beak height is measured from the grass, not the capsule-centre root");
            Vector3 v = PeckBeatRules.KernelVelocity(top, beak, false);
            float life = FeedbackTuning.PeckKernelSeconds;
            Vector3 toBeak = (beak - top).normalized;
            // Horizontal component is the aimed 3 m/s along the line; vertical lift cancels the system gravity over the flight.
            Assert.AreEqual(toBeak.x * FeedbackTuning.PeckKernelSpeed, v.x, 1e-4f);
            float endY = top.y + v.y * life - 0.5f * FeedbackTuning.PeckKernelGravity * 9.81f * life * life;
            Assert.AreEqual(top.y + toBeak.y * FeedbackTuning.PeckKernelSpeed * life, endY, 1e-4f, "gravity is compensated");
            Assert.Greater(Vector3.Dot(new Vector3(v.x, 0f, v.z).normalized, new Vector3(toBeak.x, 0f, toBeak.z).normalized), 0.999f);
        }

        [Test]
        public void ReducedMotion_KernelsStartAtTheBeak_DoNotTravel_AndCountsHalve()
        {
            Vector3 top = new Vector3(0f, 0.9f, 0f), beak = new Vector3(-0.4f, 0.5f, 0f);
            Assert.AreEqual(Vector3.zero, PeckBeatRules.KernelVelocity(top, beak, true));
            Assert.AreEqual(beak, PeckBeatRules.KernelStart(top, beak, true));
            Assert.AreEqual(top, PeckBeatRules.KernelStart(top, beak, false));
            Assert.AreEqual(5, PeckBeatRules.KernelCount(false));
            Assert.AreEqual(3, PeckBeatRules.KernelCount(true));
            Assert.AreEqual(3, PeckBeatRules.PuffCount(false));
            Assert.AreEqual(2, PeckBeatRules.PuffCount(true));
            Assert.Less(PeckBeatRules.KernelCount(true), PeckBeatRules.KernelCount(false));
        }

        // ---- Squash composition ----

        [Test]
        public void Squash_StartsAt094_EasesToOneOverTheShippedWindow_AndIsNoneUnderReducedMotion()
        {
            Assert.AreEqual(0.94f, PeckBeatRules.SquashFactor(0f, false), 1e-6f);
            float prev = 0f;
            for (float t = 0f; t < FeedbackTuning.PeckSquashSeconds; t += 0.002f)
            {
                float f = PeckBeatRules.SquashFactor(t, false);
                Assert.GreaterOrEqual(f, prev - 1e-6f, "monotone back to 1 at t=" + t);
                Assert.LessOrEqual(f, 1f);
                prev = f;
            }
            Assert.AreEqual(1f, PeckBeatRules.SquashFactor(FeedbackTuning.PeckSquashSeconds, false));
            Assert.AreEqual(1f, PeckBeatRules.SquashFactor(5f, false));
            Assert.AreEqual(1f, PeckBeatRules.SquashFactor(0f, true));
            Assert.AreEqual(1f, PeckBeatRules.SquashFactor(0.05f, true));
        }

        [TestCase(1f)]
        [TestCase(0.5f)]
        [TestCase(0.1f)]
        public void Squash_ComposesWithTheFillHeight_AsOneWrite(float ratio)
        {
            var visuals = AssetDatabase.LoadAssetAtPath<GameObject>(PilePrefab).GetComponent<FoodPileVisuals>();
            visuals.MoundPose(false, ratio, out var plain, out var plainPos);
            float squash = PeckBeatRules.SquashFactor(0f, false);
            visuals.MoundPose(false, ratio, out var squashed, out var squashedPos, squash);
            Assert.AreEqual(plain.y * 0.94f, squashed.y, 1e-5f, "fill height x 0.94");
            Assert.AreEqual(plain.x, squashed.x);
            Assert.AreEqual(plain.z, squashed.z);
            Assert.AreEqual(0f, plainPos.y, 1e-6f);
            Assert.AreEqual(0f, squashedPos.y, 1e-6f, "the widest ring stays on the ground");
            visuals.MoundPose(false, ratio, out var done, out _, PeckBeatRules.SquashFactor(1f, false));
            Assert.AreEqual(plain, done, "the squash ends exactly on the fill scale");
            visuals.MoundPose(true, ratio, out var empty, out _, squash);
            Assert.AreEqual(Vector3.one, empty, "the scattered-grain mesh is never squashed");
        }

        [Test]
        public void TheMoundScale_HasOneWriter_TheFoodPileLateUpdate()
        {
            string src = Src("FoodPileVisuals.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(src, @"_meshRoot\.localScale\s*=").Count, "one write of the mound scale");
            StringAssert.Contains("MoundPose(empty && _emptyMesh != null, ratio, out var scale, out var pos, squash);", src);
        }

        // ---- Pile system cap, material, sprite, gates ----

        [Test]
        public void ThePileKernelSystem_IsCapped_OnTheSharedMaterial_InCornSpriteMode()
        {
            var chickenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var chicken = Object.Instantiate(chickenPrefab);
            var pile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PilePrefab));
            chicken.hideFlags = pile.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var vfx = chicken.GetComponent<ChickenVFX>();
                typeof(ChickenVFX).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vfx, null);
                var visuals = pile.GetComponent<FoodPileVisuals>();
                var ensure = typeof(FoodPileVisuals).GetMethod("EnsureKernelSystem", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsTrue((bool)ensure.Invoke(visuals, new object[] { vfx }), "the pile builds its kernel system once the sprite and material exist");
                var ps = visuals.KernelSystem;
                Assert.IsNotNull(ps);
                Assert.AreEqual(FoodPileVisuals.KernelSystemName, ps.name);
                Assert.IsTrue((bool)ensure.Invoke(visuals, new object[] { vfx }));
                Assert.AreEqual(1, pile.GetComponentsInChildren<ParticleSystem>(true).Length, "a second call never builds a second system");

                var main = ps.main;
                Assert.AreEqual(FeedbackTuning.PeckKernelMaxPerPile, main.maxParticles);
                Assert.IsFalse(main.prewarm);
                Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Fx_Particles.mat"), r.sharedMaterial, "the shared Fx material");
                Assert.AreEqual(FeedbackTuning.PeckKernelSortingOrder, r.sortingOrder);
                var ts = ps.textureSheetAnimation;
                Assert.IsTrue(ts.enabled);
                Assert.AreEqual(ParticleSystemAnimationMode.Sprites, ts.mode);
                StringAssert.EndsWith("Fx_Corn.png", AssetDatabase.GetAssetPath(ts.GetSprite(0)));
                Assert.IsFalse(ps.trails.enabled);
                Assert.IsFalse(ps.collision.enabled);
                Assert.IsFalse(ps.subEmitters.enabled);
                Assert.IsFalse(ps.lights.enabled);

                // Three bites inside one lifetime (worst case at the shipped cooldowns is one) still never exceed the cap.
                var ep = new ParticleSystem.EmitParams { startLifetime = 5f, startSize = 0.12f };
                for (int bite = 0; bite < 3; bite++)
                    for (int k = 0; k < FeedbackTuning.PeckKernelCount; k++) ps.Emit(ep, 1);
                Assert.LessOrEqual(ps.particleCount, FeedbackTuning.PeckKernelMaxPerPile);
            }
            finally
            {
                Object.DestroyImmediate(chicken);
                Object.DestroyImmediate(pile);
            }
        }

        [Test]
        public void EveryPeckEmitter_AsksTheHiddenBirdGate()
        {
            string pile = Src("FoodPileVisuals.cs");
            int begin = pile.IndexOf("private void PlayPeckBeat(", System.StringComparison.Ordinal);
            int end = pile.IndexOf("private Vector3 MoundTop", System.StringComparison.Ordinal);
            string body = pile.Substring(begin, end - begin);
            Assert.Less(body.IndexOf("FoodChunkFlight.IsQuiet(pecker)", System.StringComparison.Ordinal), body.IndexOf("_chipPS.Emit(", System.StringComparison.Ordinal), "the gate comes before the first emission");
            Assert.Less(body.IndexOf("FoodChunkFlight.IsQuiet(pecker)", System.StringComparison.Ordinal), body.IndexOf("EmitPeckPuff(", System.StringComparison.Ordinal));
            Assert.Less(body.IndexOf("FoodChunkFlight.IsQuiet(pecker)", System.StringComparison.Ordinal), body.IndexOf("_squashStart = ", System.StringComparison.Ordinal), "a hidden pecker does not squash the mound either");
            StringAssert.Contains("FoodChunkFlight.IsQuiet(_controller)", Src("ChickenVFX.cs").Substring(Src("ChickenVFX.cs").IndexOf("public void EmitPeckPuff(", System.StringComparison.Ordinal)));
        }

        [Test]
        public void ThePuff_IsThreeStrawCreamDust_FromTheChickenDustFamily()
        {
            var chicken = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath));
            chicken.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var vfx = chicken.GetComponent<ChickenVFX>();
                typeof(ChickenVFX).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vfx, null);
                var e = (ChickenVFX.FxEmit)typeof(ChickenVFX).GetField("_peckPuff", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vfx);
                Assert.AreEqual(3, e.Count);
                Assert.AreEqual(FeedbackTuning.PeckPuffColor, e.Color);
                Assert.AreEqual(FeedbackTuning.PeckPuffSeconds, e.LifeMin);
                StringAssert.Contains("EmitDustAt(in e, contact);", Src("ChickenVFX.cs"));
                var dust = chicken.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == ChickenVFX.DustSystemName);
                Assert.AreEqual(2, dust.GetComponent<ParticleSystemRenderer>().sortingOrder, "ground band");
            }
            finally { Object.DestroyImmediate(chicken); }
        }
    }
}
