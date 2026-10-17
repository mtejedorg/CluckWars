using System.Reflection;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D9 (audit L09): the foot rings' R3 cream edge only separated a player colour from DARK ground. At the match
    /// camera it measured ~1.5:1 against sunlit grass / straw (rival ring ~1.2:1), and no seat's stroke clears 3:1 on grass on
    /// its own. The edge is now two-tone, baked into the one edge sprite: an INK band against the stroke (carries light ground:
    /// sunlit grass, dirt path, straw pad, mound) and a thin CREAM rim outside it (carries the fence / prop / bird shadows,
    /// which the arena does have: shadowed grass rel. luminance ~0.05-0.07). Neither tone alone works everywhere (negative
    /// controls below), together the ring reads >= 3:1 against every measured ground for every seat, identity and rival ring.
    /// The D8 ghost stays colour-only.
    /// </summary>
    public class Phase8ChunkD9RingEdgeTests
    {
        /// <summary>Ground under the rings at the match camera, Performance Mode off, rings hidden, median sRGB per ground and
        /// light (Captures/env3d_after/D/D9/before, tools/ui_capture/capture_d9_ringedge.py; "shadow" = the sun's shadow takes
        /// &gt; 25 % of the light, from a shadow-caster-off render of the same frame). PC then Mobile where they differ.</summary>
        private static readonly (string name, Color32 c)[] MeasuredGround =
        {
            ("grass sun PC", new Color32(122, 178, 83, 255)),       ("grass sun Mobile", new Color32(117, 178, 79, 255)),
            ("grass shadow", new Color32(52, 79, 40, 255)),         ("grass shadow, darkest decile", new Color32(40, 70, 38, 255)),
            ("straw sun PC", new Color32(194, 185, 128, 255)),      ("straw sun Mobile", new Color32(197, 185, 120, 255)),
            ("straw shadow PC", new Color32(88, 88, 63, 255)),      ("straw shadow Mobile", new Color32(86, 84, 61, 255)),
            ("dirt path sun PC", new Color32(191, 167, 117, 255)),  ("dirt path sun Mobile", new Color32(197, 163, 111, 255)),
            ("dirt path shadow PC", new Color32(121, 108, 54, 255)), ("dirt path shadow Mobile", new Color32(106, 94, 56, 255)),
            ("mound sun PC", new Color32(188, 157, 54, 255)),       ("mound sun Mobile", new Color32(195, 157, 58, 255)),
            ("mound shade PC", new Color32(117, 94, 50, 255)),      ("mound shade Mobile", new Color32(110, 93, 38, 255)),
        };

        private const float Target = 3f;

        private static Color Over(Color fg, float a, Color bg) => Color.Lerp(bg, fg, a); // Gamma colour space: sRGB blend, sprites unlit

        private static float Cr(Color a, Color b) => AbilityPalette.ContrastRatio(a, b);

        private static readonly (string ring, float alpha, float strokeAlpha)[] Rings =
        {
            ("identity", GroundRingStyle.IdentityEdgeAlpha, ChickenWorldBars.RingAlpha),
            ("rival", GroundRingStyle.RivalEdgeAlpha, FeedbackTuning.RivalRingAlpha),
        };

        // ── Edge colour rule ───────────────────────────────────────────────────

        [Test]
        public void EdgeTones_AreTheBrandInk_AndTheUiCream()
        {
            AssertRgb(UiGfx.TextDark, GroundRingStyle.EdgeInk, "ink band");
            AssertRgb(UiGfx.TextPrimary, GroundRingStyle.EdgeRim, "cream rim");
            Assert.AreEqual(UiGfx.TextDark, RivalIndicator.ChevronOutlineColor, "ring ink = the E1 chevron outline ink");
        }

        [TestCase(0.42f, GroundRingStyle.IdentityEdge, GroundRingStyle.IdentityRim)]   // identity floor
        [TestCase(1.09f, GroundRingStyle.IdentityEdge, GroundRingStyle.IdentityRim)]   // Fatty-sized identity ring
        [TestCase(0.98f, GroundRingStyle.RivalEdge, GroundRingStyle.RivalRim)]         // rival floor
        [TestCase(1.50f, GroundRingStyle.RivalEdge, GroundRingStyle.RivalRim)]         // Fatty-sized rival ring
        public void EdgeSprite_IsInkNextToTheStroke_CreamOutside_AndOpaqueAcrossTheSeam(float radius, float ink, float rim)
        {
            var sprite = GroundRingStyle.EdgeBand(radius, ink, rim);
            Assert.AreSame(sprite, GroundRingStyle.EdgeBand(radius, ink, rim), "edge sprites are cached per (radius, ink, rim)");
            float outer = radius + ink + rim;

            var inkPx = Sample(sprite, outer, radius + ink * 0.5f);
            AssertRgb(GroundRingStyle.EdgeInk, inkPx, $"middle of the ink band (r {radius})");
            Assert.GreaterOrEqual(inkPx.a, 0.99f, "ink band is opaque");

            var rimPx = Sample(sprite, outer, radius + ink + rim * 0.5f);
            AssertRgb(GroundRingStyle.EdgeRim, rimPx, $"middle of the cream rim (r {radius})");
            Assert.GreaterOrEqual(rimPx.a, 0.95f, "cream rim is opaque in its middle");

            Assert.Greater(Sample(sprite, outer, radius - 0.002f).a, 0.5f, "edge runs under the stroke's outer feather (no ground seam)");
            Assert.AreEqual(0f, Sample(sprite, outer, radius - 0.04f).a, 1e-3f, "edge stays out of the colour stroke");
            Assert.AreEqual(0f, Sample(sprite, outer, outer + 0.01f).a, 1e-3f, "nothing beyond the rim");
            AssertRgb(GroundRingStyle.EdgeRim, Sample(sprite, outer, outer + 0.01f), "transparent texels outside carry the rim tone (no dark bilinear fringe)");
        }

        [Test]
        public void Bands_AreWideEnoughForTheMobileRenderScale_AndTheRivalEdgeStaysLighter()
        {
            // Mobile renders at 0.8 scale: a 0.035 m ink + 0.02 m rim (first try) measured 2.3-2.9:1 on Mobile shade / rival.
            Assert.GreaterOrEqual(GroundRingStyle.IdentityEdge, 0.04f);
            Assert.GreaterOrEqual(GroundRingStyle.IdentityRim, 0.025f);
            Assert.GreaterOrEqual(GroundRingStyle.RivalEdge, 0.035f);
            Assert.GreaterOrEqual(GroundRingStyle.RivalRim, 0.02f);
            Assert.Less(GroundRingStyle.IdentityEdge + GroundRingStyle.IdentityRim, GroundRingStyle.IdentityStroke * 0.6f,
                "the edge frames the colour, it must not out-weigh the stroke");
            Assert.Greater(GroundRingStyle.IdentityEdge + GroundRingStyle.IdentityRim, GroundRingStyle.RivalEdge + GroundRingStyle.RivalRim);
            Assert.Greater(GroundRingStyle.IdentityEdgeAlpha, GroundRingStyle.RivalEdgeAlpha);
            Assert.GreaterOrEqual(GroundRingStyle.RivalEdgeAlpha, 0.8f, "the R3 rival edge at 0.55 measured 1.2:1");
        }

        // ── Contrast against the measured ground ───────────────────────────────

        [Test]
        public void Ring_ReadsThreeToOne_AgainstEveryMeasuredGround()
        {
            foreach (var (ring, alpha, _) in Rings)
            foreach (var (name, c) in MeasuredGround)
            {
                Color g = c;
                float ink = Cr(Over(GroundRingStyle.EdgeInk, alpha, g), g);
                float rim = Cr(Over(GroundRingStyle.EdgeRim, alpha, g), g);
                Assert.GreaterOrEqual(Mathf.Max(ink, rim), Target, $"{ring} ring on {name}: ink {ink:0.00}, rim {rim:0.00}");
            }
        }

        [Test]
        public void EverySeatColour_PartsFromItsInkBand()
        {
            // The ink band sits against the stroke, so a dark seat colour (P4 teal) must not merge into it.
            foreach (var (ring, alpha, strokeAlpha) in Rings)
            for (int seat = -1; seat < PlayerPalette.Count; seat++)
            foreach (var (name, c) in MeasuredGround)
            {
                Color g = c;
                float r = Cr(Over(PlayerPalette.ForCorner(seat), strokeAlpha, g), Over(GroundRingStyle.EdgeInk, alpha, g));
                Assert.GreaterOrEqual(r, 2f, $"{ring} ring seat {seat} on {name}: stroke vs ink only {r:0.00}:1");
            }
        }

        [Test]
        public void NeitherToneAlone_Holds_SoBothAreNeeded()
        {
            // Negative controls: the old cream-only edge fails sunlit ground, an ink-only edge fails the shadows.
            bool creamFails = false, inkFails = false;
            foreach (var (_, c) in MeasuredGround)
            {
                Color g = c;
                creamFails |= Cr(Over(GroundRingStyle.EdgeRim, 0.85f, g), g) < Target;   // R3: cream at 0.85
                inkFails |= Cr(Over(GroundRingStyle.EdgeInk, GroundRingStyle.IdentityEdgeAlpha, g), g) < Target;
            }
            Assert.IsTrue(creamFails, "cream alone would pass everywhere: the ground set lost its sunlit samples");
            Assert.IsTrue(inkFails, "ink alone would pass everywhere: the ground set lost its shadow samples");
        }

        // ── Live rings (prefab) ────────────────────────────────────────────────

        [Test]
        public void IdentityAndRivalRings_WearTheTwoToneEdge_AndTheGhostStaysColourOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var bars = go.GetComponent<ChickenWorldBars>();
                var ind = go.GetComponent<RivalIndicator>();
                Invoke(bars, "Awake");
                Invoke(ind, "Awake");
                bars.SetBodyFootprint(0.9f);
                ind.SetBodyFootprint(0.9f);
                ind.ShowRingForTests(3, 1f);

                AssertRing(bars, "identity", GroundRingStyle.IdentityEdge, GroundRingStyle.IdentityRim, GroundRingStyle.IdentityEdgeAlpha);
                AssertRing(ind, "rival", GroundRingStyle.RivalEdge, GroundRingStyle.RivalRim, GroundRingStyle.RivalEdgeAlpha);

                ind.ShowRingForTests(3, 0.5f);
                Assert.AreEqual(GroundRingStyle.RivalEdgeAlpha * 0.5f, Private<SpriteRenderer>(ind, "_ringEdge").color.a, 1e-4f,
                    "the rival edge fades with the rival");
                var ghost = Private<SpriteRenderer>(ind, "_ringGhost");
                AssertRgb(PlayerPalette.ForCorner(3), ghost.color, "rival ghost is the seat colour, not ink or cream");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertRing(object owner, string what, float ink, float rim, float alpha)
        {
            var ring = Private<SpriteRenderer>(owner, "_ring");
            var edge = Private<SpriteRenderer>(owner, "_ringEdge");
            var ghost = Private<SpriteRenderer>(owner, "_ringGhost");
            float radius = ring.transform.localScale.x * 0.5f;

            Assert.AreSame(GroundRingStyle.EdgeBand(radius, ink, rim), edge.sprite, $"{what}: edge is the D9 two-tone band");
            AssertRgb(Color.white, edge.color, $"{what}: edge renderer is white (tones live in the sprite)");
            Assert.AreEqual(alpha, edge.color.a, 1e-4f, $"{what}: edge alpha");
            Assert.Less(edge.sortingOrder, ring.sortingOrder, $"{what}: edge under the stroke");
            Assert.AreEqual((radius + ink + rim) / radius, edge.transform.localScale.x, 1e-4f, $"{what}: edge spans stroke + ink + rim");

            Assert.IsNotNull(ghost, $"{what}: D8 ghost missing");
            Assert.AreSame(ring.sprite, ghost.sprite, $"{what}: ghost draws the colour stroke");
            Assert.AreNotSame(edge.sprite, ghost.sprite, $"{what}: ghost must not draw the ink / cream edge");
            Assert.AreSame(ring.transform, ghost.transform.parent, $"{what}: ghost hangs off the stroke, not the edge");
            Assert.IsNull(edge.transform.Find(edge.name + GroundRingStyle.GhostSuffix), $"{what}: the edge has no ghost");
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>Texel of an edge sprite at <paramref name="r"/> metres from the centre (along +x), the sprite spanning 2 x <paramref name="outer"/>.</summary>
        private static Color Sample(Sprite s, float outer, float r)
        {
            var tex = s.texture;
            int n = tex.width;
            float u = 0.5f + r * (0.5f / outer);
            int x = Mathf.Clamp(Mathf.FloorToInt(u * n), 0, n - 1);
            return tex.GetPixel(x, n / 2);
        }

        private static void AssertRgb(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.02f, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, 0.02f, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, 0.02f, what + " (b)");
        }

        private static T Private<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
