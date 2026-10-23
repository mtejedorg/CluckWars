using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Diagnostics;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The floor-ring rival arrows (Maestro 2026-10-22: "put them in the outer circle at the floor", pass 3 "painted on the
    /// floor"; production default since decision 2a, 2026-10-23, <c>-cw_edgearrows</c> = dev-only comparison): the ring radius per framing, bearing to ring point, the painted decal (placement on the ring,
    /// yaw-only orientation, shape, rim colour, size vs distance), the arc-length de-overlap, the visibility rule (shared with
    /// the edge chevron), the dev-tools gate, and source guards for lifetime / flat orientation / allocation.
    /// </summary>
    public sealed class FloorRivalArrowsTests
    {
        private const float Pitch = 30f;               // MatchCamera._pitch
        private const float LandscapeOrtho = 5.6f;     // MatchCamera._orthoSize
        private const float Tol = 0.02f;

        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel)).Replace("\r\n", "\n");

        [TearDown]
        public void ResetSwitch() => FloorRivalArrows.EdgeArrowsRequested = false;

        // ── Ring radius ────────────────────────────────────────────────────────────────────────

        private static float RadiusAt(float aspect, out OrientationFraming.Result framing, out List<Rect> info,
                                      HudDeviceMode mode = HudDeviceMode.Touch)
        {
            framing = OrientationFraming.Compute(aspect, LandscapeOrtho, -6f, 15f, Pitch);
            info = new List<Rect>();
            HudControlZones.BuildInfoZones(info, mode, aspect, Vector4.zero, 0);
            var controls = new List<Rect>();
            HudControlZones.Build(controls, mode, aspect, Vector4.zero);
            return FloorArrowRules.FramingRadius(aspect, framing.OrthoSize, Pitch, info, controls);
        }

        // Portrait: the 5.6 m lateral half-width binds (0.8 x 5.6). Landscape: the clear depth to the top HUD band binds
        // (290 px band, decision 1a: 16:9 5.19 m above the chicken; 20:9 4.48 m), which a lateral-only rule would miss.
        [TestCase(9f, 16f, 4.48f)]
        [TestCase(9f, 20f, 4.48f)]
        [TestCase(9f, 21f, 4.29f)]    // past the portrait ortho cap: 5.36 m lateral
        [TestCase(16f, 9f, 4.15f)]
        [TestCase(20f, 9f, 3.55f)]
        [TestCase(4f, 3f, 3.63f)]     // tablet: the touch hexes bind, not the band (linear panel formula, WP8 correction)
        public void RingRadius_PerAspectTable(float w, float h, float expected)
        {
            Assert.AreEqual(expected, RadiusAt(w / h, out _, out _), Tol, $"{w}:{h}");
        }

        [TestCase(9f, 16f)]
        [TestCase(9f, 20f)]
        [TestCase(9f, 21f)]
        [TestCase(16f, 9f)]
        [TestCase(20f, 9f)]
        public void RingRadius_WholeRingStaysInTheClearViewport(float w, float h)
        {
            float aspect = w / h;
            float r = RadiusAt(aspect, out var f, out var info);
            Assert.LessOrEqual(r, FeedbackTuning.FloorArrowPreferredRadius + 1e-4f);
            Assert.LessOrEqual(r, FeedbackTuning.FloorArrowLateralFraction * f.LateralHalfExtent + 1e-4f, "lateral share");

            float m = FloorArrowRules.GroundMetresPerViewportHeight(f.OrthoSize, Pitch);
            float top = f.FocusViewportY + r / m;
            float bottom = f.FocusViewportY - r / m;
            float side = 0.5f + r / (2f * f.LateralHalfExtent);
            Assert.Less(top, info[0].yMin, "the ring's top is below the top HUD band");
            Assert.Greater(bottom, f.Portrait ? OrientationFraming.PortraitBottomBandFraction : 0f, "clear of the bottom band");
            Assert.Less(side, 1f, "the ring's sides are inside the frame");
        }

        [Test]
        public void RingRadius_ControlZonesAreAnExtent_NotBindingOnPhones()
        {
            foreach (float aspect in new[] { 9f / 16f, 9f / 20f, 16f / 9f, 20f / 9f })
            foreach (var mode in new[] { HudDeviceMode.Touch, HudDeviceMode.KeyboardMouse })
            {
                float withControls = RadiusAt(aspect, out var f, out var info, mode);
                float without = FloorArrowRules.FramingRadius(aspect, f.OrthoSize, Pitch, info);
                Assert.AreEqual(without, withControls, 1e-5f, $"aspect {aspect} {mode}: the shipped controls are far from the ring");
            }

            float tablet = RadiusAt(4f / 3f, out var tf, out var tinfo, HudDeviceMode.Touch);
            Assert.Less(tablet, FloorArrowRules.FramingRadius(4f / 3f, tf.OrthoSize, Pitch, tinfo) - 0.3f);

            var near = new List<Rect> { Rect.MinMaxRect(0.6f, 0.45f, 0.7f, 0.55f) };
            float a = 16f / 9f;
            float rNear = FloorArrowRules.FramingRadius(a, LandscapeOrtho, Pitch, new List<Rect>(), near);
            float lateral = (0.6f - 0.5f) * 2f * LandscapeOrtho * a;
            Assert.AreEqual(Mathf.Max(FeedbackTuning.FloorArrowMinRadius, 0.8f * lateral), rNear, 1e-3f);
            Assert.AreEqual(3.0f, FloorArrowRules.RingRadius(10f, 10f, 10f, 3.75f), 1e-5f, "0.8 x the control distance");
        }

        [Test]
        public void RingRadius_Guards()
        {
            Assert.AreEqual(FeedbackTuning.FloorArrowPreferredRadius, FloorArrowRules.RingRadius(0f, 10f, 10f), "no camera yet");
            Assert.AreEqual(FeedbackTuning.FloorArrowPreferredRadius, FloorArrowRules.RingRadius(float.NaN, 10f, 10f));
            Assert.AreEqual(FeedbackTuning.FloorArrowPreferredRadius, FloorArrowRules.RingRadius(100f, 100f, 100f), "capped");
            Assert.AreEqual(FeedbackTuning.FloorArrowMinRadius, FloorArrowRules.RingRadius(10f, float.NaN, 10f), "floored");
            Assert.AreEqual(FeedbackTuning.FloorArrowMinRadius, FloorArrowRules.RingRadius(10f, -3f, 10f));
            Assert.AreEqual(FeedbackTuning.FloorArrowPreferredRadius, FloorArrowRules.FramingRadius(0f, 5.6f, Pitch, null));
            Assert.AreEqual(0.8f * 5.6f, FloorArrowRules.RingRadius(5.6f, 50f, 50f), 1e-5f);
        }

        // ── Bearing and ring point ─────────────────────────────────────────────────────────────

        [TestCase(0f, 10f, 0f)]
        [TestCase(10f, 0f, 90f)]
        [TestCase(0f, -10f, 180f)]
        [TestCase(-10f, 0f, 270f)]
        [TestCase(5f, 5f, 45f)]
        [TestCase(-5f, 5f, 315f)]
        public void Bearing_ToRingPosition(float dx, float dz, float expectedDeg)
        {
            var centre = new Vector3(3f, 0.4f, -7f);
            var rival = centre + new Vector3(dx, 1.2f, dz);   // height is ignored: the bearing is planar
            float b = FloorArrowRules.Bearing(centre, rival);
            Assert.AreEqual(expectedDeg, b, 1e-3f);

            const float r = 4f;
            Vector3 p = FloorArrowRules.RingPoint(centre, r, b);
            Assert.AreEqual(r, FloorArrowRules.PlanarDistance(centre, p), 1e-4f, "on the ring");
            Vector3 toRival = new Vector3(dx, 0f, dz).normalized;
            Vector3 toPoint = new Vector3(p.x - centre.x, 0f, p.z - centre.z).normalized;
            Assert.Greater(Vector3.Dot(toRival, toPoint), 0.9999f, "the ring point lies on the line to the rival");
            Assert.AreEqual(b, FloorArrowRules.Bearing(centre, p), 1e-3f, "round trip");
        }

        [Test]
        public void Bearing_CoincidentPointsIsZero_NotNaN()
        {
            Assert.AreEqual(0f, FloorArrowRules.Bearing(Vector3.one, Vector3.one));
        }

        // ── Painted decal ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Decal_TailOnTheRing_TipOutward_YawOnlyAlongTheBearing()
        {
            var centre = new Vector3(-2f, 0f, 5f);
            const float r = 3.48f;
            for (float b = 0f; b < 360f; b += 15f)
            {
                Vector3 tail = FloorArrowRules.ArrowTail(centre, r, b);
                Vector3 tip = FloorArrowRules.ArrowTip(centre, r, b, 1.3f);
                Assert.AreEqual(r, FloorArrowRules.PlanarDistance(centre, tail), 1e-4f, $"{b}: tail centre on the ring line");
                Assert.AreEqual(r + 1.3f, FloorArrowRules.PlanarDistance(centre, tip), 1e-4f, $"{b}: tip outward");

                // The only rotation is a yaw about world up: local +Z (the tip) lands on the bearing, local +X stays flat, +Y stays up.
                float yaw = FloorArrowRules.ArrowYaw(b);
                Vector3 fwd = FloorArrowRules.RotateYaw(Vector3.forward, yaw);
                Assert.Greater(Vector3.Dot(fwd, FloorArrowRules.Direction(b)), 0.99999f, $"{b}: tip along the bearing");
                Assert.AreEqual(0f, FloorArrowRules.RotateYaw(Vector3.right, yaw).y, 1e-6f, "flat");
                Assert.AreEqual(Vector3.up, FloorArrowRules.RotateYaw(Vector3.up, yaw), "never tilted toward the camera");
            }
            Assert.AreEqual(270f, FloorArrowRules.ArrowYaw(-90f), 1e-4f);
        }

        [Test]
        public void Decal_ChevronShape_RimOutside_FadeInside_NoInversion()
        {
            const int n = FloorArrowRules.ChevronPointCount;
            var shape = new Vector2[n]; var rim = new Vector2[n]; var core = new Vector2[n];
            FloorArrowRules.ChevronPolygon(shape);
            Assert.AreEqual(new Vector2(0f, 1f), shape[1], "tip at unit length along +Z");
            Assert.AreEqual(0f, shape[0].y, 1e-6f, "tail corners on the ring line");
            Assert.AreEqual(0f, shape[2].y, 1e-6f);
            Assert.AreEqual(FeedbackTuning.FloorArrowWidthRatio, shape[0].x - shape[2].x, 1e-5f, "tail width = width ratio");
            Assert.AreEqual(FeedbackTuning.FloorArrowNotch, shape[3].y, 1e-6f, "notched tail");

            float area = FloorArrowRules.SignedArea(shape, n);
            Assert.Greater(area, 0f, "counter-clockwise");
            FloorArrowRules.OffsetPolygon(shape, n, FeedbackTuning.FloorArrowOutlineRatio, rim);
            FloorArrowRules.OffsetPolygon(shape, n, -FeedbackTuning.FloorArrowFadeInset, core);
            Assert.Greater(FloorArrowRules.SignedArea(rim, n), area, "rim grows the shape");
            float coreArea = FloorArrowRules.SignedArea(core, n);
            Assert.That(coreArea, Is.GreaterThan(0.3f * area).And.LessThan(area), "the fade core is a real, non-inverted inset");
            Assert.Greater(rim[1].y, 1f, "rim beyond the tip");
            Assert.Less(core[1].y, 1f, "core inside the tip");
        }

        [Test]
        public void Decal_RimIsTheTintsOwnHueDarkened()
        {
            foreach (var tint in new[] { PlayerPaletteColor(0), PlayerPaletteColor(1), PlayerPaletteColor(2), PlayerPaletteColor(3) })
            {
                Color rim = FloorArrowRules.OutlineFor(tint);
                Color.RGBToHSV(tint, out float h0, out _, out float v0);
                Color.RGBToHSV(rim, out float h1, out _, out float v1);
                Assert.AreEqual(h0, h1, 0.01f, "same hue");
                Assert.AreEqual(v0 * FeedbackTuning.FloorArrowOutlineValue, v1, 0.01f, "darker");
                Assert.AreEqual(FeedbackTuning.FloorArrowOutlineAlpha, rim.a, 1e-5f);
            }
        }

        private static Color PlayerPaletteColor(int corner) => CluckWars.UI.PlayerPalette.ForCorner(corner);

        [Test]
        public void Decal_SizeVsDistance_GentleAndMonotonic()
        {
            const float r = 4.48f;
            float prev = float.MaxValue;
            for (float d = 0f; d <= 80f; d += 0.25f)
            {
                float len = FloorArrowRules.ArrowLength(d, r);
                Assert.LessOrEqual(len, prev + 1e-6f, $"length at {d}");
                Assert.That(len, Is.InRange(FeedbackTuning.FloorArrowFarLength, FeedbackTuning.FloorArrowNearLength));
                Assert.AreEqual(len * FeedbackTuning.FloorArrowWidthRatio, FloorArrowRules.ArrowWidth(d, r), 1e-5f);
                prev = len;
            }
            Assert.AreEqual(FeedbackTuning.FloorArrowNearLength, FloorArrowRules.ArrowLength(r, r), 1e-5f, "at the ring: biggest");
            Assert.AreEqual(FeedbackTuning.FloorArrowFarLength, FloorArrowRules.ArrowLength(200f, r), 1e-5f, "far: smallest");
            Assert.Greater(FloorArrowRules.ArrowLength(8f, r), FloorArrowRules.ArrowLength(20f, r), "nearer is bigger");
        }

        [Test]
        public void Pass4_PaintedNumbers()
        {
            // Maestro pass 3 (length ~1.1-1.5 m, width ~0.9-1.2 m) plus the pass-4 bump of up to ~25 % after the 960x540 shot.
            Assert.That(FeedbackTuning.FloorArrowNearLength, Is.InRange(1.1f, 1.5f * 1.25f));
            Assert.That(FeedbackTuning.FloorArrowFarLength, Is.InRange(1.1f, 1.5f * 1.25f));
            Assert.That(FeedbackTuning.FloorArrowNearLength * FeedbackTuning.FloorArrowWidthRatio, Is.InRange(0.9f, 1.2f * 1.25f));
            Assert.That(FeedbackTuning.FloorArrowFarLength * FeedbackTuning.FloorArrowWidthRatio, Is.InRange(0.9f, 1.2f * 1.25f));
            Assert.Greater(FeedbackTuning.FloorArrowNearLength, FeedbackTuning.FloorArrowFarLength, "nearer slightly larger");
            Assert.AreEqual(0.85f, FeedbackTuning.FloorArrowPaintAlpha, 1e-5f);
            Assert.AreEqual(0.1f, FeedbackTuning.FloorArrowRingWidth, 1e-5f);
            Assert.That(FeedbackTuning.FloorArrowRingAlpha, Is.InRange(0.55f, 0.65f));
            Color.RGBToHSV(FeedbackTuning.FloorArrowRingColor, out _, out float sat, out float val);
            Assert.Less(sat, 0.2f, "soft cream / white, not a colour");
            Assert.Greater(val, 0.95f);
        }

        // ── Arc-length de-overlap ──────────────────────────────────────────────────────────────

        private static float[] Spread(float[] bearings, int[] keys, float sep)
        {
            var result = new float[bearings.Length];
            FloorArrowRules.Spread(bearings, keys, bearings.Length, sep, result);
            return result;
        }

        /// <summary>Smallest absolute angular difference between two bearings, degrees in [0, 180].</summary>
        private static float AngularDistance(float a, float b)
        {
            float d = Mathf.Abs(FloorArrowRules.Normalise(a) - FloorArrowRules.Normalise(b));
            return d > 180f ? 360f - d : d;
        }

        private static void AssertSeparated(float[] r, float sep)
        {
            for (int i = 0; i < r.Length; i++)
            for (int j = i + 1; j < r.Length; j++)
                Assert.GreaterOrEqual(AngularDistance(r[i], r[j]), sep - 1e-3f, $"arrows {i} and {j}");
        }

        [TestCase(4.48f)]
        [TestCase(3.48f)]
        [TestCase(2.84f)]
        public void Spread_ByRingArc_PaintedTailsNeverTouch(float radius)
        {
            float sep = FloorArrowRules.MinSeparationDegrees(radius);
            Assert.AreEqual(FloorArrowRules.MinSeparationArc(), FloorArrowRules.ArcLength(sep, radius), 1e-4f, "separation is an arc length");
            float widest = FeedbackTuning.FloorArrowNearLength * FeedbackTuning.FloorArrowWidthRatio;
            Assert.Greater(FloorArrowRules.MinSeparationArc(), widest, "more than one tail width of arc between centres");

            var r = Spread(new[] { 40f, 40f, 40f }, new[] { 7, 3, 5 }, sep);
            AssertSeparated(r, sep);
            for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
            {
                // Chord between the two tail centres on the ring must clear one tail width (the tails are straight segments).
                float chord = 2f * radius * Mathf.Sin(0.5f * AngularDistance(r[i], r[j]) * Mathf.Deg2Rad);
                Assert.Greater(chord, widest, $"R {radius}: tails {i}/{j} do not overlap");
            }
        }

        [Test]
        public void Spread_ThreeRivalsInOneDirection_FanOutSymmetrically()
        {
            float sep = FloorArrowRules.MinSeparationDegrees(4.48f);
            Assert.That(sep, Is.InRange(12f, 25f), "~21 degrees at 4.48 m");
            var r = Spread(new[] { 40f, 40f, 40f }, new[] { 7, 3, 5 }, sep);
            AssertSeparated(r, sep);
            Assert.AreEqual(40f - sep, r[1], 1e-3f, "lowest key first");
            Assert.AreEqual(40f, r[2], 1e-3f);
            Assert.AreEqual(40f + sep, r[0], 1e-3f);
        }

        [Test]
        public void Spread_IsOrderIndependent_IncludingAcrossZero()
        {
            float sep = 15f;
            var bearings = new[] { 358f, 2f, 0.5f };
            var keys = new[] { 11, 22, 33 };
            var reference = Spread(bearings, keys, sep);
            AssertSeparated(reference, sep);

            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            foreach (var p in perms)
            {
                var b = new float[3]; var k = new int[3];
                for (int i = 0; i < 3; i++) { b[i] = bearings[p[i]]; k[i] = keys[p[i]]; }
                var r = Spread(b, k, sep);
                for (int i = 0; i < 3; i++)
                    Assert.AreEqual(reference[p[i]], r[i], 1e-4f, $"key {k[i]} lands at the same bearing whatever the submit order");
            }

            float mean = (-2f + 2f + 0.5f) / 3f;
            Assert.AreEqual(0f, AngularDistance(reference[2], mean), 1e-3f, "middle arrow at the mean");
        }

        [Test]
        public void Spread_SeparatedArrowsAreUntouched_BitForBit()
        {
            var input = new[] { 10f, 130f, 250f };
            var r = Spread(input, new[] { 1, 2, 3 }, 15f);
            for (int i = 0; i < 3; i++) Assert.AreEqual(input[i], r[i], 0f);

            var two = Spread(new[] { 100f, 200f }, null, 15f);
            Assert.AreEqual(100f, two[0], 0f);
            Assert.AreEqual(200f, two[1], 0f);

            var one = Spread(new[] { 400f }, null, 15f);
            Assert.AreEqual(40f, one[0], 1e-4f, "normalised");
        }

        [Test]
        public void Spread_PairNearAThirdMerges()
        {
            var r = Spread(new[] { 0f, 10f, 26f }, new[] { 1, 2, 3 }, 15f);
            AssertSeparated(r, 15f);
            Assert.AreEqual(0f, AngularDistance(r[1], 12f), 1e-3f);
        }

        // ── Visibility rule (the edge chevron's decision, plus "beyond the ring") ──────────────

        [Test]
        public void Visibility_OnScreenRivalGetsNone_UnderTheBandGetsOne()
        {
            float aspect = 16f / 9f;
            var info = new List<Rect>();
            HudControlZones.BuildInfoZones(info, HudDeviceMode.Touch, aspect, Vector4.zero, 0);
            var avoid = new List<Rect>();
            HudControlZones.Build(avoid, HudDeviceMode.Touch, aspect, Vector4.zero);
            avoid.AddRange(info);
            var clearance = new Vector2(0.03f, 0.03f);
            float r = RadiusAt(aspect, out _, out _);

            bool Hidden(Vector2 vp) => ScreenEdgeProjection.ClampToClearViewport(vp, false, FeedbackTuning.RivalChevronSafeArea,
                info, avoid, clearance, out _, out _);

            Assert.IsFalse(Hidden(new Vector2(0.7f, 0.55f)), "in the clear viewport");
            Assert.IsFalse(FloorArrowRules.WantsArrow(Hidden(new Vector2(0.7f, 0.55f)), 9f, r), "on-screen rival: no floor arrow, however far");

            Vector2 underBand = new Vector2(0.5f, 0.9f);
            Assert.IsTrue(Hidden(underBand), "under the top HUD band counts as hidden (WP7 / C6)");
            Assert.IsTrue(FloorArrowRules.WantsArrow(Hidden(underBand), r + 3f, r), "hidden under the band and beyond the ring: an arrow");
            Assert.IsFalse(FloorArrowRules.WantsArrow(Hidden(underBand), r - 0.1f, r), "inside the ring: the edge chevron keeps it");

            Assert.IsTrue(FloorArrowRules.WantsArrow(Hidden(new Vector2(1.4f, 0.5f)), 14f, r), "off the frame");
        }

        // ── Dev switch ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Switch_FloorEverywhere_OnlyDevEdgeArrowsRestoresChevrons()
        {
            // Decision 2a (2026-10-23): the floor ring is the production default; -cw_edgearrows is a dev-only comparison switch.
            Assert.IsTrue(FloorRivalArrows.FloorArrowsActive(false, false), "production: floor arrows");
            Assert.IsTrue(FloorRivalArrows.FloorArrowsActive(false, true), "production ignores the dev switch (no CLI switch)");
            Assert.IsTrue(FloorRivalArrows.FloorArrowsActive(true, false), "dev build, no key: floor arrows");
            Assert.IsFalse(FloorRivalArrows.FloorArrowsActive(true, true), "dev build, -cw_edgearrows: the chevrons");

            Assert.IsFalse(FloorRivalArrows.EdgeArrowsRequested, "default: no edge override");
            FloorRivalArrows.EdgeArrowsRequested = true;
            Assert.AreEqual(!DevTools.Available, FloorRivalArrows.Active, "the switch only bites with dev tools (the Editor has them)");
            Assert.IsFalse(FloorRivalArrows.TryRepresent(1, new Vector3(20f, 0f, 20f), Color.red, 1f), "edge: the chevron stays");
            FloorRivalArrows.EdgeArrowsRequested = false;
            Assert.IsTrue(FloorRivalArrows.Active);

            FloorRivalArrows.EnsureForMatch();   // EditMode: must not create a DontDestroyOnLoad object
            Assert.AreEqual(0, Object.FindObjectsByType<FloorRivalArrows>(FindObjectsSortMode.None).Length, "EditMode: nothing created");
            Assert.IsFalse(FloorRivalArrows.TryRepresent(1, new Vector3(20f, 0f, 20f), Color.red, 1f), "no instance: the chevron stays");

            string src = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");
            StringAssert.Contains("public static bool Active => FloorArrowsActive(DevTools.Available, _edgeArrowsRequested);", src);
            StringAssert.Contains("!(devToolsAvailable && edgeArrowsRequested);", src);
            StringAssert.Contains("if (!Active || _instance == null) return false;", src);
            StringAssert.Contains("if (_instance != null || !Active || !Application.isPlaying) return;", Body(src, "public static void EnsureForMatch()"));
            StringAssert.DoesNotContain("RuntimeInitializeLoadType.AfterSceneLoad", src, "created with the first match, not at boot");

            string indicator = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            string awake = Body(indicator, "private void Awake()");
            int kill = awake.IndexOf("FeedbackTuning.RivalIndicatorsEnabled", System.StringComparison.Ordinal);
            int ensure = awake.IndexOf("FloorRivalArrows.EnsureForMatch();", System.StringComparison.Ordinal);
            int build = awake.IndexOf("BuildGeometry();", System.StringComparison.Ordinal);
            Assert.That(ensure, Is.GreaterThan(kill).And.LessThan(build), "every indicator ensures the ring, after the kill switch");

            string driver = Read("Assets/_Game/Scripts/Diagnostics/AutoTestDriver.cs");
            int apply = driver.IndexOf("private void ApplyScreenshotTools()", System.StringComparison.Ordinal);
            int gate = driver.IndexOf("if (!DevTools.Available)", apply, System.StringComparison.Ordinal);
            int use = driver.IndexOf("FloorRivalArrows.EdgeArrowsRequested =", apply, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(apply).And.LessThan(use), "the comparison switch is set only past the dev-tools gate");
            StringAssert.Contains("AutoTest arrows=edge", driver);
            StringAssert.Contains("AutoTest arrows=floor", driver);
        }

        [Test]
        public void Parse_FloorArrowsKey()
        {
            var on = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_floorarrows", "-cw_zones" });
            Assert.IsTrue(on.FloorArrows, "bare flag");
            Assert.IsTrue(on.Zones);
            CollectionAssert.IsEmpty(on.Problems);
            Assert.IsFalse(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_floorarrows", "0" }).FloorArrows);
            Assert.IsFalse(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_hud" }).FloorArrows, "no explicit key");
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.FloorArrowsKey);

            var edge = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_edgearrows" });
            Assert.IsTrue(edge.EdgeArrows, "bare flag");
            CollectionAssert.IsEmpty(edge.Problems);
            Assert.IsFalse(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_hud" }).EdgeArrows, "default: floor");
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.EdgeArrowsKey);
            var both = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_floorarrows", "-cw_edgearrows" });
            Assert.IsTrue(both.EdgeArrows, "both: the comparison switch wins");
            Assert.IsFalse(both.FloorArrows);
            Assert.AreEqual(1, both.Problems.Count);
            StringAssert.DoesNotContain("arrows", edge.Describe());
            StringAssert.DoesNotContain("floor", on.Describe());
        }

        [Test]
        public void Material_PrefersUrpUnlitSprite_FallsBackToTheTelegraphLineMaterial()
        {
            try
            {
                TelegraphShapes.ResetLineMaterialStateForTests();
                var urp = TelegraphShapes.CreateUnlitSpriteMaterial(Shader.Find, null);
                Assert.IsNotNull(urp);
                Assert.AreEqual(TelegraphShapes.UrpUnlitSpriteShader, urp.shader.name, "URP's own unlit sprite shader is in the project");
                Object.DestroyImmediate(urp);

                var fallback = TelegraphShapes.CreateUnlitSpriteMaterial(
                    n => n == TelegraphShapes.UrpUnlitSpriteShader ? null : Shader.Find(n), null);
                Assert.IsNotNull(fallback, "stripped from a build: the telegraph chain still draws");
                Assert.AreEqual("Sprites/Default", fallback.shader.name);
                Object.DestroyImmediate(fallback);
            }
            finally { TelegraphShapes.ResetLineMaterialStateForTests(); }
        }

        // ── Source guards ──────────────────────────────────────────────────────────────────────

        private static string Body(string src, string signature)
        {
            int start = src.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int open = src.IndexOf('{', start), depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
            }
            Assert.Fail("unbalanced " + signature);
            return null;
        }

        [Test]
        public void Source_PaintedFlat_YawOnly_NoScreenSpaceFittingLeft()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");
            string rules = Read("Assets/_Game/Scripts/Visuals/FloorArrowRules.cs");

            // The arrow's only rotation is a yaw about world up from the bearing; nothing billboards or reads the camera's pose.
            StringAssert.Contains("Quaternion.Euler(0f, FloorArrowRules.ArrowYaw(b), 0f)", Body(src, "private void PaintArrow("));
            foreach (var banned in new[] { "LookRotation", "WorldToViewportPoint", "eulerAngles.y", "cam.transform.rotation",
                                           "SpriteRenderer", "LineRenderer", "FitAlongRay", "DepthStretch", "ScreenDirection" })
                StringAssert.DoesNotContain(banned, src, banned);
            foreach (var banned in new[] { "FitAlongRay", "DepthStretch", "ScreenDirection", "ScreenRingPoint", "ViewportToScreenMetres" })
                StringAssert.DoesNotContain(banned, rules, banned);
        }

        [Test]
        public void Source_Lifetime_NotUnderTheChicken_NoPerFrameAllocation_UrpMaterial()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");

            // One root object kept across scenes, created by the first indicator's Awake, never from an indicator's LateUpdate.
            string ensure = Body(src, "public static void EnsureForMatch()");
            StringAssert.Contains("new GameObject(\"FloorRivalArrows\")", ensure);
            StringAssert.Contains("DontDestroyOnLoad(go);", ensure);
            StringAssert.Contains("if (!Active || _instance == null) return false;", Body(src, "public static bool TryRepresent("));
            StringAssert.DoesNotContain("new GameObject", Body(src, "public static bool TryRepresent("));
            StringAssert.DoesNotContain("ChickenGround.Anchor", src);
            foreach (Match m in Regex.Matches(src, @"SetParent\(([^,]+),"))
                Assert.AreEqual("transform", m.Groups[1].Value.Trim(), "paint layers hang only under this root: " + m.Value);
            StringAssert.DoesNotContain("local.transform)", src);

            // Hidden for a removed (dead / eliminated) local chicken.
            StringAssert.Contains("if (local.Combat.IsRemoved) return;", Body(src, "private void PrepareFrame()"));

            // Per frame: structs only.
            foreach (var sig in new[] { "private void LateUpdate()", "private void PaintArrow(", "private void WriteArrowColors(",
                                        "private void SetRingRadius(", "private bool Accept(", "public static bool TryRepresent(" })
            {
                foreach (Match m in Regex.Matches(Body(src, sig), @"\bnew\s+([A-Za-z_][A-Za-z0-9_.]*)"))
                    CollectionAssert.Contains(new[] { "Vector3", "Color" }, m.Groups[1].Value, $"{sig}: '{m.Value}' allocates per frame");
            }

            // URP unlit material through the shared helper (its Warn / Error on a missing shader), never Shader.Find here.
            StringAssert.Contains("TelegraphShapes.SharedUnlitSpriteMaterial(_log)", src);
            StringAssert.DoesNotContain("Shader.Find", src);
            StringAssert.Contains("_log?.Error(Source", src);

            // Nothing animates (Reduced Motion needs no branch).
            StringAssert.DoesNotContain("Time.time", src);
            StringAssert.DoesNotContain("Time.unscaledTime", src);
            StringAssert.DoesNotContain("Time.deltaTime", src);

            StringAssert.Contains("if (_log == null) ProjectContext.Instance.Container.Inject(this);", src);
        }

        [Test]
        public void Source_RivalIndicatorOffersOnlyWhatWouldBeAChevron()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("if (chevron && FloorRivalArrows.TryRepresent(GetInstanceID(), transform.position, tint, opacity))", src);
            int offer = src.IndexOf("FloorRivalArrows.TryRepresent(", System.StringComparison.Ordinal);
            int decide = src.IndexOf("if (!chevron) _wasUnderHud = false;", System.StringComparison.Ordinal);
            Assert.That(offer, Is.GreaterThan(decide), "offered after the clear-viewport / under-HUD decision is final");
        }
    }
}
