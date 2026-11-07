using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Steal beats (D7, vfx.md 2.1): the grab-line timing, where the food chunks fly and how they move, and the
    /// Reduced Motion variants. Pure so the timing, the routing and the pool behaviour are testable from the shipped constants.
    /// </summary>
    public static class StealBeatRules
    {
        // ---- Grab lines ---------------------------------------------------------------------------

        /// <summary>When victim <paramref name="index"/>'s line starts, after the cast edge.</summary>
        public static float GrabStart(int index) => Mathf.Max(0, index) * FeedbackTuning.StealGrabStaggerSeconds;

        /// <summary>Seconds one line lives: extend, then retract.</summary>
        public static float GrabLineSeconds => FeedbackTuning.StealGrabExtendSeconds + FeedbackTuning.StealGrabRetractSeconds;

        /// <summary>Seconds the whole grab lasts for <paramref name="victims"/> lines (a single flash under Reduced Motion).</summary>
        public static float GrabTotalSeconds(int victims, bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.StealGrabFlashSeconds : GrabStart(Mathf.Max(1, victims) - 1) + GrabLineSeconds;

        /// <summary>
        /// How far along the beak-to-victim span the line tip is, <paramref name="elapsed"/> seconds after the line started:
        /// 0 to <see cref="FeedbackTuning.StealGrabReach"/> over the extend time, back to 0 over the retract time. Reduced Motion
        /// holds the extended length for its flash (no travel). 0 outside the line's life.
        /// </summary>
        public static float GrabExtent(float elapsed, bool reducedMotion)
        {
            float reach = FeedbackTuning.StealGrabReach;
            if (reducedMotion) return elapsed >= 0f && elapsed < FeedbackTuning.StealGrabFlashSeconds ? reach : 0f;
            float ext = FeedbackTuning.StealGrabExtendSeconds;
            float ret = FeedbackTuning.StealGrabRetractSeconds;
            if (elapsed < 0f || elapsed >= ext + ret) return 0f;
            return elapsed < ext ? reach * (elapsed / ext) : reach * (1f - (elapsed - ext) / ret);
        }

        /// <summary>Line alpha: constant while the line lives, or a 0.15 s fade of the extended line under Reduced Motion.</summary>
        public static float GrabAlpha(float elapsed, bool reducedMotion)
        {
            if (reducedMotion)
            {
                if (elapsed < 0f || elapsed >= FeedbackTuning.StealGrabFlashSeconds) return 0f;
                return FeedbackTuning.StealGrabAlpha * (1f - elapsed / FeedbackTuning.StealGrabFlashSeconds);
            }
            return elapsed >= 0f && elapsed < GrabLineSeconds ? FeedbackTuning.StealGrabAlpha : 0f;
        }

        /// <summary>Does this victim get a line? Never toward a bird this peer must not see (that would give its position away).</summary>
        public static bool GrabLineApplies(bool victimQuiet) => !victimQuiet;

        // ---- Food chunks ----------------------------------------------------------------------------

        /// <summary>Where a robbery's kernels go.</summary>
        public enum ChunkRoute
        {
            /// <summary>To the thief (live position), arriving with sparkles.</summary>
            ToThief,
            /// <summary>The thief is hidden or faded: up above the victim and gone, never toward the thief.</summary>
            RiseAndVanish,
        }

        public static ChunkRoute Route(bool thiefQuiet) => thiefQuiet ? ChunkRoute.RiseAndVanish : ChunkRoute.ToThief;

        /// <summary>The chunk end point: the thief at chunk height, or the victim own ground position raised 1.2 m.</summary>
        public static Vector3 ChunkEnd(ChunkRoute route, Vector3 victimGround, Vector3 thiefGround) =>
            route == ChunkRoute.ToThief
                ? thiefGround + Vector3.up * FeedbackTuning.StealChunkHeight
                : victimGround + Vector3.up * FeedbackTuning.StealChunkHiddenRiseMetres;

        /// <summary>Ease-in-quad travel progress from the normalised flight time.</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t; }

        /// <summary>
        /// Chunk position at normalised flight time <paramref name="t"/>: the straight line start-to-end with an arc
        /// <see cref="FeedbackTuning.StealChunkArcHeight"/> high at the middle of the travel, plus a sideways swing so the
        /// three kernels of a robbery fan out.
        /// </summary>
        public static Vector3 ChunkPosition(Vector3 start, Vector3 end, float t, Vector3 lateral)
        {
            float e = Ease(t);
            float bump = 4f * e * (1f - e);
            return Vector3.Lerp(start, end, e) + Vector3.up * (FeedbackTuning.StealChunkArcHeight * bump) + lateral * bump;
        }

        /// <summary>Spin angle (degrees) after <paramref name="elapsed"/> seconds of flight.</summary>
        public static float SpinDegrees(float elapsed) => FeedbackTuning.StealChunkSpinDegreesPerSecond * elapsed;

        /// <summary>Seconds a chunk lives once it starts: the flight, or the short fade-in at the thief under Reduced Motion.</summary>
        public static float ChunkLifeSeconds(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.StealChunkReducedFadeSeconds : FeedbackTuning.StealChunkFlightSeconds;

        /// <summary>
        /// Chunk alpha at <paramref name="elapsed"/> seconds of its life. Full over the flight, except a vanishing chunk
        /// (hidden thief) fades over the last share of it. Reduced Motion fades in over the whole life instead.
        /// </summary>
        public static float ChunkAlpha(float elapsed, bool reducedMotion, ChunkRoute route)
        {
            float life = ChunkLifeSeconds(reducedMotion);
            float t = Mathf.Clamp01(elapsed / life);
            if (reducedMotion) return t;
            if (route == ChunkRoute.RiseAndVanish)
            {
                float f = FeedbackTuning.StealChunkVanishFraction;
                return t <= 1f - f ? 1f : Mathf.Clamp01((1f - t) / f);
            }
            return 1f;
        }

        /// <summary>Reduced Motion has no travel: the chunk sits at its end point (the thief, or the hidden-thief spot).</summary>
        public static bool ChunkTravels(bool reducedMotion) => !reducedMotion;

        /// <summary>Arrival sparkles at the thief (only on the ToThief route; the hidden route draws nothing at the thief).</summary>
        public static int ArrivalSparkles(ChunkRoute route, bool reducedMotion) =>
            route != ChunkRoute.ToThief ? 0
            : reducedMotion ? FeedbackTuning.StealArrivalSparkleCountReduced : FeedbackTuning.StealArrivalSparkleCount;

        // ---- Pool ---------------------------------------------------------------------------------------

        /// <summary>
        /// The slot to use in a pool whose <paramref name="ages"/> hold -1 for a free slot: the first free one, else the oldest
        /// live one (recycled, never grown). -1 only for an empty pool.
        /// </summary>
        public static int PickSlot(float[] ages)
        {
            if (ages == null || ages.Length == 0) return -1;
            int oldest = 0;
            for (int i = 0; i < ages.Length; i++)
            {
                if (ages[i] < 0f) return i;
                if (ages[i] > ages[oldest]) oldest = i;
            }
            return oldest;
        }

        /// <summary>Kernels a robbery of <paramref name="victims"/> rivals wants (3 each).</summary>
        public static int ChunksWanted(int victims) => Mathf.Max(0, victims) * FeedbackTuning.StealChunksPerVictim;
    }
}
