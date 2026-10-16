using System.Collections.Generic;
using System.Linq;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk C Stage 3 S2 (D3 base kit): straw floor + player-colour ring + pennant on
    /// <c>PlayerBase.prefab</c>; the generated nest basket instead of the procedural sphere tray; coop and
    /// basket pulled off the corner fence; the deposit disc kept clear of props; gameplay geometry unchanged.
    /// </summary>
    /// <remarks>
    /// Clearances are measured on real mesh vertices in world space, not renderer bounds: a yawed prop's
    /// AABB swells by up to 41 % and would make the deposit check fail for a basket that is visibly clear
    /// (memory "art metrics that passed while broken": assert features, not bounding boxes).
    /// </remarks>
    public class Phase8ChunkS2Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string StrawMaterial = "Assets/_Game/Art/Materials/BaseKit_Straw.mat";
        private const string FlagMaterial = "Assets/_Game/Art/Materials/BaseKit_PennantFlag.mat";
        private const string PoleMaterial = "Assets/_Game/Art/Materials/BaseKit_PennantPole.mat";
        private const string RingMaterial = "Assets/_Game/Art/Materials/PlayerBase.mat";
        private const string StrawTile = "Assets/Generated/Env/Textures/straw_floor_tile.png";
        private const string PennantFbx = "Assets/Generated/Env/pennant.fbx";
        private const string NestFbx = "Assets/Generated/nest_basket.fbx";

        /// <summary>Shipped deposit radius (PlayerBase._depositRadius) and base distance from the arena centre, pinned.</summary>
        private const float ShippedDepositRadius = 2.5f;
        private const float ShippedBaseCoordinate = 21.8025f;
        private const float MinFenceGap = 0.6f;

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

        private static GameObject BasePrefab() => TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath);
        private static PlayerBase Base() => BasePrefab().GetComponent<PlayerBase>();
        private static Transform Kit() => TestAssets.PrivateField<Transform>(Base(), "_kit");
        private static Renderer Named(string name) =>
            BasePrefab().GetComponentsInChildren<Renderer>(true).Single(r => r.name == name);

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        private static Vector2[] BaseCentres() =>
            ShippedMap.Corners().Select(c => Flat(MapGenerator.SpawnPointNominal(c))).ToArray();

        private static IEnumerable<Vector3> WorldVertices(Renderer r, Matrix4x4 extra)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            var m = extra * r.transform.localToWorldMatrix;
            return mesh.vertices.Select(v => m.MultiplyPoint3x4(v));
        }

        // ---- gameplay unchanged ----------------------------------------------------------------------

        [Test]
        public void DepositRadius_AndBasePositions_AreUnchanged()
        {
            Assert.AreEqual(ShippedDepositRadius, TestAssets.PrivateField<float>(Base(), "_depositRadius"), "deposit radius");
            var expected = new[]
            {
                new Vector2(+1, +1), new Vector2(-1, +1), new Vector2(-1, -1), new Vector2(+1, -1),
            };
            var centres = BaseCentres();
            Assert.AreEqual(4, centres.Length);
            for (int i = 0; i < 4; i++)
                Assert.Less(Vector2.Distance(centres[i], expected[i] * ShippedBaseCoordinate), 0.001f, $"base {i} position");
        }

        [Test]
        public void BaseColliders_AreTheSameSingleTriggerCube()
        {
            var cols = BasePrefab().GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(1, cols.Length, "the base keeps exactly its one trigger box; the kit adds no colliders");
            var box = cols[0] as BoxCollider;
            Assert.IsNotNull(box);
            Assert.IsTrue(box.isTrigger);
            Assert.AreEqual(Vector3.one, box.size);
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), box.transform.localPosition);
        }

        // ---- straw floor ----------------------------------------------------------------------------

        [Test]
        public void StrawMaterial_IsMatteUrpLit_WithTheTileableStrawTexture()
        {
            var m = TestAssets.Load<Material>(StrawMaterial);
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name);
            Assert.AreEqual(TestAssets.Load<Texture2D>(StrawTile), m.GetTexture("_BaseMap"), "_BaseMap");
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), "white base so the MPB hint is the only tint");
            Assert.AreEqual(Vector2.one, m.GetTextureScale("_BaseMap"), "tiling lives in the mesh UVs (metres / 2)");
            Assert.AreEqual(0f, m.GetFloat("_Smoothness"));
            Assert.AreEqual(0f, m.GetFloat("_SpecularHighlights"));
            Assert.IsTrue(m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"));
            Assert.AreEqual(0f, m.GetFloat("_EnvironmentReflections"));
            Assert.IsTrue(m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"));
            Assert.IsTrue(m.enableInstancing);
            Assert.AreEqual(TextureWrapMode.Repeat, TestAssets.Load<Texture2D>(StrawTile).wrapMode, "the tile must repeat");
        }

        [Test]
        public void Floor_IsTheDepositDisc_TiledEveryTwoMetres()
        {
            var floor = Named("StrawFloor");
            Assert.AreEqual(TestAssets.Load<Material>(StrawMaterial), floor.sharedMaterial);
            var mesh = floor.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(BasePadMesh.FloorAssetPath, AssetDatabase.GetAssetPath(mesh));
            Assert.AreEqual(BasePadMesh.FloorRadius, TestAssets.PrivateField<float>(Base(), "_depositRadius"), 1e-4f,
                "the straw pad is exactly the deposit disc");

            var v = mesh.vertices; var uv = mesh.uv;
            float maxR = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                maxR = Mathf.Max(maxR, Flat(v[i]).magnitude);
                Assert.Less(Vector2.Distance(uv[i], Flat(v[i]) / 2f), 1e-4f, "UV = metres / 2 (one straw tile per 2 m)");
            }
            Assert.AreEqual(BasePadMesh.FloorRadius, maxR, 1e-3f);
            Assert.AreEqual(1f, floor.transform.lossyScale.x, 1e-4f, "unscaled, so UV metres stay metres");
        }

        [Test]
        public void BakedPadMeshes_MatchTheirBuilders()
        {
            var floor = TestAssets.Load<Mesh>(BasePadMesh.FloorAssetPath);
            var ring = TestAssets.Load<Mesh>(BasePadMesh.RingAssetPath);
            var f = BasePadMesh.BuildFloor(); var r = BasePadMesh.BuildRing();
            try
            {
                CollectionAssert.AreEqual(f.vertices, floor.vertices);
                CollectionAssert.AreEqual(f.triangles, floor.triangles);
                CollectionAssert.AreEqual(r.vertices, ring.vertices);
                CollectionAssert.AreEqual(r.triangles, ring.triangles);
                Assert.IsTrue(floor.normals.All(n => n == Vector3.up) && ring.normals.All(n => n == Vector3.up), "flat, facing up");
            }
            finally { Object.DestroyImmediate(f); Object.DestroyImmediate(r); }
        }

        [Test]
        public void PadPieces_CastNoShadows_AndSitJustAboveTheGround()
        {
            foreach (var n in new[] { "StrawFloor", "PlayerRing" })
            {
                var r = Named(n);
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{n}: a ground-level pad casts no shadow (C5 rule)");
                Assert.IsTrue(r.receiveShadows, $"{n}: still receives");
                float y = r.transform.localPosition.y;
                Assert.That(y, Is.InRange(0.005f, 0.03f), $"{n}: above the grass, below a chicken's feet");
            }
            Assert.Greater(Named("PlayerRing").transform.localPosition.y, Named("StrawFloor").transform.localPosition.y,
                "ring above the floor: no z-fighting where they overlap");
        }

        // ---- tint -----------------------------------------------------------------------------------

        [Test]
        public void TintTargets_AreRingAndFlag_FloorIsAHint_PoleIsWood()
        {
            var b = Base();
            var identity = TestAssets.PrivateField<Renderer[]>(b, "_identityRenderers");
            var floor = TestAssets.PrivateField<Renderer[]>(b, "_floorRenderers");
            CollectionAssert.AreEquivalent(new[] { "PlayerRing", "Pennant_Flag" }, identity.Select(r => r.name));
            CollectionAssert.AreEquivalent(new[] { "StrawFloor" }, floor.Select(r => r.name));
            float tint = TestAssets.PrivateField<float>(b, "_floorTint");
            Assert.That(tint, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.15f), "straw hint <= 15 %");
            Assert.AreEqual(TestAssets.Load<Material>(PoleMaterial), Named("Pennant_Pole").sharedMaterial);
            Assert.AreEqual(TestAssets.Load<Material>(FlagMaterial), Named("Pennant_Flag").sharedMaterial);
            Assert.AreEqual(TestAssets.Load<Material>(RingMaterial), Named("PlayerRing").sharedMaterial);
        }

        [Test]
        public void FloorTint_NeverExceedsFifteenPercent()
        {
            for (int i = 0; i < 4; i++)
            {
                var c = PlayerPalette.ForCorner(i);
                var f = PlayerBase.FloorTintFor(c, 1f); // asking for 100 % must still clamp
                for (int ch = 0; ch < 3; ch++)
                    Assert.LessOrEqual(Mathf.Abs(1f - f[ch]), 0.15f * Mathf.Abs(1f - c[ch]) + 1e-5f, $"corner {i} channel {ch}");
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void RingAndFlag_TakeTheCornerColour_FloorTheHint_PoleNothing(int corner)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(BasePrefab(), scene);
            var pb = go.GetComponent<PlayerBase>();
            var identity = TestAssets.PrivateField<Renderer[]>(pb, "_identityRenderers");
            var floor = TestAssets.PrivateField<Renderer[]>(pb, "_floorRenderers");
            float tint = TestAssets.PrivateField<float>(pb, "_floorTint");
            var colour = PlayerPalette.ForCorner(corner);

            PlayerBase.ApplyKitTint(identity, floor, tint, colour, new MaterialPropertyBlock());

            var read = new MaterialPropertyBlock();
            foreach (var r in identity)
            {
                r.GetPropertyBlock(read);
                Assert.AreEqual(colour, read.GetColor("_BaseColor"), $"{r.name} corner {corner}");
            }
            floor[0].GetPropertyBlock(read);
            Assert.AreEqual(PlayerBase.FloorTintFor(colour, tint), read.GetColor("_BaseColor"), "straw hint");
            var pole = go.GetComponentsInChildren<Renderer>().Single(r => r.name == "Pennant_Pole");
            Assert.IsFalse(pole.HasPropertyBlock(), "the wooden pole is never tinted");
        }

        // ---- pennant ---------------------------------------------------------------------------------

        [Test]
        public void Pennant_IsTheGeneratedFbx_Scaled_WithoutColliders()
        {
            var fbxMeshes = AssetDatabase.LoadAllAssetsAtPath(PennantFbx).OfType<Mesh>().ToArray();
            Assert.AreSame(fbxMeshes.Single(m => m.name == "Pennant_Pole"), Named("Pennant_Pole").GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(fbxMeshes.Single(m => m.name == "Pennant_Flag"), Named("Pennant_Flag").GetComponent<MeshFilter>().sharedMesh);
            var pennant = Named("Pennant_Flag").transform.parent;
            Assert.That(pennant.localScale.x, Is.InRange(1.3f, 1.5f));
            Assert.AreEqual(pennant.localScale.x, pennant.localScale.y, 1e-5f);
            Assert.AreEqual(0, Kit().GetComponentsInChildren<Collider>(true).Length, "no collider on the kit");
        }

        [Test]
        public void KitYaw_Gives_EveryCorner_TheSameLayout()
        {
            var c = BaseCentres();
            Assert.AreEqual(0f, PlayerBase.KitYawFor(new Vector3(c[0].x, 0, c[0].y)), 1e-3f);
            for (int i = 0; i < 4; i++)
            {
                float yaw = PlayerBase.KitYawFor(new Vector3(c[i].x, 0, c[i].y));
                Assert.AreEqual(0f, Mathf.DeltaAngle(-90f * i, yaw), 1e-3f, $"corner {i}");
            }
            Assert.AreEqual(0f, PlayerBase.KitYawFor(Vector3.zero), "origin (Ability Lab) falls back to no rotation");
        }

        [Test]
        public void Pennant_IsOutsideTheDeposit_AndOutOfTheApproachCone()
        {
            float radius = TestAssets.PrivateField<float>(Base(), "_depositRadius");
            var pennantRenderers = new[] { Named("Pennant_Pole"), Named("Pennant_Flag") };
            var kitInverse = Kit().worldToLocalMatrix; // prefab kit sits at the base origin
            foreach (var centre in BaseCentres())
            {
                var place = Matrix4x4.TRS(new Vector3(centre.x, 0, centre.y),
                    Quaternion.Euler(0, PlayerBase.KitYawFor(new Vector3(centre.x, 0, centre.y)), 0), Vector3.one) * kitInverse;
                var toCentre = -centre.normalized;
                foreach (var r in pennantRenderers)
                foreach (var p in WorldVertices(r, place))
                {
                    var d = Flat(p) - centre;
                    Assert.GreaterOrEqual(d.magnitude, radius, $"{r.name} vertex inside the deposit disc at base {centre}");
                    // Beside the pad on a fence side, never in the front approach cone (centre -> base, +-60 deg).
                    float fromApproach = Vector2.Angle(d, toCentre);
                    Assert.GreaterOrEqual(fromApproach, 60f, $"{r.name} sits in the deposit approach at base {centre}");
                }
            }
        }

        // ---- sphere tray gone, nest basket used --------------------------------------------------------

        [Test]
        public void SphereTray_IsGone_FromThePrefabAndTheFactory()
        {
            Assert.AreEqual(0, BasePrefab().GetComponentsInChildren<PlaceholderModel>(true).Length);
            CollectionAssert.DoesNotContain(System.Enum.GetNames(typeof(PlaceholderMeshFactory.Kind)), "BaseNest");
            int tris = BasePrefab().GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);
            Assert.Less(tris, 600, "the whole kit (pad + ring + pennant) is a few hundred triangles; the tray alone was 9 296");
        }

        // ---- Map.unity dressing -----------------------------------------------------------------------

        private static Scene OpenMap() => EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);

        private static Transform[] All(Scene s) =>
            s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();

        [Test]
        public void BaseDiscs_KeepTheirFootprint_ButNoLongerDraw()
        {
            var all = All(OpenMap());
            for (int i = 0; i < 4; i++)
            {
                var disc = all.Single(t => t.name == "BaseZone_" + i);
                Assert.IsFalse(disc.GetComponent<MeshRenderer>().enabled, $"BaseZone_{i}: the straw pad replaces the dark disc");
                Assert.Less(Vector2.Distance(Flat(disc.position), BaseCentres()[i]), 0.001f);
                Assert.AreEqual(ShippedDepositRadius * 2f, disc.localScale.x, 1e-4f);
            }
        }

        [Test]
        public void EveryBase_UsesTheGeneratedNestBasket_WithoutColliders()
        {
            var nestMesh = AssetDatabase.LoadAssetAtPath<GameObject>(NestFbx).GetComponentInChildren<MeshFilter>().sharedMesh;
            var all = All(OpenMap());
            for (int i = 0; i < 4; i++)
            {
                var nest = all.Single(t => t.name == $"Base{i}_Nest");
                Assert.AreSame(nestMesh, nest.GetComponentInChildren<MeshFilter>().sharedMesh, $"Base{i}_Nest");
                Assert.AreEqual(0, nest.GetComponentsInChildren<Collider>(true).Length);
                Assert.AreEqual(0, all.Single(t => t.name == $"Base{i}_Barrel").GetComponentsInChildren<Collider>(true).Length);
            }
        }

        [Test]
        public void NoPropVertex_IsInsideAnyDepositDisc()
        {
            var scene = OpenMap();
            var centres = BaseCentres();
            var offenders = new List<string>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(false)))
            {
                if (!r.enabled || r.name == "GeneratedGround" || r.name.StartsWith("BaseZone_") || r.name.StartsWith("PileZone_")) continue;
                var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                if (r.bounds.size.x > 30f) continue; // the surround ground
                var b = Flat(r.bounds.center);
                if (centres.All(c => Vector2.Distance(b, c) > 12f)) continue;
                foreach (var p in WorldVertices(r, Matrix4x4.identity))
                foreach (var c in centres)
                    if (Vector2.Distance(Flat(p), c) < ShippedDepositRadius) { offenders.Add($"{r.transform.parent?.name}/{r.name}"); goto next; }
                next:;
            }
            CollectionAssert.IsEmpty(offenders.Distinct().ToList(), "props inside a deposit disc");
        }

        [Test]
        public void CoopAndBasket_StandOffTheCornerFence()
        {
            var scene = OpenMap();
            var all = All(scene);
            // Inner face of the boundary fence: the arena-side extent of the outer Wall_* panels.
            float face = all.SelectMany(t => t.GetComponents<Renderer>())
                .Where(r => r.enabled && r.transform.parent != null && r.transform.parent.name.StartsWith("Wall_"))
                .Min(r => r.transform.parent.name.StartsWith("Wall_N") || r.transform.parent.name.StartsWith("Wall_S")
                    ? Mathf.Min(Mathf.Abs(r.bounds.min.z), Mathf.Abs(r.bounds.max.z))
                    : Mathf.Min(Mathf.Abs(r.bounds.min.x), Mathf.Abs(r.bounds.max.x)));
            Assert.That(face, Is.InRange(24f, 25.5f), "fence face");
            for (int i = 0; i < 4; i++)
            foreach (var name in new[] { $"Base{i}_Coop", $"Base{i}_Nest" })
            {
                var r = all.Single(t => t.name == name).GetComponentInChildren<MeshRenderer>();
                float worst = WorldVertices(r, Matrix4x4.identity).Max(p => Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)));
                Assert.GreaterOrEqual(face - worst, MinFenceGap - 0.005f, $"{name}: {face - worst:0.00} m from the fence");
            }
        }

        [Test]
        public void BaseDressing_IsFourFold()
        {
            var all = All(OpenMap());
            foreach (var kind in new[] { "Coop", "Nest", "Barrel" })
            {
                var t0 = all.Single(t => t.name == $"Base0_{kind}");
                for (int i = 1; i < 4; i++)
                {
                    var q = Quaternion.Euler(0, -90f * i, 0);
                    var t = all.Single(x => x.name == $"Base{i}_{kind}");
                    Assert.Less(Vector3.Distance(q * t0.position, t.position), 0.01f, $"Base{i}_{kind} position");
                    if (kind == "Coop")
                        Assert.AreEqual(0f, Mathf.DeltaAngle(t0.eulerAngles.y - 90f * i, t.eulerAngles.y), 0.01f, $"Base{i}_Coop yaw");
                }
            }
        }
    }
}
