using System.IO;
using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision E1 a: a rival's ground ring and screen-edge chevron carry the rival's PLAYER colour
    /// (<see cref="PlayerPalette.ForCorner"/>, the nameplate-dot / identity-ring source), never the class colour.
    /// Drives the real Chicken prefab's <see cref="RivalIndicator"/> through its test seams (no networked controller in EditMode)
    /// and reads the sprite colours it actually lit.
    /// </summary>
    public class Phase8ChunkE1Tests
    {
        private GameObject _chicken;
        private GameObject _camGo;
        private RivalIndicator _ind;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            Assert.IsNotNull(prefab, "Chicken prefab missing.");
            _chicken = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _ind = _chicken.GetComponent<RivalIndicator>();
            Assert.IsNotNull(_ind, "RivalIndicator is not on the chicken prefab.");
            typeof(RivalIndicator).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_ind, null);

            _camGo = new GameObject("E1Cam");
            var cam = _camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 10f;
            cam.nearClipPlane = 0.1f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ind != null) Object.DestroyImmediate(_ind); // OnDestroy takes the root-level chevron down
            if (_chicken != null) Object.DestroyImmediate(_chicken);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
        }

        private SpriteRenderer Field(string name) =>
            (SpriteRenderer)typeof(RivalIndicator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_ind);

        private Color RingColour(int corner)
        {
            _ind.ShowRingForTests(corner, 1f);
            return Field("_ring").color;
        }

        private Color ChevronColour(int corner)
        {
            _ind.ShowChevronForTests(_camGo.GetComponent<Camera>(), corner, 1f);
            return Field("_chevron").color;
        }

        private static void AssertRgb(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, what + " R");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, what + " G");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, what + " B");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RivalRing_AndChevron_UseThePlayerColourOfTheSeat(int seat)
        {
            var want = PlayerPalette.ForCorner(seat);

            var ring = RingColour(seat);
            AssertRgb(want, ring, $"seat {seat} ring");
            Assert.AreEqual(FeedbackTuning.RivalRingAlpha, ring.a, 1e-4f, "ring alpha unchanged by the recolour");

            var chev = ChevronColour(seat);
            AssertRgb(want, chev, $"seat {seat} chevron");
            Assert.AreEqual(FeedbackTuning.RivalChevronAlpha, chev.a, 1e-4f, "chevron alpha unchanged by the recolour");
            Assert.IsTrue(Field("_chevron").gameObject.activeSelf, "chevron lit");
        }

        [Test]
        public void TwoRivalsOfTheSameClass_OnDifferentSeats_GetDifferentArrowColours()
        {
            // The colour is a function of the seat alone: class never enters it (TintFor takes no class), so two
            // Warriors on seats 1 and 2 cannot share an arrow colour, while one seat is one colour whatever it plays.
            var seen = new System.Collections.Generic.List<Color>();
            for (int seat = 0; seat < PlayerPalette.Count; seat++) seen.Add(ChevronColour(seat));
            for (int i = 0; i < seen.Count; i++)
            for (int j = i + 1; j < seen.Count; j++)
                Assert.That(Mathf.Abs(seen[i].r - seen[j].r) + Mathf.Abs(seen[i].g - seen[j].g) + Mathf.Abs(seen[i].b - seen[j].b),
                    Is.GreaterThan(0.3f), $"seats {i} and {j} arrows are indistinguishable");
        }

        [Test]
        public void UnstampedCorner_ShowsTheNeutralColour_LikeTheNameplate()
        {
            AssertRgb(PlayerPalette.Neutral, RingColour(-1), "ring with no corner");
            AssertRgb(PlayerPalette.Neutral, ChevronColour(-1), "chevron with no corner");
        }

        [Test]
        public void RivalIndicator_NoLongerReadsClassColours()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string src = File.ReadAllText(Path.Combine(root, "Assets/_Game/Scripts/Visuals/RivalIndicator.cs"));
            StringAssert.DoesNotContain("TintColor", src);
            StringAssert.DoesNotContain("ChickenClassRegistrySO", src);
            StringAssert.DoesNotContain("ResolveClassColor", src);
            StringAssert.Contains("PlayerPalette.ForCorner", src);
        }

        // ── Chevron ink outline (E1 follow-up: no seat colour clears 3:1 against the sunlit grass on its own) ──

        /// <summary>Sunlit grass right behind the edge chevrons at the match camera after E3, median of the band round each
        /// arrow (Captures/env3d_after/D/E1/after_measure, e1_chevron_contrast.py): the lightest of the measured spots
        /// (PC 149,172,81 / Mobile 151,178,82) and the darkest (PC 108,171,82 / Mobile 105,175,81).</summary>
        private static readonly Color[] MeasuredGrass =
        {
            new Color32(151, 178, 82, 255), new Color32(149, 172, 81, 255),
            new Color32(108, 171, 82, 255), new Color32(105, 175, 81, 255),
        };

        private static Color Over(Color fg, float a, Color bg) => Color.Lerp(bg, fg, a); // Gamma colour space: sRGB blend

        [Test]
        public void ChevronOutline_IsAnInkChild_DrawnDirectlyBehindTheFill()
        {
            ChevronColour(3);
            var fill = Field("_chevron");
            var edge = Field("_chevronOutline");
            Assert.IsNotNull(edge, "chevron has no outline renderer");
            Assert.AreSame(fill.transform, edge.transform.parent, "outline must ride the chevron's transform (same pose/size)");
            Assert.AreEqual(Vector3.zero, edge.transform.localPosition);
            Assert.AreEqual(Vector3.one, edge.transform.localScale);
            Assert.AreEqual(Quaternion.identity, edge.transform.localRotation);
            Assert.AreEqual(fill.sortingLayerID, edge.sortingLayerID);
            Assert.AreEqual(fill.sortingOrder - 1, edge.sortingOrder, "outline sits directly behind the fill");
            Assert.IsTrue(edge.gameObject.activeInHierarchy, "outline lit with the chevron");
            AssertRgb(RivalIndicator.ChevronOutlineColor, edge.color, "outline");
            Assert.AreEqual(FeedbackTuning.RivalChevronOutlineAlpha, edge.color.a, 1e-4f);
        }

        [Test]
        public void ChevronOutline_FadesWithTheRival()
        {
            _ind.ShowChevronForTests(_camGo.GetComponent<Camera>(), 0, 0.95f);
            Assert.AreEqual(FeedbackTuning.RivalChevronOutlineAlpha * 0.95f, Field("_chevronOutline").color.a, 1e-4f);
        }

        [Test]
        public void ChevronOutline_IsDarkInk_ThatPartsFromEverySeatColour()
        {
            var ink = RivalIndicator.ChevronOutlineColor;
            AssertRgb(UiGfx.TextDark, ink, "outline is the brand ink (rival pill fill)");
            for (int seat = -1; seat < PlayerPalette.Count; seat++)
            {
                var c = PlayerPalette.ForCorner(seat);
                Assert.Less(AbilityPalette.Luminance(ink) + 0.05f, AbilityPalette.Luminance(c),
                    $"seat {seat}: ink is not darker than the seat colour");
                // Composited as drawn (fill alpha over ink alpha over grass): the fill still parts from its own edge.
                var g = MeasuredGrass[0];
                float r = AbilityPalette.ContrastRatio(Over(c, FeedbackTuning.RivalChevronAlpha, g),
                    Over(ink, FeedbackTuning.RivalChevronOutlineAlpha, g));
                Assert.GreaterOrEqual(r, 3f, $"seat {seat}: fill vs ink edge only {r:0.00}:1 - would need a cream edge");
            }
        }

        [Test]
        public void ChevronOutline_HoldsThreeToOne_AgainstTheMeasuredGrass()
        {
            var ink = RivalIndicator.ChevronOutlineColor;
            foreach (var g in MeasuredGrass)
            {
                float r = AbilityPalette.ContrastRatio(Over(ink, FeedbackTuning.RivalChevronOutlineAlpha, g), g);
                Assert.GreaterOrEqual(r, 3f, $"ink edge over grass {g} only {r:0.00}:1");
            }
        }

        [Test]
        public void ChevronOutlineSprite_RingsTheUnchangedFill_WithoutClippingOrDarkeningIt()
        {
            ChevronColour(0);
            var fillSprite = Field("_chevron").sprite;
            var edgeSprite = Field("_chevronOutline").sprite;
            var ft = fillSprite.texture;
            var et = edgeSprite.texture;

            // Fill unchanged: 96 texels, one world unit.
            Assert.AreEqual(96, ft.width);
            Assert.AreEqual(96f, fillSprite.pixelsPerUnit);
            Assert.AreEqual(fillSprite.pixelsPerUnit, edgeSprite.pixelsPerUnit, "outline must share the fill's texel scale");
            int pad = (et.width - ft.width) / 2;
            Assert.Greater(pad, FeedbackTuning.RivalChevronOutlineRatio * ft.width, "outline margin smaller than the edge");
            Assert.AreEqual(ft.width + 2 * pad, et.width, "outline sprite is centred on the fill");

            var f = ft.GetPixels();
            var e = et.GetPixels();
            int n = et.width;
            bool Fill(int x, int y) => x >= 0 && y >= 0 && x < ft.width && y < ft.height && f[y * ft.width + x].a > 0.5f;
            float Edge(int x, int y) => e[(y + pad) * n + (x + pad)].a;

            int ringed = 0, interior = 0;
            for (int y = 0; y < ft.height; y++)
            for (int x = 0; x < ft.width; x++)
            {
                if (!Fill(x, y)) continue;
                bool boundary = !Fill(x + 1, y) || !Fill(x - 1, y) || !Fill(x, y + 1) || !Fill(x, y - 1);
                if (!boundary) { Assert.AreEqual(0f, Edge(x, y), 1e-4f, $"ink under the fill interior at {x},{y}"); interior++; continue; }
                // Every empty neighbour of the fill, and the texel past it, is solid ink: a >= 2-texel (~1.6 px at 1080) rim.
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    if (Fill(x + d.x, y + d.y)) continue;
                    Assert.GreaterOrEqual(Edge(x + d.x, y + d.y), 0.99f, $"edge gap next to {x},{y}");
                    if (!Fill(x + 2 * d.x, y + 2 * d.y))
                        Assert.GreaterOrEqual(Edge(x + 2 * d.x, y + 2 * d.y), 0.99f, $"edge thinner than 2 texels at {x},{y}");
                    ringed++;
                }
            }
            Assert.Greater(interior, 500, "fill lost its solid body");
            Assert.Greater(ringed, 100, "no outline found round the fill");

            // Nothing reaches the outline texture's border: the rim round the tip is not clipped.
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(0f, e[i].a, 1e-4f, "outline clipped at the bottom");
                Assert.AreEqual(0f, e[(n - 1) * n + i].a, 1e-4f, "outline clipped at the tip");
                Assert.AreEqual(0f, e[i * n].a, 1e-4f, "outline clipped at the left");
                Assert.AreEqual(0f, e[i * n + n - 1].a, 1e-4f, "outline clipped at the right");
            }
        }

        [Test]
        public void RingCreamEdge_StillSitsUnderTheRivalRing_WhateverThePlayerColour()
        {
            // R3's cream edge is what keeps the darkest player colour (P4) readable on grass; it must exist and stay cream.
            RingColour(3);
            var edge = Field("_ringEdge");
            Assert.IsNotNull(edge, "rival ring lost its cream edge child");
            AssertRgb(UiGfx.TextPrimary, edge.color, "rival ring edge");
        }
    }
}
