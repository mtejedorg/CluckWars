using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Gameplay;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A3 "Ground overlays on the ground": flat overlays parented to a chicken (identity
    /// ring, rival ring, state blobs, slow streaks, wind-up glow, target bracket/glyph) sit on
    /// the chicken's feet, not at its root. The root is at the CharacterController capsule's
    /// mid-height (~0.8 m up), so a local Y of 0.02 floated every "ground" sprite ~0.8 m high.
    /// </summary>
    public class Phase8ChunkA3Tests
    {
        private const string ChickenPrefab = "Assets/_Game/Prefabs/Chicken.prefab";
        private const float Tolerance = 0.05f;

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

        private GameObject Spawn(float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            Assert.IsNotNull(prefab, "Chicken prefab missing.");
            _chicken = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _chicken.transform.position = new Vector3(3f, 0.8f, -2f);
            _chicken.transform.localScale = Vector3.one * scale;

            // Awake does not run on edit-mode instantiation of non-[ExecuteAlways] scripts;
            // run the overlay builders the way a spawn would.
            foreach (var type in new[]
            {
                typeof(ChickenWorldBars), typeof(RivalIndicator), typeof(ChickenStateOverlays),
                typeof(ControlStateVFX), typeof(TargetHighlight),
            })
            {
                var c = _chicken.GetComponent(type);
                Assert.IsNotNull(c, $"{type.Name} is not on the chicken prefab.");
                type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(c, null);
            }
            return _chicken;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        [TestCase(1f, Tolerance)]
        [TestCase(0.85f, Tolerance)]
        // The lift above the grass is local and scales with the chicken (0.045 * 1.35 ~ 0.06).
        [TestCase(1.35f, 0.08f)]
        public void EveryGroundOverlay_SitsAtTheFeet_NotTheRoot(float scale, float tolerance)
        {
            var root = Spawn(scale);
            float groundY = ChickenGround.GroundWorldY(root.GetComponent<ChickenController>());
            Assert.Less(groundY, root.transform.position.y - 0.5f * scale,
                "The grass should be well below the root pivot (capsule mid-height).");

            foreach (string name in GroundOverlays)
            {
                var t = Find(root, name);
                Assert.IsNotNull(t, $"{name} was not built.");
                float lift = t.position.y - groundY;
                Assert.GreaterOrEqual(lift, 0f, $"{name} is below the grass ({lift:0.000}).");
                Assert.Less(lift, tolerance,
                    $"{name} floats {lift:0.000} m above the grass (root is {root.transform.position.y - groundY:0.00} m up).");
            }
        }

        [Test]
        public void Anchor_FollowsAScaleChangeAfterTheOverlaysWereBuilt()
        {
            // ChickenController.Spawned applies the class scale AFTER the overlays' Awake.
            var root = Spawn(1f);
            root.transform.localScale = Vector3.one * 1.35f;
            ChickenGround.Refresh(root.GetComponent<ChickenController>());
            float groundY = ChickenGround.GroundWorldY(root.GetComponent<ChickenController>());
            float ring = Find(root, "IdentityRing").position.y - groundY;
            Assert.That(ring, Is.InRange(0f, 0.05f), "ring must stay on the grass after a scale change");
        }

        [Test]
        public void Helper_MatchesCapsuleBottomMinusSkin_AndNeverFallsBackToTheRoot()
        {
            var go = new GameObject("cc");
            try
            {
                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.6f; cc.center = Vector3.zero;
                cc.skinWidth = 0.08f;
                Assert.AreEqual(-0.8f, ChickenGround.LocalFeetY(cc), 1e-5f);
                Assert.AreEqual(-0.88f, ChickenGround.LocalGroundY(cc), 1e-5f);
                cc.center = new Vector3(0f, 0.1f, 0f);
                Assert.AreEqual(-0.7f, ChickenGround.LocalFeetY(cc), 1e-5f);
                go.transform.localScale = Vector3.one * 2f;
                Assert.AreEqual(-0.74f, ChickenGround.LocalGroundY(cc), 1e-5f, "skin is a world distance");
            }
            finally { Object.DestroyImmediate(go); }
            Assert.Less(ChickenGround.LocalFeetY((CharacterController)null), -0.5f,
                "No CC must fall back to the prefab's feet offset, never to the root (0).");
        }
    }
}
