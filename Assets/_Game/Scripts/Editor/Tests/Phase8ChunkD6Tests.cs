using System;
using System.IO;
using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision D6 (a): the Speedy model is drawn x1.3 taller and slightly leaner, in the ART.md Speedy orange.
    /// VISUAL ONLY: the CharacterController, the networked root scale and every stat are untouched, so the hitbox and
    /// the balance are exactly what they were. The scale lives on the registry entry (<c>ModelScale</c>) and is applied
    /// to the model root about the feet; the overlays above the head (nameplate, cargo bars, badges, stun stars) are
    /// lifted by <c>controllerHeight * (scaleY - 1)</c>.
    /// </summary>
    /// <remarks>
    /// Colour: the model is the baked albedo atlas times the per-class wash (ChickenVisuals.ApplyTint lerps white to
    /// TintColor by TintStrength; the project is in GAMMA colour space, so that is a plain sRGB multiply). The test
    /// decodes each class's atlas, takes the lit (lightest-decile) texel mean times the wash, and holds the Speedy away
    /// from the Fatty yellow and the D8 crate brown. Rendered numbers (match camera): Captures/env3d_after/C/D6/.
    /// </remarks>
    public class Phase8ChunkD6Tests
    {
        private const string ChickenPrefab = "Assets/_Game/Prefabs/Chicken.prefab";
        private const string SpeedyStatsPath = "Assets/_Game/Data/Classes/Speedy.asset";

        // Rendered (match camera, sunlit face) D8 crate colour, STATE.md D8.
        private static readonly Color CrateRendered = new Color32(154, 107, 76, 255);

        private static ChickenClassRegistrySO Registry() =>
            TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

        private static ChickenClassRegistrySO.Entry Entry(ChickenClass c)
        {
            Assert.IsTrue(Registry().TryGet(c, out var e), $"{c} missing from the registry");
            return e;
        }

        // ---- the visual scale -------------------------------------------------------------------------

        [Test]
        public void Speedy_ModelScale_IsTallerAndLeaner()
        {
            var s = Entry(ChickenClass.Speedy).ResolvedModelScale;
            Assert.AreEqual(1.3f, s.y, 1e-4f, "height x1.3 (Maestro D6 a)");
            Assert.AreEqual(1.2f, s.x, 1e-4f, "width");
            Assert.AreEqual(1.2f, s.z, 1e-4f, "depth");
            Assert.Less(s.x, s.y, "'slightly leaner': width grows less than height");
            Assert.Greater(s.x / s.y, 0.85f, "...but only slightly (not a stretched model)");
        }

        [TestCase(ChickenClass.Warrior)]
        [TestCase(ChickenClass.Fatty)]
        [TestCase(ChickenClass.Assassin)]
        public void OtherClasses_ModelScale_IsUnchanged(ChickenClass cls) =>
            Assert.AreEqual(Vector3.one, Entry(cls).ResolvedModelScale, $"{cls} must keep its authored scale");

        [Test]
        public void UnauthoredModelScale_ResolvesToOne()
        {
            var e = new ChickenClassRegistrySO.Entry();
            Assert.AreEqual(Vector3.zero, e.ModelScale);
            Assert.AreEqual(Vector3.one, e.ResolvedModelScale, "an entry saved before the field existed must not collapse the model");
        }

        // ---- hitbox and stats unchanged ---------------------------------------------------------------

        [Test]
        public void CharacterController_Dimensions_AreUnchanged()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            var cc = prefab.GetComponent<CharacterController>();
            Assert.AreEqual(1.6f, cc.height, 1e-4f);
            Assert.AreEqual(0.4f, cc.radius, 1e-4f);
            Assert.AreEqual(Vector3.zero, cc.center);
            Assert.AreEqual(0.08f, cc.skinWidth, 1e-4f);
        }

        [Test]
        public void Speedy_Stats_AreUnchanged()
        {
            var st = AssetDatabase.LoadAssetAtPath<ChickenStatsSO>(SpeedyStatsPath);
            Assert.IsNotNull(st);
            Assert.AreEqual(0.85f, st.Scale, 1e-4f, "root (networked/collider) scale is NOT the visual scale");
            Assert.AreEqual(12.15f, st.MoveSpeed, 1e-4f);
            Assert.AreEqual(720f, st.TurnSpeed, 1e-4f);
            Assert.AreEqual(10, st.CargoCapacity);
            Assert.AreEqual(3f, st.PeckAmount, 1e-4f);
            Assert.AreEqual(0.71f, st.PeckCooldown, 1e-4f);
            Assert.AreEqual(Entry(ChickenClass.Speedy).Stats, st, "registry still points at the same stats asset");
        }

        [Test]
        public void Speedy_RigIsStillGenericWithAllClips()
        {
            var e = Entry(ChickenClass.Speedy);
            Assert.IsNotNull(e.ModelAvatar);
            Assert.IsFalse(e.ModelAvatar.isHuman, "Generic rig guardrail (a Humanoid avatar sinks the bird through the floor)");
            Assert.IsTrue(e.Clips.IsComplete, $"missing clip: {e.Clips.FirstMissing}");
        }

        // ---- overlays follow the bigger model ---------------------------------------------------------

        [Test]
        public void HeadLift_RaisesNameplateBarsBadgesAndStars_ByTheModelHeightGrowth()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefab);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                foreach (var type in new[] { typeof(ChickenNameplate), typeof(ChickenWorldBars), typeof(ChickenStatusBadges), typeof(ChickenStateOverlays) })
                {
                    var c = root.GetComponent(type);
                    Assert.IsNotNull(c, type.Name);
                    type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);
                }
                float lift = root.GetComponent<CharacterController>().height * (Entry(ChickenClass.Speedy).ResolvedModelScale.y - 1f);
                Assert.AreEqual(0.48f, lift, 1e-3f);

                string[] names = { "Nameplate", "WorldBars", "StatusBadges", "StunStars" };
                var before = new float[names.Length];
                for (int i = 0; i < names.Length; i++) before[i] = Child(root, names[i]).localPosition.y;

                root.GetComponent<ChickenNameplate>().SetHeadLift(lift);
                root.GetComponent<ChickenWorldBars>().SetHeadLift(lift);
                root.GetComponent<ChickenStatusBadges>().SetHeadLift(lift);
                root.GetComponent<ChickenStateOverlays>().SetHeadLift(lift);

                for (int i = 0; i < names.Length; i++)
                    Assert.AreEqual(before[i] + lift, Child(root, names[i]).localPosition.y, 1e-3f, names[i]);

                // Ground overlays describe the hitbox and must not move.
                var anchor = root.transform.Find(ChickenGroundAnchor);
                Assert.IsTrue(anchor == null || Mathf.Abs(anchor.localPosition.y - ChickenGround.LocalGroundY(root.GetComponent<ChickenController>())) < 1e-3f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private const string ChickenGroundAnchor = "GroundAnchor";

        private static Transform Child(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Assert.Fail($"child '{name}' not built");
            return null;
        }

        // ---- colour -----------------------------------------------------------------------------------

        /// <summary>Lit (lightest decile) mean of the class atlas times the class wash, in sRGB.</summary>
        private static Color LitBodyColour(ChickenClass cls)
        {
            var e = Entry(cls);
            var mat = e.ModelPrefab.GetComponentInChildren<Renderer>(true).sharedMaterial;
            var tex = mat.GetTexture("_BaseMap");
            Assert.IsNotNull(tex, $"{cls} model has no base map");
            string path = AssetDatabase.GetAssetPath(tex);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded,
                    File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                var px = decoded.GetPixels();
                var wash = Color.Lerp(Color.white, e.TintColor, Mathf.Clamp01(e.TintStrength));
                var lum = new float[px.Length]; var order = new int[px.Length];
                for (int i = 0; i < px.Length; i++) { order[i] = i; lum[i] = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b; }
                Array.Sort(order, (a, b) => lum[a].CompareTo(lum[b]));
                double r = 0, g = 0, b2 = 0; int from = px.Length - px.Length / 10;
                for (int k = from; k < px.Length; k++) { var c = px[order[k]]; r += c.r; g += c.g; b2 += c.b; }
                int n = px.Length - from;
                return new Color((float)(r / n) * wash.r, (float)(g / n) * wash.g, (float)(b2 / n) * wash.b, 1f);
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void Speedy_WashIsStrongerThanTheOldQuarter_AndStaysOrange()
        {
            var e = Entry(ChickenClass.Speedy);
            Assert.AreEqual(0.6f, e.TintStrength, 1e-4f, "0.6 is the DataIntegrityTests ceiling");
            Assert.AreEqual(new Color(0.9098039f, 0.3529412f, 0.1647059f, 1f).r, e.TintColor.r, 1e-4f, "ART.md Speedy #E85A2A");
            Assert.AreEqual(0.3529412f, e.TintColor.g, 1e-4f);
            Assert.AreEqual(0.1647059f, e.TintColor.b, 1e-4f);
        }

        [Test]
        public void Speedy_IsClearlyApartFromFatty_Crates_AndTheOtherClasses()
        {
            var speedy = LitBodyColour(ChickenClass.Speedy);
            float vsFatty = Phase8ChunkD8Tests.DeltaE2000(speedy, LitBodyColour(ChickenClass.Fatty));
            float vsWarrior = Phase8ChunkD8Tests.DeltaE2000(speedy, LitBodyColour(ChickenClass.Warrior));
            float vsCrate = Phase8ChunkD8Tests.DeltaE2000(speedy, CrateRendered);
            TestContext.Out.WriteLine($"Speedy lit {speedy}: vs Fatty {vsFatty:0.0}, Warrior {vsWarrior:0.0}, crate {vsCrate:0.0}");
            Assert.GreaterOrEqual(vsFatty, 20f, $"Speedy vs Fatty dE00 {vsFatty:0.0}");
            Assert.GreaterOrEqual(vsCrate, 12f, $"Speedy vs D8 crate dE00 {vsCrate:0.0}");
            // The texel model under-states the Warrior (rendered 28.9, see D6 capture notes); keep only a sanity floor.
            Assert.GreaterOrEqual(vsWarrior, 8f, $"Speedy vs Warrior dE00 {vsWarrior:0.0}");
        }
    }
}
