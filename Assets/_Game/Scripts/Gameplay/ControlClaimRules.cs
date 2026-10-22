using System.Collections.Generic;
using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>The three timed control effects a chicken can send another chicken's authority.</summary>
    public enum ControlKind : byte
    {
        None = 0,
        Stun = 1,   // ChickenController.RPC_ApplyStun
        Root = 2,   // ChickenController.RPC_ApplyRoot
        Slow = 3,   // ChickenController.RPC_ApplyAbilitySlow
    }

    /// <summary>
    /// The control effect an ability sends to its victims, as authored: kind, duration (before the
    /// victim's passive shortens it) and, for a slow, the speed multiplier. Declared by the ability
    /// (<see cref="AbilityBaseSO.EmittedControl"/>), read by <see cref="ControlClaimRules"/>.
    /// </summary>
    public readonly struct ControlEmission
    {
        public static readonly ControlEmission None = default;

        public readonly ControlKind Kind;
        public readonly float Seconds;
        public readonly float SlowFactor;

        public ControlEmission(ControlKind kind, float seconds, float slowFactor = 1f)
        {
            Kind = kind;
            Seconds = seconds;
            SlowFactor = slowFactor;
        }

        public static ControlEmission Stun(float seconds) => new ControlEmission(ControlKind.Stun, seconds);
        public static ControlEmission Root(float seconds) => new ControlEmission(ControlKind.Root, seconds);
        public static ControlEmission Slow(float seconds, float factor) => new ControlEmission(ControlKind.Slow, seconds, factor);
    }

    /// <summary>
    /// Receiver-side rules for <c>ChickenController.RPC_ApplyStun</c>, <c>RPC_ApplyRoot</c> and
    /// <c>RPC_ApplyAbilitySlow</c> (F4, decisions 2026-10-22). Pure and Fusion-free, like
    /// <see cref="ExecuteRules"/> and <see cref="StealRules"/>: the controller gathers the facts and
    /// this class decides, so EditMode can exercise every branch.
    /// </summary>
    /// <remarks>
    /// All three RPCs are <c>RpcSources.All</c>; before F4 they took neither a sender nor a source,
    /// so any peer could stun, root or slow any chicken for any time. Every call now names the
    /// casting chicken, and a claim is honoured only when ALL of these hold:
    /// <list type="bullet">
    /// <item>the sender speaks for that chicken (local invoke: it is simulated here; off the wire:
    /// the sender is its state authority, which for a bot is the master) and it is neither the
    /// victim nor a Doppelganger decoy;</item>
    /// <item>the duration (and, for a slow, the factor) is inside the ceiling the shipped ability
    /// pool can produce for that effect (<see cref="CeilingFor"/>);</item>
    /// <item>the caster is within the longest reach of any ability in the pool that produces the
    /// effect, plus drift and slack — or, for a root only, the victim stands inside a Root Egg zone
    /// owned by the caster (a placed egg acts while its owner is elsewhere).</item>
    /// </list>
    /// <para>
    /// <b>Pool-wide bounds, not the caster's own loadout.</b> Same choice as
    /// <see cref="StealRules.MaxSingleSteal"/>: the victim's peer does not know a remote chicken's
    /// slots (they are set in <c>onBeforeSpawned</c> on the spawning peer only and never
    /// replicated), and class legality changes for balance reasons, so the bound is the worst case
    /// across the whole registry. A forged call from across the arena, a 10 s stun or a 0.1 slow
    /// is still refused.
    /// </para>
    /// <para>
    /// <b>Trust model.</b> As CW-230 (<see cref="ExecuteRules.HasExecuteEvidence"/>): Shared Mode
    /// trusts each state authority over its own objects, so a peer can still send in-bounds
    /// control from its own chicken without having pressed anything. What is closed is control
    /// from peers that do not own the named caster, and control no shipped ability could produce.
    /// </para>
    /// </remarks>
    public static class ControlClaimRules
    {
        /// <summary>Extra planar slack on the range check, on top of the drift margin. The execute's slack, for the same reasons.</summary>
        public const float RangeSlackMeters = ExecuteRules.RangeSlackMeters;

        /// <summary>Tolerance on the authored duration and factor ceilings (float round trip through the wire).</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>The largest effect of one kind the pool can produce, and the furthest it can reach.</summary>
        public readonly struct Ceiling
        {
            /// <summary>The pool was available. False skips the magnitude and range checks (the caller logs it as an Error).</summary>
            public readonly bool Known;
            /// <summary>Longest authored duration. 0 when nothing in the pool produces the effect: every claim is then refused.</summary>
            public readonly float MaxSeconds;
            /// <summary>Strongest authored slow factor (smallest multiplier); 1 for stun/root.</summary>
            public readonly float MinSlowFactor;
            /// <summary>Furthest a producing ability's caster can be from its victim when the effect is sent.</summary>
            public readonly float MaxReach;
            /// <summary>Root only: the largest radius a zone-placing root ability authors (Root Egg's EggRadius). 0 = no zone can vouch.</summary>
            public readonly float MaxZoneRadius;
            /// <summary>Root only: how far ahead of its caster such a zone is placed (Root Egg: 0, at the feet).</summary>
            public readonly float MaxZonePlacement;
            /// <summary>Root only: the longest such a zone lives (Root Egg's EggLifetime). 0 = no zone can vouch.</summary>
            public readonly float MaxZoneLifetime;

            public Ceiling(bool known, float maxSeconds, float minSlowFactor, float maxReach,
                float maxZoneRadius = 0f, float maxZonePlacement = 0f, float maxZoneLifetime = 0f)
            {
                Known = known;
                MaxSeconds = maxSeconds;
                MinSlowFactor = minSlowFactor;
                MaxReach = maxReach;
                MaxZoneRadius = maxZoneRadius;
                MaxZonePlacement = maxZonePlacement;
                MaxZoneLifetime = maxZoneLifetime;
            }

            public static readonly Ceiling Unknown = new Ceiling(false, 0f, 1f, 0f);
        }

        /// <summary>
        /// The ceiling for <paramref name="kind"/> over <paramref name="pool"/> (the registry's
        /// <c>All</c>). <see cref="Ceiling.Unknown"/> for a null pool.
        /// </summary>
        public static Ceiling CeilingFor(IEnumerable<AbilityBaseSO> pool, ControlKind kind)
        {
            if (pool == null) return Ceiling.Unknown;

            float maxSeconds = 0f, minFactor = 1f, maxReach = 0f, zoneRadius = 0f, zonePlacement = 0f, zoneLifetime = 0f;
            foreach (var ability in pool)
            {
                if (ability == null) continue;
                var e = ability.EmittedControl;
                if (e.Kind != kind || !(e.Seconds > 0f)) continue;
                maxSeconds = Mathf.Max(maxSeconds, e.Seconds);
                if (kind == ControlKind.Slow) minFactor = Mathf.Min(minFactor, e.SlowFactor);
                maxReach = Mathf.Max(maxReach, Reach(ability));
                if (kind == ControlKind.Root && ability.PlacesZone)
                {
                    // The zone's footprint as the ability authors it (Root Egg: ForwardCircle, EggRadius, offset 0).
                    zoneRadius = Mathf.Max(zoneRadius, ability.AimRadius);
                    zonePlacement = Mathf.Max(zonePlacement, ability.AimForwardOffset);
                    // How long such a zone can live, from the ability's own area spec (WP3). A zone ability that
                    // declares no spec contributes 0 and so can never vouch: refused, not trusted forever.
                    if (ability.TryGetAreaEffect(out var area) && area.Effect == ZoneEffect.Root)
                        zoneLifetime = Mathf.Max(zoneLifetime, area.Lifetime);
                }
            }
            if (kind == ControlKind.Slow && maxReach > 0f) maxReach += SlowReachHeadroomMeters;
            return new Ceiling(true, maxSeconds, minFactor, maxReach, zoneRadius, zonePlacement, zoneLifetime);
        }

        // ---- Root-zone evidence (security review 2026-10-22, hardened in WP3) ---------------
        //
        // A zone is replicated state its own authority wrote, so the victim must not take its geometry on
        // faith: an attacker-authored zone could claim any radius or stand anywhere. The victim accepts a zone
        // as evidence only when (1) the zone's state authority is the claimed caster's, (2) its owner and
        // authority are still the ones this peer snapshotted when it first saw the zone, (3) this peer resolved
        // the caster at that moment, (4) its centre is within the authored placement of where the caster stood
        // then, (5) the zone is not older, measured locally, than the pool's longest zone lifetime plus the
        // latency cap, and (6) its radius is clamped to the largest radius the pool authors.
        //
        // Trade-off of (3), accepted: a peer that first sees a zone before it can resolve the caster (a caster
        // still in flight) gets no evidence from that zone, so a root it sends from out of direct reach is refused
        // there once and logged ("rejected RPC_ApplyRoot ... OutOfRange"). The old fallback anchored on the
        // caster's CURRENT position plus drift over the zone's whole age, which an attacker could stretch across
        // most of the arena over an 8 s egg - against every peer.
        //
        // Late joiner (qa W1): a peer that joined after the egg was placed first sees it up to a lifetime late,
        // so where the caster stands at that moment is not where it laid the egg. Only for a zone first seen
        // within JoinWindowSeconds of this peer's own join (a local, unforgeable fact), the caster may have
        // drifted over the zone's claimed age = MaxZoneLifetime - the networked timer's remaining time, clamped
        // to [0, MaxZoneLifetime]. The age is attacker-writable, so it is charged twice: the drift it buys widens
        // the centre allowance, and the same age is spent from the vouching window (rule 5 counts claimed age +
        // local age). A peer that was already in the match never gets the extension.

        /// <summary>What the victim's peer knows about one Root zone (live, or recorded at its despawn).</summary>
        public struct RootZoneEvidence
        {
            /// <summary>The zone's state authority equals the claimed caster's.</summary>
            public bool SameAuthorityAsCaster;
            /// <summary>The zone's owner or state authority changed after this peer snapshotted them: the evidence is void.</summary>
            public bool IdentityChanged;
            public Vector3 Center;
            /// <summary>The zone's own (replicated, untrusted) radius; clamped before use.</summary>
            public float Radius;
            /// <summary>The caster was resolvable when this peer first saw the zone (its Spawned here). False = no evidence.</summary>
            public bool HasCasterAtSpawn;
            public Vector3 CasterAtSpawn;
            /// <summary>How far the caster may have moved between placing the zone and this peer seeing it.</summary>
            public float SpawnDrift;
            /// <summary>Real seconds since this peer first saw the zone (local clock, never replicated).</summary>
            public float SecondsSinceSeen;
            /// <summary>This peer first saw the zone within <see cref="JoinWindowSeconds"/> of its own join (local fact).</summary>
            public bool FirstSeenAtJoin;
            /// <summary>The zone's networked lifetime timer remaining when this peer first saw it (attacker-writable; NaN/negative = unknown).</summary>
            public float RemainingAtFirstSight;
            /// <summary>The caster's drift speed (m/s) when this peer first saw the zone; prices the late-joiner extension.</summary>
            public float CasterDriftSpeed;
        }

        /// <summary>How soon after this peer's own join a zone counts as "already there when I arrived" (qa W1).</summary>
        public const float JoinWindowSeconds = 2f;

        /// <summary>
        /// Seconds the zone had already lived when this peer first saw it, as far as this peer may believe: 0 unless it
        /// was first seen at this peer's join; then <c>MaxZoneLifetime - RemainingAtFirstSight</c>, clamped to
        /// [0, MaxZoneLifetime]. An unknown remaining time (NaN, negative) claims the whole lifetime; such a zone is
        /// also refused outright by <see cref="IsCredibleZoneCenter"/> (fail closed).
        /// <para>Residual, accepted: a zone authority can claim a near-dead egg to widen the centre allowance, but only
        /// against a peer that joined within <see cref="JoinWindowSeconds"/>, and the claimed age is spent from the same
        /// vouching window (at most MaxZoneLifetime + latency in total).</para>
        /// </summary>
        public static float AgeAtFirstSight(in RootZoneEvidence z, in Ceiling c)
        {
            if (!z.FirstSeenAtJoin || !(c.MaxZoneLifetime > 0f)) return 0f;
            float remaining = z.RemainingAtFirstSight >= 0f ? z.RemainingAtFirstSight : 0f;   // NaN reads as 0
            return Mathf.Clamp(c.MaxZoneLifetime - remaining, 0f, c.MaxZoneLifetime);
        }

        /// <summary>The evidence identity check of one Render (qa W2): what to do with the snapshot.</summary>
        public enum IdentityObservation : byte
        {
            Unchanged = 0,
            /// <summary>The snapshot had no authority yet (None when first seen) and it is valid now: capture it, once.</summary>
            CaptureAuthority = 1,
            /// <summary>The owner, or an already captured authority, differs: the evidence is void for good.</summary>
            Changed = 2,
        }

        /// <summary>
        /// Owner and authority are snapshotted once. A stored authority of None means "not captured yet", never
        /// "changed": it is captured on the first frame the zone has a valid one. Until then the evidence cannot vouch
        /// (None never equals the caster's authority).
        /// </summary>
        public static IdentityObservation ObserveIdentity(bool sameOwner, bool snapshotAuthorityKnown, bool authorityKnown, bool sameAuthority)
        {
            if (!sameOwner) return IdentityObservation.Changed;
            if (!snapshotAuthorityKnown) return authorityKnown ? IdentityObservation.CaptureAuthority : IdentityObservation.Unchanged;
            return sameAuthority ? IdentityObservation.Unchanged : IdentityObservation.Changed;
        }

        /// <summary>
        /// The zone centre is where the caster could have placed it: within the pool's authored placement
        /// (+ <see cref="RangeSlackMeters"/>) of the caster's position when this peer first saw the zone, plus the
        /// drift over that observation delay. False when the caster was not resolved then: there is no anchor
        /// to check against, so the zone is not evidence.
        /// </summary>
        public static bool IsCredibleZoneCenter(in RootZoneEvidence z, in Ceiling c)
        {
            if (!z.HasCasterAtSpawn) return false;
            // Seen at join with no readable timer: its age is unknown, so no drift can be priced - not evidence.
            if (z.FirstSeenAtJoin && !(z.RemainingAtFirstSight >= 0f)) return false;
            float allowance = Mathf.Max(0f, z.SpawnDrift) + Mathf.Max(0f, z.CasterDriftSpeed) * AgeAtFirstSight(z, c);
            return IsInsideZone(z.CasterAtSpawn, Mathf.Max(0f, c.MaxZonePlacement) + RangeSlackMeters + allowance, z.Center, 0f);
        }

        /// <summary>
        /// The zone is still inside its authored life as this peer counts it: its age at first sight
        /// (<see cref="AgeAtFirstSight"/>, 0 unless first seen at this peer's join) plus the local seconds since, at
        /// most the pool's longest zone lifetime plus <see cref="ExecuteRules.MaxLatencySeconds"/>. NaN and negative
        /// ages fail.
        /// </summary>
        public static bool IsWithinZoneLifetime(in RootZoneEvidence z, in Ceiling c) =>
            c.MaxZoneLifetime > 0f && z.SecondsSinceSeen >= 0f &&
            AgeAtFirstSight(z, c) + z.SecondsSinceSeen <= c.MaxZoneLifetime + ExecuteRules.MaxLatencySeconds;

        /// <summary>
        /// True when <paramref name="z"/> may vouch for a root on a victim at <paramref name="victim"/>: same and
        /// unchanged authority/owner, a caster anchor seen at spawn, a credible centre, inside its authored life, and
        /// the victim inside the radius clamped to <see cref="Ceiling.MaxZoneRadius"/> (+ <paramref name="margin"/>).
        /// False with no known ceiling.
        /// </summary>
        public static bool RootZoneVouches(in RootZoneEvidence z, Vector3 victim, float margin, in Ceiling c)
        {
            if (!c.Known || !(c.MaxZoneRadius > 0f) || !z.SameAuthorityAsCaster || z.IdentityChanged) return false;
            if (!IsWithinZoneLifetime(z, c)) return false;
            if (!IsCredibleZoneCenter(z, c)) return false;
            float radius = z.Radius > 0f ? Mathf.Min(z.Radius, c.MaxZoneRadius) : 0f;   // NaN reads as 0
            return IsInsideZone(z.Center, radius, victim, margin);
        }

        /// <summary>
        /// Extra reach on the timed-slow bound only (qa W3, 2026-10-22). Dust Kick is the only slow emitter and the
        /// Stun/Root bounds get incidental headroom from a longer-reaching emitter (Belly Flop's jump), the slow bound
        /// none: a kick at the very edge of its 4.5 m rear arc sat on the bound. Its caster is a FLEEING Speedy,
        /// often under Speed Burst (x1.5 on 12.15 m/s), and the drift margin reads <c>ChickenCargo.DriftSpeed</c> =
        /// base MoveSpeed + knockback, which cannot see an authority-local speed buff on a remote caster. The buff's
        /// extra 6.1 m/s over the floored latency plus two ticks (0.15 + 0.0625 s) is 1.3 m; 1.5 m covers it.
        /// Not added to stun or root, and no other bound moves.
        /// </summary>
        public const float SlowReachHeadroomMeters = 1.5f;

        /// <summary>
        /// How far apart <paramref name="ability"/>'s caster and victim can be when its control
        /// effect is sent: the aim shape's total forward reach, plus the longest jump of its tier
        /// for an ability that jumps.
        /// </summary>
        /// <remarks>
        /// The jump allowance is added for EVERY jumping ability, not only the ones that resolve
        /// before their jump (<see cref="StealRules.Reach"/>'s rule). Belly Flop stuns at its
        /// landing, which is right where the caster is on its own peer, but the victim's peer may
        /// still hold the take-off pose when the RPC lands (an RPC can overtake the state of the
        /// tick it was sent in). A conservative superset, like every bound here.
        /// </remarks>
        public static float Reach(AbilityBaseSO ability)
        {
            if (ability == null) return 0f;
            float shape = Mathf.Max(0f, ability.AimRadius) + Mathf.Max(0f, ability.AimForwardOffset);
            return shape + (ability.JumpTier != JumpLengthTier.None ? StealRules.MaxJumpTravel(ability.JumpTier) : 0f);
        }

        /// <summary>Furthest a caster may be from its victim: the pool's reach, the slack and the drift margin.</summary>
        public static float MaxDistance(float maxReach, float driftMargin) =>
            Mathf.Max(0f, maxReach) + RangeSlackMeters + Mathf.Max(0f, driftMargin);

        /// <summary>True for a finite duration in (0, ceiling]. With an unknown ceiling only (0, +inf) is required.</summary>
        public static bool IsPlausibleSeconds(float seconds, in Ceiling ceiling) =>
            seconds > 0f && !float.IsInfinity(seconds) &&
            (!ceiling.Known || seconds <= ceiling.MaxSeconds + Epsilon);

        /// <summary>True for a slow factor in [strongest authored, 1). With an unknown ceiling only (0, 1) is required.</summary>
        public static bool IsPlausibleSlowFactor(float factor, in Ceiling ceiling) =>
            factor > 0f && factor < 1f &&
            (!ceiling.Known || factor >= ceiling.MinSlowFactor - Epsilon);

        /// <summary>Planar (XZ) containment of <paramref name="point"/> in a zone of <paramref name="radius"/>, plus <paramref name="margin"/>.</summary>
        public static bool IsInsideZone(Vector3 center, float radius, Vector3 point, float margin)
        {
            float limit = Mathf.Max(0f, radius) + Mathf.Max(0f, margin);
            float dx = center.x - point.x, dz = center.z - point.z;
            return dx * dx + dz * dz <= limit * limit;
        }

        public enum Verdict
        {
            Accepted,
            ImplausibleDuration,
            ImplausibleFactor,
            CasterUnresolved,
            SelfApplied,
            DecoyCaster,
            NotAttributable,
            OutOfRange,
        }

        /// <summary>The facts the victim's authority can establish about a control claim.</summary>
        public struct Claim
        {
            public ControlKind Kind;
            public float Seconds;
            /// <summary>Slow only; ignored for stun and root.</summary>
            public float SlowFactor;
            public bool CasterResolved;
            public bool CasterIsVictim;
            public bool CasterIsDecoy;
            /// <summary>Local invoke: the caster is simulated here. Off the wire: the sender is its state authority.</summary>
            public bool SenderSpeaksForCaster;
            public float PlanarDistance;
            /// <summary>Drift of both chickens over the sender's latency plus two ticks.</summary>
            public float RangeMargin;
            /// <summary>Root only: the victim stands inside a live (or just despawned) Root Egg owned by the caster.</summary>
            public bool InsideCastersRootZone;
        }

        /// <summary>Decides a control claim against the pool's ceiling for its kind.</summary>
        public static Verdict Evaluate(in Claim c, in Ceiling ceiling)
        {
            if (!IsPlausibleSeconds(c.Seconds, ceiling)) return Verdict.ImplausibleDuration;
            if (c.Kind == ControlKind.Slow && !IsPlausibleSlowFactor(c.SlowFactor, ceiling)) return Verdict.ImplausibleFactor;
            if (!c.CasterResolved) return Verdict.CasterUnresolved;
            if (c.CasterIsVictim) return Verdict.SelfApplied;
            if (c.CasterIsDecoy) return Verdict.DecoyCaster;
            if (!c.SenderSpeaksForCaster) return Verdict.NotAttributable;
            if (!ceiling.Known) return Verdict.Accepted;   // no honest range bound (logged once as an Error at spawn)
            bool inReach = c.PlanarDistance <= MaxDistance(ceiling.MaxReach, c.RangeMargin);
            bool inOwnZone = c.Kind == ControlKind.Root && c.InsideCastersRootZone;
            if (!inReach && !inOwnZone) return Verdict.OutOfRange;
            return Verdict.Accepted;
        }

        public static string Describe(Verdict v, ControlKind kind, in Ceiling ceiling)
        {
            switch (v)
            {
                case Verdict.ImplausibleDuration:
                    return ceiling.Known
                        ? $"the duration is not in (0, {ceiling.MaxSeconds:0.00}] s, the longest {kind} any ability in the pool applies."
                        : "the duration is not a positive finite number.";
                case Verdict.ImplausibleFactor:
                    return ceiling.Known
                        ? $"the slow factor is not in [{ceiling.MinSlowFactor:0.00}, 1), the strongest slow any ability in the pool applies."
                        : "the slow factor is not in (0, 1).";
                case Verdict.CasterUnresolved: return "no live chicken resolves from the caster id.";
                case Verdict.SelfApplied:      return "the named caster is this chicken.";
                case Verdict.DecoyCaster:      return "the named caster is a Doppelganger decoy, which casts nothing.";
                case Verdict.NotAttributable:  return "the sender is not the state authority of the named caster.";
                case Verdict.OutOfRange:
                    return $"the caster is further than any {kind} in the pool reaches ({ceiling.MaxReach:0.00} m + slack)" +
                           (kind == ControlKind.Root ? " and this chicken is not inside a Root Egg it owns." : ".");
                default: return "accepted.";
            }
        }
    }
}
