using System;
using System.IO;
using System.Linq;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// The Death Mark victim's screen mood (<see cref="MarkScreenEffect"/>, <see cref="MarkScreenFeature"/>,
    /// CWMarkMood.shader): weight clamping and the off switch, the renderer wiring that keeps the shader in player
    /// builds, and a REAL render per quality tier (Game + Map at the match-camera pose) that checks DIM / COLD / RED
    /// do what Maestro's rules say. Set CW_MARK_CAPTURE_DIR to also write the frames as PNGs.
    /// </summary>
    public class MarkScreenEffectTests
    {
        private const string PcRenderer = "Assets/Settings/PC_Renderer.asset";
        private const string MobileRenderer = "Assets/Settings/Mobile_Renderer.asset";
        private const string ShaderPath = "Assets/_Game/Art/Shaders/CWMarkMood.shader";
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const int W = 960, H = 540;

        private string _originalScenePath;
        private int _originalQuality;
        private bool _originalAsync;

        [SetUp]
        public void SetUp()
        {
            MarkScreenEffect.Clear();
            _originalScenePath = SceneManager.GetActiveScene().path;
            _originalQuality = QualitySettings.GetQualityLevel();
            _originalAsync = ShaderUtil.allowAsyncCompilation;
        }

        [TearDown]
        public void TearDown()
        {
            MarkScreenEffect.Clear();
            MarkScreenEffect.RenderOutsidePlayMode = false;
            ShaderUtil.allowAsyncCompilation = _originalAsync;
            if (QualitySettings.GetQualityLevel() != _originalQuality) QualitySettings.SetQualityLevel(_originalQuality, true);
            if (SceneManager.GetActiveScene().path != _originalScenePath)
            {
                if (string.IsNullOrEmpty(_originalScenePath))
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else
                    EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
            }
        }

        // ---- state ------------------------------------------------------------------------------------------------

        [Test]
        public void Set_ClampsEveryWeightTo01()
        {
            MarkScreenEffect.Set(-0.5f, 1.7f, 0.25f);
            Assert.AreEqual(new Vector3(0f, 1f, 0.25f), MarkScreenEffect.Current);
            MarkScreenEffect.Set(2f, -3f, 9f);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), MarkScreenEffect.Current);
        }

        [Test]
        public void Set_NaNOrInfinity_ReadsAsOffOrFull_NeverStuck()
        {
            MarkScreenEffect.Set(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), MarkScreenEffect.Current);
        }

        [Test]
        public void Clear_ZeroesWeights_AndTurnsThePassOff()
        {
            MarkScreenEffect.Set(1f, 1f, 1f);
            Assert.AreEqual(MarkScreenPass.Graded, MarkScreenEffect.ActivePass);
            MarkScreenEffect.Clear();
            Assert.AreEqual(Vector3.zero, MarkScreenEffect.Current);
            Assert.AreEqual(MarkScreenPass.None, MarkScreenEffect.ActivePass);
        }

        [Test]
        public void AllZero_OrUnderEpsilon_EnqueuesNothing()
        {
            MarkScreenEffect.Set(0f, 0f, 0f);
            Assert.AreEqual(MarkScreenPass.None, MarkScreenEffect.ActivePass);
            float tiny = FeedbackTuning.MarkMoodOffEpsilon * 0.5f;
            MarkScreenEffect.Set(tiny, tiny, tiny);
            Assert.AreEqual(MarkScreenPass.None, MarkScreenEffect.ActivePass, "faded-out weights must cost nothing");
        }

        [Test]
        public void PassSelection_OnlyColdPaysForTheColourRead()
        {
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(new Vector3(1f, 0f, 0f)), "dim only");
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(new Vector3(0f, 0f, 1f)), "red only");
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(new Vector3(1f, 0f, 1f)), "dim + red");
            Assert.AreEqual(MarkScreenPass.Graded, MarkScreenEffect.PassFor(new Vector3(1f, 0.5f, 0f)), "cold");
            Assert.AreEqual(MarkScreenPass.Graded, MarkScreenEffect.PassFor(new Vector3(0f, 1f, 0f)), "cold alone");
        }

        [Test]
        public void Uniforms_DimIsVignetteShaped_CentreNeverOpaque()
        {
            MarkScreenEffect.Uniforms(new Vector3(1f, 1f, 1f), out _, out var shape, out var red, out _);
            Assert.Less(shape.x, shape.y, "darker at the edge than at the centre");
            Assert.LessOrEqual(shape.x, 0.25f, "nothing opaque covers the centre");
            Assert.LessOrEqual(shape.y, 1f);
            Assert.Less(red.w, 0.12f, "red stays almost imperceptible");

            MarkScreenEffect.Uniforms(Vector3.zero, out _, out var off, out var offRed, out var offGrade);
            Assert.AreEqual(0f, off.x); Assert.AreEqual(0f, off.y); Assert.AreEqual(0f, offRed.w); Assert.AreEqual(0f, offGrade.x);
        }

        [Test]
        public void Uniforms_ColdTurnsTheDarknessVioletAndDesaturates_RedIsSevenPercent()
        {
            MarkScreenEffect.Uniforms(new Vector3(1f, 1f, 1f), out var dark, out _, out var red, out var grade);
            var violet = FeedbackTuning.MarkMoodColdColour;
            Assert.AreEqual(violet.r, dark.x, 1e-5f); Assert.AreEqual(violet.g, dark.y, 1e-5f); Assert.AreEqual(violet.b, dark.z, 1e-5f);
            Assert.Greater(dark.z, dark.x, "violet: blue over red"); Assert.Greater(dark.x, dark.y, "violet: red over green");
            Assert.AreEqual(FeedbackTuning.MarkMoodColdDesaturation, grade.x, 1e-5f);
            Assert.AreEqual(0.07f, red.w, 1e-5f);

            MarkScreenEffect.Uniforms(new Vector3(1f, 0f, 0f), out var dimDark, out _, out _, out var dimGrade);
            Assert.AreEqual(0f, dimGrade.x, "no desaturation without cold");
            Assert.AreEqual(FeedbackTuning.MarkMoodDimColour.b, dimDark.z, 1e-5f);
        }

        // ---- wiring / build inclusion --------------------------------------------------------------------------------

        [TestCase(PcRenderer)]
        [TestCase(MobileRenderer)]
        public void Renderer_HasOneActiveMoodFeature_SerializingTheShader(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(data, path);
            Assert.IsTrue(data.rendererFeatures.All(f => f != null), path + " has a missing renderer feature (broken sub-asset)");
            var mood = data.rendererFeatures.OfType<MarkScreenFeature>().ToList();
            Assert.AreEqual(1, mood.Count, path);
            Assert.IsTrue(mood[0].isActive, path);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath), mood[0].MoodShader,
                "the feature must serialize CWMarkMood.shader: that reference is what keeps it in player builds");
        }

        [Test]
        public void MoodShader_CompilesWithBothPasses()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            Assert.AreEqual(MarkScreenFeature.ShaderName, shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "CWMarkMood.shader has compile errors");
            Assert.IsTrue(shader.isSupported, "CWMarkMood.shader is not supported on this device");
            var mat = new Material(shader);
            try
            {
                Assert.AreEqual(2, mat.passCount);
                Assert.AreEqual(MarkScreenFeature.OverlayPassIndex, mat.FindPass("CW Mark Mood Overlay"));
                Assert.AreEqual(MarkScreenFeature.GradedPassIndex, mat.FindPass("CW Mark Mood Graded"));
            }
            finally { UnityEngine.Object.DestroyImmediate(mat); }
        }

        [Test]
        public void MoodFeature_SkipsOverlayAndNonGameCameras()
        {
            var go = new GameObject("__MoodCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                Assert.IsTrue(MarkScreenFeature.ShouldDraw(cam, CameraRenderType.Base));
                Assert.IsFalse(MarkScreenFeature.ShouldDraw(cam, CameraRenderType.Overlay), "the UI overlay camera (HUD) is never dimmed");
                cam.cameraType = CameraType.SceneView;
                Assert.IsFalse(MarkScreenFeature.ShouldDraw(cam, CameraRenderType.Base));
                Assert.IsFalse(MarkScreenFeature.ShouldDraw(null, CameraRenderType.Base));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- real render ------------------------------------------------------------------------------------------

        [TestCase("PC")]
        [TestCase("Mobile")]
        public void RealRender_DimColdRed_DoWhatTheRulesSay(string tier)
        {
            ShaderUtil.allowAsyncCompilation = false;
            int level = Array.IndexOf(QualitySettings.names, tier);
            Assert.GreaterOrEqual(level, 0, $"no '{tier}' quality level");
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(GameScene));
            QualitySettings.SetQualityLevel(level, true);
            var rp = GraphicsSettings.currentRenderPipeline;
            Assert.IsNotNull(rp, tier);

            var matchCam = UnityEngine.Object.FindFirstObjectByType<MatchCamera>(FindObjectsInactive.Include);
            Assert.IsNotNull(matchCam, "Game.unity has no MatchCamera");
            var mc = new SerializedObject(matchCam);
            float pitch = mc.FindProperty("_pitch").floatValue, distance = mc.FindProperty("_distance").floatValue;
            float ortho = mc.FindProperty("_orthoSize").floatValue, near = mc.FindProperty("_nearClipPlane").floatValue;

            var camGo = new GameObject("__MoodTestCam");
            RenderTexture rt = null;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = RenderTierPolicy.PostProcessingFor(rp);
                cam.orthographic = true;
                cam.orthographicSize = ortho;
                cam.nearClipPlane = near;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = mc.FindProperty("_backgroundColor").colorValue;
                var rot = Quaternion.Euler(pitch, 225f, 0f);
                cam.transform.SetPositionAndRotation(-(rot * Vector3.forward) * distance, rot);

                int msaa = 1;
                var msaaProp = rp.GetType().GetProperty("msaaSampleCount");
                if (msaaProp != null) msaa = Mathf.Max(1, Convert.ToInt32(msaaProp.GetValue(rp)));
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };

                MarkScreenEffect.RenderOutsidePlayMode = true;
                var off = Render(cam, rt, 0f, 0f, 0f, tier, "off");
                var dim = Render(cam, rt, 1f, 0f, 0f, tier, "dim");
                var cold = Render(cam, rt, 1f, 1f, 0f, tier, "cold");
                var red = Render(cam, rt, 1f, 1f, 1f, tier, "cold_red");
                var offAgain = Render(cam, rt, 0f, 0f, 0f, tier, null);

                var centre = new RectInt(W * 2 / 5, H * 2 / 5, W / 5, H / 5);
                Func<Color32[], RectInt, Vector3> mean = (px, r) => Mean(px, r);
                Vector3 offC = mean(off, centre), dimC = mean(dim, centre), coldC = mean(cold, centre);
                Vector3 offE = Corners(off), dimE = Corners(dim), coldE = Corners(cold), redE = Corners(red);
                float lumOffC = Lum(offC), lumDimC = Lum(dimC), lumOffE = Lum(offE), lumDimE = Lum(dimE);
                string ctx = $"[{tier}] off C{offC} E{offE} | dim C{dimC} E{dimE} | cold C{coldC} E{coldE} | red E{redE}";
                TestContext.WriteLine(ctx);

                Assert.Greater(lumOffC, 20f, "scene too dark to judge " + ctx);
                // PC frames carry a little frame-to-frame noise of their own (time-driven shaders / post), so "identical"
                // is judged on the mean; a pass left on would shift it by tens of levels.
                Assert.Less(MeanDiff(off, offAgain), 0.5f, "all-zero must render as if never set " + ctx);
                // DIM: centre lightly darkened, edges much more.
                Assert.Less(lumDimC, lumOffC - 2f, "DIM must darken the centre a little " + ctx);
                Assert.Greater(lumDimC / lumOffC, 0.75f, "DIM must not black out the centre " + ctx);
                Assert.Less(lumDimE / Mathf.Max(1f, lumOffE), lumDimC / lumOffC - 0.15f, "DIM must be heavier at the edges " + ctx);
                // COLD: centre desaturated, edge darkness violet (blue over green, more than with DIM alone).
                Assert.Less(Sat(coldC), Sat(dimC) * 0.75f, "COLD must desaturate the scene " + ctx);
                Assert.Greater(coldE.z - coldE.y, dimE.z - dimE.y + 3f, "COLD darkness must turn violet " + ctx);
                Assert.LessOrEqual(Lum(coldE), Lum(dimE) + 8f, "COLD must stay darkness, not a lighter violet fog " + ctx);
                // RED: a small, visible-in-numbers red lift in the darkness, nowhere near a red wash.
                Assert.Greater(redE.x - coldE.x, 2f, "RED tint missing " + ctx);
                Assert.Less(redE.x - coldE.x, 30f, "RED must stay almost imperceptible " + ctx);
            }
            finally
            {
                MarkScreenEffect.Clear();
                MarkScreenEffect.RenderOutsidePlayMode = false;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static Color32[] Render(Camera cam, RenderTexture rt, float d, float c, float r, string tier, string label)
        {
            MarkScreenEffect.Set(d, c, r);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels32();
            var dir = Environment.GetEnvironmentVariable("CW_MARK_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir) && label != null)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"mark_{tier}_{label}.png"), t.EncodeToPNG());
            }
            UnityEngine.Object.DestroyImmediate(t);
            return px;
        }

        private static Vector3 Mean(Color32[] px, RectInt r)
        {
            double sr = 0, sg = 0, sb = 0; int n = 0;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                var c = px[y * W + x];
                sr += c.r; sg += c.g; sb += c.b; n++;
            }
            return new Vector3((float)(sr / n), (float)(sg / n), (float)(sb / n));
        }

        /// <summary>Mean of the four corner patches (each 1/10 of the frame per side).</summary>
        private static Vector3 Corners(Color32[] px)
        {
            int w = W / 10, h = H / 10;
            return (Mean(px, new RectInt(0, 0, w, h)) + Mean(px, new RectInt(W - w, 0, w, h))
                  + Mean(px, new RectInt(0, H - h, w, h)) + Mean(px, new RectInt(W - w, H - h, w, h))) / 4f;
        }

        private static float Lum(Vector3 c) => 0.299f * c.x + 0.587f * c.y + 0.114f * c.z;

        private static float Sat(Vector3 c)
        {
            float mx = Mathf.Max(c.x, Mathf.Max(c.y, c.z)), mn = Mathf.Min(c.x, Mathf.Min(c.y, c.z));
            return mx - mn;
        }

        private static float MeanDiff(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            return (float)(sum / (3.0 * a.Length));
        }
    }
}
