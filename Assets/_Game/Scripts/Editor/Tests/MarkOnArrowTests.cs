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
            StringAssert.Contains("UiElements.SetShown(_pointer, !onArrow);", hud);
            string ri = Read("Assets/_Game/Scripts/Visuals/RivalIndicator.cs");
            StringAssert.Contains("public bool MarkInsideCircle", ri);
            StringAssert.Contains("HuntRules.InsideCircle(distance, FloorRivalArrows.RingRadiusFor(cam), _markInside)", ri);
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
