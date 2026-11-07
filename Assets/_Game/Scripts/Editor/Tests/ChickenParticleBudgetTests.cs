using System.Linq;
using System.Reflection;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace CluckWars.Tests
{
    /// <summary>
    /// D1 (textured particles): every chicken ParticleSystem renders the one shared <c>Fx_Particles.mat</c> in sprite mode over
    /// <c>Fx_Atlas</c>, carries an explicit maxParticles within the vfx.md 0.2 caps, and the per-chicken system count stays
    /// within the budget. The systems are built in <c>Awake</c> at runtime (the prefab holds none), so the test wakes the
    /// two builders (<see cref="ChickenVFX"/>, <see cref="HitFeedback"/>) on a throwaway instance of the shipped prefab.
    /// </summary>
    public sealed class ChickenParticleBudgetTests
    {
        private const string MaterialPath = "Assets/_Game/Art/Materials/Fx_Particles.mat";
        private const string AtlasPath = "Assets/_Game/Art/UI/Fx/Fx_Atlas.spriteatlas";
        private const string FxDir = "Assets/_Game/Art/UI/Fx";

        private GameObject _chicken;
        private ParticleSystem[] _systems;

        [SetUp]
        public void Instantiate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            Assert.IsNotNull(prefab, "Chicken.prefab missing");
            _chicken = Object.Instantiate(prefab);
            _chicken.hideFlags = HideFlags.HideAndDontSave;
            // Awake does not run in EditMode: wake the two components that build particle systems.
            foreach (var t in new[] { typeof(ChickenVFX), typeof(HitFeedback) })
            {
                var c = _chicken.GetComponent(t);
                Assert.IsNotNull(c, t.Name + " missing on Chicken.prefab");
                t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
            }
            _systems = _chicken.GetComponentsInChildren<ParticleSystem>(true);
        }

        [TearDown]
        public void Cleanup()
        {
            if (_chicken != null) Object.DestroyImmediate(_chicken);
        }

        private static Material Shared() => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        [Test]
        public void EveryParticleSystem_UsesTheSharedFxMaterial_NotAnInstance()
        {
            var shared = Shared();
            Assert.IsNotNull(shared, "Fx_Particles.mat is missing");
            Assert.IsNotEmpty(_systems);
            foreach (var ps in _systems)
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Assert.AreSame(shared, r.sharedMaterial, ps.name + " does not render the shared Fx_Particles.mat (per-instance or untextured material)");
            }
        }

        [Test]
        public void EveryParticleSystem_IsInSpriteMode_WithTheFxSprites()
        {
            foreach (var ps in _systems)
            {
                var ts = ps.textureSheetAnimation;
                Assert.IsTrue(ts.enabled, ps.name + ": Texture Sheet Animation is off (untextured square)");
                Assert.AreEqual(ParticleSystemAnimationMode.Sprites, ts.mode, ps.name);
                Assert.GreaterOrEqual(ts.spriteCount, 1, ps.name + " has no sprite");
                for (int i = 0; i < ts.spriteCount; i++)
                {
                    var sp = ts.GetSprite(i);
                    Assert.IsNotNull(sp, ps.name + " sprite " + i);
                    StringAssert.StartsWith(FxDir, AssetDatabase.GetAssetPath(sp), ps.name + ": sprite is not an Fx_* sprite");
                }
            }
        }

        [Test]
        public void EveryParticleSystem_HasExplicitMaxParticles_WithinTheCaps()
        {
            foreach (var ps in _systems)
            {
                int max = ps.main.maxParticles;
                Assert.Greater(max, 0, ps.name);
                Assert.LessOrEqual(max, FeedbackTuning.ParticlesPerChickenCap, ps.name + " alone exceeds the per-chicken live cap");
                Assert.AreNotEqual(1000, max, ps.name + ": maxParticles is still the Unity default (1000), i.e. not set explicitly");
                Assert.IsFalse(ps.main.prewarm, ps.name + ": prewarm must be off");
                Assert.IsFalse(ps.trails.enabled, ps.name + ": Trails module is forbidden");
                Assert.IsFalse(ps.subEmitters.enabled, ps.name + ": Sub-emitters are forbidden");
                Assert.IsFalse(ps.collision.enabled, ps.name + ": Collision is forbidden");
                Assert.IsFalse(ps.lights.enabled, ps.name + ": Lights are forbidden");
                Assert.AreNotEqual(ParticleSystemRenderMode.Mesh, ps.GetComponent<ParticleSystemRenderer>().renderMode, ps.name + ": mesh particles are forbidden");
            }
        }

        [Test]
        public void PerChickenSystemCount_IsWithinTheBudget()
        {
            Assert.LessOrEqual(_systems.Length, FeedbackTuning.ParticleSystemsPerChickenCap,
                "ParticleSystems per chicken: " + string.Join(", ", _systems.Select(p => p.name)));
        }

        [Test]
        public void ParticleSystems_AreOnePerSpriteFamily_WithinTheTotalBudget()
        {
            var names = _systems.Select(p => p.name).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { ChickenVFX.DustSystemName, ChickenVFX.FeatherSystemName, ChickenVFX.SparkleSystemName, ChickenVFX.ShardSystemName, ChickenVFX.StreakSystemName }, names,
                "D4 families: Dust, Feather, Sparkle, Shard, Streak (Kernel arrives with its item)");
            int total = _systems.Sum(p => p.main.maxParticles);
            Assert.LessOrEqual(total, FeedbackTuning.ParticlesPerChickenCap,
                "sum of maxParticles over the families must stay within the per-chicken cap");
            Assert.AreEqual(2, _systems.Single(p => p.name == ChickenVFX.DustSystemName).GetComponent<ParticleSystemRenderer>().sortingOrder, "dust is the ground band");
            var shard = _systems.Single(p => p.name == ChickenVFX.ShardSystemName);
            Assert.AreEqual(ChickenVFX.BodyFxSortingOrder, shard.GetComponent<ParticleSystemRenderer>().sortingOrder, "shards are body-height FX");
            Assert.AreEqual(3, shard.main.maxParticles, "the three egg shards");
            Assert.AreEqual(1f, shard.main.gravityModifier.constant, "shards keep real gravity 1");
            Assert.IsTrue(shard.rotationOverLifetime.enabled, "shards spin");
            var streak = _systems.Single(p => p.name == ChickenVFX.StreakSystemName);
            var streakRend = streak.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(ChickenVFX.BodyFxSortingOrder, streakRend.sortingOrder, "streaks are body-height FX");
            Assert.AreEqual(ParticleSystemRenderMode.Stretch, streakRend.renderMode, "streaks are Stretched Billboards");
            Assert.AreEqual(0.12f, streakRend.velocityScale, 1e-4f, "stretch speed scale 0.12");
            Assert.AreEqual(ParticleSystemSimulationSpace.World, streak.main.simulationSpace, "left behind in world space");
            Assert.IsTrue(streak.sizeOverLifetime.enabled, "size 0.30 -> 0");
            Assert.AreEqual(ChickenVFX.StreakMaxParticles, streak.main.maxParticles);
            StringAssert.Contains("Fx_SpeedLine", AssetDatabase.GetAssetPath(streak.textureSheetAnimation.GetSprite(0)));
            Assert.AreEqual(64, total, "the five families are pinned to the 64 per-chicken cap (7+16+20+3+18)");
        }

        [Test]
        public void TheStunOrbitSystem_IsGone()
        {
            Assert.IsFalse(_systems.Any(p => p.name == "VFX_Stun"),
                "VFX_Stun duplicated the stars on removal and was deleted in D1");
        }

        [Test]
        public void SharedMaterial_IsAlphaBlendedUnlit_WithNoSoftParticlesOrDepthFade()
        {
            var m = Shared();
            Assert.IsNotNull(m);
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", m.shader.name);
            Assert.AreEqual(1f, m.GetFloat("_Surface"), "transparent");
            Assert.AreEqual(0f, m.GetFloat("_Blend"), "alpha blend");
            Assert.AreEqual(0f, m.GetFloat("_ZWrite"));
            Assert.AreEqual(0f, m.GetFloat("_SoftParticlesEnabled"), "soft particles read the depth texture: off on mobile");
            Assert.AreEqual(0f, m.GetFloat("_CameraFadingEnabled"), "no camera depth fade");
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, m.renderQueue);
        }

        [Test]
        public void FxAtlas_PacksTheFxSprites_WithoutRotationOrTightPacking()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(atlas, "Fx_Atlas.spriteatlas is missing");
            var packing = atlas.GetPackingSettings();
            Assert.IsFalse(packing.enableRotation, "rotated sprites break sheet/sprite-mode UVs");
            Assert.IsFalse(packing.enableTightPacking);
            Assert.AreEqual(4, packing.padding);
            Assert.IsTrue(atlas.IsIncludeInBuild());
            Assert.AreEqual(1024, atlas.GetPlatformSettings("DefaultTexturePlatform").maxTextureSize);

            var packed = atlas.GetPackables().Select(AssetDatabase.GetAssetPath).ToHashSet();
            // Every Fx sprite a particle (or sprite renderer) draws must be in it. The tiled dashed ring (Default texture
            // type), the egg speckle tile and the egg crack mask are shader-sampled data textures, not sprites: out.
            string[] data = { "Fx_DashedRing", "Fx_EggSpeckle", "Fx_EggCrackMask" };
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { FxDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (data.Contains(name)) { Assert.IsFalse(packed.Contains(path), name + " is a data texture and must stay out of the atlas"); continue; }
                Assert.IsTrue(packed.Contains(path), name + " is missing from Fx_Atlas");
            }
        }
    }
}
