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
    /// The floor-arrow prototype (<c>-cw_floorarrows</c>, Maestro 2026-10-22: "put them in the outer circle at the floor"):
    /// the ring radius per framing, bearing to ring point, the angular de-overlap, the visibility rule (shared with the edge
    /// chevron), distance to size / alpha, the dev-tools gate, and source guards for the lifetime / allocation rules.
    /// </summary>
    public sealed class FloorRivalArrowsTests
    {
        private const float Pitch = 30f;               // MatchCamera._pitch
        private const float LandscapeOrtho = 5.6f;     // MatchCamera._orthoSize
        private const float Tol = 0.02f;

        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel)).Replace("\r\n", "\n");

        [TearDown]
        public void ResetSwitch() => FloorRivalArrows.DevRequested = false;

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
        // (16:9: 4.36 m above the chicken; 20:9: 3.55 m), which the brief's lateral-only rule would have missed.
        [TestCase(9f, 16f, 4.48f)]
        [TestCase(9f, 20f, 4.48f)]
        [TestCase(9f, 21f, 4.29f)]    // past the portrait ortho cap: 5.36 m lateral
        [TestCase(16f, 9f, 3.48f)]
        [TestCase(20f, 9f, 2.84f)]
        [TestCase(4f, 3f, 3.70f)]     // tablet: the touch hexes bind (4.63 m away), not the band (4.22 without controls)
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

            // The ring's farthest points in viewport units (the arrows on it are fitted one by one: FitAlongRay tests).
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

            // 4:3 touch (tablet): the hex cluster is the nearest limit, so the ring shrinks to stay off it.
            float tablet = RadiusAt(4f / 3f, out var tf, out var tinfo, HudDeviceMode.Touch);
            Assert.Less(tablet, FloorArrowRules.FramingRadius(4f / 3f, tf.OrthoSize, Pitch, tinfo) - 0.3f);

            // A control 2 m to the right of the chicken (16:9: 2 m lateral = 0.1 viewport) pulls the ring in to 0.8 x 2 -> the 2 m floor.
            var near = new List<Rect> { Rect.MinMaxRect(0.6f, 0.45f, 0.7f, 0.55f) };
            float a = 16f / 9f;
            float rNear = FloorArrowRules.FramingRadius(a, LandscapeOrtho, Pitch, new List<Rect>(), near);
            Assert.Less(rNear, FloorArrowRules.FramingRadius(a, LandscapeOrtho, Pitch, new List<Rect>()));
            float lateral = (0.6f - 0.5f) * 2f * LandscapeOrtho * a;
            Assert.AreEqual(Mathf.Max(FeedbackTuning.FloorArrowMinRadius, 0.8f * lateral), rNear, 1e-3f);
            Assert.AreEqual(3.0f, FloorArrowRules.RingRadius(10f, 10f, 10f, 3.75f), 1e-5f, "0.8 x the control distance");
        }

        // ── Screen space: direction, depth stretch, fit ────────────────────────────────────────

        [Test]
        public void ScreenDirection_FollowsTheProjectedBearing_AndTheStretchUndoesForeshortening()
        {
            Assert.AreEqual(0f, Vector2.Angle(Vector2.up, FloorArrowRules.ScreenDirection(0f, Pitch)), 1e-3f);
            Assert.AreEqual(0f, Vector2.Angle(Vector2.right, FloorArrowRules.ScreenDirection(90f, Pitch)), 1e-3f);
            Assert.AreEqual(0f, Vector2.Angle(Vector2.down, FloorArrowRules.ScreenDirection(180f, Pitch)), 1e-3f);
            // 45 degrees on the ground is (0.707, 0.707 x 0.5) on screen: 63.4 degrees from screen-up, not 45.
            Assert.AreEqual(63.435f, Vector2.SignedAngle(FloorArrowRules.ScreenDirection(45f, Pitch), Vector2.up), 0.01f);

            Assert.AreEqual(2f, FloorArrowRules.DepthStretch(Pitch), 1e-4f, "1 / sin 30");
            // An up-pointing arrow of screen length L is L x stretch long on the ground and L again on screen.
            const float len = 2f;
            float groundLen = len * FloorArrowRules.DepthStretch(Pitch);
            Assert.AreEqual(len, groundLen * Mathf.Sin(Pitch * Mathf.Deg2Rad), 1e-4f, "reads as long as a sideways one");

            // Ring point / ground round trip: the screen point maps back onto the ground ring.
            for (float b = 0f; b < 360f; b += 15f)
            {
                Vector2 g = FloorArrowRules.ScreenToGround(FloorArrowRules.ScreenRingPoint(b, 3.5f, Pitch), Pitch);
                Assert.AreEqual(3.5f, g.magnitude, 1e-3f, $"bearing {b}");
            }
        }

        private static Rect FrameAt(float aspect, OrientationFraming.Result f)
        {
            Rect r = FloorArrowRules.ViewportToScreenMetres(new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, f.FocusViewportY), f.OrthoSize, aspect);
            float m = FeedbackTuning.FloorArrowEdgeMargin;
            return Rect.MinMaxRect(r.xMin + m, r.yMin + m, r.xMax - m, r.yMax - m);
        }

        private static int ObstaclesAt(float aspect, OrientationFraming.Result f, HudDeviceMode mode, int statusRows, Rect[] into)
        {
            var info = new List<Rect>(); var ctl = new List<Rect>();
            HudControlZones.BuildInfoZones(info, mode, aspect, Vector4.zero, statusRows);
            HudControlZones.Build(ctl, mode, aspect, Vector4.zero);
            int n = 0;
            into[n++] = FeedbackTuning.FloorArrowBodyRect;
            var focus = new Vector2(0.5f, f.FocusViewportY);
            foreach (var z in ctl) into[n++] = FloorArrowRules.ViewportToScreenMetres(z, focus, f.OrthoSize, aspect);
            foreach (var z in info) into[n++] = FloorArrowRules.ViewportToScreenMetres(z, focus, f.OrthoSize, aspect);
            return n;
        }

        [Test]
        public void Fit_SidewaysArrowIsFullSize_UpArrowSqueezedBetweenHeadAndTopBand()
        {
            float aspect = 16f / 9f;
            float r = RadiusAt(aspect, out var f, out _);
            var obstacles = new Rect[12];
            int n = ObstaclesAt(aspect, f, HudDeviceMode.Touch, 0, obstacles);
            Rect frame = FrameAt(aspect, f);

            float side = FloorArrowRules.FitAlongRay(FloorArrowRules.ScreenRingPoint(90f, r, Pitch),
                FloorArrowRules.ScreenDirection(90f, Pitch), FeedbackTuning.FloorArrowNearSize, frame, obstacles, n, out float sideOff);
            Assert.AreEqual(FeedbackTuning.FloorArrowNearSize, side, 1e-4f);
            Assert.AreEqual(0f, sideOff, 1e-4f, "centred on the ring when there is room");

            Vector2 c = FloorArrowRules.ScreenRingPoint(0f, r, Pitch);
            float up = FloorArrowRules.FitAlongRay(c, Vector2.up, FeedbackTuning.FloorArrowNearSize, frame, obstacles, n, out float upOff);
            float bandBottom = obstacles[n - 1].yMin;   // the top band, last info zone with no status rows
            Assert.That(up, Is.InRange(FeedbackTuning.FloorArrowMinSize, FeedbackTuning.FloorArrowNearSize - 0.5f), "16:9: ~1.1 m of room");
            Assert.LessOrEqual(c.y + upOff + 0.5f * up, bandBottom + 1e-4f, "tip below the top band");
            Assert.GreaterOrEqual(c.y + upOff - 0.5f * up, FeedbackTuning.FloorArrowBodyRect.yMax - 1e-4f, "base above the bird's head");
        }

        [TestCase(9f, 16f)]
        [TestCase(9f, 20f)]
        [TestCase(16f, 9f)]
        [TestCase(20f, 9f)]
        public void Fit_EveryBearing_StaysInFrame_OutOfEveryHudZoneAndOffTheBird(float w, float h)
        {
            float aspect = w / h;
            float r = RadiusAt(aspect, out var f, out _);
            var obstacles = new Rect[12];
            Rect frame = FrameAt(aspect, f);
            foreach (var mode in new[] { HudDeviceMode.Touch, HudDeviceMode.KeyboardMouse })
            foreach (int rows in new[] { 0, 3 })
            {
                int n = ObstaclesAt(aspect, f, mode, rows, obstacles);
                for (float b = 0f; b < 360f; b += 5f)
                {
                    Vector2 c = FloorArrowRules.ScreenRingPoint(b, r, Pitch), d = FloorArrowRules.ScreenDirection(b, Pitch);
                    float size = FloorArrowRules.FitAlongRay(c, d, FeedbackTuning.FloorArrowNearSize, frame, obstacles, n, out float off);
                    Assert.GreaterOrEqual(size, FeedbackTuning.FloorArrowMinSize - 1e-4f, "never vanishes");
                    if (size <= FeedbackTuning.FloorArrowMinSize + 1e-4f) continue;   // squeezed to the floor: allowed to overlap
                    for (float s = -0.5f; s <= 0.5f; s += 0.05f)
                    {
                        Vector2 p = c + d * (off + s * size);
                        string at = $"{w}:{h} {mode} rows={rows} bearing {b} sample {s}";
                        Assert.IsTrue(p.x >= frame.xMin - 1e-3f && p.x <= frame.xMax + 1e-3f && p.y >= frame.yMin - 1e-3f && p.y <= frame.yMax + 1e-3f, at + " in frame");
                        for (int i = 0; i < n; i++)
                        {
                            Rect z = obstacles[i];
                            bool inside = p.x > z.xMin + 1e-3f && p.x < z.xMax - 1e-3f && p.y > z.yMin + 1e-3f && p.y < z.yMax - 1e-3f;
                            Assert.IsFalse(inside, at + $" inside obstacle {i}");
                        }
                    }
                }
            }
        }

        [Test]
        public void Fit_RingPointUnderAZone_KeepsTheMinimumAtTheRing()
        {
            var obstacles = new[] { Rect.MinMaxRect(2f, -1f, 6f, 1f) };
            float size = FloorArrowRules.FitAlongRay(new Vector2(3f, 0f), Vector2.right, 2f,
                Rect.MinMaxRect(-10f, -10f, 10f, 10f), obstacles, 1, out float off);
            Assert.AreEqual(FeedbackTuning.FloorArrowMinSize, size, 1e-5f);
            Assert.AreEqual(0f, off, 1e-5f);
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

        // ── Angular de-overlap ─────────────────────────────────────────────────────────────────

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

        [Test]
        public void Spread_ThreeRivalsInOneDirection_FanOutSymmetrically()
        {
            float sep = FloorArrowRules.MinSeparationDegrees(4.48f);
            Assert.That(sep, Is.InRange(10f, 30f), "about one arrow width plus a gap");
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

            // The fan is centred on the cluster's own mean (0.17 degrees), across the 0/360 seam.
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
            // 0 and 10 overlap (sep 15): they move to -2.5 / 12.5 (centred on 5). 26 then overlaps 12.5, so all three merge.
            var r = Spread(new[] { 0f, 10f, 26f }, new[] { 1, 2, 3 }, 15f);
            AssertSeparated(r, 15f);
            float mean = 12f;
            Assert.AreEqual(0f, AngularDistance(r[1], mean), 1e-3f);
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

        // ── Distance to size / alpha ───────────────────────────────────────────────────────────

        [Test]
        public void DistanceToScaleAndAlpha_MonotonicAndClamped()
        {
            const float r = 4.48f;
            float prevSize = float.MaxValue, prevAlpha = float.MaxValue;
            for (float d = 0f; d <= 80f; d += 0.25f)
            {
                float s = FloorArrowRules.ArrowSize(d, r), a = FloorArrowRules.ArrowAlpha(d, r);
                Assert.LessOrEqual(s, prevSize + 1e-6f, $"size at {d}");
                Assert.LessOrEqual(a, prevAlpha + 1e-6f, $"alpha at {d}");
                Assert.That(s, Is.InRange(FeedbackTuning.FloorArrowFarSize, FeedbackTuning.FloorArrowNearSize));
                Assert.That(a, Is.InRange(FeedbackTuning.FloorArrowFarAlpha, FeedbackTuning.FloorArrowNearAlpha));
                prevSize = s; prevAlpha = a;
            }
            Assert.AreEqual(FeedbackTuning.FloorArrowNearSize, FloorArrowRules.ArrowSize(r, r), 1e-5f, "at the ring: biggest");
            Assert.AreEqual(FeedbackTuning.FloorArrowFarSize, FloorArrowRules.ArrowSize(200f, r), 1e-5f, "far: smallest");
            Assert.Greater(FloorArrowRules.ArrowSize(8f, r), FloorArrowRules.ArrowSize(20f, r), "nearer is bigger");
            Assert.Greater(FloorArrowRules.ArrowAlpha(8f, r), FloorArrowRules.ArrowAlpha(20f, r), "nearer is stronger");
        }

        [Test]
        public void Iteration2_ArrowsAndRingAreBoldEnoughToRead()
        {
            // 2026-10-22 A/B shots: 1.0 / 0.6 m at 0.95 / 0.55 alpha and a 0.035 m cream ring at 0.22 read as specks.
            Assert.That(FeedbackTuning.FloorArrowNearSize, Is.InRange(1.8f, 2.4f));
            Assert.That(FeedbackTuning.FloorArrowFarSize, Is.InRange(1.1f, 1.3f));
            Assert.AreEqual(0.95f, FeedbackTuning.FloorArrowNearAlpha, 1e-5f);
            Assert.AreEqual(0.75f, FeedbackTuning.FloorArrowFarAlpha, 1e-5f);
            Assert.Greater(FeedbackTuning.FloorArrowOutlineRatio, FeedbackTuning.RivalChevronOutlineRatio, "thicker ink than the chevron");
            Assert.That(FeedbackTuning.FloorArrowRingWidth, Is.InRange(0.08f, 0.1f));
            Assert.That(FeedbackTuning.FloorArrowRingAlpha, Is.InRange(0.45f, 0.55f));
            Assert.Greater(FeedbackTuning.FloorArrowRingWidth, FeedbackTuning.AmbientSlotOutlineWidth, "heavier than the reach outlines");

            // Cool white: blue-leaning hue, far from the yellow range ring (60 degrees) and the cream rim.
            Color.RGBToHSV(FeedbackTuning.FloorArrowRingColor, out float hue, out float sat, out float val);
            Assert.That(hue * 360f, Is.InRange(180f, 240f));
            Assert.That(sat, Is.InRange(0.05f, 0.35f), "a tinted white, not a blue");
            Assert.Greater(val, 0.9f);
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

        // ── Dev switch ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Gate_InertWithoutDevTools_AndWhenNotRequested()
        {
            Assert.IsFalse(FloorRivalArrows.IsEnabled(true, false), "requested in a production build: off");
            Assert.IsFalse(FloorRivalArrows.IsEnabled(false, true));
            Assert.IsFalse(FloorRivalArrows.IsEnabled(false, false));
            Assert.IsTrue(FloorRivalArrows.IsEnabled(true, true));

            FloorRivalArrows.DevRequested = false;
            Assert.IsFalse(FloorRivalArrows.TryRepresent(1, new Vector3(20f, 0f, 20f), Color.red, 1f), "off: the chevron stays");
            Assert.AreEqual(0, Object.FindObjectsByType<FloorRivalArrows>(FindObjectsSortMode.None).Length, "off: nothing created");

            string src = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");
            StringAssert.Contains("public static bool DevEnabled => IsEnabled(_devRequested, DevTools.Available);", src);
            StringAssert.Contains("if (!DevEnabled || _instance == null) return false;", src);

            string driver = Read("Assets/_Game/Scripts/Diagnostics/AutoTestDriver.cs");
            int apply = driver.IndexOf("private void ApplyScreenshotTools()", System.StringComparison.Ordinal);
            int gate = driver.IndexOf("if (!DevTools.Available)", apply, System.StringComparison.Ordinal);
            int use = driver.IndexOf("FloorRivalArrows.DevRequested =", apply, System.StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(apply).And.LessThan(use), "the switch is set only past the dev-tools gate");
        }

        [Test]
        public void Parse_FloorArrowsKey()
        {
            var on = AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_floorarrows", "-cw_zones" });
            Assert.IsTrue(on.FloorArrows, "bare flag");
            Assert.IsTrue(on.Zones);
            CollectionAssert.IsEmpty(on.Problems);
            Assert.IsFalse(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_floorarrows", "0" }).FloorArrows);
            Assert.IsFalse(AutoTestConfig.FromCommandLine(new[] { "x.exe", "-cw_hud" }).FloorArrows, "default off");
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.FloorArrowsKey);
            StringAssert.DoesNotContain("floor", on.Describe());
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
        public void Source_NotParentedUnderTheChicken_NoPerFrameAllocation_UrpMaterial()
        {
            string src = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");

            // Lifetime: one root object kept across scenes; nothing is ever parented under a chicken (ChickenVisuals' renderer
            // scan / ghost swap). Created EAGERLY when the switch is set, never from an indicator's LateUpdate (qa: first frame lost).
            string ensure = Body(src, "private static void EnsureInstance()");
            StringAssert.Contains("new GameObject(\"FloorRivalArrows\")", ensure);
            StringAssert.Contains("DontDestroyOnLoad(go);", ensure);
            StringAssert.Contains("if (DevEnabled && Application.isPlaying) EnsureInstance();", src);
            StringAssert.Contains("if (!DevEnabled || _instance == null) return false;", Body(src, "public static bool TryRepresent("));
            StringAssert.DoesNotContain("new GameObject", Body(src, "public static bool TryRepresent("));
            StringAssert.DoesNotContain("ChickenGround.Anchor", src);
            foreach (Match m in Regex.Matches(src, @"SetParent\(([^,]+),"))
                CollectionAssert.Contains(new[] { "transform", "root.transform", "fillGo.transform" }, m.Groups[1].Value.Trim(),
                    "children hang only under this root or its own arrows: " + m.Value);
            StringAssert.DoesNotContain("local.transform)", src);

            // Hidden for a removed (dead / eliminated) local chicken.
            StringAssert.Contains("if (local.Combat.IsRemoved) return;", Body(src, "private void PrepareFrame()"));

            // Per frame: no reference-type allocation in the hot path (structs only).
            foreach (var sig in new[] { "private void LateUpdate()", "private void PlaceArrow(", "private void ShowRing()",
                                        "private bool Accept(", "public static bool TryRepresent(", "private Rect BuildScreenObstacles(" })
            {
                string body = Body(src, sig);
                foreach (Match m in Regex.Matches(body, @"\bnew\s+([A-Za-z_][A-Za-z0-9_.]*)"))
                    CollectionAssert.Contains(new[] { "Vector3", "Vector2", "Color", "Rect" }, m.Groups[1].Value,
                        $"{sig}: '{m.Value}' allocates per frame");
            }

            // URP unlit sprite material through the shared helper (its Warn / Error on a missing shader), never Shader.Find here.
            StringAssert.Contains("TelegraphShapes.SharedUnlitSpriteMaterial(_log)", src);
            StringAssert.DoesNotContain("SharedSolidLineMaterial", src);
            StringAssert.DoesNotContain("Shader.Find", src);
            StringAssert.Contains("_log?.Error(Source", src);

            // No pulsing / bobbing (Reduced Motion needs no branch): nothing time-driven.
            StringAssert.DoesNotContain("Time.time", src);
            StringAssert.DoesNotContain("Time.unscaledTime", src);
            StringAssert.DoesNotContain("Mathf.Sin(Time", src);

            // Self-injection per CONVENTIONS, never gated on HasInstance.
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
