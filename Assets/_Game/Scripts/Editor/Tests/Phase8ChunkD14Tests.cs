using System.Linq;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D14 "pile slow-band marking": a chicken is slowed to 0.8x within <c>FoodPile.CollectReach</c> (1 m) of a pile's
    /// surface, so every pile gets a faint worn-straw apron exactly that wide around its footprint ellipse
    /// (<see cref="PileBandMesh"/>, driven by <see cref="FoodPileVisuals"/>). It fades with fill and is off when empty. These
    /// tests pin the geometry (reach from the true yawed footprint, 1:1 world metres under the footprint-scaled root), the
    /// opacity law and the prefab wiring; the "quieter than the play marks" look is checked by the match-camera captures
    /// (<c>tools/ui_capture/capture_d14_pileband.py</c>).
    /// </summary>
    public class Phase8ChunkD14Tests
    {
        private const string PilePrefab = "Assets/_Game/Prefabs/FoodPile.prefab";

        /// <summary>The spec's tolerance: the apron's outer edge is the collect reach +-0.15 m from the footprint, on every axis.</summary>
        private const float ReachTolerance = 0.15f;

        private static readonly Vector2[] Footprints = { new Vector2(3.6f, 3.0f), new Vector2(5.5f, 4.6f), new Vector2(7.8f, 6.2f), new Vector2(1.6f, 1.2f) };
        private static readonly float[] Yaws = { 0f, 45f, 135f, 200f, 315f };

        private static Vector3[] OuterRing(Mesh mesh)
        {
            int stride = PileBandMesh.Segments + 1;
            int last = PileBandMesh.OffsetFractions.Length - 1;
            return mesh.vertices.Skip(last * stride).Take(stride).ToArray();
        }

        [Test]
        public void OuterEdge_IsCollectReachFromTheFootprint_OnEveryAxis()
        {
            const float reach = 1.0f;
            foreach (var fp in Footprints)
            {
                var mesh = new Mesh();
                PileBandMesh.Fill(mesh, fp, reach);
                var outer = OuterRing(mesh);
                foreach (var yaw in Yaws)
                {
                    var q = Quaternion.Euler(0f, yaw, 0f);
                    var centre = new Vector3(3f, 0f, -2f);
                    foreach (var v in outer)
                    {
                        float d = PileFootprintShape.DistanceToSurfaceXZ(centre + q * v, centre, yaw, fp);
                        Assert.AreEqual(reach, d, ReachTolerance, $"footprint {fp} yaw {yaw}: outer edge point {v} is {d:F3} m from the surface, expected {reach}.");
                    }
                }
                // The four axis tips: the apron extends exactly semi-axis + reach along local +-X and +-Z.
                float a = fp.x * 0.5f, b = fp.y * 0.5f;
                Assert.AreEqual(a + reach, outer.Max(v => v.x), ReachTolerance, $"footprint {fp}: +X tip");
                Assert.AreEqual(a + reach, -outer.Min(v => v.x), ReachTolerance, $"footprint {fp}: -X tip");
                Assert.AreEqual(b + reach, outer.Max(v => v.z), ReachTolerance, $"footprint {fp}: +Z tip");
                Assert.AreEqual(b + reach, -outer.Min(v => v.z), ReachTolerance, $"footprint {fp}: -Z tip");
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Mesh_FadesToZeroAtTheOuterEdge_AndStartsInsideTheFootprint()
        {
            var fp = new Vector2(3.6f, 3.0f);
            var mesh = new Mesh();
            PileBandMesh.Fill(mesh, fp, 1f);
            int stride = PileBandMesh.Segments + 1;
            var cols = mesh.colors;
            var verts = mesh.vertices;
            for (int i = 0; i < stride; i++)
            {
                Assert.AreEqual(0f, cols[(PileBandMesh.OffsetFractions.Length - 1) * stride + i].a, 1e-5f, "outer edge alpha must be exactly 0 (the real slow-band limit)");
                Assert.AreEqual(1f, cols[i].a, 1e-5f, "inner edge alpha is full");
                Assert.Less(PileFootprintShape.DistanceToSurfaceXZ(verts[i], Vector3.zero, 0f, fp), 1e-3f, "inner edge sits inside the footprint, hidden under the mound");
            }
            for (int r = 1; r < PileBandMesh.Alphas.Length; r++)
                Assert.LessOrEqual(PileBandMesh.Alphas[r], PileBandMesh.Alphas[r - 1], "vertex alpha must fall monotonically outward");
            Assert.AreEqual(0, mesh.triangles.Count(t => t >= verts.Length), "triangle indices in range");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void PosedUnderAFootprintScaledYawedRoot_LandsOneToOneInWorldMetres()
        {
            const float reach = 1.0f, lift = 0.015f;
            var fp = new Vector2(3.6f, 3.0f);
            var root = new GameObject("PileRoot");
            var band = new GameObject("SlowBand");
            var mesh = new Mesh();
            try
            {
                foreach (var yaw in Yaws)
                {
                    // Exactly what FoodPile stamps on its root: the footprint in X,Z, the blocker height in Y, then the pile's yaw.
                    root.transform.SetPositionAndRotation(new Vector3(16.23f, 0f, 16.23f), Quaternion.Euler(0f, yaw, 0f));
                    root.transform.localScale = new Vector3(fp.x, 1.2f, fp.y);
                    band.transform.SetParent(root.transform, false);
                    FoodPileVisuals.PoseBand(band.transform, root.transform.lossyScale, lift);
                    PileBandMesh.Fill(mesh, fp, reach);

                    foreach (var v in OuterRing(mesh))
                    {
                        var w = band.transform.TransformPoint(v);
                        Assert.AreEqual(root.transform.position.y + lift, w.y, 1e-4f, $"yaw {yaw}: apron height");
                        float d = PileFootprintShape.DistanceToSurfaceXZ(w, root.transform.position, yaw, fp);
                        Assert.AreEqual(reach, d, ReachTolerance, $"yaw {yaw}: apron edge {d:F3} m from the yawed footprint, expected {reach}.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(band);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BandAlpha_IsZeroWhenEmpty_AndRisesMonotonicallyWithFill()
        {
            const float peak = 0.45f;
            Assert.AreEqual(0f, FoodPileVisuals.BandAlpha(0f, peak), "empty pile: apron off");
            Assert.AreEqual(0f, FoodPileVisuals.BandAlpha(-0.1f, peak), "negative ratio is empty too");

            float prev = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float a = FoodPileVisuals.BandAlpha(i / 100f, peak);
                Assert.Greater(a, prev, $"alpha must rise strictly with fill (ratio {i / 100f:F2})");
                Assert.LessOrEqual(a, peak + 1e-6f, "never above the peak");
                prev = a;
            }
            Assert.AreEqual(peak, FoodPileVisuals.BandAlpha(1f, peak), 1e-6f, "full pile: peak");
            Assert.AreEqual(peak, FoodPileVisuals.BandAlpha(5f, peak), 1e-6f, "ratio clamps at full");
            Assert.AreEqual(peak * FoodPileVisuals.BandFloor, FoodPileVisuals.BandAlpha(1e-4f, peak), 0.002f, "a nearly drained pile keeps the floor, not 0 (food is still there to slow on)");
        }

        [Test]
        public void EveryFoodPilePrefab_HasAWiredSlowBandRenderer()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            Assert.Contains(PilePrefab, paths);
            int checkedCount = 0;
            foreach (var path in paths)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var visuals = root.GetComponentsInChildren<FoodPileVisuals>(true);
                foreach (var v in visuals)
                {
                    checkedCount++;
                    var so = new SerializedObject(v);
                    var band = so.FindProperty("_slowBand").objectReferenceValue as Renderer;
                    Assert.IsNotNull(band, $"{path}: FoodPileVisuals._slowBand is not wired (the pile would silently show no slow-band apron).");
                    Assert.IsNotNull(band.GetComponent<MeshFilter>(), $"{path}: slow band has no MeshFilter to receive the runtime mesh.");
                    Assert.IsTrue(band.transform.IsChildOf(v.transform) && band.transform != v.transform, $"{path}: slow band must be a child of the pile root (its scale is counter-applied against the root's).");
                    Assert.IsFalse(band.enabled, $"{path}: slow band must start disabled (it turns on only while the pile has food).");
                    Assert.IsNull(band.GetComponent<Collider>(), $"{path}: slow band is visual only; a collider would change the NavMesh / physics.");
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, band.shadowCastingMode, $"{path}: apron must not cast a shadow.");
                    Assert.IsFalse(band.receiveShadows, $"{path}: apron must not receive shadows (a flat tint, not a lit surface).");
                    Assert.Less(band.sortingOrder, 0, $"{path}: apron must sort before the other transparents so the foot ring and glow draw over it.");
                    Assert.IsNotNull(band.sharedMaterial, $"{path}: slow band has no material.");
                    StringAssert.Contains("Particles/Unlit", band.sharedMaterial.shader.name, $"{path}: the apron's soft fade is vertex alpha, so it needs a shader that multiplies vertex colour (URP Particles/Unlit), got {band.sharedMaterial.shader.name}.");
                    Assert.AreEqual(3000, band.sharedMaterial.renderQueue, $"{path}: apron material must be in the transparent queue.");
                    Assert.Greater(so.FindProperty("_bandPeakAlpha").floatValue, 0f, $"{path}: peak alpha 0 would make the apron invisible.");
                    Assert.Less(so.FindProperty("_bandLift").floatValue, 0.02f, $"{path}: apron must sit under the 0.02 m pile disc slab so the disc stays the crisp mark.");
                    Assert.Greater(so.FindProperty("_bandLift").floatValue, 0.012f, $"{path}: apron must sit over the 0.012 m dirt paths.");
                }
            }
            Assert.Greater(checkedCount, 0, "No FoodPileVisuals found under Assets/_Game; the wiring check proved nothing.");
        }

        [Test]
        public void ApronIsQuieterThanTheRingAndDiscEdge_ByAuthoredOpacity()
        {
            // The play marks are near-opaque lines / a solid slab; the apron's PEAK opacity (inner edge, full pile) must stay well under that.
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PilePrefab);
            var so = new SerializedObject(root.GetComponent<FoodPileVisuals>());
            Assert.LessOrEqual(so.FindProperty("_bandPeakAlpha").floatValue, 0.5f, "apron peak opacity must stay a faint tint (match-camera captures: ~7 % luma on the grass)");
        }
    }
}
