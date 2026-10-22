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

        private static float RadiusAt(float aspect, out OrientationFraming.Result framing, out List<Rect> info)
        {
            framing = OrientationFraming.Compute(aspect, LandscapeOrtho, -6f, 15f, Pitch);
            info = new List<Rect>();
            HudControlZones.BuildInfoZones(info, HudDeviceMode.Touch, aspect, Vector4.zero, 0);
            return FloorArrowRules.FramingRadius(aspect, framing.OrthoSize, Pitch, info);
        }

        // Portrait: the 5.6 m lateral half-width binds (0.8 x 5.6). Landscape: the clear depth to the top HUD band binds
        // (16:9: 4.36 m above the chicken; 20:9: 3.55 m), which the brief's lateral-only rule would have missed.
        [TestCase(9f, 16f, 4.48f)]
        [TestCase(9f, 20f, 4.48f)]
        [TestCase(9f, 21f, 4.29f)]    // past the portrait ortho cap: 5.36 m lateral
        [TestCase(16f, 9f, 3.48f)]
        [TestCase(20f, 9f, 2.84f)]
        [TestCase(4f, 3f, 4.22f)]
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

            // The ring's farthest points, plus the near arrow's half length outward, in viewport units.
            float m = FloorArrowRules.GroundMetresPerViewportHeight(f.OrthoSize, Pitch);
            float reach = r + 0.5f * FeedbackTuning.FloorArrowNearSize;
            float top = f.FocusViewportY + reach / m;
            float bottom = f.FocusViewportY - reach / m;
            float side = 0.5f + reach / (2f * f.LateralHalfExtent);
            Assert.Less(top, info[0].yMin, "the ring's top arrow is below the top HUD band");
            Assert.Greater(bottom, f.Portrait ? OrientationFraming.PortraitBottomBandFraction : 0f, "clear of the bottom band");
            Assert.Less(side, 1f, "the ring's side arrows are inside the frame");
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
            StringAssert.Contains("if (!DevEnabled) return false;", src);

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

            // Lifetime: a scene-root object; nothing is ever parented under a chicken (ChickenVisuals' renderer scan / ghost swap).
            StringAssert.Contains("new GameObject(\"FloorRivalArrows\").AddComponent<FloorRivalArrows>()", src);
            StringAssert.DoesNotContain("ChickenGround.Anchor", src);
            foreach (Match m in Regex.Matches(src, @"SetParent\(([^,]+),"))
                Assert.AreEqual("transform", m.Groups[1].Value.Trim().Replace("go.transform", "transform"),
                    "children hang only under this root or its own arrows: " + m.Value);
            StringAssert.DoesNotContain("local.transform)", src);

            // Per frame: no reference-type allocation in the hot path (structs only).
            foreach (var sig in new[] { "private void LateUpdate()", "private void PlaceArrow(int i)", "private void ShowRing()",
                                        "private bool Accept(", "public static bool TryRepresent(" })
            {
                string body = Body(src, sig);
                foreach (Match m in Regex.Matches(body, @"\bnew\s+([A-Za-z_][A-Za-z0-9_.]*)"))
                    CollectionAssert.Contains(new[] { "Vector3", "Color", "GameObject" }, m.Groups[1].Value,
                        $"{sig}: '{m.Value}' allocates per frame");
            }
            Assert.That(Regex.Matches(Body(src, "public static bool TryRepresent("), @"new GameObject").Count, Is.EqualTo(1),
                "the one GameObject is the lazily created instance");
            StringAssert.DoesNotContain("new GameObject", Body(src, "private void LateUpdate()"));

            // URP-safe shared material through the telegraph helper (its Error on a missing shader), never Shader.Find here.
            StringAssert.Contains("TelegraphShapes.SharedSolidLineMaterial(_log)", src);
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
