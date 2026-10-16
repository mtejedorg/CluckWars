using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using CluckWars.Gameplay;
using CluckWars.EditorTools;
using CluckWars.Logging;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C F1 surround haze, Stage 1 (PC / console full-screen pass) + the shared <see cref="HazeSettings"/>:
    /// zero haze anywhere inside the fence (square rule, corners included), a monotone ramp beyond it, the shader
    /// constants encode the same formula, the pass lives on PC_Renderer only and its shader ships with the build.
    /// Evidence: Captures/env3d_after/C/F1/.
    /// </summary>
    public class Phase8ChunkCF1Tests
    {
        private const string PcRenderer     = "Assets/Settings/PC_Renderer.asset";
        private const string MobileRenderer = "Assets/Settings/Mobile_Renderer.asset";
        private const string PcRpAsset      = "Assets/Settings/PC_RPAsset.asset";
        private const string MobileRpAsset  = "Assets/Settings/Mobile_RPAsset.asset";
        private const string HazeShaderPath = "Assets/_Game/Art/Shaders/CWHaze.shader";

        private static float Half => HazeSettings.FenceHalfSize;

        [Test]
        public void AmountAt_IsZeroEverywhereInsideTheFence_CornersAndEdgesIncluded()
        {
            var c = HazeSettings.ArenaCentre;
            // Corners, edge midpoints, centre, and the fence posts' outer face (25.73 m) at every corner.
            float[] reach = { 0f, Half * 0.5f, Half, 25.73f };
            foreach (float r in reach)
            foreach (var dir in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1),
                                        new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
            foreach (float y in new[] { 0f, 3f, 10f })
            {
                var p = new Vector3(c.x + dir.x * r, y, c.y + dir.y * r);
                Assert.AreEqual(0f, HazeSettings.AmountAt(p), $"haze inside the fence at {p}");
            }

            // Dense grid over the whole fenced square.
            for (float x = -Half; x <= Half; x += Half / 16f)
            for (float z = -Half; z <= Half; z += Half / 16f)
                Assert.AreEqual(0f, HazeSettings.AmountAt(new Vector3(c.x + x, 0f, c.y + z)), $"haze at ({x}, {z})");
        }

        [Test]
        public void RampStart_ClearsTheFenceAndTheFenceSourceIsTheArenaSize()
        {
            Assert.AreEqual(MapGenerator.ArenaHalfSize, HazeSettings.FenceHalfSize, "haze must follow the live arena size");
            Assert.Greater(HazeSettings.RampStart, 25.73f, "ramp starts inside the fence posts' outer face");
            Assert.Greater(HazeSettings.RampEnd, HazeSettings.RampStart);
            Assert.That(HazeSettings.MaxAmount, Is.InRange(0.3f, 0.8f));
        }

        [Test]
        public void AmountAt_IsPositiveBeyondTheRampStart_AndCappedAtMax()
        {
            foreach (var dir in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back, new Vector3(1, 0, 1).normalized })
            {
                float justPast = HazeSettings.RampStart + 0.5f;
                // Square distance: along a diagonal the component, not the radius, must pass the ramp start.
                var p = dir * justPast / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.z));
                Assert.Greater(HazeSettings.AmountAt(p), 0f, $"no haze just past the ramp start at {p}");
                Assert.AreEqual(HazeSettings.MaxAmount, HazeSettings.AmountAt(dir * 200f), 1e-5f, "beyond the ramp end");
            }
        }

        [Test]
        public void AmountAt_IsMonotoneWithDistance()
        {
            foreach (var dir in new[] { new Vector3(1, 0, 0), new Vector3(1, 0, 1).normalized, new Vector3(-0.3f, 0, 1).normalized })
            {
                float prev = 0f;
                for (float d = 0f; d <= 120f; d += 0.25f)
                {
                    float a = HazeSettings.AmountAt(dir * d);
                    Assert.GreaterOrEqual(a, prev - 1e-6f, $"haze decreased at {d} m along {dir}");
                    prev = a;
                }
            }
        }

        [Test]
        public void ShaderConstants_EncodeTheSameFormulaAsAmountAt()
        {
            Vector4 prm = HazeSettings.ShaderParams;
            Vector4 col = HazeSettings.ShaderColour;
            for (float x = -90f; x <= 90f; x += 3.7f)
            for (float z = -90f; z <= 90f; z += 4.1f)
            {
                // Mirror of CWHaze.shader's fragment.
                float d = Mathf.Max(Mathf.Abs(x - prm.x), Mathf.Abs(z - prm.y));
                float shader = Mathf.Min(Mathf.Max(d - prm.z, 0f) * prm.w, col.w);
                Assert.AreEqual(HazeSettings.AmountAt(new Vector3(x, 0f, z)), shader, 1e-5f, $"tiers disagree at ({x}, {z})");
            }
            Assert.AreEqual(HazeSettings.ShaderColourFor(QualitySettings.activeColorSpace), col, "ShaderColour follows the active colour space");
        }

        [Test]
        public void PcRenderer_HasOneActiveHazeFeature_WithTheHazeShader()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRenderer);
            Assert.IsNotNull(data, PcRenderer);
            var haze = data.rendererFeatures.OfType<CWHazeFeature>().ToList();
            Assert.AreEqual(1, haze.Count, "PC_Renderer haze features");
            Assert.IsTrue(haze[0].isActive, "haze feature is disabled on PC_Renderer");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Shader>(HazeShaderPath), haze[0].HazeShader,
                "the feature must serialize CWHaze.shader (that reference is what keeps it in the build)");
            Assert.IsTrue(data.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().Any(),
                "SSAO is still on PC_Renderer (its depth-normals prepass is what makes the haze depth request free)");
        }

        [Test]
        public void MobileRenderer_HasNoHazeFeature()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRenderer);
            Assert.IsNotNull(data, MobileRenderer);
            Assert.IsFalse(data.rendererFeatures.Any(f => f is CWHazeFeature), "Mobile tier must never run the haze pass");
        }

        [TestCase(PcRpAsset, PcRenderer)]
        [TestCase(MobileRpAsset, MobileRenderer)]
        public void RpAsset_UsesOnlyItsOwnRenderer(string rpPath, string rendererPath)
        {
            var list = new SerializedObject(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath)).FindProperty("m_RendererDataList");
            Assert.IsNotNull(list, rpPath);
            var expected = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            for (int i = 0; i < list.arraySize; i++)
                Assert.AreEqual(expected, list.GetArrayElementAtIndex(i).objectReferenceValue, $"{rpPath} renderer #{i}");
        }

        [Test]
        public void HazeShader_IsFindableAndCompiles()
        {
            var shader = Shader.Find(CWHazeFeature.ShaderName);
            Assert.IsNotNull(shader, CWHazeFeature.ShaderName);
            Assert.AreEqual(HazeShaderPath, AssetDatabase.GetAssetPath(shader));
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "CWHaze.shader has compile errors");
            Assert.IsTrue(shader.isSupported, "CWHaze.shader unsupported on the Editor graphics API");
        }

        [Test]
        public void ShouldHaze_SkipsOverlayCameras_AndEverythingWithoutAMatchCamera()
        {
            Assert.IsFalse(CWHazeFeature.ShouldHaze(null, CameraRenderType.Base));
            var go = new GameObject("__F1TestCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                Assert.IsFalse(CWHazeFeature.ShouldHaze(cam, CameraRenderType.Overlay), "overlay / UI camera");
                Assert.IsTrue(MatchCamera.Instance == null, "EditMode precondition: no live MatchCamera");
                Assert.IsFalse(CWHazeFeature.ShouldHaze(cam, CameraRenderType.Base), "menu camera with no arena loaded");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- Stage 3: colour space, tier policy, runtime swap ---------------------------------------------------------

        [Test]
        public void ShaderColour_IsColourInGamma_AndItsLinearFormInLinear()
        {
            var c = HazeSettings.Colour;
            var gamma = HazeSettings.ShaderColourFor(ColorSpace.Gamma);
            Assert.AreEqual(c.r, gamma.x, 1e-6f); Assert.AreEqual(c.g, gamma.y, 1e-6f); Assert.AreEqual(c.b, gamma.z, 1e-6f);
            Assert.AreEqual(HazeSettings.MaxAmount, gamma.w, 1e-6f);
            var lin = c.linear;
            var linear = HazeSettings.ShaderColourFor(ColorSpace.Linear);
            Assert.AreEqual(lin.r, linear.x, 1e-6f); Assert.AreEqual(lin.g, linear.y, 1e-6f); Assert.AreEqual(lin.b, linear.z, 1e-6f);
            Assert.AreEqual(HazeSettings.MaxAmount, linear.w, 1e-6f);
        }

        [Test]
        public void HazeColour_KeepsTheVerifiedLook()
        {
            // The haze that was tuned by eye and captured (Gamma project: the buffer holds this value as-is).
            Assert.AreEqual(0.319f, HazeSettings.Colour.r, 1e-4f);
            Assert.AreEqual(0.367f, HazeSettings.Colour.g, 1e-4f);
            Assert.AreEqual(0.263f, HazeSettings.Colour.b, 1e-4f);
        }

        [Test]
        public void HazedBackground_IsTheBackgroundBlendedTowardTheHazeByMax_AsThePcPassDoes()
        {
            var bg = new Color(0.45f, 0.58f, 0.66f, 1f);
            var h = HazeSettings.ShaderColour;
            var hazed = SurroundHazeMapSO.HazedBackground(bg);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var want = Color.Lerp(linear ? bg.linear : bg, new Color(h.x, h.y, h.z), HazeSettings.MaxAmount);
            if (linear) want = want.gamma;
            Assert.AreEqual(want.r, hazed.r, 1e-5f); Assert.AreEqual(want.g, hazed.g, 1e-5f); Assert.AreEqual(want.b, hazed.b, 1e-5f);
            Assert.AreEqual(1f, hazed.a);
        }

        [Test]
        public void TierPolicy_PcGetsThePass_EverythingElseGetsTheBakedCopies()
        {
            var pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcRpAsset);
            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileRpAsset);
            Assert.IsNotNull(pc, PcRpAsset); Assert.IsNotNull(mobile, MobileRpAsset);
            Assert.IsTrue(RenderTierPolicy.PcHazePassFor(pc));
            Assert.IsFalse(RenderTierPolicy.MobileBakedHazeFor(pc), "PC must not also get baked copies");
            Assert.IsFalse(RenderTierPolicy.PcHazePassFor(mobile), "Mobile never runs the pass");
            Assert.IsTrue(RenderTierPolicy.MobileBakedHazeFor(mobile));
            Assert.IsFalse(RenderTierPolicy.PcHazePassFor(null));
            Assert.IsTrue(RenderTierPolicy.MobileBakedHazeFor(null), "unknown pipeline falls back to the cheap baked path");
        }

        private sealed class RecordingLog : ILogService
        {
            public readonly List<string> Errors = new List<string>();
            public LogLevel MinLevel { get; set; } = LogLevel.Verbose;
            public bool IsEnabled(LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) => Errors.Add(message);
        }

        private static (MeshRenderer r, Material[] orig, Material[] hazed) MakeEntryParts(string name, int slots, List<UnityEngine.Object> junk)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name; junk.Add(go);
            var r = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var orig = new Material[slots]; var hazed = new Material[slots];
            for (int i = 0; i < slots; i++)
            {
                orig[i] = new Material(shader) { name = name + "_o" + i }; hazed[i] = new Material(shader) { name = name + "_h" + i };
                junk.Add(orig[i]); junk.Add(hazed[i]);
            }
            r.sharedMaterials = orig;
            return (r, orig, hazed);
        }

        [Test]
        public void Swap_AppliesHazedOnMobile_RestoresOriginalOnPc_IsIdempotent_AndNeverMutatesMaterials()
        {
            var junk = new List<UnityEngine.Object>();
            try
            {
                var a = MakeEntryParts("F1_A", 1, junk);
                var b = MakeEntryParts("F1_B", 2, junk);
                var setGo = new GameObject("F1_Set"); junk.Add(setGo);
                var set = setGo.AddComponent<SurroundHazeSet>();
                set.EditorSetEntries(new List<SurroundHazeSet.Entry>
                {
                    new SurroundHazeSet.Entry(a.r, a.orig, a.hazed, 0.1f, 0),
                    new SurroundHazeSet.Entry(b.r, b.orig, b.hazed, 0.2f, 1),
                }, null);
                var colourBefore = a.orig[0].color;
                var log = new RecordingLog();
                var swapGo = new GameObject("F1_Swap"); junk.Add(swapGo);
                var swap = swapGo.AddComponent<SurroundHazeSwap>();
                swap.Construct(log);

                Assert.IsFalse(swap.Sync(wantBaked: false), "PC at start: nothing to do");
                CollectionAssert.AreEqual(a.orig, a.r.sharedMaterials);

                Assert.IsTrue(swap.Sync(wantBaked: true));
                Assert.IsTrue(swap.BakedShown);
                CollectionAssert.AreEqual(a.hazed, a.r.sharedMaterials);
                CollectionAssert.AreEqual(b.hazed, b.r.sharedMaterials);
                Assert.IsFalse(swap.Sync(wantBaked: true), "idempotent");

                Assert.IsTrue(swap.Sync(wantBaked: false), "tier change back to PC restores the originals");
                CollectionAssert.AreEqual(a.orig, a.r.sharedMaterials);
                CollectionAssert.AreEqual(b.orig, b.r.sharedMaterials);
                Assert.AreEqual(colourBefore, a.orig[0].color, "shared material assets are never mutated");
                CollectionAssert.IsEmpty(log.Errors, "a sound set logs nothing");
            }
            finally { foreach (var o in junk) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        [Test]
        public void Swap_ReportsBrokenEntriesOnce_AndStillSwapsTheValidOnes()
        {
            var junk = new List<UnityEngine.Object>();
            try
            {
                var good = MakeEntryParts("F1_Good", 1, junk);
                var mismatch = MakeEntryParts("F1_Mismatch", 2, junk);
                var gone = MakeEntryParts("F1_Gone", 1, junk);
                var setGo = new GameObject("F1_Set"); junk.Add(setGo);
                var set = setGo.AddComponent<SurroundHazeSet>();
                set.EditorSetEntries(new List<SurroundHazeSet.Entry>
                {
                    new SurroundHazeSet.Entry(good.r, good.orig, good.hazed, 0.1f, 0),
                    new SurroundHazeSet.Entry(mismatch.r, mismatch.orig, new[] { mismatch.hazed[0] }, 0.1f, 0),   // slot-count mismatch
                    new SurroundHazeSet.Entry(null, gone.orig, gone.hazed, 0.1f, 0),                              // null renderer
                    new SurroundHazeSet.Entry(gone.r, gone.orig, new Material[] { null }, 0.1f, 0),               // null material
                }, null);
                var log = new RecordingLog();
                var swapGo = new GameObject("F1_Swap"); junk.Add(swapGo);
                var swap = swapGo.AddComponent<SurroundHazeSwap>();
                swap.Construct(log);

                Assert.IsTrue(swap.Sync(wantBaked: true));
                Assert.IsFalse(swap.Sync(wantBaked: true));
                Assert.IsTrue(swap.Sync(wantBaked: false));
                Assert.IsTrue(swap.Sync(wantBaked: true));
                Assert.AreEqual(1, log.Errors.Count, "wiring problems are reported once, not per swap:\n" + string.Join("\n", log.Errors));
                StringAssert.Contains("3 problem", log.Errors[0]);
                CollectionAssert.AreEqual(good.hazed, good.r.sharedMaterials, "the valid entry still swaps");
                CollectionAssert.AreEqual(mismatch.orig, mismatch.r.sharedMaterials, "a mismatched entry is skipped, not half-applied");
            }
            finally { foreach (var o in junk) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        // ---- Stage 3: the shipped Map.unity set ------------------------------------------------------------------------

        private const string MapScene = SurroundHazeBaker.MapScenePath;

        private static void WithMap(Action<Scene, GameObject, SurroundHazeSet> body)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Assert.AreNotEqual(MapScene, SceneManager.GetSceneAt(i).path, "precondition: Map.unity is not already open");
            var map = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            try
            {
                var surroundings = map.GetRootGameObjects().FirstOrDefault(g => g.name == SurroundHazeBaker.SurroundingsName);
                Assert.IsNotNull(surroundings, "Map.unity has no " + SurroundHazeBaker.SurroundingsName);
                var set = surroundings.GetComponent<SurroundHazeSet>();
                Assert.IsNotNull(set, "MapSurroundings has no SurroundHazeSet: run Cluck Wars/Map/Bake Mobile Surround Haze");
                body(map, surroundings, set);
                Assert.IsFalse(map.isDirty, "the tests must not dirty Map.unity");
            }
            finally { EditorSceneManager.CloseScene(map, removeScene: true); }
        }

        private static bool Skipped(Renderer r) => r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer;

        [Test]
        public void MapSet_EveryEntryIsWiredAndOutsideTheFence()
        {
            WithMap((map, surroundings, set) =>
            {
                Assert.Greater(set.Entries.Count, 50, "the bake produced a plausible number of surround renderers");
                Assert.IsNotNull(set.Map, "manifest asset reference");
                Assert.AreEqual(set.Entries.Count, set.Map.RendererCount, "manifest and set come from the same bake");
                var problems = new List<string>();
                for (int i = 0; i < set.Entries.Count; i++)
                {
                    var e = set.Entries[i];
                    string p = SurroundHazeSwap.EntryProblem(e);
                    if (p != null) { problems.Add($"#{i}: {p}"); continue; }
                    // The one exception is the surround grass plane: it lies under the whole arena and carries the ramp per texel
                    // (0 inside the fence, baked into its ramp textures), so it is the single band -1 entry.
                    bool ramp = e.Band == -1;
                    if (ramp != (e.Renderer.name == SurroundHazeBaker.GrassName))
                        problems.Add($"{e.Renderer.name}: band {e.Band}; only {SurroundHazeBaker.GrassName} uses the per-texel ramp");
                    float near = SurroundHazeBaker.NearestSquareDistance(e.Renderer.bounds);
                    if (!ramp && near <= HazeSettings.FenceHalfSize)
                        problems.Add($"{e.Renderer.name}: footprint reaches {near:0.00} m, inside the fence ({HazeSettings.FenceHalfSize:0.00} m)");
                    if (!e.Renderer.transform.IsChildOf(surroundings.transform))
                        problems.Add($"{e.Renderer.name}: not under {SurroundHazeBaker.SurroundingsName}");
                }
                CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
                Assert.AreEqual(0, SurroundHazeSwap.ReportWiring(set, null), "ReportWiring agrees");
            });
        }

        [Test]
        public void MapSet_EverySurroundRendererPastTheRampStartHasAnEntry()
        {
            // Catches the GroundDressingBaker order issue: it rebuilds MapSurroundings/Vignettes, so the haze bake
            // has to run after it or the new renderers silently stay unhazed on Mobile.
            WithMap((map, surroundings, set) =>
            {
                var inSet = new HashSet<Renderer>(set.Entries.Where(e => e.Renderer != null).Select(e => e.Renderer));
                var missing = surroundings.GetComponentsInChildren<Renderer>(true)
                    .Where(r => !Skipped(r) && HazeSettings.SquareDistance(r.bounds.center) >= HazeSettings.RampStart && !inSet.Contains(r))
                    .Select(r => r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name).ToList();
                CollectionAssert.IsEmpty(missing, "surround renderers without a hazed entry (re-run the haze bake):\n" + string.Join("\n", missing));
            });
        }

        [Test]
        public void MapSet_ContainsNoArenaFenceOrPlayfieldRenderer()
        {
            WithMap((map, surroundings, set) =>
            {
                var inSet = new HashSet<Renderer>(set.Entries.Where(e => e.Renderer != null).Select(e => e.Renderer));
                foreach (var root in map.GetRootGameObjects())
                {
                    if (root == surroundings) continue;
                    var leaked = root.GetComponentsInChildren<Renderer>(true).Where(inSet.Contains).Select(r => root.name + "/" + r.name).ToList();
                    CollectionAssert.IsEmpty(leaked, $"renderers of '{root.name}' (arena / props / playfield ground) must keep their materials");
                }
                foreach (var e in set.Entries)
                {
                    Assert.IsFalse(e.Renderer.name.StartsWith("Wall_", StringComparison.Ordinal)
                                   || e.Renderer.name.StartsWith("InnerWall_", StringComparison.Ordinal), e.Renderer.name + " is a fence panel");
                    foreach (var m in e.Original)
                        Assert.AreNotEqual(FenceFadeRules.ShaderName, m.shader.name, e.Renderer.name + " uses the fence fade shader");
                }
            });
        }

        [Test]
        public void HazedCopies_AreUrpAndKeepEmissionOn()
        {
            var map = AssetDatabase.LoadAssetAtPath<SurroundHazeMapSO>(SurroundHazeBaker.MapAssetPath);
            Assert.IsNotNull(map, SurroundHazeBaker.MapAssetPath);
            Assert.Greater(map.Copies.Count, 0);
            foreach (var c in map.Copies)
            {
                Assert.IsNotNull(c.Hazed, "copy of " + (c.Original != null ? c.Original.name : "?"));
                StringAssert.StartsWith(SurroundHazeBaker.HazedFolder, AssetDatabase.GetAssetPath(c.Hazed));
                StringAssert.StartsWith("Universal Render Pipeline/", c.Hazed.shader.name, c.Hazed.name);
                Assert.IsTrue(c.Hazed.IsKeywordEnabled("_EMISSION"), c.Hazed.name + ": _EMISSION off, the copy would only darken");
                Assert.AreNotEqual(MaterialGlobalIlluminationFlags.None, c.Hazed.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive,
                    c.Hazed.name + ": URP validation would switch _EMISSION back off");
                Assert.AreNotSame(c.Original, c.Hazed);
            }
        }
    }
}
