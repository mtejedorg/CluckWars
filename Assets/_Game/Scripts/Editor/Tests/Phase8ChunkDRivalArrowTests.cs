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
    /// desktop strip) but still point at the rival; (3, WP7) the HUD information zones (top band, status strip with rows)
    /// mirror the USS in both orientations, and "off screen" means outside the clear viewport. The geometry is pure
    /// (ScreenEdgeProjection / HudControlZones), so it is pinned here without a live match.
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

            // Cross-check the closed form against UI Toolkit's definition of the scale: a LINEAR blend of the two ratios
            // (UGUI's CanvasScaler blends in log space; UI Toolkit does not). Panels measured by the WP8 UiProbe in standalone
            // players: 2400x1080 -> 2133x960, 540x1200 (= any 9:20 phone) -> 776x1724, 540x960 -> 923x1641.
            foreach (var (w, h, pw, ph) in new[] { (2400f, 1080f, 2133f, 960f), (540f, 1200f, 776f, 1724f), (540f, 960f, 923f, 1641f), (960f, 540f, 1920f, 1080f) })
            {
                float scale = Mathf.Lerp(w / 1920f, h / 1080f, 0.5f);
                var size = HudControlZones.PanelSize(w / h);
                Assert.That(size.x, Is.EqualTo(w / scale).Within(0.5f), $"{w}x{h} width");
                Assert.That(size.y, Is.EqualTo(h / scale).Within(0.5f), $"{w}x{h} height");
                Assert.That(size.x, Is.EqualTo(pw).Within(1f), $"{w}x{h} measured width");
                Assert.That(size.y, Is.EqualTo(ph).Within(1f), $"{w}x{h} measured height");
            }
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

                var p = Regex.Match(uss, @"^\.layout--portrait \.cw-hex--a" + (i + 1) + @"\s*\{\s*right:\s*(\d+)px;\s*bottom:\s*(\d+)px;", RegexOptions.Multiline);
                Assert.That(p.Success, Is.True, $"portrait a{i + 1} rule not found");
                Assert.That(float.Parse(p.Groups[1].Value), Is.EqualTo(HudControlZones.TouchHexRightBottomPortraitPx[i].x), $"portrait a{i + 1} right");
                Assert.That(float.Parse(p.Groups[2].Value), Is.EqualTo(HudControlZones.TouchHexRightBottomPortraitPx[i].y), $"portrait a{i + 1} bottom");
            }
            var pj = Regex.Match(uss, @"^\.layout--portrait \.cw-joy-base\s*\{\s*left:\s*(\d+)px;", RegexOptions.Multiline);
            Assert.That(pj.Success && float.Parse(pj.Groups[1].Value) == HudControlZones.JoystickLeftPortraitPx, Is.True, "portrait joystick left");
            var ps = Regex.Match(uss, @"^\.layout--portrait \.cw-status-strip\s*\{[^}]*left:\s*(\d+)px;", RegexOptions.Multiline);
            Assert.That(ps.Success && float.Parse(ps.Groups[1].Value) == HudControlZones.StatusStripPortraitLeftPx, Is.True, "portrait status strip left");
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
            StringAssert.Contains("FeedbackTuning.RivalOnScreenHysteresis", src);
            // WP7: projected against the CLEAR viewport (info zones) and kept out of control + info zones.
            Assert.That(Regex.IsMatch(src, @"ScreenEdgeProjection\.TryProject\(cam, transform\.position,\s*FeedbackTuning\.RivalChevronSafeArea, info, HudControlZones\.CurrentAvoid\(cam\)"),
                Is.True, "the arrow must be projected with the HUD info zones and every avoid zone");
            StringAssert.Contains("var info = HudControlZones.CurrentInfo(cam);", src);
            StringAssert.Contains("ScreenEdgeProjection.ClampToClearViewport(covered, true", src);
            StringAssert.Contains("ScreenEdgeProjection.InfoZoneCoverage(", src);
            // The spread pass runs on the clear band, with the same avoid zones, re-aiming at the stored target.
            StringAssert.Contains("ScreenEdgeProjection.ClearBand(FeedbackTuning.RivalChevronSafeArea, HudControlZones.CurrentInfo(cam)", src);
            StringAssert.Contains("ind._spreadTarget", src);
            StringAssert.DoesNotContain("Debug.Log", src);
        }

        // ── 3. Information zones (WP7, decisions C6 / F6: both orientations) ──────────────────────────────

        private static readonly float[] PortraitAspects = { 1080f / 1920f, 1080f / 2400f, 1080f / 2410f };

        private static List<Rect> Info(HudDeviceMode mode, float aspect, int rows, Vector4? insets = null)
        {
            var list = new List<Rect>();
            HudControlZones.BuildInfoZones(list, mode, aspect, insets ?? Vector4.zero, rows);
            return list;
        }

        private static string Uss(string file) => File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/UI/Styles/" + file));

        private static float Px(string css, string pattern)
        {
            var m = Regex.Match(css, pattern, RegexOptions.Multiline);
            Assert.That(m.Success, Is.True, "not found: " + pattern);
            return float.Parse(m.Groups[1].Value);
        }

        [Test]
        public void InfoZones_MirrorMatchTopBarUss_AndTheBandIsExactlyTheFullBoard()
        {
            string css = Uss("MatchTopBar.uss");
            Assert.That(Px(css, @"^\.cw-topbar\s*\{[^}]*height:\s*(\d+)px"), Is.EqualTo(HudControlZones.TopBandLandscapePx), ".cw-topbar height");

            var lay = Regex.Match(css, @"\.layout--portrait \.cw-topbar-layout\s*\{\s*min-height:\s*(\d+)px;\s*padding-bottom:\s*(\d+)px");
            Assert.That(lay.Success, Is.True, ".layout--portrait .cw-topbar-layout rule not found");
            Assert.That(float.Parse(lay.Groups[1].Value), Is.EqualTo(HudControlZones.TopBandPortraitLayoutPx), "portrait layout min-height");
            Assert.That(float.Parse(lay.Groups[2].Value), Is.EqualTo(HudControlZones.TopBandPortraitFadePx), "portrait fade (padding-bottom)");

            // The portrait band (layout minus fade) is the four-row board: top padding + panel padding/border + 4 rows.
            float padTop = Px(css, @"^\.cw-topbar-layout\s*\{[^}]*padding-top:\s*(\d+)px");
            float panelPad = Px(css, @"^\.cw-lb-panel\s*\{[^}]*padding:\s*(\d+)px");
            float panelBorder = Px(css, @"^\.cw-lb-panel\s*\{[^}]*border-top-width:\s*(\d+)px");
            float rowH = Px(css, @"^\.layout--portrait \.cw-lb-row\s*\{\s*height:\s*(\d+)px");
            float rowGap = Px(css, @"^\.cw-lb-row\s*\{[^}]*margin-bottom:\s*(\d+)px");
            float board = padTop + 2f * panelPad + 2f * panelBorder + 4f * (rowH + rowGap);
            Assert.That(board, Is.EqualTo(HudControlZones.TopBandPortraitLayoutPx - HudControlZones.TopBandPortraitFadePx),
                "the portrait info zone must be exactly the full board (re-derive min-height when a row changes)");
        }

        [Test]
        public void InfoZones_MirrorTheStatusStripInTouchControlsUss()
        {
            string css = Uss("TouchControls.uss");
            var strip = Regex.Match(css, @"^\.cw-status-strip\s*\{[^}]*left:\s*(\d+)px;\s*bottom:\s*(\d+)px;\s*width:\s*(\d+)px", RegexOptions.Multiline);
            Assert.That(strip.Success, Is.True, ".cw-status-strip rule not found");
            Assert.That(float.Parse(strip.Groups[1].Value), Is.EqualTo(HudControlZones.StatusStripLeftPx));
            Assert.That(float.Parse(strip.Groups[2].Value), Is.EqualTo(HudControlZones.StatusStripBottomPx));
            Assert.That(float.Parse(strip.Groups[3].Value), Is.EqualTo(HudControlZones.StatusStripWidthPx));

            Assert.That(Px(css, @"^\.layout--portrait \.cw-status-strip\s*\{\s*width:\s*(\d+)px"), Is.EqualTo(HudControlZones.StatusStripPortraitWidthPx));
            var desk = Regex.Match(css, @"\.cw-hud--desktop \.cw-status-strip,[^{]*\{\s*left:\s*(\d+)px;\s*bottom:\s*(\d+)px");
            Assert.That(desk.Success, Is.True, "desktop status strip rule not found");
            Assert.That(float.Parse(desk.Groups[1].Value), Is.EqualTo(HudControlZones.StatusStripDesktopLeftPx));
            Assert.That(float.Parse(desk.Groups[2].Value), Is.EqualTo(HudControlZones.StatusStripDesktopBottomPx));

            var row = Regex.Match(css, @"^\.cw-status-row\s*\{[^}]*height:\s*(\d+)px;\s*margin-top:\s*(\d+)px", RegexOptions.Multiline);
            Assert.That(row.Success, Is.True, ".cw-status-row rule not found");
            Assert.That(float.Parse(row.Groups[1].Value) + float.Parse(row.Groups[2].Value), Is.EqualTo(HudControlZones.StatusRowPitchPx));

            // Spec 1.5: in portrait the strip's right edge is at most the joystick's right edge.
            Assert.That(HudControlZones.StatusStripLeftPx + HudControlZones.StatusStripPortraitWidthPx,
                Is.LessThanOrEqualTo(HudControlZones.JoystickLeftPx + HudControlZones.JoystickSizePx));
        }

        [Test]
        public void InfoZones_TopBandAlways_StatusStripOnlyWhileItHasRows()
        {
            float land = 16f / 9f;
            var z = Info(HudDeviceMode.Touch, land, 0);
            Assert.That(z.Count, Is.EqualTo(1), "no rows: the top band only");
            Assert.That(z[0].xMin, Is.EqualTo(0f)); Assert.That(z[0].xMax, Is.EqualTo(1f)); Assert.That(z[0].yMax, Is.EqualTo(1f));
            Assert.That(z[0].yMin, Is.EqualTo(1f - HudControlZones.TopBandLandscapeZonePx / 1080f).Within(Eps), "landscape: the bar, per the fade switch");

            z = Info(HudDeviceMode.Touch, land, 2);
            Assert.That(z.Count, Is.EqualTo(2));
            Vector2 panel = HudControlZones.PanelSize(land);
            Assert.That(z[1].xMin, Is.EqualTo(80f / panel.x).Within(Eps));
            Assert.That(z[1].width, Is.EqualTo(380f / panel.x).Within(Eps));
            Assert.That(z[1].yMin, Is.EqualTo(380f / panel.y).Within(Eps));
            Assert.That(z[1].height, Is.EqualTo(140f / panel.y).Within(Eps), "two rows of 62 + 8");

            float port = 1080f / 1920f;
            Vector2 pp = HudControlZones.PanelSize(port);
            var p = Info(HudDeviceMode.Touch, port, 1, new Vector4(0f, 0.02f, 0f, 0.055f));
            Assert.That(p[0].yMin, Is.EqualTo(1f - 0.055f - 276f / pp.y).Within(Eps), "portrait: below the safe-area top, the full board");
            Assert.That(p[1].width, Is.EqualTo(280f / pp.x).Within(Eps), "portrait strip is 280 wide");
            Assert.That(p[1].yMin, Is.EqualTo(0.02f + 380f / pp.y).Within(Eps), "the strip moves up with the bottom inset");

            var d = Info(HudDeviceMode.KeyboardMouse, land, 1);
            Assert.That(d[1].xMin, Is.EqualTo(40f / panel.x).Within(Eps), "desktop strip left 40");
            Assert.That(d[1].yMin, Is.EqualTo(60f / panel.y).Within(Eps), "desktop strip bottom 60");
        }

        /// <summary>Every (aspect, mode) the shipped layout is specified for: every mode in landscape; touch in portrait
        /// (spec 1.5 has no portrait desktop / pad layout).</summary>
        private static IEnumerable<(float aspect, HudDeviceMode mode)> Layouts()
        {
            foreach (float a in Aspects)
            foreach (var m in Modes)
                yield return (a, m);
            foreach (float a in PortraitAspects)
                yield return (a, HudDeviceMode.Touch);
        }

        [Test]
        public void InfoZones_NeverOverlapAControlZone_OrEachOther()
        {
            foreach (var (aspect, mode) in Layouts())
            {
                var info = Info(mode, aspect, 3);
                var ctrl = Zones(mode, aspect);
                Assert.That(info[0].Overlaps(info[1]), Is.False, $"band vs strip, aspect {aspect:0.00} {mode}");
                foreach (var c in ctrl)
                foreach (var i in info)
                    Assert.That(c.Overlaps(i), Is.False, $"control {c} vs info {i}, aspect {aspect:0.00} {mode}");
            }
        }

        [Test]
        public void Sweep_ClearViewport_EveryHiddenRivalGetsAnArrowOutsideEveryZone_AndNoVisibleOneDoes()
        {
            // W2 + W3 of spec 3.5, both orientations: an arrow exactly when the target is outside the clear viewport; never
            // inside a control or info zone; inside the (clear) band; always aimed at the target.
            int checkedCount = 0;
            foreach (var (aspect, mode) in Layouts())
            foreach (int rows in new[] { 0, 1, 3 })
            {
                var info = Info(mode, aspect, rows);
                var avoid = new List<Rect>(Zones(mode, aspect));
                avoid.AddRange(info);
                var clr = Clearance(aspect);
                Rect clear = ScreenEdgeProjection.ClearBand(Band, info, clr);

                for (float x = -1.5f; x <= 2.5f; x += 0.05f)
                for (float y = -1.5f; y <= 2.5f; y += 0.05f)
                {
                    var target = new Vector2(x, y);
                    bool off = ScreenEdgeProjection.ClampToClearViewport(target, false, Band, info, avoid, clr, out Vector2 p, out Vector2 bearing);
                    string ctx = $"aspect {aspect:0.00} mode {mode} rows {rows} target {target}";
                    Assert.That(off, Is.EqualTo(!ScreenEdgeProjection.IsInClearViewport(target, info)), "arrow iff hidden: " + ctx);
                    if (!off) continue;
                    checkedCount++;

                    Assert.That(ScreenEdgeProjection.IsInsideAnyZone(p, avoid, clr), Is.False, "in a zone: " + ctx);
                    Assert.That(p.x, Is.InRange(clear.xMin - Eps, clear.xMax + Eps), "left the clear band (x): " + ctx);
                    Assert.That(p.y, Is.InRange(clear.yMin - Eps, clear.yMax + Eps), "left the clear band (y): " + ctx);
                    Vector2 d = target - p;
                    if (d.magnitude > 0.02f)
                        Assert.That(Vector2.Dot(bearing, d.normalized), Is.GreaterThan(0.9999f), "arrow does not point at the rival: " + ctx);
                }
            }
            Assert.That(checkedCount, Is.GreaterThan(1000));
        }

        [Test]
        public void RivalUnderTheTopBand_GetsItsArrowAtTheBandsLowerEdge_InBothOrientations()
        {
            foreach (float aspect in new[] { 16f / 9f, 1080f / 2400f })
            {
                var info = Info(HudDeviceMode.Touch, aspect, 0);
                var avoid = new List<Rect>(Zones(HudDeviceMode.Touch, aspect));
                avoid.AddRange(info);
                var clr = Clearance(aspect);
                var target = new Vector2(0.5f, 0.5f * (info[0].yMin + 1f));   // halfway up the band

                bool off = ScreenEdgeProjection.ClampToClearViewport(target, false, Band, info, avoid, clr, out Vector2 p, out Vector2 bearing);
                Assert.That(off, Is.True, $"under the HUD is hidden (aspect {aspect:0.00})");
                Assert.That(p.x, Is.EqualTo(0.5f).Within(Eps), "straight below the rival");
                Assert.That(p.y, Is.EqualTo(info[0].yMin - clr.y).Within(Eps), "pinned at the band's lower edge, the arrow clear of it");
                Assert.That(Vector2.Dot(bearing, Vector2.up), Is.GreaterThan(0.9999f), "pointing up at the rival");
            }
        }

        [Test]
        public void LandscapeTopBand_IsOneSwitch_330WithTheFade_290Without()
        {
            // Maestro 2026-10-23 (1a): the fade is not HUD, 290 px in both orientations. The switch is
            // HudControlZones.LandscapeTopBandIncludesFade, nothing else.
            Assert.IsFalse(HudControlZones.LandscapeTopBandIncludesFade, "decision 1a: the landscape fade is excluded");
            Assert.That(HudControlZones.TopBandLandscapeZonePx, Is.EqualTo(290f), "shipped landscape info zone");
            Assert.That(HudControlZones.TopBandLandscapeZonePx, Is.EqualTo(HudControlZones.LandscapeTopBandIncludesFade
                ? HudControlZones.TopBandLandscapePx
                : HudControlZones.TopBandLandscapePx - HudControlZones.TopBandLandscapeFadePx));
            Assert.That(HudControlZones.TopBandLandscapePx - HudControlZones.TopBandLandscapeFadePx, Is.EqualTo(290f));
            string zones = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/HudControlZones.cs"));
            StringAssert.Contains(": 1f - TopBandLandscapeZonePx / ph;", zones, "BuildInfoZones reads the switch, not the raw bar height");
        }

        [Test]
        public void ChickenNameplate_RunsAfterRivalIndicator_SoItReadsThisFramesVerdict()
        {
            int Order(System.Type t)
            {
                var a = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(t, typeof(DefaultExecutionOrder));
                Assert.That(a, Is.Not.Null, t.Name + " has no [DefaultExecutionOrder]");
                return a.order;
            }
            Assert.That(Order(typeof(ChickenNameplate)), Is.GreaterThan(Order(typeof(RivalIndicator))));
        }

        [Test]
        public void UnderHudRival_WithoutAnArrow_KeepsItsNameplate_AndAStoppedHudLeavesNoStripZone()
        {
            string src = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/RivalIndicator.cs"));
            StringAssert.Contains("if (!chevron) _wasUnderHud = false;", src, "never a ring with neither nameplate nor arrow");
            string touch = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Input/TouchControlsController.cs"));
            StringAssert.Contains("private void OnDisable() => StatusRowCount = 0;", touch);
            StringAssert.Contains("if (!_bound) { StatusRowCount = 0; return; }", touch);
            string zones = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/HudControlZones.cs"));
            StringAssert.Contains("touch != null && touch.isActiveAndEnabled ? touch.StatusRowCount : 0", zones);
        }

        [Test]
        public void ChickenNameplate_HidesWhileTheRivalIndicatorSaysSo()
        {
            string src = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/ChickenNameplate.cs"));
            StringAssert.Contains("_rivalIndicator.HidesNameplate", src);
            StringAssert.Contains("public bool TryGetPlateViewport(Camera cam, out Rect viewport)", src);
            string touch = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Input/TouchControlsController.cs"));
            StringAssert.Contains("StatusRowCount = count;", touch);
            string zones = File.ReadAllText(Path.Combine(ProjectRoot(), "Assets/_Game/Scripts/Visuals/HudControlZones.cs"));
            StringAssert.Contains("statusRows == _cacheStatusRows", zones, "the zone cache is keyed on the strip's rows");
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
