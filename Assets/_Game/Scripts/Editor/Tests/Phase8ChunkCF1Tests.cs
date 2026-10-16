using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CluckWars.Gameplay;
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
            var lin = HazeSettings.Colour.linear;
            Assert.AreEqual(lin.r, col.x, 1e-6f); Assert.AreEqual(lin.g, col.y, 1e-6f); Assert.AreEqual(lin.b, col.z, 1e-6f);
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
            finally { Object.DestroyImmediate(go); }
        }
    }
}
