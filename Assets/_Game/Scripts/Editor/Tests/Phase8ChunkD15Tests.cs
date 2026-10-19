using System.Collections.Generic;
using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using PileSpec = CluckWars.Tests.ShippedMap.PileSpec;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D15 "mirrored piles": <see cref="PileFootprintShape.RadialLongAxisYaw"/> used to fold the yaw into [0, 180),
    /// which is right for the ELLIPSE (collider, NavMesh carve, every distance: symmetric under a half turn) but wrong for the
    /// irregular mound / zone-disc OUTLINE, which is not: opposite piles got the same yaw, so from their own seat they looked
    /// like mirrored twins instead of turned copies. The yaw now keeps its full turn. These tests pin that the four seats'
    /// piles are exact 90-degree rotated copies (outline, mound and baked disc), that the collider and carve are untouched
    /// (identical geometry at the old folded yaw and the new full yaw), and that the baked discs are what a fresh bake produces.
    /// </summary>
    public class Phase8ChunkD15Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string FoodPilePrefabPath = "Assets/_Game/Prefabs/FoodPile.prefab";
        private const float Tol = 1e-3f;

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene() => _originalScenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        private static readonly string[] Tiers = { "Personal", "Contested" };

        private static List<PileSpec> Shipped() => ShippedMap.ShippedPiles(Vector2.zero, Vector2.zero);

        private static PileSpec Pile(List<PileSpec> all, string tier, int seat) => all.First(p => p.Name == tier + seat);

        /// <summary>The rotation (about Y, about the arena centre) that carries seat 0's pile of a tier onto seat <paramref name="seat"/>'s: read off the yaw difference, then PROVEN against the position.</summary>
        private static Quaternion TurnTo(PileSpec p0, PileSpec pi)
        {
            var q = Quaternion.Euler(0f, pi.Yaw - p0.Yaw, 0f);
            Assert.Less(Vector3.Distance(q * p0.Pos, pi.Pos), 1e-3f, $"{pi.Name} is not {p0.Name} turned by its yaw difference ({pi.Yaw - p0.Yaw:F1} deg).");
            return q;
        }

        private static void AssertSameWorldPoints(IList<Vector3> expected, IList<Vector3> actual, string what)
        {
            Assert.AreEqual(expected.Count, actual.Count, what + ": vertex count");
            for (int i = 0; i < expected.Count; i++)
                Assert.Less(Vector3.Distance(expected[i], actual[i]), Tol, $"{what}: vertex {i} {actual[i]:F4} != expected {expected[i]:F4}.");
        }

        // ---- yaw function -------------------------------------------------------------------------

        [Test]
        public void Yaw_KeepsItsFullTurn_AndMatchesTheShippedFourFoldLayout()
        {
            var all = Shipped();
            foreach (var p in all.Where(p => p.Name != "Center"))
            {
                Assert.GreaterOrEqual(p.Yaw, 0f, p.Name);
                Assert.Less(p.Yaw, 360f, p.Name);
                var longAxis = Quaternion.Euler(0f, p.Yaw, 0f) * Vector3.right;
                Assert.Greater(Vector3.Dot(longAxis, p.Pos.normalized), 0.999f, $"{p.Name}: the long axis's +X end must point radially OUTWARD (the full turn; a fold would only guarantee +-).");
            }
            foreach (var tier in Tiers)
            {
                var p0 = Pile(all, tier, 0);
                for (int i = 1; i < 4; i++)
                {
                    var pi = Pile(all, tier, i);
                    Assert.AreEqual(0f, Mathf.DeltaAngle(p0.Yaw - 90f * i, pi.Yaw), Tol, $"{pi.Name} yaw must be seat 0's - {90 * i} on the full circle (seats turn by Rot90, which is a NEGATIVE Euler yaw).");
                }
                // opposite seats are half a turn apart, not equal (the old fold gave 0 here)
                Assert.AreEqual(180f, Mathf.Abs(Mathf.DeltaAngle(p0.Yaw, Pile(all, tier, 2).Yaw)), Tol, $"{tier}0 and {tier}2 must differ by a half turn.");
            }
            Assert.AreEqual(0f, PileFootprintShape.RadialLongAxisYaw(Vector3.zero), "centre pile stays unrotated");
        }

        [Test]
        public void Yaw_IsContinuousAcrossTheAxes_NoSignOfZeroSurprise()
        {
            // -0 z on the -x axis made Atan2 return -180 once; the result must be a proper in-range angle on every axis.
            foreach (var p in new[] { new Vector3(20f, 0f, 0f), new Vector3(-20f, 0f, 0f), new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, -20f), new Vector3(-20f, 0f, -0f) })
            {
                float yaw = PileFootprintShape.RadialLongAxisYaw(p);
                Assert.GreaterOrEqual(yaw, 0f); Assert.Less(yaw, 360f);
                Assert.Greater(Vector3.Dot(Quaternion.Euler(0f, yaw, 0f) * Vector3.right, p.normalized), 0.9999f, $"yaw {yaw} at {p}");
            }
        }

        // ---- the outline is rotated, not mirrored ---------------------------------------------------

        private static Vector3[] DiscWorld(Mesh unitDisc, PileSpec s) =>
            unitDisc.vertices.Select(v => PileFootprintShape.ToWorld(new Vector2(v.x * s.Footprint.x, v.z * s.Footprint.y), s.Pos, s.Yaw, 0f)).ToArray();

        [Test]
        public void DiscOutline_OfEverySeat_IsSeat0sOutlineTurnedBy90PerSeat_NotItsMirror()
        {
            var disc = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.PileDiscMeshAssetPath);
            Assert.IsNotNull(disc, ZoneMarkerBaker.PileDiscMeshAssetPath);
            var all = Shipped();
            foreach (var tier in Tiers)
            {
                var p0 = Pile(all, tier, 0);
                var w0 = DiscWorld(disc, p0);

                // Teeth: the outline must NOT be half-turn symmetric, or none of this could tell rotated from mirrored.
                var half = w0.Select(v => new Vector3(2f * p0.Pos.x - v.x, 0f, 2f * p0.Pos.z - v.z)).ToArray();
                float asym = half.Max(h => w0.Min(v => Vector3.Distance(h, v)));
                Assert.Greater(asym, 0.1f, $"{tier}: the irregular disc is (nearly) half-turn symmetric, so a fold could not have shown (asymmetry {asym:F3} m).");

                for (int i = 1; i < 4; i++)
                {
                    var pi = Pile(all, tier, i);
                    var q = TurnTo(p0, pi);
                    AssertSameWorldPoints(w0.Select(v => q * v).ToArray(), DiscWorld(disc, pi), $"{pi.Name} disc outline");
                }
            }

            // The old behaviour (yaw folded mod 180) drew every pile whose true yaw is in [180, 360) a half turn off: that is the bug.
            int wrong = 0;
            foreach (var s0 in all.Where(p => p.Name != "Center" && p.Yaw >= 180f))
            {
                var folded = s0; folded.Yaw = Mathf.Repeat(s0.Yaw, 180f);
                var truth = DiscWorld(disc, s0);
                float off = DiscWorld(disc, folded).Select((v, k) => Vector3.Distance(v, truth[k])).Max();
                Assert.Greater(off, 0.1f, $"{s0.Name} at the folded yaw would already match its true outline, so the fold was harmless here (offset {off:F3} m).");
                wrong++;
            }
            Assert.AreEqual(4, wrong, "expected 4 of the 8 outer piles to have a full yaw >= 180");
        }

        [Test]
        public void MoundOutline_OfEverySeat_IsSeat0sMoundTurnedBy90PerSeat()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath);
            var so = new SerializedObject(prefab.GetComponent<FoodPileVisuals>());
            var mesh = so.FindProperty("_fullMesh").objectReferenceValue as Mesh;
            Assert.IsNotNull(mesh, "FoodPileVisuals._fullMesh is not wired.");
            var all = Shipped();

            Vector3[] MoundWorld(PileSpec s)
            {
                var (go, filter) = Instantiate(s, 1f);
                try { return mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray(); }
                finally { Object.DestroyImmediate(go); }
            }

            foreach (var tier in Tiers)
            {
                var p0 = Pile(all, tier, 0);
                var w0 = MoundWorld(p0);
                for (int i = 1; i < 4; i++)
                {
                    var pi = Pile(all, tier, i);
                    var q = TurnTo(p0, pi);
                    AssertSameWorldPoints(w0.Select(v => q * v).ToArray(), MoundWorld(pi), $"{pi.Name} mound mesh");
                }
            }
        }

        // ---- collider and carve are unchanged ------------------------------------------------------

        private static (GameObject root, MeshFilter mound) Instantiate(PileSpec s, float scale)
        {
            if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefabPath));
            var pile = go.GetComponent<FoodPile>();
            float height = TestAssets.PrivateField<float>(pile, "_blockerHeight");
            go.transform.SetPositionAndRotation(s.Pos, Quaternion.Euler(0f, s.Yaw, 0f));
            go.transform.localScale = new Vector3(s.Footprint.x * scale, height, s.Footprint.y * scale);
            var mound = new SerializedObject(go.GetComponent<FoodPileVisuals>()).FindProperty("_moundFilter").objectReferenceValue as MeshFilter;
            Assert.IsNotNull(mound, "FoodPileVisuals._moundFilter is not wired.");
            return (go, mound);
        }

        /// <summary>Blocker prism vertices + every carve box's 8 world corners, as a canonical sorted list (identity does not depend on vertex order).</summary>
        private static List<Vector3> BlockingGeometry(PileSpec s)
        {
            var (go, _) = Instantiate(s, 1f);
            try
            {
                typeof(FoodPile).GetMethod("CreateBlocker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(go.GetComponent<FoodPile>(), null);
                Physics.SyncTransforms();
                var pts = new List<Vector3>();
                var blocker = go.transform.Find("Blocker");
                Assert.IsNotNull(blocker, "no Blocker child");
                var hull = blocker.GetComponent<MeshCollider>();
                Assert.IsNotNull(hull, "no blocker MeshCollider");
                pts.AddRange(hull.sharedMesh.vertices.Select(v => blocker.TransformPoint(v)));
                var obstacles = go.GetComponentsInChildren<NavMeshObstacle>(true);
                Assert.AreEqual(PileFootprintShape.CarveBoxCount, obstacles.Length, "carve boxes");
                foreach (var o in obstacles)
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = o.center + Vector3.Scale(o.size * 0.5f, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        pts.Add(o.transform.TransformPoint(corner));
                    }
                return pts.OrderBy(p => Mathf.Round(p.x * 1000f)).ThenBy(p => Mathf.Round(p.z * 1000f)).ThenBy(p => Mathf.Round(p.y * 1000f)).ToList();
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ColliderAndCarve_AreIdenticalAtTheOldFoldedYawAndTheNewFullYaw_ForEveryPile()
        {
            foreach (var s in Shipped())
            {
                var folded = s; folded.Yaw = Mathf.Repeat(s.Yaw, 180f);
                var a = BlockingGeometry(folded);
                var b = BlockingGeometry(s);
                Assert.AreEqual(a.Count, b.Count, s.Name);
                for (int i = 0; i < a.Count; i++)
                    Assert.Less(Vector3.Distance(a[i], b[i]), Tol, $"{s.Name}: blocking geometry point {i} moved from {a[i]:F4} to {b[i]:F4}: the collider / carve must not change.");
            }
        }

        [Test]
        public void EllipseQueries_AreIdenticalAtTheFoldedAndTheFullYaw()
        {
            var rng = new System.Random(15);
            foreach (var s in Shipped())
            {
                float folded = Mathf.Repeat(s.Yaw, 180f);
                for (int k = 0; k < 40; k++)
                {
                    var p = s.Pos + new Vector3((float)rng.NextDouble() * 16f - 8f, 0f, (float)rng.NextDouble() * 16f - 8f);
                    Assert.AreEqual(PileFootprintShape.DistanceToSurfaceXZ(p, s.Pos, folded, s.Footprint), PileFootprintShape.DistanceToSurfaceXZ(p, s.Pos, s.Yaw, s.Footprint), 1e-4f, $"{s.Name}: collect distance");
                    Assert.Less(Vector3.Distance(PileFootprintShape.ApproachPoint(p, s.Pos, folded, s.Footprint, 0.7f), PileFootprintShape.ApproachPoint(p, s.Pos, s.Yaw, s.Footprint, 0.7f)), 1e-3f, $"{s.Name}: approach point");
                    var dir = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
                    Assert.AreEqual(PileFootprintShape.RadiusToward(dir, folded, s.Footprint), PileFootprintShape.RadiusToward(dir, s.Yaw, s.Footprint), 1e-4f, $"{s.Name}: radius toward");
                }
            }
        }

        // ---- baked scene discs == a fresh bake -----------------------------------------------------

        [Test]
        public void BakedMapDiscs_MatchAFreshBake()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geo = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;

            // The disc mesh asset equals the FBX mesh with its node rotation applied (computed here read-only; EnsurePileDiscMeshAsset would rewrite the asset).
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ZoneMarkerBaker.PileDiscFbxPath);
            var src = fbx.GetComponentInChildren<MeshFilter>();
            var rot = src.transform.localRotation;
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.PileDiscMeshAssetPath);
            AssertSameWorldPoints(src.sharedMesh.vertices.Select(v => rot * v).ToArray(), asset.vertices, "PileDiscIrregular asset vs fresh bake from the FBX");

            int checkedOuter = 0;
            foreach (Transform child in geo)
            {
                if (!child.name.StartsWith("PileZone_")) continue;
                Assert.AreSame(asset, child.GetComponent<MeshFilter>().sharedMesh, $"{child.name} does not use the baked irregular disc.");
                if (!ZoneMarkerBaker.IsOuterPileZone(child.name)) { Assert.AreEqual(0f, Mathf.DeltaAngle(child.localEulerAngles.y, 0f), Tol, $"{child.name} (centre) must stay unrotated"); continue; }
                float expected = PileFootprintShape.RadialLongAxisYaw(child.localPosition);
                Assert.AreEqual(0f, Mathf.DeltaAngle(child.localEulerAngles.y, expected), 0.01f, $"{child.name}: baked yaw {child.localEulerAngles.y:F2} vs a fresh bake's {expected:F2} (re-run Cluck Wars/Map/Apply Ground + Zone Fixes).");
                checkedOuter++;
            }
            Assert.AreEqual(8, checkedOuter, "expected 4 personal + 4 contested discs");

            // And the baked discs themselves are 4-fold rotated copies (their world vertices), not mirrors.
            foreach (var tier in Tiers)
            {
                var d0 = geo.Find($"PileZone_{tier}0");
                var w0 = asset.vertices.Select(v => d0.TransformPoint(v)).ToArray();
                for (int i = 1; i < 4; i++)
                {
                    var di = geo.Find($"PileZone_{tier}{i}");
                    var q = Quaternion.Euler(0f, di.eulerAngles.y - d0.eulerAngles.y, 0f);
                    AssertSameWorldPoints(w0.Select(v => q * v).ToArray(), asset.vertices.Select(v => di.TransformPoint(v)).ToArray(), $"baked PileZone_{tier}{i}");
                }
            }
        }
    }
}
