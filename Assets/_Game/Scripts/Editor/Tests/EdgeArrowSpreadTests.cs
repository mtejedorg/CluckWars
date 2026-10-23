using System.Collections.Generic;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-022 / CW-008 (Maestro: spread stacked edge arrows = yes). The status strip IS an avoid zone since WP7 (C6 overrode
    /// CW-009); these fixtures use the control zones alone, and Phase8ChunkDRivalArrowTests sweeps the info zones. Two rivals
    /// pushed off the same screen edge by the same HUD control zone used to clamp to the SAME point and read as one arrow.
    /// <see cref="EdgeArrowSpread"/> is the pure pass that gives each its own slot, inside the band, outside every zone.
    /// </summary>
    public class EdgeArrowSpreadTests
    {
        private static readonly Rect Band = FeedbackTuning.RivalChevronSafeArea;
        private const float Eps = 1e-4f;
        private static readonly float[] Aspects = { 2400f / 1080f, 16f / 9f, 4f / 3f, 21f / 9f };

        private static Vector2 Clearance(float aspect)
        {
            float r = FeedbackTuning.RivalChevronSizeFraction * FeedbackTuning.RivalChevronClearanceRadius;
            return new Vector2(r / aspect, r);
        }

        private static Vector2 MinSep(float aspect)
        {
            float s = FeedbackTuning.RivalChevronSizeFraction * FeedbackTuning.RivalChevronSpacing;
            return new Vector2(s / aspect, s);
        }

        private static List<Rect> Zones(HudDeviceMode mode, float aspect)
        {
            var list = new List<Rect>();
            HudControlZones.Build(list, mode, aspect, Vector4.zero);
            return list;
        }

        /// <summary>The OLD placement for one off-screen rival: clamp to the band, slide out of the zones, nothing else.</summary>
        private static Vector2 OldPlace(Vector2 targetViewport, IReadOnlyList<Rect> zones, float aspect)
        {
            ScreenEdgeProjection.ClampToEdge(targetViewport, false, Band, zones, Clearance(aspect), out var p, out _);
            return p;
        }

        private static bool OnPerimeter(Vector2 p) =>
            Mathf.Abs(p.x - Band.xMin) < Eps || Mathf.Abs(p.x - Band.xMax) < Eps ||
            Mathf.Abs(p.y - Band.yMin) < Eps || Mathf.Abs(p.y - Band.yMax) < Eps;

        // Two rivals whose edge points sit under the same HUD zone, so the slide ends on the SAME boundary point (measured at
        // 2400x1080): touch = both far off the left edge, behind the joystick; desktop = both below the screen, behind the hex strip.
        private static Vector2[] StackedTargets(HudDeviceMode mode) => mode == HudDeviceMode.Touch
            ? new[] { new Vector2(-0.30f, 0.20f), new Vector2(-0.30f, 0.30f) }
            : new[] { new Vector2(0.45f, -0.30f), new Vector2(0.50f, -0.30f) };

        [TestCase(HudDeviceMode.Touch)]
        [TestCase(HudDeviceMode.KeyboardMouse)]
        public void OldBehaviour_TwoRivalsBehindTheSameZone_StackOnOnePoint(HudDeviceMode mode)
        {
            const float aspect = 2400f / 1080f;
            var zones = Zones(mode, aspect);
            var t = StackedTargets(mode);
            var a = OldPlace(t[0], zones, aspect);
            var b = OldPlace(t[1], zones, aspect);

            Assert.That(ScreenEdgeProjection.IsInsideAnyZone(a, zones, Clearance(aspect)), Is.False, "sanity: the slide left the zone");
            Assert.That(EdgeArrowSpread.AnyOverlap(new[] { a, b }, MinSep(aspect)), Is.True,
                $"the scenario must reproduce the bug: both arrows at {a} / {b}");
        }

        [Test]
        public void Spread_SeparatesTheStackedArrows_AndKeepsThemInTheBandAndOutOfTheZones()
        {
            foreach (var mode in new[] { HudDeviceMode.Touch, HudDeviceMode.KeyboardMouse })
            foreach (float aspect in Aspects)
            {
                var zones = Zones(mode, aspect);
                var t = StackedTargets(mode);
                var desired = new[] { OldPlace(t[0], zones, aspect), OldPlace(t[1], zones, aspect) };
                var result = new Vector2[2];
                EdgeArrowSpread.Spread(desired, Band, zones, Clearance(aspect), MinSep(aspect), result);

                string ctx = $"{mode} aspect {aspect:0.00}";
                Assert.That(EdgeArrowSpread.AnyOverlap(result, MinSep(aspect)), Is.False, ctx + ": arrows still overlap");
                foreach (var p in result)
                {
                    Assert.That(OnPerimeter(p), Is.True, ctx + $": {p} left the edge band");
                    Assert.That(ScreenEdgeProjection.IsInsideAnyZone(p, zones, Clearance(aspect)), Is.False, ctx + $": {p} is under a HUD control");
                }
            }
        }

        [Test]
        public void Spread_LoneArrow_IsNeverMoved()
        {
            var zones = Zones(HudDeviceMode.Touch, 16f / 9f);
            var desired = new[] { new Vector2(0.5f, 0.955f) };
            var result = new Vector2[1];
            EdgeArrowSpread.Spread(desired, Band, zones, Clearance(16f / 9f), MinSep(16f / 9f), result);
            Assert.That(result[0], Is.EqualTo(desired[0]));
        }

        [Test]
        public void Spread_ArrowsAlreadyApart_AreNotMoved()
        {
            const float aspect = 16f / 9f;
            var zones = Zones(HudDeviceMode.KeyboardMouse, aspect);
            var desired = new[] { new Vector2(0.2f, Band.yMax), new Vector2(0.6f, Band.yMax), new Vector2(Band.xMax, 0.5f) };
            var result = new Vector2[3];
            EdgeArrowSpread.Spread(desired, Band, zones, Clearance(aspect), MinSep(aspect), result);
            for (int i = 0; i < 3; i++) Assert.That(result[i], Is.EqualTo(desired[i]), $"arrow {i}");
        }

        [Test]
        public void Spread_ThreeStackedArrows_AllGetTheirOwnSlot()
        {
            const float aspect = 2400f / 1080f;
            var zones = Zones(HudDeviceMode.Touch, aspect);
            var p = OldPlace(StackedTargets(HudDeviceMode.Touch)[0], zones, aspect);
            var desired = new[] { p, p, p };
            var result = new Vector2[3];
            EdgeArrowSpread.Spread(desired, Band, zones, Clearance(aspect), MinSep(aspect), result);

            Assert.That(EdgeArrowSpread.AnyOverlap(result, MinSep(aspect)), Is.False);
            foreach (var r in result) Assert.That(ScreenEdgeProjection.IsInsideAnyZone(r, zones, Clearance(aspect)), Is.False);
        }

        [Test]
        public void Spread_IsDeterministic_AndIndependentOfInputOrder()
        {
            const float aspect = 2400f / 1080f;
            var zones = Zones(HudDeviceMode.Touch, aspect);
            var p = OldPlace(StackedTargets(HudDeviceMode.Touch)[0], zones, aspect);
            var q = new Vector2(p.x, p.y + 0.01f);   // distinct perimeter parameter, so the order is decided by position, not index

            var r1 = new Vector2[2];
            var r1b = new Vector2[2];
            var r2 = new Vector2[2];
            EdgeArrowSpread.Spread(new[] { p, q }, Band, zones, Clearance(aspect), MinSep(aspect), r1);
            EdgeArrowSpread.Spread(new[] { p, q }, Band, zones, Clearance(aspect), MinSep(aspect), r1b);
            EdgeArrowSpread.Spread(new[] { q, p }, Band, zones, Clearance(aspect), MinSep(aspect), r2);

            Assert.That(r1b, Is.EqualTo(r1), "same input, same output");
            Assert.That(r2[1], Is.EqualTo(r1[0]), "p lands in the same place whichever order the rivals were queued");
            Assert.That(r2[0], Is.EqualTo(r1[1]), "q lands in the same place whichever order the rivals were queued");
        }

        [Test]
        public void StatusStrip_IsAnInfoZone_OnlyWhileItHasRows_NeverAControlZone()
        {
            // Control zones are still only controls. Touch = joystick + 4 hexes, desktop = the 4-hex strip.
            Assert.That(Zones(HudDeviceMode.Touch, 16f / 9f).Count, Is.EqualTo(5));
            Assert.That(Zones(HudDeviceMode.KeyboardMouse, 16f / 9f).Count, Is.EqualTo(4));

            // WP7 / C6 reversed CW-009: the status strip is an INFO zone (chevrons avoid it) while it shows rows.
            var info = new List<Rect>();
            HudControlZones.BuildInfoZones(info, HudDeviceMode.Touch, 16f / 9f, Vector4.zero, 0);
            Assert.That(info.Count, Is.EqualTo(1), "no rows: the top band only");
            HudControlZones.BuildInfoZones(info, HudDeviceMode.Touch, 16f / 9f, Vector4.zero, 2);
            Assert.That(info.Count, Is.EqualTo(2), "rows showing: the strip is a zone too");
        }

        [Test]
        public void RivalIndicator_FlushIsRobust_QueueCleanedInFinally_AndOnDisableDequeues()
        {
            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Assets/_Game/Scripts/Visuals/RivalIndicator.cs"));
            int flush = src.IndexOf("private static void FlushSpread()", System.StringComparison.Ordinal);
            string body = src.Substring(flush, src.IndexOf("private void OnDestroy()", flush, System.StringComparison.Ordinal) - flush);
            StringAssert.Contains("finally", body);
            StringAssert.Contains("if (cam == null) return;", body);
            StringAssert.Contains("SpreadQueue.Remove(this);", src);
            string spread = System.IO.File.ReadAllText(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Assets/_Game/Scripts/Visuals/EdgeArrowSpread.cs"));
            StringAssert.Contains("var order = _order;", spread);
            StringAssert.Contains("var placed = _placed;", spread);
        }
    }
}
