using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk C ring check: the player-colour foot ring (and every other ground overlay) must sit on the grass
    /// for ALL FOUR classes, not just the unit-scale Warrior. Loads the real prefab and the real per-class stats from
    /// the registry, applies the class scale the way <c>ChickenController.Spawned</c> does (scale, then
    /// <c>ChickenGround.Refresh</c>), and measures the ground independently of <c>ChickenGround</c>: capsule bottom in
    /// world space minus the resting skin gap.
    /// </summary>
    public class Phase8ChunkCRingTests
    {
        private const string ChickenPrefab = "Assets/_Game/Prefabs/Chicken.prefab";

        // Overlays author their own small anchor-local lift (rings 0.02, blobs / streaks 0.04, bracket 0.045); the world lift is that
        // times the class scale (0.017 .. 0.061 over the four classes). Anything outside this band is under the grass or floating.
        private const float MinLift = 0.01f;
        private const float MaxLift = 0.08f;
        private const float RingLocalLift = 0.02f;

        private static readonly string[] GroundOverlays =
        {
            "IdentityRing", "RivalRing", "SlowBlob", "RootBlob", "RootShackle", "SlowStreaks",
            "AbilityWindupGlow", "TargetBracket", "TargetNoEffectGlyph",
        };

        private GameObject _chicken;

        [TearDown]
        public void TearDown()
        {
            if (_chicken != null) Object.DestroyImmediate(_chicken);
        }

        private GameObject SpawnAs(ChickenClass cls)
        {
            var registry = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsTrue(registry.TryGet(cls, out var entry), $"{cls} missing from the registry");
            Assert.IsNotNull(entry.Stats, $"{cls} has no stats");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            Assert.IsNotNull(prefab, "Chicken prefab missing.");
            _chicken = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // Overlays build in Awake at the prefab's scale; the class scale arrives afterwards in Spawned.
            foreach (var type in new[]
            {
                typeof(ChickenWorldBars), typeof(RivalIndicator), typeof(ChickenStateOverlays),
                typeof(ControlStateVFX), typeof(TargetHighlight),
            })
            {
                var c = _chicken.GetComponent(type);
                Assert.IsNotNull(c, $"{type.Name} is not on the chicken prefab.");
                type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);
            }

            _chicken.transform.localScale = Vector3.one * entry.Stats.Scale;
            ChickenGround.Refresh(_chicken.GetComponent<ChickenController>());
            return _chicken;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        [TestCase(ChickenClass.Warrior)]
        [TestCase(ChickenClass.Speedy)]
        [TestCase(ChickenClass.Fatty)]
        [TestCase(ChickenClass.Assassin)]
        public void EveryClass_GroundOverlays_SitJustAboveTheGrass(ChickenClass cls)
        {
            var root = SpawnAs(cls);
            var cc = root.GetComponent<CharacterController>();
            float scale = root.transform.lossyScale.y;

            // Independent ground: capsule bottom (world) minus the resting skin gap (a world distance).
            float bottom = root.transform.position.y + scale * (cc.center.y - cc.height * 0.5f);
            float grass = bottom - cc.skinWidth;

            foreach (string name in GroundOverlays)
            {
                var t = Find(root, name);
                Assert.IsNotNull(t, $"{cls}: {name} was not built.");
                float lift = t.position.y - grass;
                Assert.That(lift, Is.InRange(MinLift, MaxLift),
                    $"{cls} (scale {scale:0.##}): {name} is {lift:0.000} m above the grass; negative means under it.");
            }

            // The player-colour rings in particular: exactly their authored 0.02 above the grass.
            foreach (string ring in new[] { "IdentityRing", "RivalRing" })
                Assert.AreEqual(RingLocalLift * scale, Find(root, ring).position.y - grass, 0.003f,
                    $"{cls}: {ring} is not at grass + {RingLocalLift} * scale.");
        }
    }
}
