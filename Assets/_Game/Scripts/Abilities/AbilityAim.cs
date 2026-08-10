using UnityEngine;

namespace CluckWars.Abilities
{
    /// <summary>
    /// The shape an ability's area occupies, shared by every consumer that needs to
    /// agree on it: the hold-to-aim preview decal, the target threat-overlay, the
    /// usability gate, and the impact flash (FEEDBACK.md §4). One enum, one source
    /// of truth — see <see cref="AbilityAim"/> for the geometry and
    /// <see cref="AbilityBaseSO.WouldAffect"/> for the full targeting predicate.
    /// </summary>
    public enum AbilityAimShape : byte
    {
        None          = 0, // self-buff, no area          (Speed Burst, Turtle, Invisibility, Spine Coat, Egg Shell, Doppelganger)
        SelfCircle    = 1, // circle centred on caster    (Cluck Shock, Ambush)
        ForwardCircle = 2, // circle at forward offset    (Feather Trap, Root Egg)
        Aura          = 3, // persistent circle, follows  (Feather Aura)
        Cone          = 4, // forward arc                 (Wing Slam at 120°, Peck at 140°)
        Jump          = 5, // teleport arc + landing ring (Shadowstep)
        SingleTarget  = 6, // nearest valid in range      (Mark Kill, Sneaky Steal)
        Capsule       = 7, // swept lane along forward    (Roll Push, Flying Peck)
    }

    /// <summary>
    /// Pure, MonoBehaviour-free geometry for every <see cref="AbilityAimShape"/>.
    /// Follows the same shape as <c>FoodPileMath</c> in <c>FoodPile.cs</c> — no Unity
    /// lifecycle, no physics queries, so it is EditMode-testable without a scene.
    /// </summary>
    /// <remarks>
    /// All distance tests are planar (XZ). The arena is flat; ignoring Y here fixes a
    /// latent bug where the old <c>Physics.OverlapSphere</c> / <c>sqrMagnitude</c>
    /// checks were 3D and could silently exclude a target standing on a ramp or a
    /// jump-cleared obstacle.
    /// </remarks>
    public static class AbilityAim
    {
        /// <summary>
        /// Is <paramref name="candidatePos"/> inside the area described by
        /// <paramref name="shape"/>? Geometry only — no aliveness, side, or
        /// per-ability filtering; see <see cref="AbilityBaseSO.WouldAffect"/> for the
        /// full predicate that layers those on top.
        /// </summary>
        public static bool InShape(AbilityAimShape shape, Vector3 casterPos, Vector3 casterForward,
                                    Vector3 candidatePos, float radius, float forwardOffset, float coneAngleDeg)
        {
            if (shape == AbilityAimShape.None) return false;
            if (radius < 0f) return false;

            Vector3 forwardXZ = PlanarForward(casterForward);

            switch (shape)
            {
                case AbilityAimShape.SelfCircle:
                case AbilityAimShape.Aura:
                case AbilityAimShape.SingleTarget:
                    return PlanarDistance(casterPos, candidatePos) <= radius;

                case AbilityAimShape.ForwardCircle:
                case AbilityAimShape.Jump:
                {
                    Vector3 center = casterPos + forwardXZ * forwardOffset;
                    return PlanarDistance(center, candidatePos) <= radius;
                }

                case AbilityAimShape.Capsule:
                {
                    // A swept lane: every point within `radius` of the segment running from
                    // the caster to `forwardOffset` metres ahead. Both caps are round, so
                    // the near cap reaches slightly BEHIND the caster — that is the whole
                    // point of the shape and the reason Roll Push stops whiffing on someone
                    // standing on your toes, where a ForwardCircle at the same reach leaves
                    // a hole at point-blank range.
                    //
                    // A negative offset clamps to 0 rather than mirroring: a backward
                    // capsule is never what an author meant, and silently drawing one would
                    // put the preview behind the player.
                    float length = Mathf.Max(0f, forwardOffset);

                    // length == 0 collapses the segment to the caster's own position, so
                    // this degenerates EXACTLY to SelfCircle — the direct analogue of the
                    // Cone-at-360° safety net above, and locked by the same style of test.
                    float t = Mathf.Clamp(Vector3.Dot(FlattenXZ(candidatePos - casterPos), forwardXZ), 0f, length);
                    Vector3 closestOnAxis = casterPos + forwardXZ * t;
                    return PlanarDistance(closestOnAxis, candidatePos) <= radius;
                }

                case AbilityAimShape.Cone:
                {
                    if (PlanarDistance(casterPos, candidatePos) > radius) return false;
                    // >= 360 degenerates exactly to SelfCircle (Ambush relies on this:
                    // it inherits Cone from StunBurstAbilitySO but never narrows the angle).
                    if (coneAngleDeg >= 360f) return true;

                    Vector3 toCandidate = FlattenXZ(candidatePos - casterPos);
                    // Candidate exactly at the caster's position has no direction to
                    // measure an angle against — treat it as inside rather than NaN-ing
                    // out of Vector3.Angle.
                    if (toCandidate.sqrMagnitude < 0.0001f) return true;

                    float angle = Vector3.Angle(forwardXZ, toCandidate);
                    return angle <= coneAngleDeg * 0.5f;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// World-space centre of the shape — the decal's centre AND the distance-sort
        /// origin <see cref="AbilityBaseSO.GatherTargets"/> ranks candidates against, so one
        /// definition serves both. Caster position for every shape except the ones that
        /// extend along forward.
        /// </summary>
        public static Vector3 ShapeCenter(AbilityAimShape shape, Vector3 casterPos, Vector3 casterForward, float forwardOffset)
        {
            switch (shape)
            {
                case AbilityAimShape.ForwardCircle:
                case AbilityAimShape.Jump:
                    return casterPos + PlanarForward(casterForward) * forwardOffset;

                // The capsule's axis runs from the caster to forwardOffset ahead, so its
                // centre is the axis midpoint — not the far end. Clamped identically to
                // InShape so the drawn centre and the tested shape can never disagree.
                case AbilityAimShape.Capsule:
                    return casterPos + PlanarForward(casterForward) * (Mathf.Max(0f, forwardOffset) * 0.5f);

                default:
                    return casterPos;
            }
        }

        /// <summary>XZ-flattened, normalised forward. Falls back to world forward for a zero-length input so callers never divide by zero.</summary>
        private static Vector3 PlanarForward(Vector3 forward)
        {
            Vector3 flat = FlattenXZ(forward);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        private static Vector3 FlattenXZ(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
