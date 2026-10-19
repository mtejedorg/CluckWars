using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk D, Maestro-approved edge-arrow polish (RivalIndicator): (1) a rival's arrow is hidden while ANY part of
    /// the rival is on screen, with hysteresis; (2) arrows never sit in a HUD control zone (joystick, ability hexes, the
    /// desktop strip) but still point at the rival. The geometry is pure (ScreenEdgeProjection / HudControlZones), so it is
    /// pinned here without a live match.
    /// </summary>
    public class Phase8ChunkDRivalArrowTests
    {
        private static readonly Rect Band = FeedbackTuning.RivalChevronSafeArea;
        private const float Eps = 1e-4f;

        private static readonly float[] Aspects = { 2400f / 1080f, 16f / 9f, 4f / 3f, 21f / 9f };
        private static readonly HudDeviceMode[] Modes = { HudDeviceMode.Touch, HudDeviceMode.KeyboardMouse, HudDeviceMode.Gamepad };

        private static Vector2 Clearance(float aspect)
        {
            float r = FeedbackTuning.RivalChevronSizeFraction * FeedbackTuning.RivalChevronClearanceRadius;
            return new Vector2(r / aspect, r);
        }

        private static List<Rect> Zones(HudDeviceMode mode, float aspect, Vector4? insets = null)
        {
            var list = new List<Rect>();
            HudControlZones.Build(list, mode, aspect, insets ?? Vector4.zero);
            return list;
        }

        private static bool OnPerimeter(Vector2 p)
        {
            return Mathf.Abs(p.x - Band.xMin) < Eps || Mathf.Abs(p.x - Band.xMax) < Eps ||
                   Mathf.Abs(p.y - Band.yMin) < Eps || Mathf.Abs(p.y - Band.yMax) < Eps;
        }

        // ── 1. Hidden while any part of the rival is on screen ───────────────────────────────────────────

        [Test]
        public void BodyOverlappingTheFrame_IsOnScreen_EvenWithTheFeetOutside()
        {
            // Feet below the bottom edge (y = -0.03) but the body reaches up to y = +0.05: the bird is visible.
            var body = Rect.MinMaxRect(0.4f, -0.03f, 0.42f, 0.05f);
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(body, false, FeedbackTuning.RivalOnScreenHysteresis), Is.True);
        }

        [Test]
        public void BodyFullyOutside_IsOffScreen_OnEveryEdge()
        {
            var h = FeedbackTuning.RivalOnScreenHysteresis;
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(0.4f, -0.2f, 0.42f, -0.1f), false, h), Is.False, "below");
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(0.4f, 1.1f, 0.42f, 1.2f), false, h), Is.False, "above");
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(-0.2f, 0.4f, -0.1f, 0.5f), false, h), Is.False, "left");
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(1.1f, 0.4f, 1.2f, 0.5f), false, h), Is.False, "right");
        }

        [Test]
        public void Hysteresis_AppliesOnTheWayOutOnly()
        {
            float h = FeedbackTuning.RivalOnScreenHysteresis;
            // Fully outside the left edge by half the margin.
            var justOut = Rect.MinMaxRect(-0.1f, 0.4f, -0.5f * h, 0.5f);
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(justOut, true, h), Is.True,
                "A rival that WAS on screen stays on screen until it is fully a margin beyond the frame (no ring/arrow flicker).");
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(justOut, false, h), Is.False,
                "Coming in needs real overlap, so the arrow is not suppressed for a rival that is still off screen.");

            var farOut = Rect.MinMaxRect(-0.2f, 0.4f, -2f * h, 0.5f);
            Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(farOut, true, h), Is.False, "beyond the margin it hands over to the arrow");
        }

        [Test]
        public void NoFlicker_AcrossASlowDriftThroughTheBorder()
        {
            // Walk a rival's right-hand edge across the left border and back in tiny steps: the verdict may change at most
            // once per direction (never chatter).
            float h = FeedbackTuning.RivalOnScreenHysteresis;
            bool state = true;
            int flips = 0;
            for (int i = 0; i <= 200; i++)
            {
                float x = 0.05f - i * 0.001f;                 // rival's right edge 0.05 -> -0.15
                bool now = ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(x - 0.03f, 0.4f, x, 0.5f), state, h);
                if (now != state) flips++;
                state = now;
            }
            for (int i = 0; i <= 200; i++)
            {
                float x = -0.15f + i * 0.001f;
                bool now = ScreenEdgeProjection.IsPartlyOnScreen(Rect.MinMaxRect(x - 0.03f, 0.4f, x, 0.5f), state, h);
                if (now != state) flips++;
                state = now;
            }
            Assert.That(flips, Is.EqualTo(2), "out once, back in once");
        }

        [Test]
        public void RealCamera_ChickenWithFeetBelowTheFrame_IsStillOnScreen()
        {
            var go = new GameObject("rival-arrow-cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = 10f; cam.aspect = 2400f / 1080f;
                go.transform.position = new Vector3(0f, 20f, -20f);
                go.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

                // Feet a hair below the bottom edge, body 1.2 m tall.
                Vector3 feet = GroundPoint(cam, 0.5f, -0.012f);
                Assert.That(cam.WorldToViewportPoint(feet).y, Is.LessThan(0f), "test setup: feet are below the frame");
                var b = new Bounds(feet + Vector3.up * 0.6f, new Vector3(0.7f, 1.2f, 0.7f));
                Assert.That(ScreenEdgeProjection.TryProjectBounds(cam, b, out Rect vp), Is.True);
                Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(vp, false, FeedbackTuning.RivalOnScreenHysteresis), Is.True,
                    "Body in view -> no arrow (the old feet-point test drew one on top of the bird).");

                // Fully gone: arrow territory.
                Vector3 gone = GroundPoint(cam, 0.5f, -0.4f);
                var b2 = new Bounds(gone + Vector3.up * 0.6f, new Vector3(0.7f, 1.2f, 0.7f));
                Assert.That(ScreenEdgeProjection.TryProjectBounds(cam, b2, out Rect vp2), Is.True);
                Assert.That(ScreenEdgeProjection.IsPartlyOnScreen(vp2, false, FeedbackTuning.RivalOnScreenHysteresis), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static Vector3 GroundPoint(Camera cam, float vx, float vy)
        {
            Ray r = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
            float t = -r.origin.y / r.direction.y;
            return r.origin + r.direction * t;
        }

        // ── 2. Arrows stay out of the control zones, and still point at the rival ───────────────────────────

        [Test]
        public void PanelSize_MatchesThePanelSettingsAsset()
        {
            var ps = Resources.Load<PanelSettings>("PanelSettings");
            Assert.That(ps, Is.Not.Null, "Resources/PanelSettings.asset");
            Assert.That(ps.scaleMode, Is.EqualTo(PanelScaleMode.ScaleWithScreenSize));
            Assert.That(ps.referenceResolution.x, Is.EqualTo((int)HudControlZones.ReferenceWidth));
            Assert.That(ps.referenceResolution.y, Is.EqualTo((int)HudControlZones.ReferenceHeight));
            Assert.That(ps.match, Is.EqualTo(HudControlZones.ReferenceMatch).Within(1e-5f),
                "PanelSize is the closed form for match 0.5; a changed match must update HudControlZones.PanelSize.");

            // Cross-check the closed form against Unity's own definition of the scale at 2400x1080.
            float scale = Mathf.Sqrt((2400f / 1920f) * (1080f / 1080f));
            var size = HudControlZones.PanelSize(2400f / 1080f);
            Assert.That(size.x, Is.EqualTo(2400f / scale).Within(0.5f));
            Assert.That(size.y, Is.EqualTo(1080f / scale).Within(0.5f));
        }

        [Test]
        public void Zones_MirrorTouchControlsUss()
        {
            string uss = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/UI/Styles/TouchControls.uss"));

            var joy = Regex.Match(uss, @"\.cw-joy-base\s*\{[^}]*left:\s*(\d+)px;\s*bottom:\s*(\d+)px;\s*width:\s*(\d+)px;\s*height:\s*(\d+)px");
            Assert.That(joy.Success, Is.True, ".cw-joy-base rule not found");
            Assert.That(float.Parse(joy.Groups[1].Value), Is.EqualTo(HudControlZones.JoystickLeftPx));
            Assert.That(float.Parse(joy.Groups[2].Value), Is.EqualTo(HudControlZones.JoystickBottomPx));
            Assert.That(float.Parse(joy.Groups[3].Value), Is.EqualTo(HudControlZones.JoystickSizePx));
            Assert.That(float.Parse(joy.Groups[4].Value), Is.EqualTo(HudControlZones.JoystickSizePx));

            var hex = Regex.Match(uss, @"^\.cw-hex\s*\{[^}]*width:\s*(\d+)px;\s*height:\s*(\d+)px", RegexOptions.Multiline);
            Assert.That(hex.Success, Is.True, ".cw-hex rule not found");
            Assert.That(float.Parse(hex.Groups[1].Value), Is.EqualTo(HudControlZones.HexSizePx));
            Assert.That(float.Parse(hex.Groups[2].Value), Is.EqualTo(HudControlZones.HexSizePx));

            for (int i = 0; i < 4; i++)
            {
                var m = Regex.Match(uss, @"^\.cw-hex--a" + (i + 1) + @"\s*\{\s*right:\s*(\d+)px;\s*bottom:\s*(\d+)px;", RegexOptions.Multiline);
                Assert.That(m.Success, Is.True, $".cw-hex--a{i + 1} rule not found");
                Assert.That(float.Parse(m.Groups[1].Value), Is.EqualTo(HudControlZones.TouchHexRightBottomPx[i].x), $"a{i + 1} right");
                Assert.That(float.Parse(m.Groups[2].Value), Is.EqualTo(HudControlZones.TouchHexRightBottomPx[i].y), $"a{i + 1} bottom");

                var d = Regex.Match(uss, @"\.cw-hud--desktop \.cw-hex--a" + (i + 1) + @",[^{]*\{\s*left:\s*50%;\s*right:\s*auto;\s*bottom:\s*(\d+)px;\s*margin-left:\s*(-?\d+)px");
                Assert.That(d.Success, Is.True, $"desktop a{i + 1} rule not found");
                Assert.That(float.Parse(d.Groups[1].Value), Is.EqualTo(HudControlZones.DesktopStripBottomPx), $"desktop a{i + 1} bottom");
                Assert.That(float.Parse(d.Groups[2].Value), Is.EqualTo(HudControlZones.DesktopStripLeftFromCentrePx[i]), $"desktop a{i + 1} margin-left");
            }
        }

        [Test]
        public void TouchZones_CoverTheJoystickAndEveryHex()
        {
            var z = Zones(HudDeviceMode.Touch, 2400f / 1080f);
            Assert.That(z.Count, Is.EqualTo(5), "joystick + 4 hexes");

            Vector2 panel = HudControlZones.PanelSize(2400f / 1080f);
            // Joystick centre: (80 + 140, 80 + 140) panel px from the bottom-left.
            Assert.That(Contains(z, new Vector2(220f / panel.x, 220f / panel.y)), Is.True, "joystick centre");
            for (int i = 0; i < 4; i++)
            {
                var rb = HudControlZones.TouchHexRightBottomPx[i];
                var c = new Vector2(1f - (rb.x + 75f) / panel.x, (rb.y + 75f) / panel.y);
                Assert.That(Contains(z, c), Is.True, $"hex a{i + 1} centre");
            }
        }

        private static bool Contains(List<Rect> zones, Vector2 p)
        {
            foreach (var r in zones) if (r.Contains(p)) return true;
            return false;
        }

        [Test]
        public void BottomLeftRival_NoLongerGetsAnArrowInsideTheJoystick()
        {
            float aspect = 2400f / 1080f;
            var zones = Zones(HudDeviceMode.Touch, aspect);
            var clr = Clearance(aspect);
            var target = new Vector2(-0.3f, -0.3f);

            // Before: the plain clamp is the band's bottom-left corner, which is inside the joystick zone.
            ScreenEdgeProjection.ClampToEdge(target, false, Band, out Vector2 plain, out _);
            Assert.That(ScreenEdgeProjection.IsInsideAnyZone(plain, zones, clr), Is.True, "premise: the old spot was in the joystick");

            bool off = ScreenEdgeProjection.ClampToEdge(target, false, Band, zones, clr, out Vector2 placed, out Vector2 bearing);
            Assert.That(off, Is.True);
            Assert.That(ScreenEdgeProjection.IsInsideAnyZone(placed, zones, clr), Is.False);
            Assert.That(OnPerimeter(placed), Is.True, "it slides ALONG the edge band, it does not float into the play field");
            Vector2 expected = (target - placed).normalized;
            Assert.That(Vector2.Dot(bearing, expected), Is.GreaterThan(0.9999f), "still aimed at the rival");
        }

        [Test]
        public void UnblockedRival_IsPlacedExactlyAsBefore()
        {
            float aspect = 2400f / 1080f;
            var zones = Zones(HudDeviceMode.Touch, aspect);
            var clr = Clearance(aspect);
            // Straight above, left of centre top: nothing there.
            var target = new Vector2(0.45f, 1.6f);

            ScreenEdgeProjection.ClampToEdge(target, false, Band, out Vector2 plain, out Vector2 plainBearing);
            ScreenEdgeProjection.ClampToEdge(target, false, Band, zones, clr, out Vector2 placed, out Vector2 bearing);

            Assert.That(placed, Is.EqualTo(plain));
            Assert.That(bearing, Is.EqualTo(plainBearing));
        }

        [Test]
        public void Sweep_NoArrowEverLandsInAControlZone_ForAnyModeAspectOrTarget()
        {
            int checkedCount = 0;
            foreach (float aspect in Aspects)
            foreach (var mode in Modes)
            {
                var zones = Zones(mode, aspect);
                var clr = Clearance(aspect);

                for (float x = -1.5f; x <= 2.5f; x += 0.05f)
                for (float y = -1.5f; y <= 2.5f; y += 0.05f)
                {
                    var target = new Vector2(x, y);
                    bool off = ScreenEdgeProjection.ClampToEdge(target, false, Band, zones, clr, out Vector2 p, out Vector2 bearing);
                    if (!off) continue;
                    checkedCount++;

                    string ctx = $"aspect {aspect:0.00} mode {mode} target {target}";
                    Assert.That(ScreenEdgeProjection.IsInsideAnyZone(p, zones, clr), Is.False, "in a control zone: " + ctx);
                    Assert.That(p.x, Is.InRange(Band.xMin - Eps, Band.xMax + Eps), "left the band (x): " + ctx);
                    Assert.That(p.y, Is.InRange(Band.yMin - Eps, Band.yMax + Eps), "left the band (y): " + ctx);
                    Assert.That(OnPerimeter(p), Is.True, "not pinned to the edge band: " + ctx);
                    Assert.That(bearing.magnitude, Is.EqualTo(1f).Within(Eps), "bearing not a unit vector: " + ctx);
                    Vector2 d = target - p;
                    if (d.magnitude > 0.02f)
                        Assert.That(Vector2.Dot(bearing, d.normalized), Is.GreaterThan(0.9999f), "arrow does not point at the rival: " + ctx);
                }
            }
            Assert.That(checkedCount, Is.GreaterThan(1000));
        }

        [Test]
        public void Zones_RespectTheDeviceSafeArea()
        {
            // A notch-style inset (6 % left, 3 % bottom, 5 % right) shifts the controls, and so the zones, with the safe area.
            float aspect = 2400f / 1080f;
            var plain = Zones(HudDeviceMode.Touch, aspect);
            var inset = Zones(HudDeviceMode.Touch, aspect, new Vector4(0.06f, 0.03f, 0.05f, 0f));

            Assert.That(inset[0].xMin, Is.EqualTo(plain[0].xMin + 0.06f).Within(Eps), "joystick moves right with the left inset");
            Assert.That(inset[0].yMin, Is.EqualTo(plain[0].yMin + 0.03f).Within(Eps), "joystick moves up with the bottom inset");
            Assert.That(inset[1].xMax, Is.EqualTo(plain[1].xMax - 0.05f).Within(Eps), "hexes move left with the right inset");
        }

        [Test]
        public void DesktopLayout_HasNoJoystickZone_ButCoversTheBottomCentreStrip()
        {
            float aspect = 16f / 9f;
            var z = Zones(HudDeviceMode.KeyboardMouse, aspect);
            Assert.That(z.Count, Is.EqualTo(4));
            Assert.That(Contains(z, new Vector2(0.05f, 0.05f)), Is.False, "no joystick on desktop / pad: the bottom-left corner is free");
            Assert.That(Contains(z, new Vector2(0.5f, 0.05f)), Is.True, "bottom-centre strip");
        }

        [Test]
        public void RivalIndicator_UsesTheBodyTestAndTheZoneAwareProjection()
        {
            // The wiring that makes both behaviours real. Source-pinned like the E1 colour test, because the networked
            // controller does not exist in EditMode.
            string src = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/RivalIndicator.cs"));
            StringAssert.Contains("ScreenEdgeProjection.IsPartlyOnScreen", src);
            StringAssert.Contains("HudControlZones.Current", src);
            StringAssert.Contains("FeedbackTuning.RivalOnScreenHysteresis", src);
            Assert.That(Regex.IsMatch(src, @"ScreenEdgeProjection\.TryProject\(cam, transform\.position,\s*FeedbackTuning\.RivalChevronSafeArea,\s*HudControlZones\.Current"),
                Is.True, "the arrow must be projected with the HUD zones");
            StringAssert.DoesNotContain("Debug.Log", src);
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
