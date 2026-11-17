namespace CluckWars.Visuals
{
    /// <summary>What a rival's indicator does about its arrow this frame (K5r).</summary>
    public enum MarkedArrowDecision : byte
    {
        /// <summary>The ordinary rule: this rival is not the local hunter's mark, or the floor ring declined it (the edge chevron
        /// or the ground ring follows as for any rival).</summary>
        Normal = 0,
        /// <summary>The marked target is represented by its emphasised floor arrow: no edge chevron; its ground ring only while
        /// the bird is in view.</summary>
        FloorArrow = 1,
        /// <summary>The marked target is inside the floor circle and out of the clear viewport: no arrow of any kind.</summary>
        NoArrow = 2,
    }

    /// <summary>
    /// The local hunter's marked target and its arrow, as a pure rule (owner 2026-11-17: the mark rides the arrow at the floor,
    /// over the head only inside the circle). <see cref="RivalIndicator"/> offers the arrow to the floor ring first (forced, even
    /// while the bird is in view) and passes the verdict in.
    /// </summary>
    public static class MarkedTargetArrowRules
    {
        /// <param name="marked">Is this rival the local Assassin's mark, with the hunt marker showing?</param>
        /// <param name="outsideCircle">Beyond the floor circle round the hunter's bird (with the hysteresis, <see cref="HuntRules.InsideCircle"/>).</param>
        /// <param name="hiddenFromClearViewport">The rival's own edge-chevron verdict: off the frame, or mostly under a HUD zone.</param>
        /// <param name="floorAccepted">The floor ring took the offer (only meaningful, and only offered, when marked and outside).</param>
        public static MarkedArrowDecision Decide(bool marked, bool outsideCircle, bool hiddenFromClearViewport, bool floorAccepted)
        {
            if (!marked) return MarkedArrowDecision.Normal;
            if (!outsideCircle) return hiddenFromClearViewport ? MarkedArrowDecision.NoArrow : MarkedArrowDecision.Normal;
            return floorAccepted ? MarkedArrowDecision.FloorArrow : MarkedArrowDecision.Normal;
        }

        /// <summary>Whether the caller must offer the arrow to the floor ring: the marked target outside the circle, on screen or not.</summary>
        public static bool OffersFloorArrow(bool marked, bool outsideCircle) => marked && outsideCircle;
    }
}
