using System.Linq;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
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
    /// Phase 8 chunk C Stage 3 S1: the grain-and-corn mound (D2) wired into the food pile, the
    /// tint floor, and the irregular pile discs.
    /// </summary>
    public class Phase8ChunkS1Tests
    {
        private const string FoodPilePrefab = "Assets/_Game/Prefabs/FoodPile.prefab";
        private const string MoundFbx = "Assets/Generated/Env/food_mound.fbx";
        private const string EmptyFbx = "Assets/Generated/Env/food_mound_empty.fbx";
        private const string MoundMaterial = "Assets/_Game/Art/Materials/FoodMound.mat";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string GlowMaterial = "Assets/_Game/Art/Materials/PileGlow.mat";
        private const string MapGeneratorSource = "Assets/_Game/Scripts/Gameplay/MapGenerator.cs";

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

        private static Mesh FbxMesh(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().sharedMesh;

        private static FoodPileVisuals Visuals() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefab).GetComponent<FoodPileVisuals>();

        // ---- mound wiring --------------------------------------------------------------------------

        [Test]
        public void Prefab_Mound_IsTheGrainFbx_WithTheEmptySwapMesh_AndNoPlaceholderModel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefab);
            var v = Visuals();
            var filter = TestAssets.PrivateField<MeshFilter>(v, "_moundFilter");
            var meshRoot = TestAssets.PrivateField<Transform>(v, "_meshRoot");
            Assert.IsNotNull(filter);
            Assert.AreSame(FbxMesh(MoundFbx), filter.sharedMesh, "prefab mound mesh");
            Assert.AreSame(FbxMesh(MoundFbx), TestAssets.PrivateField<Mesh>(v, "_fullMesh"));
            Assert.AreSame(FbxMesh(EmptyFbx), TestAssets.PrivateField<Mesh>(v, "_emptyMesh"));
            Assert.AreEqual(0f, TestAssets.PrivateField<float>(v, "_widestRingDepth"), "the FBX ground ring is its widest, at y 0");
            Assert.AreEqual(1f, TestAssets.PrivateField<float>(v, "_fullHeight"));
            Assert.AreEqual(0.3f, TestAssets.PrivateField<float>(v, "_emptyHeight"));
            Assert.AreSame(meshRoot, filter.transform.parent,
                "the FBX node rotation lives on a child so the root's Y scale stays world-vertical");
            Assert.AreEqual(Quaternion.identity, meshRoot.localRotation);
            Assert.AreEqual(0, prefab.GetComponentsInChildren<PlaceholderModel>(true).Length, "the procedural placeholder mound is gone");
            Assert.AreEqual(MoundMaterial, AssetDatabase.GetAssetPath(filter.GetComponent<MeshRenderer>().sharedMaterial));
        }

        [Test]
        public void MoundPose_FoodPresent_ScalesByFill_AndSeatsTheRingOnTheGround()
        {
            var v = Visuals();
            v.MoundPose(false, 1f, out var s1, out var p1);
            Assert.AreEqual(new Vector3(1f, 1f, 1f), s1);
            Assert.AreEqual(0f, p1.y, 1e-6f);
            v.MoundPose(false, 0.5f, out var s2, out _);
            Assert.AreEqual(0.65f, s2.y, 1e-5f);
            Assert.AreEqual(1f, s2.x);
            Assert.AreEqual(1f, s2.z);
            v.MoundPose(false, 0.0001f, out var s3, out _);
            Assert.AreEqual(0.3f, s3.y, 1e-3f, "a sliver of food still reads as a low mound");
        }

        [Test]
        public void MoundPose_Empty_IsScaleOne_AndLiftedAboveTheDisc()
        {
            Visuals().MoundPose(true, 0f, out var scale, out var pos);
            Assert.AreEqual(Vector3.one, scale, "the empty-grain mesh is authored in footprint space");
            Assert.AreEqual(0.015f, pos.y, 1e-6f);
            Assert.AreEqual(0f, pos.x);
            Assert.AreEqual(0f, pos.z);
        }

        [Test]
        public void EmptyMesh_FitsTheFootprint_AndIsFlat()
        {
            var rot = AssetDatabase.LoadAssetAtPath<GameObject>(EmptyFbx).GetComponentInChildren<MeshFilter>().transform.localRotation;
            var pts = FbxMesh(EmptyFbx).vertices.Select(p => rot * p).ToArray();
            Assert.LessOrEqual(pts.Max(p => new Vector2(p.x, p.z).magnitude), 0.5f, "inside the unit oval");
            Assert.That(pts.Min(p => p.y), Is.GreaterThanOrEqualTo(-1e-3f));
            Assert.LessOrEqual(pts.Max(p => p.y), 0.03f, "clumps stay a few cm thick");
        }

        // ---- material ------------------------------------------------------------------------------

        [Test]
        public void FoodMoundMaterial_IsMatteGrain_WithAWhiteBaseColour()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MoundMaterial);
            Assert.IsNotNull(m);
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name);
            // Phase 8 D3: the base map is the mound's UNIQUE albedo (grain tile x4 baked in + dips + crest kernels) at tiling 0.25,
            // so UV0 (which spans 0..4) samples it once; pinned in Phase8ChunkD3Tests.
            Assert.AreEqual(Phase8ChunkD3Tests.MoundAlbedo, AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
            Assert.AreEqual(Color.white, m.GetColor("_BaseColor"), "white base: the MPB tint carries");
            Assert.AreEqual(0f, m.GetFloat("_Smoothness"));
            Assert.IsTrue(m.enableInstancing);
            Assert.IsTrue(m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF") && m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"));
            Assert.AreEqual(new Vector2(0.25f, 0.25f), m.GetTextureScale("_BaseMap"), "the mound UVs span 0..4: tiling 0.25 maps the unique albedo once");
        }

        // ---- tint floor ----------------------------------------------------------------------------

        [Test]
        public void TintFloor_IsThirtyFivePercent_OfTheEmptyToFullLerp()
        {
            Assert.AreEqual(0.35f, FoodPileVisuals.TintFloor);
            var empty = new Color(0.4f, 0.3f, 0.15f);
            var full = new Color(1f, 0.85f, 0.3f);
            var floor = Color.Lerp(empty, full, 0.35f);
            Assert.AreEqual(floor, FoodPileVisuals.TintFor(empty, full, 0f), "empty pile shows the floor colour");
            Assert.AreEqual(floor, FoodPileVisuals.TintFor(empty, full, 0.2f), "below the floor is clamped");
            Assert.AreEqual(floor, FoodPileVisuals.TintFor(empty, full, -1f));
            Assert.AreEqual(Color.Lerp(empty, full, 0.6f), FoodPileVisuals.TintFor(empty, full, 0.6f), "above the floor the lerp is unchanged");
            Assert.AreEqual(full, FoodPileVisuals.TintFor(empty, full, 1f));
            Assert.AreEqual(full, FoodPileVisuals.TintFor(empty, full, 7f));
        }

        [Test]
        public void TintFor_NeverDarkerThanTheFloor_AndIsMonotoneInFill()
        {
            var scheme = AssetDatabase.LoadAssetAtPath<ColorSchemeSO>(TestAssets.ColorSchemePath);
            float floorLum = TintFor(scheme, 0f).grayscale;
            float last = floorLum;
            for (int i = 0; i <= 100; i++)
            {
                float lum = TintFor(scheme, i / 100f).grayscale;
                Assert.GreaterOrEqual(lum, floorLum - 1e-6f, $"fill {i}%");
                Assert.GreaterOrEqual(lum, last - 1e-6f, $"fill {i}% must not get darker as the pile fills");
                last = lum;
            }
            Assert.Greater(floorLum, scheme.FoodPileEmpty.grayscale, "the floor is brighter than the raw empty colour");
        }

        private static Color TintFor(ColorSchemeSO s, float ratio) => FoodPileVisuals.TintFor(s.FoodPileEmpty, s.FoodPileFull, ratio);

        // ---- pile discs ----------------------------------------------------------------------------

        [Test]
        public void ShippedMap_PileDiscs_AreIrregular_ColliderFree_AndShadowFree()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geometry = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;
            var piles = geometry.Cast<Transform>().Where(t => t.name.StartsWith("PileZone_")).ToList();
            Assert.AreEqual(9, piles.Count, "1 centre + 4 personal + 4 contested");

            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.PileDiscMeshAssetPath);
            var cylinder = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.MeshAssetPath);
            Assert.IsNotNull(asset, ZoneMarkerBaker.PileDiscMeshAssetPath);
            Assert.AreEqual(FbxMesh(ZoneMarkerBaker.PileDiscFbxPath).triangles.Length, asset.triangles.Length, "same triangles as the FBX");
            Assert.AreNotEqual(cylinder.triangles.Length, asset.triangles.Length);

            foreach (var t in piles)
            {
                Assert.AreSame(asset, t.GetComponent<MeshFilter>().sharedMesh, $"{t.name}: mesh");
                var r = t.GetComponent<MeshRenderer>();
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{t.name}: castShadows");
                Assert.IsTrue(r.receiveShadows, $"{t.name}: receiveShadows");
                Assert.IsNull(t.GetComponent<Collider>(), $"{t.name}: no collider");
                Assert.AreEqual(1f, t.localScale.y, 1e-6f, $"{t.name}: the baked slab is already 2 cm thick");
                Assert.AreEqual(0f, t.localPosition.y, 1e-6f, $"{t.name}: slab spans y 0..0.02, as the cylinder did");
                Assert.AreEqual("Assets/_Game/Art/Materials/PileZone.mat", AssetDatabase.GetAssetPath(r.sharedMaterial));
            }
        }

        [Test]
        public void PileDiscMesh_IsAnIrregularOval_InsideTheUnitFootprint_AndTwoCentimetresThick()
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneMarkerBaker.PileDiscMeshAssetPath);
            var v = m.vertices;
            Assert.LessOrEqual(v.Max(p => new Vector2(p.x, p.z).magnitude), 0.5f + 1e-4f, "never outside the D9 collider ellipse");
            Assert.That(v.Min(p => p.y), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(v.Max(p => p.y), Is.EqualTo(0.02f).Within(1e-4f));
            var rim = v.Where(p => p.y > 0.019f).Select(p => new Vector2(p.x, p.z).magnitude).Where(r => r > 0.3f).ToArray();
            Assert.Greater(rim.Max() - rim.Min(), 0.03f, "an irregular outline, not a regular polygon");
            Assert.AreEqual(v.Length, m.normals.Length, "normals present");
        }

        // ---- centre-pile warm glow -----------------------------------------------------------------

        private static MeshRenderer Glow() => TestAssets.PrivateField<Renderer>(Visuals(), "_centreGlow") as MeshRenderer;

        [Test]
        public void CentreGlow_IsAFlatHaloOnThePileRoot_DisabledByDefault_AndShadowFree()
        {
            var glow = Glow();
            Assert.IsNotNull(glow, "FoodPileVisuals._centreGlow is wired to a MeshRenderer");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPilePrefab);
            Assert.AreSame(prefab.transform, glow.transform.parent, "on the pile root (scales with the footprint), not under the Y-scaled mound");
            Assert.IsFalse(glow.enabled, "off by default: only the centre pile turns it on, so outer piles cost no extra draw");
            Assert.AreEqual(ShadowCastingMode.Off, glow.shadowCastingMode);
            Assert.IsFalse(glow.receiveShadows);
            Assert.IsNull(glow.GetComponent<Collider>(), "no collider");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Mesh>(PileGlowRingMesh.AssetPath), glow.GetComponent<MeshFilter>().sharedMesh);
            float worldLift = glow.transform.localPosition.y * TestAssets.PrivateField<float>(prefab.GetComponent<FoodPile>(), "_blockerHeight");
            Assert.That(worldLift, Is.InRange(0.021f, 0.05f), "just above the 2 cm pile disc slab");
            CollectionAssert.DoesNotContain(TestAssets.PrivateField<Renderer[]>(Visuals(), "_renderers") ?? new Renderer[0], glow, "the mound tint never writes the glow");
        }

        [Test]
        public void CentreGlowMaterial_IsUnderAssets_UnlitAlphaBlended_AndCannotBloom()
        {
            var m = Glow().sharedMaterial;
            Assert.AreEqual(GlowMaterial, AssetDatabase.GetAssetPath(m), "project material (C3 rule: under Assets/)");
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", m.shader.name, "unlit: no lighting, no specular");
            Assert.AreEqual(1f, m.GetFloat("_Surface"), "transparent");
            Assert.AreEqual(0f, m.GetFloat("_Blend"), "alpha blend, not additive: it can only lerp toward the glow colour");
            Assert.AreEqual((float)BlendMode.SrcAlpha, m.GetFloat("_SrcBlend"));
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, m.GetFloat("_DstBlend"));
            Assert.AreEqual(0f, m.GetFloat("_ZWrite"));
            Assert.GreaterOrEqual(m.renderQueue, (int)RenderQueue.Transparent);
            Assert.IsTrue(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
            Assert.IsTrue(m.enableInstancing);

            var c = TestAssets.PrivateField<Color>(Visuals(), "_glowColor");
            Assert.LessOrEqual(Mathf.Max(c.r, Mathf.Max(c.g, c.b)), 1f, "LDR colour: alpha-blending toward it never exceeds the PC bloom threshold (1.0)");
            Color.RGBToHSV(c, out float h, out float s, out _);
            Assert.That(h * 360f, Is.InRange(35f, 55f), "warm gold (food ramp), never a player colour");
            Assert.Greater(s, 0.5f);
        }

        [Test]
        public void GlowRingMesh_MatchesTheProfile_FacesUp_AndFadesToZeroAtBothEdges()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(PileGlowRingMesh.AssetPath);
            var built = PileGlowRingMesh.Build();
            Assert.AreEqual(built.vertexCount, asset.vertexCount);
            Assert.AreEqual(built.triangles.Length, asset.triangles.Length);
            var v = asset.vertices; var col = asset.colors;
            Assert.AreEqual(v.Length, col.Length, "vertex alpha carries the falloff");
            Assert.That(v.Max(p => Mathf.Abs(p.y)), Is.LessThan(1e-6f), "flat");
            float inner = v.Min(p => new Vector2(p.x, p.z).magnitude), outer = v.Max(p => new Vector2(p.x, p.z).magnitude);
            Assert.Less(inner, 0.41f, "starts under the mound (outline 0.41-0.495)");
            Assert.That(outer, Is.InRange(0.6f, 0.85f), "a soft halo, not a second disc across the plaza");
            for (int i = 0; i < v.Length; i++)
            {
                float r = new Vector2(v[i].x, v[i].z).magnitude;
                if (Mathf.Abs(r - inner) < 1e-4f || Mathf.Abs(r - outer) < 1e-4f) Assert.AreEqual(0f, col[i].a, 1e-6f, $"edge vertex {i} r={r}");
            }
            var t = asset.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Assert.Greater(n.y, 0f, $"triangle {i / 3} faces up (back-face culled)");
            }
        }

        [Test]
        public void GlowAlpha_IsZeroWhenEmpty_DimsWithFill_AndPeaksWhenFull()
        {
            const float peak = 0.6f;
            Assert.AreEqual(0f, FoodPileVisuals.GlowAlpha(0f, peak), "empty pile: glow off (it reads 'food here')");
            Assert.AreEqual(0f, FoodPileVisuals.GlowAlpha(-0.1f, peak));
            Assert.AreEqual(peak, FoodPileVisuals.GlowAlpha(1f, peak), 1e-6f);
            Assert.AreEqual(peak * FoodPileVisuals.GlowFloor, FoodPileVisuals.GlowAlpha(1e-6f, peak), 1e-4f, "a sliver still glows, dimly");
            Assert.Less(FoodPileVisuals.GlowAlpha(0.5f, peak), FoodPileVisuals.GlowAlpha(1f, peak));
            float last = 0f;
            for (int i = 1; i <= 100; i++) { float a = FoodPileVisuals.GlowAlpha(i / 100f, peak); Assert.GreaterOrEqual(a, last); last = a; }
            Assert.That(TestAssets.PrivateField<float>(Visuals(), "_glowPeakAlpha"), Is.InRange(0.3f, 0.8f));
        }

        [Test]
        public void OnlyTheCentrePile_IsStampedCentre()
        {
            var src = System.IO.File.ReadAllText(MapGeneratorSource);
            var stamped = System.Text.RegularExpressions.Regex.Matches(src, @"SpawnPile\([^;]*centre:\s*true");
            Assert.AreEqual(1, stamped.Count, "exactly one SpawnPile call passes centre: true");
            StringAssert.Contains("Vector3.zero", stamped[0].Value, "and it is the arena-centre spawn");
            StringAssert.Contains("_centerPileAmount", stamped[0].Value);
            StringAssert.Contains("pile.IsCentre = centre;", src, "the flag is stamped in onBeforeSpawned (valid from tick zero on every peer)");
            Assert.IsNotNull(typeof(FoodPile).GetProperty(nameof(FoodPile.IsCentre)).GetCustomAttributes(typeof(Fusion.NetworkedAttribute), false).FirstOrDefault(), "[Networked]");
        }
    }
}
