using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Gameplay;
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
    /// Phase 8 D1 "mound clips to lemon": with the near-white grain tile (mean 231/255) the lit top of a full
    /// mound rendered 241/238/90 on PC (Neutral tonemap white) and 255/249/83 on Mobile (clipped): tint x texture x
    /// light went over 1, the hue slid from food gold (#F5C842, 45 deg) to lemon (58-60 deg) and the grain washed out.
    /// <c>grain_corn_tile_neutral.png</c> was re-levelled to a mid value with more contrast (luminance mean 231 -> 154,
    /// std 14.5 -> 19.8, green x0.965 so the PC post lands near gold too).
    /// <para>
    /// This is a REAL render, not an analytic proxy: Game + Map loaded as in a match, a <c>FoodPile.prefab</c> instance
    /// at the full footprint (read from the scene's <see cref="MapGenerator"/>) with the full-fill tint
    /// (<see cref="FoodPileVisuals.TintFor"/>) in its property block, rendered by a camera posed exactly like
    /// <see cref="MatchCamera"/> (its serialized pitch / distance / ortho size / near plane, seat 0's yaw) under each
    /// quality level's own URP asset (PC with its post, Mobile without, tier MSAA). The mound is isolated by a render
    /// pair (mound drawn vs ShadowsOnly), so its shadow and the ground are identical in both frames.
    /// "Lit top" = mean colour of the brightest 25 % (Rec.709 luma) of the mound's pixels, the same rule as
    /// <c>tools/ui_capture/capture_d1_mound.py</c>' metrics.
    /// </para>
    /// </summary>
    public class Phase8ChunkD1Tests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const string PilePrefab = "Assets/_Game/Prefabs/FoodPile.prefab";
        private const string ColorSchemePath = "Assets/_Game/Data/ColorScheme.asset";
        private const string GrainTile = "Assets/Generated/Env/Textures/grain_corn_tile_neutral.png";

        /// <summary>Seat 0's match-camera yaw (MatchCamera: bearing from corner (+,+) to the centre).</summary>
        public const float Seat0Yaw = 225f;

        /// <summary>Lit top red ceiling (D1 target): headroom under the clip on Mobile and under PC's tonemap white (241).</summary>
        public const float MaxLitTopRed = 230f;

        /// <summary>Food gold #F5C842 hue (45.3 deg) +-6: Mobile renders ~43.5, PC's post (contrast + Neutral) ~47.5; lemon was 58-60.</summary>
        public const float MinHue = 39.5f, MaxHue = 51.5f;

        /// <summary>
        /// Grain detail floor: std of the high-pass luma (pixel minus its 7x7 box mean) over the lit half, at this test's
        /// 960x540. Shipped tile 4.8-6.2; the pre-D1 tile read 3.4 on the PC centre mound (washed out) but higher on Mobile,
        /// where clipped plateaus leave hard edges, so this is a floor against an over-flattened tile, not the D1 detector
        /// (the red / hue / clip asserts are: the pre-D1 tile fails all three on both tiers).
        /// </summary>
        public const float MinGrainStd = 4.0f;

        /// <summary>Fraction of mound pixels allowed to have a channel at 254+ (was 37-55 % on Mobile).</summary>
        public const float MaxClippedFraction = 0.01f;

        private const int W = 960, H = 540;

        public struct MoundRead
        {
            public string Tier, Pile;
            public int Pixels;
            public Color32 LitTop;
            public float LitTopR, LitTopG, LitTopB, Hue, GrainStd, Clipped;
            public bool Post;
            public int Msaa;

            public override string ToString() => FormattableString.Invariant(
                $"{Tier} {Pile}: {Pixels} mound px, lit top {LitTopR:0.0}/{LitTopG:0.0}/{LitTopB:0.0} hue {Hue:0.0} deg, grain std {GrainStd:0.00}, clipped {Clipped:P2}, post {Post}, msaa {Msaa}");
        }

        private string _originalScenePath;
        private int _originalQuality;

        [SetUp]
        public void CaptureState()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            _originalQuality = QualitySettings.GetQualityLevel();
        }

        [TearDown]
        public void RestoreState()
        {
            QualitySettings.SetQualityLevel(_originalQuality, true);
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            foreach (var tier in new[] { "PC", "Mobile" })
            foreach (var pile in new[] { "centre", "personal" })
                yield return new TestCaseData(tier, pile).SetName($"FullMound_AtTheMatchCamera_{tier}_{pile}_LitTopIsGoldNotClipped_GrainVisible");
        }

        [TestCaseSource(nameof(Cases))]
        public void FullMound_AtTheMatchCamera(string tier, string pile)
        {
            var r = Measure(tier, pile, null);
            TestContext.WriteLine(r.ToString());
            Assert.Greater(r.Pixels, 1500, $"Mound barely visible / not isolated by the render pair. {r}");
            Assert.LessOrEqual(r.LitTopR, MaxLitTopRed, $"Lit top too bright (clips toward lemon). {r}");
            Assert.That(r.Hue, Is.InRange(MinHue, MaxHue), $"Lit top hue off food gold (#F5C842 = 45.3 deg). {r}");
            Assert.LessOrEqual(r.Clipped, MaxClippedFraction, $"Mound pixels clipped. {r}");
            Assert.GreaterOrEqual(r.GrainStd, MinGrainStd, $"Grain detail washed out. {r}");
        }

        /// <summary>
        /// The tile itself (cheap check, no render): mid value with contrast, near-neutral so the tint carries the hue,
        /// and still tileable (a point operation; edge columns/rows wrap like the source did).
        /// </summary>
        [Test]
        public void GrainTile_IsMidValueWithContrast_AndNearNeutral()
        {
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(System.IO.File.ReadAllBytes(GrainTile)), "could not decode " + GrainTile);
                var px = tex.GetPixels32();
                double sr = 0, sg = 0, sb = 0, sy = 0, syy = 0;
                foreach (var c in px)
                {
                    double y = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    sr += c.r; sg += c.g; sb += c.b; sy += y; syy += y * y;
                }
                double n = px.Length, mean = sy / n, std = Math.Sqrt(Math.Max(0, syy / n - mean * mean));
                string info = $"tile mean RGB {sr / n:0.0}/{sg / n:0.0}/{sb / n:0.0}, luma mean {mean:0.0} std {std:0.0}";
                TestContext.WriteLine(info);
                Assert.That(mean, Is.InRange(135.0, 175.0), "grain tile must be mid-value (near-white tiles clip the lit top). " + info);
                Assert.GreaterOrEqual(std, 17.0, "grain tile needs contrast to read as grain. " + info);
                Assert.Less(Math.Max(sr, Math.Max(sg, sb)) / n - Math.Min(sr, Math.Min(sg, sb)) / n, 15.0, "grain tile must stay near-neutral: the fill tint carries the hue. " + info);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// Renders the full mound at the match camera under <paramref name="tier"/>'s quality level and measures it.
        /// <paramref name="baseMapOverridePng"/> (a PNG file path) swaps the grain tile through the property block, loaded after
        /// the scene loads (diagnostics only, e.g. to re-measure the pre-D1 tile).
        /// Leaves Game + Map open and the quality level changed: callers (the test's TearDown) restore both.
        /// </summary>
        public static MoundRead Measure(string tier, string pile, string baseMapOverridePng)
        {
            int level = Array.IndexOf(QualitySettings.names, tier);
            Assert.GreaterOrEqual(level, 0, $"no '{tier}' quality level ({string.Join(",", QualitySettings.names)})");
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            var game = SceneManager.GetSceneByPath(GameScene);
            SceneManager.SetActiveScene(game);
            QualitySettings.SetQualityLevel(level, true);
            var rp = GraphicsSettings.currentRenderPipeline;
            Assert.IsNotNull(rp, "no render pipeline for " + tier);

            var gen = UnityEngine.Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
            Assert.IsNotNull(gen, "Game.unity has no MapGenerator");
            var genSo = new SerializedObject(gen);
            var footprint = genSo.FindProperty(pile == "centre" ? "_centerPileFootprint" : "_personalPileFootprint").vector2Value;

            var matchCam = UnityEngine.Object.FindFirstObjectByType<MatchCamera>(FindObjectsInactive.Include);
            Assert.IsNotNull(matchCam, "Game.unity has no MatchCamera");
            var mc = new SerializedObject(matchCam);
            float pitch = mc.FindProperty("_pitch").floatValue, distance = mc.FindProperty("_distance").floatValue;
            float ortho = mc.FindProperty("_orthoSize").floatValue, near = mc.FindProperty("_nearClipPlane").floatValue;
            var background = mc.FindProperty("_backgroundColor").colorValue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PilePrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, game);
            var camGo = new GameObject("__D1TestCam");
            RenderTexture rt = null;
            Texture2D a = null, b = null, overrideTex = null;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = new Vector3(footprint.x, 1f, footprint.y);   // FoodPile.ApplyFootprint at full: root X,Z = footprint, Y 1
                var glow = go.transform.Find("CentreGlow");
                if (glow != null) glow.gameObject.SetActive(false);   // the halo is ground-only and identical in both frames; keep it out of the mask edge
                var moundT = go.transform.Find("Mound/MoundMesh");
                Assert.IsNotNull(moundT, "FoodPile.prefab has no Mound/MoundMesh");
                var mound = moundT.GetComponent<Renderer>();

                var colors = AssetDatabase.LoadAssetAtPath<ColorSchemeSO>(ColorSchemePath);
                var tint = FoodPileVisuals.TintFor(colors.FoodPileEmpty, colors.FoodPileFull, 1f);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                if (!string.IsNullOrEmpty(baseMapOverridePng))
                {
                    overrideTex = new Texture2D(2, 2);
                    Assert.IsTrue(overrideTex.LoadImage(System.IO.File.ReadAllBytes(baseMapOverridePng)), "could not decode " + baseMapOverridePng);
                    mpb.SetTexture("_BaseMap", overrideTex);
                }
                mound.SetPropertyBlock(mpb);

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                bool post = RenderTierPolicy.PostProcessingFor(rp);
                data.renderPostProcessing = post;
                cam.orthographic = true;
                cam.orthographicSize = ortho;
                cam.nearClipPlane = near;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                var rot = Quaternion.Euler(pitch, Seat0Yaw, 0f);
                cam.transform.SetPositionAndRotation(Vector3.zero - rot * Vector3.forward * distance, rot);

                int msaa = 1;
                var msaaProp = rp.GetType().GetProperty("msaaSampleCount");
                if (msaaProp != null) msaa = Mathf.Max(1, Convert.ToInt32(msaaProp.GetValue(rp)));
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };

                a = Render(cam, rt);
                mound.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                b = Render(cam, rt);
                mound.shadowCastingMode = ShadowCastingMode.On;

                var r = Analyse(a.GetPixels32(), b.GetPixels32());
                r.Tier = tier; r.Pile = pile; r.Post = post; r.Msaa = msaa;
                return r;
            }
            finally
            {
                if (a != null) UnityEngine.Object.DestroyImmediate(a);
                if (b != null) UnityEngine.Object.DestroyImmediate(b);
                if (overrideTex != null) UnityEngine.Object.DestroyImmediate(overrideTex);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Texture2D Render(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        private static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Mound mask = pixels that change by more than 4/255 when the mound is hidden, eroded 2 px off the anti-aliased edge.</summary>
        private static MoundRead Analyse(Color32[] with, Color32[] without)
        {
            var raw = new bool[with.Length];
            for (int i = 0; i < with.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(with[i].r - without[i].r), Mathf.Max(Mathf.Abs(with[i].g - without[i].g), Mathf.Abs(with[i].b - without[i].b)));
                raw[i] = d > 4;
            }
            var mask = new bool[raw.Length];
            for (int y = 2; y < H - 2; y++)
            for (int x = 2; x < W - 2; x++)
            {
                bool all = true;
                for (int dy = -2; dy <= 2 && all; dy++)
                for (int dx = -2; dx <= 2 && all; dx++)
                    all = raw[(y + dy) * W + x + dx];
                mask[y * W + x] = all;
            }

            var luma = new float[with.Length];
            for (int i = 0; i < with.Length; i++) luma[i] = Luma(with[i]);
            var idx = Enumerable.Range(0, mask.Length).Where(i => mask[i]).ToArray();
            var r = new MoundRead { Pixels = idx.Length };
            if (idx.Length == 0) return r;

            var sorted = idx.Select(i => luma[i]).OrderBy(v => v).ToArray();
            float topThr = sorted[(int)(sorted.Length * 0.75f)], halfThr = sorted[sorted.Length / 2];
            double sr = 0, sg = 0, sb = 0; int nTop = 0, clipped = 0;
            foreach (var i in idx)
            {
                var c = with[i];
                if (c.r >= 254 || c.g >= 254 || c.b >= 254) clipped++;
                if (luma[i] < topThr) continue;
                sr += c.r; sg += c.g; sb += c.b; nTop++;
            }
            r.LitTopR = (float)(sr / nTop); r.LitTopG = (float)(sg / nTop); r.LitTopB = (float)(sb / nTop);
            Color.RGBToHSV(new Color(r.LitTopR / 255f, r.LitTopG / 255f, r.LitTopB / 255f), out float h, out _, out _);
            r.Hue = h * 360f;
            r.Clipped = clipped / (float)idx.Length;

            // High-pass grain: luma minus its 7x7 box mean (integral image), std over the lit half of the mound.
            var sat = new double[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
            {
                double row = 0;
                for (int x = 0; x < W; x++)
                {
                    row += luma[y * W + x];
                    sat[(y + 1) * (W + 1) + x + 1] = sat[y * (W + 1) + x + 1] + row;
                }
            }
            double s1 = 0, s2 = 0; int n = 0;
            foreach (var i in idx)
            {
                if (luma[i] < halfThr) continue;
                int x = i % W, y = i / W;
                int x0 = Mathf.Max(0, x - 3), x1 = Mathf.Min(W, x + 4), y0 = Mathf.Max(0, y - 3), y1 = Mathf.Min(H, y + 4);
                double box = sat[y1 * (W + 1) + x1] - sat[y0 * (W + 1) + x1] - sat[y1 * (W + 1) + x0] + sat[y0 * (W + 1) + x0];
                double hp = luma[i] - box / ((x1 - x0) * (y1 - y0));
                s1 += hp; s2 += hp * hp; n++;
            }
            r.GrainStd = n > 1 ? (float)Math.Sqrt(Math.Max(0, s2 / n - (s1 / n) * (s1 / n))) : 0f;
            return r;
        }
    }
}
