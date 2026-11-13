using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Jump beats (D5, vfx.md 2.6): how many particles each beat earns and where the arc dots sit.
    /// Pure so the counts, the Reduced Motion variants and the match with the animator arc are testable from the shipped constants.
    /// </summary>
    public static class JumpBeatRules
    {
        /// <summary>A slot edge: the dash starting (Launch) or ending (Land).</summary>
        public enum Edge { None, Launch, Land }

        /// <summary>The edge of the Jump slot active flag between two observations.</summary>
        public static Edge SlotEdge(bool wasActive, bool isActive) =>
            isActive && !wasActive ? Edge.Launch : (!isActive && wasActive ? Edge.Land : Edge.None);

        /// <summary>True when a teleport-jump event arrives so soon after a slot edge that it is the same jump (one beat set per jump).</summary>
        public static bool EventPathDeduped(float now, float lastSlotEdgeTime) =>
            now - lastSlotEdgeTime < FeedbackTuning.JumpPathDedupeSeconds;

        /// <summary>
        /// Bystander landing shockwave from the shipped Jump (a dash: its slot edge, not JumpEventId, is the signal). One ring per landing
        /// edge; not under Reduced Motion (a world ring is emphasis on a move the player made), not while the bird is hidden/faded, and
        /// not when the teleport event path already armed a ring for this same jump.
        /// </summary>
        public static bool SlotLandingShockwave(Edge edge, bool reducedMotion, bool hidden, float now, float lastEventShockTime) =>
            edge == Edge.Land && !reducedMotion && !hidden && !EventPathDeduped(now, lastEventShockTime);

        /// <summary>
        /// Shockwave from the teleport-jump event path (JumpTier above None bumps JumpEventId): skipped under Reduced Motion, while
        /// hidden, or inside the dedupe window of a slot edge (the slot path owns that jump).
        /// </summary>
        public static bool EventShockwave(bool reducedMotion, bool hidden, float now, float lastSlotEdgeTime) =>
            !reducedMotion && !hidden && !EventPathDeduped(now, lastSlotEdgeTime);

        /// <summary>Launch dust puffs: 6, or 3 fading in place under Reduced Motion.</summary>
        public static int LaunchDustCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.JumpReducedDustCount : FeedbackTuning.JumpLaunchDustCount;

        /// <summary>Landing dust puffs: 8, or 3 fading in place under Reduced Motion.</summary>
        public static int LandingDustCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.JumpReducedDustCount : FeedbackTuning.JumpLandingDustCount;

        /// <summary>Feathers thrown up at the launch: none under Reduced Motion (they travel).</summary>
        public static int LaunchFeatherCount(bool reducedMotion) => reducedMotion ? 0 : FeedbackTuning.JumpLaunchFeatherCount;

        /// <summary>Arc dots along the chord: none under Reduced Motion.</summary>
        public static int ArcDotCount(bool reducedMotion) => reducedMotion ? 0 : FeedbackTuning.JumpArcDotCount;

        /// <summary>Travel fraction of dot <paramref name="index"/> of <paramref name="count"/>: 0.1 .. 0.9 evenly.</summary>
        public static float ArcSampleT(int index, int count) =>
            count <= 1 ? 0.5f : 0.1f + 0.8f * index / (count - 1);

        /// <summary>The arc height (above the ground chord) of dot <paramref name="index"/>: the animator half-sine lift.</summary>
        public static float ArcDotLift(int index, int count, float chordLength) =>
            ChickenAnimator.JumpArcLift(ArcSampleT(index, count), ChickenAnimator.JumpArcScale(chordLength));

        /// <summary>World position of dot <paramref name="index"/>: along the launch to landing chord, at the body-centre height plus the arc lift.</summary>
        public static Vector3 ArcDotPoint(Vector3 launch, Vector3 landing, int index, int count)
        {
            Vector3 flat = Planar.Flat(landing - launch);
            float t = ArcSampleT(index, count);
            Vector3 p = launch + flat * t;
            p.y = launch.y + FeedbackTuning.JumpArcDotBaseHeight + ArcDotLift(index, count, flat.magnitude);
            return p;
        }

        /// <summary>Does a jump of this planar chord earn arc dots at all (a proxy whose pivot already slid does not)?</summary>
        public static bool DrawsArc(float chordLength) => chordLength >= FeedbackTuning.JumpArcMinChord;
    }
}
