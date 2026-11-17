using System.IO;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// K5r (owner 2026-11-17: the mark is integrated in the arrow at the floor, and only appears above the head inside the circle):
    /// the anchor rule with its hysteresis, the marked target's arrow decision, the emphasised arrow, and the badge placed
    /// beyond the arrow's tip. Radii and sizes come from <see cref="FloorArrowRules"/> and <see cref="FeedbackTuning"/>, never literals.
    /// </summary>
    public sealed class MarkOnArrowTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), rel)).Replace("\r\n", "\n");

        /// <summary>A ring radius that binds (below the preferred one), derived by the production rule.</summary>
        private static float Ring() => FloorArrowRules.RingRadius(6f, 3.5f, 5f);

        // ---- The anchor ----

        [Test]
        public void Anchor_InsideOverHead_OutsideOnArrow_WithHysteresis()
        {
            float r = Ring(), h = FeedbackTuning.MarkCircleHysteresisMetres;
            Assert.Less(r, FeedbackTuning.FloorArrowPreferredRadius, "the fixture ring binds, so it is not the preferred constant");
            Assert.AreEqual(MarkAnchor.OverHead, HuntRules.AnchorFor(r - 0.1f, r, false, true), "inside");
            Assert.AreEqual(MarkAnchor.OverHead, HuntRules.AnchorFor(r, r, false, true), "on the line counts as inside");
            Assert.AreEqual(MarkAnchor.OnArrow, HuntRules.AnchorFor(r + 0.01f, r, false, true), "just outside, coming from outside");
            Assert.AreEqual(MarkAnchor.OverHead, HuntRules.AnchorFor(r + h * 0.5f, r, true, true), "inside stays inside through the hysteresis band");
            Assert.AreEqual(MarkAnchor.OverHead, HuntRules.AnchorFor(r + h - 0.01f, r, true, true), "up to the band's far edge");
            Assert.AreEqual(MarkAnchor.OnArrow, HuntRules.AnchorFor(r + h + 0.01f, r, true, true), "past the band it goes out");
            Assert.AreEqual(MarkAnchor.OnArrow, HuntRules.AnchorFor(r + h * 0.5f, r, false, true), "outside stays outside inside the band");
        }

        [Test]
        public void Anchor_FallsBackToEdgeWithoutAnArrow()
        {
            float r = Ring();
            Assert.AreEqual(MarkAnchor.EdgeFallback, HuntRules.AnchorFor(r + 3f, r, false, false), "outside, floor ring painted none");
            Assert.AreEqual(MarkAnchor.OverHead, HuntRules.AnchorFor(r - 1f, r, false, false), "inside is over the head whatever the pool says");
        }

        [Test]
        public void Anchor_IsSingleValued_AndIdempotentInTheInsideFlag()
        {
            // The indicator keeps the hysteresis state (InsideCircle); the HUD feeds that result back into AnchorFor and must get the
            // same side of the line, so the arrow and the marker can never both show or both be missing.
            float r = Ring();
            for (float d = 0f; d <= r + 3f; d += 0.05f)
            {
                foreach (bool was in new[] { false, true })
                {
                    bool inside = HuntRules.InsideCircle(d, r, was);
                    var anchor = HuntRules.AnchorFor(d, r, inside, true);
                    Assert.AreEqual(inside ? MarkAnchor.OverHead : MarkAnchor.OnArrow, anchor, $"d={d} was={was}");
                    Assert.AreEqual(inside, HuntRules.InsideCircle(d, r, inside), $"stable d={d} was={was}");
                }
            }
        }

        // ---- The marked target's arrow ----

        [Test]
        public void MarkedTarget_OutsideGetsAForcedArrow_EvenOnScreen()
        {
            Assert.IsTrue(MarkedTargetArrowRules.OffersFloorArrow(true, true), "offered whether or not the bird is in view");
            Assert.AreEqual(MarkedArrowDecision.FloorArrow, MarkedTargetArrowRules.Decide(true, true, false, true), "on screen, accepted");
            Assert.AreEqual(MarkedArrowDecision.FloorArrow, MarkedTargetArrowRules.Decide(true, true, true, true), "off screen, accepted");
        }

        [Test]
        public void MarkedTarget_InsideTheCircle_HasNoArrow()
        {
            Assert.IsFalse(MarkedTargetArrowRules.OffersFloorArrow(true, false), "nothing is offered");
            Assert.AreEqual(MarkedArrowDecision.NoArrow, MarkedTargetArrowRules.Decide(true, false, true, false), "hidden under a zone: no chevron either");
            Assert.AreEqual(MarkedArrowDecision.Normal, MarkedTargetArrowRules.Decide(true, false, false, false), "in view: the ground ring as for any rival");
        }

        [Test]
        public void MarkedTarget_FloorDeclined_FallsBackToTheOrdinaryChevronRule()
        {
            Assert.AreEqual(MarkedArrowDecision.Normal, MarkedTargetArrowRules.Decide(true, true, true, false));
            Assert.AreEqual(MarkedArrowDecision.Normal, MarkedTargetArrowRules.Decide(true, true, false, false));
        }

        [Test]
        public void UnmarkedRivals_AreUnchanged()
        {
            foreach (bool outside in new[] { false, true })
                foreach (bool hidden in new[] { false, true })
                    foreach (bool accepted in new[] { false, true })
                        Assert.AreEqual(MarkedArrowDecision.Normal, MarkedTargetArrowRules.Decide(false, outside, hidden, accepted));
            Assert.IsFalse(MarkedTargetArrowRules.OffersFloorArrow(false, true), "an unmarked rival is never offered by this rule");
        }

        // ---- The emphasised arrow ----

        [Test]
        public void Emphasis_ScalesAndTintsOneArrowOnly()
        {
            Assert.AreEqual(1f, FloorArrowRules.EmphasisScale(false), "an ordinary arrow keeps its size");
            Assert.AreEqual(FeedbackTuning.MarkArrowScale, FloorArrowRules.EmphasisScale(true));
            Assert.Greater(FeedbackTuning.MarkArrowScale, 1f);

            string fra = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");
            // The flag is per offer (per pool slot), set by the one emphasised entry point only.
            StringAssert.Contains("return _instance.Accept(key, rivalPos, tint, opacity, false);", fra);
            StringAssert.Contains("return _instance.Accept(key, rivalPos, tint, opacity, true);", fra);
            StringAssert.Contains("_emphasised[i] = emphasised;", fra);
            StringAssert.Contains("FloorArrowRules.EmphasisScale(_emphasised[i])", fra);
            // No new renderer or material: the pooled meshes and the shared material only; nothing animates.
            StringAssert.DoesNotContain("new Material", fra);
            StringAssert.DoesNotContain("Shader.Find", fra);
            StringAssert.DoesNotContain("Time.time", fra);
            // The tint rides the existing change-gated colour write.
            StringAssert.Contains("if (tint != _shownTint[i]) WriteArrowColors(i, tint);", fra);
            // The marker's own colour is the arrow's tint.
            StringAssert.Contains("HuntRules.MarkerColour(o.MarkerMode, o.TargetCorner)", Read("Assets/_Game/Scripts/Visuals/MarkFeedback.cs"));
        }

        [Test]
        public void Emphasis_KeepsTheRingsSeparationFromOtherArrows()
        {
            float r = Ring();
            Assert.AreEqual(FloorArrowRules.MinSeparationDegrees(r), FloorArrowRules.MinSeparationDegrees(r, 1f), 1e-4f, "scale 1 is the ordinary value");
            float sep = FloorArrowRules.MinSeparationDegrees(r, FeedbackTuning.MarkArrowScale);
            Assert.Greater(sep, FloorArrowRules.MinSeparationDegrees(r));
            // The tails never touch: the arc between an emphasised tail and an ordinary one is at least the mean of their widths
            // (width = near length x ratio) times the ordinary spacing factor.
            float nearWidth = FeedbackTuning.FloorArrowNearLength * FeedbackTuning.FloorArrowWidthRatio;
            float meanWidth = nearWidth * 0.5f * (1f + FeedbackTuning.MarkArrowScale);
            Assert.GreaterOrEqual(FloorArrowRules.ArcLength(sep, r), meanWidth * FeedbackTuning.FloorArrowSpacing - 1e-3f);

            string fra = Read("Assets/_Game/Scripts/Visuals/FloorRivalArrows.cs");
            StringAssert.Contains("FloorArrowRules.MinSeparationDegrees(_radius, anyEmphasised ? FeedbackTuning.MarkArrowScale : 1f)", fra);
        }

        [Test]
        public void VictimsHunterArrow_IsUntouched_SourceGuard()
        {
            string ri = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            // The victim's forced hunter arrow: the plain, un-emphasised offer, no ring, exactly as before K5r.
            StringAssert.Contains("if (forcedHunter && !chevron)", ri);
            StringAssert.Contains("FloorRivalArrows.TryRepresent(GetInstanceID(), transform.position, tint, opacity);\n                HideChevron(false, tint, opacity);", ri);
            Assert.Less(ri.IndexOf("if (forcedHunter && !chevron)", System.StringComparison.Ordinal),
                        ri.IndexOf("else if (marked && ApplyMarkedArrow(", System.StringComparison.Ordinal), "the victim's branch is decided first");
            // The skull badge still sits on the arrow's midpoint, from the ordinary (un-scaled) painted length.
            string hud = Read("Assets/_Game/Scripts/UI/MarkHudController.cs");
            StringAssert.Contains("Vector3 mid = tail + dir * (length * 0.5f);", hud);
            Assert.AreEqual(1f, FloorArrowRules.EmphasisScale(false), "TryGetArrow's length for the hunter's arrow is unscaled");
        }

        [Test]
        public void TheMarker_HasExactlyOnePlace_SourceGuard()
        {
            string hud = Read("Assets/_Game/Scripts/UI/MarkHudController.cs");
            StringAssert.Contains("if (_anchor == MarkAnchor.OnArrow)", hud);
            StringAssert.Contains("else if (MarkerPlacement.Place(", hud);
            StringAssert.Contains("public static MarkAnchor AnchorNow", hud);
            StringAssert.Contains("UiElements.SetShown(_pointer, HuntRules.PointerShown(_anchor));", hud);
            string ri = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("public bool MarkInsideCircle", ri);
            StringAssert.Contains("HuntRules.InsideCircle(distance, FloorRivalArrows.RingRadiusFor(cam), _markInside)", ri);
        }

        // ---- K5r-b: the anchor owns the scale and the pointer, in every mode ----

        private static readonly MarkMarkerMode[] Modes = { MarkMarkerMode.Hunt, MarkMarkerMode.Kill, MarkMarkerMode.Walled, MarkMarkerMode.Shelled };

        [Test]
        public void MarkerScale_IsTheAnchorsBaseTimesEveryAnimation_InEveryMode()
        {
            Assert.AreEqual(FeedbackTuning.MarkMarkerOnArrowScale, HuntRules.AnchorBaseScale(MarkAnchor.OnArrow));
            Assert.AreEqual(1f, HuntRules.AnchorBaseScale(MarkAnchor.OverHead));
            Assert.AreEqual(1f, HuntRules.AnchorBaseScale(MarkAnchor.EdgeFallback));
            foreach (var mode in Modes)
                foreach (bool reduced in new[] { false, true })
                    for (float t = 0f; t < 1f; t += 0.07f)
                    {
                        float pulse = HuntRules.KillPulseFactor(mode, reduced, t);
                        foreach (float pop in new[] { FeedbackTuning.MarkMarkerPopFrom, 1f })
                            foreach (float live in new[] { 1f, FeedbackTuning.MarkLivePopFrom })
                            {
                                float arrow = HuntRules.MarkerScale(MarkAnchor.OnArrow, pop, live, pulse);
                                float head = HuntRules.MarkerScale(MarkAnchor.OverHead, pop, live, pulse);
                                Assert.AreEqual(head * FeedbackTuning.MarkMarkerOnArrowScale, arrow, 1e-5f, $"{mode} reduced={reduced} t={t} pop={pop} live={live}");
                                Assert.Less(arrow, head);
                            }
                        if (mode != MarkMarkerMode.Kill || reduced) Assert.AreEqual(1f, pulse, "only KILL pulses, never under Reduced Motion");
                        else Assert.That(pulse, Is.InRange(1f, 1f + FeedbackTuning.MarkMarkerKillPulse + 1e-5f));
                    }
            // Settled (pop, live pop done): the badge is exactly the base scale, whatever the mode.
            foreach (var mode in Modes)
                Assert.AreEqual(FeedbackTuning.MarkMarkerOnArrowScale, HuntRules.MarkerScale(MarkAnchor.OnArrow, 1f, 1f, HuntRules.KillPulseFactor(mode, true, 0f)), 1e-6f);
        }

        [Test]
        public void Pointer_IsAFunctionOfTheAnchorOnly()
        {
            Assert.IsFalse(HuntRules.PointerShown(MarkAnchor.OnArrow));
            Assert.IsTrue(HuntRules.PointerShown(MarkAnchor.OverHead));
            Assert.IsTrue(HuntRules.PointerShown(MarkAnchor.EdgeFallback));
            // The controller takes no other input for it: the only write is the anchor's.
            string hud = Read("Assets/_Game/Scripts/UI/MarkHudController.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(hud, @"SetShown\(_pointer,").Count, "one pointer write");
            StringAssert.DoesNotContain("_marker.style.scale", hud, "the box is never scaled: only the body, so the chip stays outside any scale");
            StringAssert.Contains("_body.style.scale", hud);
        }

        [Test]
        public void Chip_OnTheArrow_StaysAtTheHudFontFloor_AndInsideTheBox()
        {
            // What UiProbe measures: the stylesheet's font size times the PARENT's world scale (the element's own scale is juice).
            // The chip's parent is the marker box, which is never scaled; its own scale on the arrow is 1.
            float font = float.Parse(System.Text.RegularExpressions.Regex.Match(
                Read("Assets/UI/Styles/MarkHud.uss"), @"\.cw-mark-distance\s*\{[^}]*?font-size:\s*([0-9.]+)px").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            float floor = CluckWars.Diagnostics.UiLayoutRules.FontFloor(CluckWars.Diagnostics.UiScope.Hud);
            Assert.AreEqual(1f, MarkerPlacement.ChipScale(true, FeedbackTuning.MarkMarkerOnArrowScale), "full size on the arrow");
            Assert.GreaterOrEqual(font * 1f, floor, "the resolved chip font on the arrow (parent scale 1)");
            Assert.GreaterOrEqual(font * MarkerPlacement.ChipScale(false, FeedbackTuning.MarkMarkerPopFrom), floor, "and at the appear pop elsewhere");
            // Placement: a fixed offset on the arrow, the stylesheet's own scaled elsewhere.
            Assert.AreEqual(-FeedbackTuning.MarkOnArrowChipOffsetPx, MarkerPlacement.ChipOffsetPx(true, true, FeedbackTuning.MarkMarkerOnArrowScale));
            Assert.AreEqual(FeedbackTuning.MarkOnArrowChipOffsetPx, MarkerPlacement.ChipOffsetPx(true, false, FeedbackTuning.MarkMarkerOnArrowScale));
            Assert.AreEqual(MarkerPlacement.ChipBaseBelowCentrePx, MarkerPlacement.ChipOffsetPx(false, false, 1f));
            // The uxml puts the chip beside the scaled body, not inside it.
            string uxml = Read("Assets/UI/MarkHud.uxml");
            int bodyOpen = uxml.IndexOf("name=\"MarkBody\"", System.StringComparison.Ordinal);
            int chip = uxml.IndexOf("name=\"MarkDistance\"", System.StringComparison.Ordinal);
            int star = uxml.IndexOf("name=\"MarkStarGlyph\"", System.StringComparison.Ordinal);
            Assert.Greater(chip, star, "the chip comes after the last body child");
            Assert.Greater(uxml.IndexOf("</ui:VisualElement>", star, System.StringComparison.Ordinal), -1);
            Assert.Greater(bodyOpen, 0);
        }

        [Test]
        public void HiddenMarkedTarget_KeepsItsArrow_WithoutARing_SourceGuard()
        {
            string ri = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("forcedHunter || MarkFeedback.IsMarkedByLocal(_controller) ? 1f", ri);
            StringAssert.Contains("bool concealed = _controller.VisualOpacity < MinVisibleOpacity;", ri);
        }

        // ---- The badge on the arrow ----

        private static readonly Vector2 Panel = new Vector2(1920f, 1080f);

        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, -1f)]
        [TestCase(0f, 1f)]
        [TestCase(0.7f, -0.7f)]
        public void OnArrow_BadgeSitsBeyondTheTip_OnTheOutwardSide(float ox, float oy)
        {
            var tip = new Vector2(960f, 540f);
            var outward = new Vector2(ox, oy).normalized;
            Vector2 c = MarkerPlacement.OnArrowCentre(tip, outward, Panel, false, default);
            float beyond = Vector2.Dot(c - tip, outward);
            Assert.AreEqual(FeedbackTuning.MarkOnArrowGapPx + MarkerPlacement.OnArrowRadiusPx, beyond, 0.01f, "the gap past the tip, then the badge");
            Assert.AreEqual(0f, Vector2.Dot(c - tip, new Vector2(-outward.y, outward.x)), 0.01f, "on the arrow's own axis");
        }

        [Test]
        public void OnArrow_NeverCoversTheHuntersOwnPlate()
        {
            // The "You" plate sits right where an arrow pointing up from the bird would put its badge.
            var tip = new Vector2(960f, 420f);
            var outward = new Vector2(0f, -1f);   // up the screen (panel y is down)
            var plate = Rect.MinMaxRect(860f, 330f, 1060f, 380f);
            Vector2 c = MarkerPlacement.OnArrowCentre(tip, outward, Panel, true, plate);
            float r = MarkerPlacement.OnArrowRadiusPx, m = FeedbackTuning.MarkMarkerYouPlateMarginPx;
            var badge = Rect.MinMaxRect(c.x - r, c.y - r, c.x + r, c.y + r);
            Assert.IsFalse(badge.Overlaps(Rect.MinMaxRect(plate.xMin - m, plate.yMin - m, plate.xMax + m, plate.yMax + m)), "off the plate");
            Assert.Less(c.y, tip.y, "still on the outward side of the tip");
        }

        [Test]
        public void OnArrow_StaysWholeOnThePanel()
        {
            float r = MarkerPlacement.OnArrowRadiusPx;
            Vector2 c = MarkerPlacement.OnArrowCentre(new Vector2(Panel.x - 2f, 4f), new Vector2(1f, -1f), Panel, false, default);
            Assert.LessOrEqual(c.x, Panel.x - r + 1e-3f);
            Assert.GreaterOrEqual(c.y, r - 1e-3f);
        }

        /// <summary>The chip's height as the stylesheet declares it (.cw-mark-distance height), never a literal.</summary>
        private static float ChipHeightFromUss()
        {
            string uss = Read("Assets/UI/Styles/MarkHud.uss");
            int rule = uss.IndexOf(".cw-mark-distance {", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(rule, 0, "the chip rule exists");
            string body = uss.Substring(rule, uss.IndexOf('}', rule) - rule);
            var m = System.Text.RegularExpressions.Regex.Match(body, @"[^-]height:\s*([0-9.]+)px");
            Assert.IsTrue(m.Success, "the chip rule declares a height");
            return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        [TestCase(2133f, 960f)]
        [TestCase(960f, 2133f)]
        public void OnArrow_BadgeAndChip_StayInsideThePanel_AtEveryEdgeAndCorner(float w, float h)
        {
            var panel = new Vector2(w, h);
            float chipH = ChipHeightFromUss();
            Assert.AreEqual(chipH, 2f * MarkerPlacement.ChipHalfHeightPx, 1.5f, "the placement's chip extent is the stylesheet's");
            float r = MarkerPlacement.OnArrowRadiusPx;
            float[] xs = { 0f, w * 0.5f, w }, ys = { 0f, h * 0.5f, h };
            foreach (float x in xs)
            foreach (float y in ys)
            {
                if (x == w * 0.5f && y == h * 0.5f) continue;
                var tip = new Vector2(x, y);
                foreach (bool plateOn in new[] { false, true })
                foreach (var outward in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right, new Vector2(1f, 1f), new Vector2(-1f, -1f) })
                {
                    var plate = Rect.MinMaxRect(w * 0.5f - 60f, h * 0.5f - 20f, w * 0.5f + 60f, h * 0.5f + 20f);
                    Vector2 c0 = MarkerPlacement.OnArrowCentre(tip, outward, panel, plateOn, plate);
                    Vector2 c = MarkerPlacement.SettleOnArrow(c0, outward, panel, plateOn, plate, out bool above);
                    string what = $"tip {tip} outward {outward} plate {plateOn} -> {c}, chip {(above ? "above" : "below")}";
                    var badge = Rect.MinMaxRect(c.x - r, c.y - r, c.x + r, c.y + r);
                    float cy = c.y + (above ? -1f : 1f) * FeedbackTuning.MarkOnArrowChipOffsetPx;
                    var chip = Rect.MinMaxRect(c.x - MarkerPlacement.ChipHalfWidthPx, cy - chipH * 0.5f, c.x + MarkerPlacement.ChipHalfWidthPx, cy + chipH * 0.5f);
                    Assert.IsTrue(badge.xMin >= 0f && badge.yMin >= 0f && badge.xMax <= w && badge.yMax <= h, "badge inside: " + what);
                    Assert.IsTrue(chip.xMin >= 0f && chip.yMin >= 0f && chip.xMax <= w && chip.yMax <= h, "chip inside: " + what);
                }
            }
        }

        [Test]
        public void OnArrow_TheLoggedCase_ArrowPointingDownNearTheBottom_ChipDoesNotLeaveTheBottom()
        {
            var panel = new Vector2(2133f, 960f);
            var tip = new Vector2(1260f, 890f);   // the 175412 run: the chip was 946..987 on a 960 panel
            Vector2 c0 = MarkerPlacement.OnArrowCentre(tip, Vector2.down, panel, false, default);
            Vector2 c = MarkerPlacement.SettleOnArrow(c0, Vector2.down, panel, false, default, out bool above);
            float cy = c.y + (above ? -1f : 1f) * FeedbackTuning.MarkOnArrowChipOffsetPx;
            Assert.LessOrEqual(cy + MarkerPlacement.ChipHalfHeightPx, panel.y);
            Assert.GreaterOrEqual(cy - MarkerPlacement.ChipHalfHeightPx, 0f);
        }

        [Test]
        public void OnArrow_ChipTakesTheSideAwayFromTheBird_AndNeverTheEdgeOrThePlate()
        {
            var mid = new Vector2(960f, 540f);
            Assert.IsTrue(MarkerPlacement.OnArrowChipAbove(mid, new Vector2(0f, -1f), Panel, false, default), "arrow up the screen: chip above");
            Assert.IsFalse(MarkerPlacement.OnArrowChipAbove(mid, new Vector2(0f, 1f), Panel, false, default), "arrow down: chip below");
            Assert.IsFalse(MarkerPlacement.OnArrowChipAbove(new Vector2(960f, 40f), new Vector2(0f, -1f), Panel, false, default), "above would leave the panel");
            Assert.IsTrue(MarkerPlacement.OnArrowChipAbove(new Vector2(960f, Panel.y - 40f), new Vector2(0f, 1f), Panel, false, default), "below would leave the panel");
            // A "You" plate just above the badge: the chip goes to the other side.
            var plate = Rect.MinMaxRect(880f, 420f, 1040f, 470f);
            Assert.IsFalse(MarkerPlacement.OnArrowChipAbove(mid, new Vector2(0f, -1f), Panel, true, plate), "the plate is where the chip would sit");
        }
    }
}
