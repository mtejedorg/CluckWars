using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>Why an <see cref="AbilityZone"/>'s authority despawns it (WP3 despawn rules, after the B10 reset check).</summary>
    public enum ZoneEnd : byte
    {
        None = 0,
        Lifetime = 1,
        Consumed = 2,
        /// <summary>Caster zone: the owner no longer resolves on the authority, or was removed (B5).</summary>
        OwnerGone = 3,
        /// <summary>Caster zone that ends with its ability: the source slot stopped being active.</summary>
        AbilityEnded = 4,
    }

    /// <summary>
    /// The pure rules of an area effect (WP3): where its centre is, who it may affect, what "inside" means and when it
    /// ends. <see cref="AbilityZone"/> and <c>ChickenController.CheckAbilityZoneSlow</c> gather the facts and call these,
    /// so every branch is EditMode-testable without a runner (<c>AreaEffectTypeTests</c>).
    /// </summary>
    public static class AreaEffectRules
    {
        /// <summary><c>AbilityZone.SourceSlot</c> value for a zone that does not end with its ability (every Static AoE).</summary>
        public const byte NoSourceSlot = byte.MaxValue;

        /// <summary>
        /// The one centre every overlap and feedback test of a zone uses.
        /// <list type="bullet">
        /// <item><see cref="ZoneAnchor.World"/>: the stamped spawn position, else the transform (the authority before its
        /// own self-stamp) - exactly the pre-WP3 <c>AbilityZone.Center</c>.</item>
        /// <item><see cref="ZoneAnchor.Caster"/>: the owner's transform as THIS peer sees it; false while the owner is
        /// not available (late join, owner gone or removed). Legitimate, so silent: the zone is inert and hidden, and
        /// never stands at the prefab root.</item>
        /// </list>
        /// </summary>
        public static bool TryResolveCenter(ZoneAnchor anchor, bool hasStamp, Vector3 stamp, Vector3 transformPosition,
            bool ownerAvailable, Vector3 ownerPosition, out Vector3 center)
        {
            if (anchor == ZoneAnchor.Caster)
            {
                center = ownerAvailable ? ownerPosition : default;
                return ownerAvailable;
            }
            center = hasStamp ? stamp : transformPosition;
            return true;
        }

        /// <summary>
        /// Zones never affect their own caster, and <b>ignore Doppelganger decoys</b> (B7, decided 2026-10-21): no zone
        /// slows, roots or confirms a catch on a decoy, whichever channel or peer evaluates it.
        /// </summary>
        /// <remarks>
        /// <b>CW-232 "Doppelganger fidelity" (open ticket):</b> Maestro's later goal is a decoy that is a perfect copy of
        /// its caster. A perfect copy would have to be slowed, rooted and stunned VISIBLY, so this rule (and the decoy's
        /// early return in <c>ChickenController.FixedUpdateNetwork</c>, and the decoy skips in
        /// <c>AbilityZone.IsCaught</c>) is exactly what that ticket reverses. It is pinned by
        /// <c>AreaEffectTypeTests.ZonesIgnoreDecoys</c> so the reversal is a deliberate, visible change.
        /// </remarks>
        public static bool IgnoresChicken(bool isOwner, bool isDecoy) => isOwner || isDecoy;

        /// <summary>
        /// Planar (XZ) containment, the one "inside" for every zone channel: root (OnEnter), slow (WhileInside), the
        /// cast confirmation and the drawn ring agree on it.
        /// </summary>
        /// <remarks>
        /// <b>B7 (2026-10-21), a deliberate small reach change:</b> the slow zones and Feather Aura used a 3D distance
        /// until WP3, while root, aim and the ring were planar. On flat ground the two are identical; on the mound and
        /// ramps a chicken a little above or below the centre now counts as inside its drawn ring, where the 3D test
        /// left it out (the height difference was eating radius). No authored number moved.
        /// </remarks>
        public static bool IsInside(Vector3 center, float radius, Vector3 point)
        {
            float dx = center.x - point.x, dz = center.z - point.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>
        /// No overlapping auras (WP4 qa): a live Caster zone that ends with slot <paramref name="zoneSlot"/> of the same
        /// owner blocks a new cast from that slot. Needed because a running slot refuses a re-cast but the zone only
        /// notices its slot ended on its own next authority tick: a cooldown shorter than the aura (Relentless can take
        /// any cooldown to x0.25) could re-activate the slot first, and the old zone would then see "slot active" and
        /// live on to its lifetime backstop beside the new one. The refusal lasts at most that one tick.
        /// </summary>
        public static bool BlocksRecast(ZoneAnchor anchor, bool sameOwner, byte zoneSlot, int castSlot) =>
            anchor == ZoneAnchor.Caster && sameOwner && zoneSlot != NoSourceSlot && zoneSlot == castSlot;

        /// <summary>
        /// Whether, and why, the zone's authority despawns it this tick (after the B10 round-reset check, which runs
        /// first). Lifetime and consumption are every zone's; the owner and slot rules are a Caster zone's only.
        /// </summary>
        public static ZoneEnd Despawn(bool lifetimeExpired, bool consumed, ZoneAnchor anchor, bool ownerAvailable,
            byte sourceSlot, bool sourceSlotActive)
        {
            if (lifetimeExpired) return ZoneEnd.Lifetime;
            if (consumed) return ZoneEnd.Consumed;
            if (anchor != ZoneAnchor.Caster) return ZoneEnd.None;
            if (!ownerAvailable) return ZoneEnd.OwnerGone;
            if (sourceSlot != NoSourceSlot && !sourceSlotActive) return ZoneEnd.AbilityEnded;
            return ZoneEnd.None;
        }
    }
}
