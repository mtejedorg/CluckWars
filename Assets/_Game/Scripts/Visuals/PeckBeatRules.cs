using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure rules of the Peck chip (D11, vfx.md 2.7): when the beat plays (only when the pile's replicated <c>Amount</c> dropped),
    /// which bird it belongs to, the kernel launch velocity and the mound squash curve. Pure so each rule is testable from the
    /// shipped constants without a Fusion runner.
    /// </summary>
    public static class PeckBeatRules
    {
        /// <summary>Smallest Amount drop that counts as a bite (the Peck bite is 3; steal drains and float noise stay below).</summary>
        public const float MinDrop = 0.01f;

        /// <summary>True when <paramref name="amount"/> fell below the last observed amount. NaN last = first observation (baseline).</summary>
        public static bool AmountDropped(float lastAmount, float amount) =>
            !float.IsNaN(lastAmount) && amount < lastAmount - MinDrop;

        /// <summary>
        /// The pecker a drop belongs to: the nearest candidate whose Peck window is open, or -1 (no pecker: the beat does not play,
        /// an empty-pile press shows only the existing refusal). Lists are parallel; <paramref name="windowOpen"/> is the replicated
        /// "Peck slot active" state of each candidate in reach of the pile.
        /// </summary>
        public static int PickPecker(IReadOnlyList<float> distance, IReadOnlyList<bool> windowOpen)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < distance.Count && i < windowOpen.Count; i++)
            {
                if (!windowOpen[i] || distance[i] >= bestD) continue;
                bestD = distance[i];
                best = i;
            }
            return best;
        }

        /// <summary>True from the landing stamp until <see cref="FeedbackTuning.PeckLandedWindowSeconds"/> later (never before the stamp).</summary>
        public static bool LandedRecently(float now, float landedAt) =>
            now >= landedAt && now - landedAt <= FeedbackTuning.PeckLandedWindowSeconds;

        /// <summary>Animator speed that makes one play of a <paramref name="clipSeconds"/> clip last <paramref name="castSeconds"/>; 1 when either is unknown.</summary>
        public static float ClipSpeed(float clipSeconds, float castSeconds) =>
            clipSeconds > 0f && castSeconds > 0f ? clipSeconds / castSeconds : 1f;

        public static int KernelCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.PeckKernelCountReduced : FeedbackTuning.PeckKernelCount;

        public static int PuffCount(bool reducedMotion) =>
            reducedMotion ? FeedbackTuning.PeckPuffCountReduced : FeedbackTuning.PeckPuffCount;

        /// <summary>The beak: <see cref="FeedbackTuning.PeckBeakDistance"/> ahead of the pecker (planar forward), <see cref="FeedbackTuning.PeckBeakHeight"/>
        /// above the grass under it (the chicken root is the capsule mid-height, so the height is measured from <paramref name="groundY"/>).</summary>
        public static Vector3 BeakPoint(Vector3 peckerPosition, Vector3 peckerForward, float groundY)
        {
            peckerForward = Planar.Flat(peckerForward);
            if (peckerForward.sqrMagnitude < 1e-6f) peckerForward = Vector3.forward;
            Vector3 p = peckerPosition + peckerForward.normalized * FeedbackTuning.PeckBeakDistance;
            p.y = groundY + FeedbackTuning.PeckBeakHeight;
            return p;
        }

        /// <summary>
        /// Launch velocity of a kernel from <paramref name="start"/> to <paramref name="beak"/>: straight toward the beak at
        /// <see cref="FeedbackTuning.PeckKernelSpeed"/>, plus the lift that cancels the system's gravity over the flight so it
        /// arrives where the beak is. Zero under Reduced Motion (the kernel is emitted at the beak and only fades).
        /// </summary>
        public static Vector3 KernelVelocity(Vector3 start, Vector3 beak, bool reducedMotion)
        {
            if (reducedMotion) return Vector3.zero;
            float life = FeedbackTuning.PeckKernelSeconds;
            Vector3 dir = beak - start;
            Vector3 v = dir.sqrMagnitude > 1e-6f ? dir.normalized * FeedbackTuning.PeckKernelSpeed : Vector3.zero;
            v.y += 0.5f * FeedbackTuning.PeckKernelGravity * 9.81f * life;
            return v;
        }

        /// <summary>Where a kernel starts: the mound top (Reduced Motion: at the beak).</summary>
        public static Vector3 KernelStart(Vector3 moundTop, Vector3 beak, bool reducedMotion) => reducedMotion ? beak : moundTop;

        /// <summary>Mound Y-scale factor <paramref name="elapsed"/> seconds after the bite: x0.94 easing back to 1 over 0.12 s. Always 1 under Reduced Motion.</summary>
        public static float SquashFactor(float elapsed, bool reducedMotion)
        {
            if (reducedMotion || elapsed < 0f || elapsed >= FeedbackTuning.PeckSquashSeconds) return 1f;
            float t = elapsed / FeedbackTuning.PeckSquashSeconds;
            return Mathf.Lerp(FeedbackTuning.PeckSquashScaleY, 1f, t);
        }
    }
}
