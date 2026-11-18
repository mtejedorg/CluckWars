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
        /// <summary>The frame-to-frame noise the real-render tests already allow (mean absolute difference, 0..255).</summary>
        private const float FrameNoise = 0.5f;

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
                Assert.AreEqual(3, mat.passCount);   // K13 added the exempt mask as a third pass; the first two keep their indices
                Assert.AreEqual(MarkScreenFeature.OverlayPassIndex, mat.FindPass("CW Mark Mood Overlay"));
                Assert.AreEqual(MarkScreenFeature.GradedPassIndex, mat.FindPass("CW Mark Mood Graded"));
                Assert.AreEqual(MarkScreenFeature.MaskPassIndex, mat.FindPass("CW Mark Mood Exempt Mask"));
                Assert.AreEqual(FeedbackTuning.MarkExemptStencilValue, mat.GetFloat("_CW_MarkExemptRef"),
                    "the shader's default stencil value is the tuning constant");
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

        // ---- Exempt birds (K13) ---------------------------------------------------------------------------------------

        /// <summary>The shader's tint coverage at a screen point (its MoodLayer alpha), from the same uniforms the feature sets.</summary>
        private static float MoodCoverage(Vector2 uv, Vector4 shape, Vector4 red, Vector4 grade)
        {
            float r = (uv - new Vector2(0.5f, 0.5f)).magnitude * 1.41421356f;
            float t = Mathf.Clamp01((r - shape.z) * shape.w);
            t = t * t * (3f - 2f * t);
            float darkA = Mathf.Lerp(shape.x, shape.y, t);
            float redA = red.w * Mathf.Lerp(grade.y, 1f, t);
            return 1f - (1f - darkA) * (1f - redA);
        }

        private static Vector3 Patch(Color32[] px, Vector2Int at) => Mean(px, new RectInt(at.x - 4, at.y - 4, 8, 8));

        /// <summary>A bright, flat object the camera sees in front of the arena; <paramref name="exempt"/> puts the exempt layer bit on it.</summary>
        private static GameObject ExemptProbe(Camera cam, Vector2 viewport, float size, Color colour, bool exempt)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = exempt ? "__ExemptMarked" : "__ExemptUnmarked";
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, Mathf.Max(cam.nearClipPlane, 0f) + 3f));
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = colour };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (exempt) r.renderingLayerMask |= FeedbackTuning.MarkExemptRenderingLayer;
            return go;
        }

        /// <summary>The hunter's glow ("hunt") or the victim's full dim + cold ("victim", the Graded path), one tier.</summary>
        [TestCase("PC", "hunt")]
        [TestCase("PC", "victim")]
        [TestCase("Mobile", "hunt")]
        [TestCase("Mobile", "victim")]
        public void RealRender_ExemptBird_KeepsItsTrueColour_EverythingElseStaysTinted(string tier, string mood)
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
            float ortho = mc.FindProperty("_orthoSize").floatValue;

            var camGo = new GameObject("__ExemptTestCam");
            RenderTexture rt = null;
            GameObject marked = null, unmarked = null;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = RenderTierPolicy.PostProcessingFor(rp);
                cam.orthographic = true;
                cam.orthographicSize = ortho;
                cam.nearClipPlane = mc.FindProperty("_nearClipPlane").floatValue;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = mc.FindProperty("_backgroundColor").colorValue;
                var rot = Quaternion.Euler(pitch, 225f, 0f);
                cam.transform.SetPositionAndRotation(-(rot * Vector3.forward) * distance, rot);

                int msaa = 1;
                var msaaProp = rp.GetType().GetProperty("msaaSampleCount");
                if (msaaProp != null) msaa = Mathf.Max(1, Convert.ToInt32(msaaProp.GetValue(rp)));
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };
                cam.targetTexture = rt;
                cam.aspect = (float)W / H;

                // Two bright objects near opposite screen corners, where both moods are strong; one carries the exempt bit.
                Vector2 markedVp = new Vector2(0.07f, 0.12f), unmarkedVp = new Vector2(0.93f, 0.88f), emptyVp = new Vector2(0.07f, 0.88f);
                var colour = new Color(0.9f, 0.45f, 0.1f);
                marked = ExemptProbe(cam, markedVp, ortho * 0.3f, colour, exempt: true);
                unmarked = ExemptProbe(cam, unmarkedVp, ortho * 0.3f, colour, exempt: false);
                Vector2Int Pix(Vector2 vp) => new Vector2Int(Mathf.RoundToInt(vp.x * W), Mathf.RoundToInt(vp.y * H));
                Vector2Int markedPx = Pix(markedVp), unmarkedPx = Pix(unmarkedVp), emptyPx = Pix(emptyVp);

                MarkScreenEffect.RenderOutsidePlayMode = true;
                void SetMood()
                {
                    MarkScreenEffect.Clear();
                    if (mood == "hunt") MarkScreenEffect.SetHunt(1f, FeedbackTuning.MarkKillColor, FeedbackTuning.MarkHuntMoodGlowPounce);
                    else MarkScreenEffect.Set(1f, 1f, 0f);
                }
                Color32[] Shot(bool tinted, int exempt)
                {
                    MarkScreenEffect.Clear();
                    if (tinted) SetMood();
                    MarkScreenEffect.SetExemptCount(exempt);
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
                    UnityEngine.Object.DestroyImmediate(t);
                    cam.targetTexture = rt;
                    return px;
                }

                var untinted = Shot(false, 0);
                var withExempt = Shot(true, 1);       // the mark is on and one bird is exempt
                var noExemption = Shot(true, 0);      // the mark is on, nobody exempt: how the tint rendered before K13

                // The coverage the tint has at each probe, from the shader's own uniforms.
                SetMood();
                var hunt = MarkScreenEffect.HuntCurrent;
                MarkScreenEffect.Uniforms(MarkScreenEffect.Current, hunt.Edge, hunt.Colour, hunt.Glow, out _, out var shape, out var red, out var grade);
                float markedA = MoodCoverage(markedVp, shape, red, grade), unmarkedA = MoodCoverage(unmarkedVp, shape, red, grade);
                string ctx = $"[{tier}/{mood}] coverage marked {markedA:0.00} unmarked {unmarkedA:0.00}; untinted {Patch(untinted, markedPx)} / {Patch(untinted, unmarkedPx)}";
                TestContext.WriteLine(ctx);
                Assert.Greater(markedA, 0.2f, "the probe sits where the tint is strong " + ctx);
                Assert.Greater(Lum(Patch(untinted, markedPx)), 20f, "probe not visible " + ctx);

                // The exempt object: its true colour, within the frame noise of the untinted render.
                var exemptRect = new RectInt(markedPx.x - 4, markedPx.y - 4, 8, 8);
                Assert.Less(RectDiff(untinted, withExempt, exemptRect), FrameNoise, "the exempt bird must keep its true colour " + ctx);
                // Without the exemption the same pixels ARE tinted (so the test can tell the two apart).
                Assert.Greater(RectDiff(untinted, noExemption, exemptRect), 8f * FrameNoise, "baseline tint missing " + ctx);
                if (mood == "victim")
                    Assert.Less(Lum(Patch(noExemption, markedPx)), Lum(Patch(untinted, markedPx)) * (1f - 0.5f * markedA), "baseline dim missing " + ctx);

                // The unmarked object and the empty background are tinted exactly as without the exemption.
                var unmarkedRect = new RectInt(unmarkedPx.x - 4, unmarkedPx.y - 4, 8, 8);
                var emptyRect = new RectInt(emptyPx.x - 4, emptyPx.y - 4, 8, 8);
                Assert.Less(RectDiff(noExemption, withExempt, unmarkedRect), FrameNoise, "the unmarked object keeps the tint " + ctx);
                Assert.Less(RectDiff(noExemption, withExempt, emptyRect), FrameNoise, "the background keeps the tint " + ctx);
                Assert.Greater(RectDiff(untinted, withExempt, unmarkedRect), 8f * FrameNoise, "unmarked object not tinted " + ctx);
                if (mood == "victim")
                    Assert.Less(Lum(Patch(withExempt, unmarkedPx)), Lum(Patch(untinted, unmarkedPx)) * (1f - 0.5f * unmarkedA), "unmarked object not dimmed " + ctx);
                if (mood == "victim")
                    Assert.Less(Sat(Patch(withExempt, unmarkedPx)), Sat(Patch(untinted, unmarkedPx)) * (1f - 0.5f * grade.x),
                        "the Graded path must still desaturate the unmarked object " + ctx);

                // With no marked object the render is today's: drop the bit and ask for an exemption anyway.
                marked.GetComponent<MeshRenderer>().renderingLayerMask = 1u;
                var nothingMarked = Shot(true, 1);
                Assert.Less(MeanDiff(noExemption, nothingMarked), FrameNoise, "with no marked renderer the render must equal the unexempted one " + ctx);
            }
            finally
            {
                MarkScreenEffect.Clear();
                MarkScreenEffect.RenderOutsidePlayMode = false;
                if (marked != null) UnityEngine.Object.DestroyImmediate(marked);
                if (unmarked != null) UnityEngine.Object.DestroyImmediate(unmarked);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        // ---- The hunter's screen edge (K10) ---------------------------------------------------------------------------

        [Test]
        public void SetHunt_ClampsAndIsNaNSafe_PassesTheColourThrough()
        {
            var c = new Color(0.2f, 0.4f, 0.6f, 1f);
            MarkScreenEffect.SetHunt(-0.5f, c, 1.7f);
            Assert.AreEqual((0f, 1f, c), MarkScreenEffect.HuntCurrent);
            MarkScreenEffect.SetHunt(float.NaN, c, float.NaN);
            Assert.AreEqual((0f, 0f, c), MarkScreenEffect.HuntCurrent, "NaN reads as off, never stuck");
            MarkScreenEffect.SetHunt(2f, c, 0.3f);
            Assert.AreEqual((1f, 0.3f, c), MarkScreenEffect.HuntCurrent);
        }

        [Test]
        public void Clear_ZeroesTheHuntMoodToo_AndTurnsThePassOff()
        {
            MarkScreenEffect.SetHunt(1f, Color.yellow, 0.4f);
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.ActivePass);
            MarkScreenEffect.Clear();
            Assert.AreEqual((0f, 0f, Color.clear), MarkScreenEffect.HuntCurrent);
            Assert.AreEqual(MarkScreenPass.None, MarkScreenEffect.ActivePass);
        }

        [Test]
        public void HuntMood_AloneIsAlwaysTheOverlay_NeverGraded_AndNoneUnderEpsilon()
        {
            float eps = FeedbackTuning.MarkMoodOffEpsilon;
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(Vector3.zero, 1f, 0f), "dark edge only");
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(Vector3.zero, 0f, FeedbackTuning.MarkHuntMoodGlowPounce), "glow only");
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.PassFor(Vector3.zero, 1f, FeedbackTuning.MarkHuntMoodGlowBlocked));
            Assert.AreEqual(MarkScreenPass.None, MarkScreenEffect.PassFor(Vector3.zero, eps * 0.5f, eps * 0.5f), "faded out costs nothing");
            Assert.AreEqual(MarkScreenPass.Graded, MarkScreenEffect.PassFor(new Vector3(0f, 1f, 0f), 1f, 0.3f), "the victim's cold still grades");
            MarkScreenEffect.SetHunt(1f, Color.yellow, 0.3f);
            Assert.AreEqual(MarkScreenPass.Overlay, MarkScreenEffect.ActivePass);
        }

        [Test]
        public void HuntUniforms_CentreUntouched_EdgeAndGlowFromTheTuning_ColourPassedThrough()
        {
            var colour = FeedbackTuning.MarkKillColor;
            MarkScreenEffect.Uniforms(Vector3.zero, 1f, colour, FeedbackTuning.MarkHuntMoodGlowPounce, out var dark, out var shape, out var red, out var grade);
            Assert.AreEqual(0f, shape.x, "no darkness at the centre");
            Assert.AreEqual(FeedbackTuning.MarkHuntMoodEdge, shape.y, 1e-5f);
            Assert.AreEqual(FeedbackTuning.MarkHuntMoodVignetteInner, shape.z, 1e-5f);
            Assert.AreEqual(1f / (FeedbackTuning.MarkMoodVignetteOuter - FeedbackTuning.MarkHuntMoodVignetteInner), shape.w, 1e-4f);
            Assert.AreEqual(FeedbackTuning.MarkMoodDimColour.r, dark.x, 1e-5f);
            Assert.AreEqual(FeedbackTuning.MarkMoodDimColour.g, dark.y, 1e-5f);
            Assert.AreEqual(FeedbackTuning.MarkMoodDimColour.b, dark.z, 1e-5f);
            Assert.AreEqual(colour.r, red.x, 1e-5f); Assert.AreEqual(colour.g, red.y, 1e-5f); Assert.AreEqual(colour.b, red.z, 1e-5f);
            Assert.AreEqual(FeedbackTuning.MarkHuntMoodGlowPounce, red.w, 1e-5f);
            Assert.AreEqual(0f, grade.x, "never desaturated");
            Assert.AreEqual(FeedbackTuning.MarkHuntMoodGlowCentreShare, grade.y, 1e-5f);

            MarkScreenEffect.Uniforms(Vector3.zero, 0.5f, colour, 0f, out _, out var half, out var none, out _);
            Assert.AreEqual(0.5f * FeedbackTuning.MarkHuntMoodEdge, half.y, 1e-5f, "edge scales with its weight");
            Assert.AreEqual(0f, none.w, "no glow, no tint");
        }

        [Test]
        public void Uniforms_VictimOnlyIsUnchanged_AndTheVictimWinsWhenBothAreSet()
        {
            var w = new Vector3(1f, 0.5f, 1f);
            MarkScreenEffect.Uniforms(w, out var d0, out var s0, out var r0, out var g0);
            MarkScreenEffect.Uniforms(w, 0f, Color.clear, 0f, out var d1, out var s1, out var r1, out var g1);
            Assert.AreEqual((d0, s0, r0, g0), (d1, s1, r1, g1), "no hunt mood: the same four vectors");
            MarkScreenEffect.Uniforms(w, 1f, Color.yellow, 0.4f, out var d2, out var s2, out var r2, out var g2);
            Assert.AreEqual((d0, s0, r0, g0), (d2, s2, r2, g2), "both set: the victim's mood is drawn");
        }

        [TestCase("PC")]
        [TestCase("Mobile")]
        public void RealRender_HuntEdge_DarkensAndTintsTheCornersAndLeavesTheCentre(string tier)
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

            var camGo = new GameObject("__HuntMoodTestCam");
            RenderTexture rt = null;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = RenderTierPolicy.PostProcessingFor(rp);
                cam.orthographic = true;
                cam.orthographicSize = mc.FindProperty("_orthoSize").floatValue;
                cam.nearClipPlane = mc.FindProperty("_nearClipPlane").floatValue;
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
                var off = RenderHunt(cam, rt, 0f, Color.clear, 0f);
                var dark = RenderHunt(cam, rt, 1f, Color.clear, 0f);
                var yellow = RenderHunt(cam, rt, 1f, FeedbackTuning.MarkKillColor, FeedbackTuning.MarkHuntMoodGlowPounce);
                var red = RenderHunt(cam, rt, 1f, FeedbackTuning.MarkHuntBlockedColor, FeedbackTuning.MarkHuntMoodGlowBlocked);

                var centre = new RectInt(W * 2 / 5, H * 2 / 5, W / 5, H / 5);
                Vector3 offE = Corners(off), darkE = Corners(dark), yellowE = Corners(yellow), redE = Corners(red);
                string ctx = $"[{tier}] corners off{offE} dark{darkE} yellow{yellowE} red{redE}";
                TestContext.WriteLine(ctx);
                Assert.Greater(Lum(Mean(off, centre)), 20f, "scene too dark to judge " + ctx);

                // The centre stays within the frame-to-frame noise of the untinted render, for every state.
                foreach (var (label, px) in new[] { ("dark", dark), ("yellow", yellow), ("red", red) })
                    Assert.Less(RectDiff(off, px, centre), 0.5f, $"{label}: the centre must stay untouched {ctx}");

                // Dark edge: the corners darken by a clear share of the tuned edge alpha, nothing desaturated at the centre.
                Assert.Less(Lum(darkE), Lum(offE) * (1f - 0.5f * FeedbackTuning.MarkHuntMoodEdge), "dark edge must darken the corners " + ctx);

                // Glow: the corners move toward the glow's colour (the distance to it shrinks by a share of the tuned alpha).
                var kill = FeedbackTuning.MarkKillColor; var block = FeedbackTuning.MarkHuntBlockedColor;
                Assert.Less(Dist(yellowE, kill), Dist(darkE, kill) * (1f - 0.5f * FeedbackTuning.MarkHuntMoodGlowPounce), "yellow glow missing " + ctx);
                Assert.Less(Dist(redE, block), Dist(darkE, block) * (1f - 0.5f * FeedbackTuning.MarkHuntMoodGlowBlocked), "red glow missing " + ctx);
            }
            finally
            {
                MarkScreenEffect.Clear();
                MarkScreenEffect.RenderOutsidePlayMode = false;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static Color32[] RenderHunt(Camera cam, RenderTexture rt, float edge, Color colour, float glow)
        {
            MarkScreenEffect.Clear();
            MarkScreenEffect.SetHunt(edge, colour, glow);
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
            UnityEngine.Object.DestroyImmediate(t);
            return px;
        }

        /// <summary>Mean absolute per-channel difference of two frames inside a rectangle.</summary>
        private static float RectDiff(Color32[] a, Color32[] b, RectInt r)
        {
            double sum = 0; int n = 0;
            for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int i = y * W + x;
                sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                n += 3;
            }
            return (float)(sum / n);
        }

        /// <summary>Distance between a mean pixel (0..255) and a colour (0..1), in 0..255 units.</summary>
        private static float Dist(Vector3 px, Color c) => (px - new Vector3(c.r, c.g, c.b) * 255f).magnitude;
    }
}
