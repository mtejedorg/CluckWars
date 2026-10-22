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

            public Ceiling(bool known, float maxSeconds, float minSlowFactor, float maxReach)
            {
                Known = known;
                MaxSeconds = maxSeconds;
                MinSlowFactor = minSlowFactor;
                MaxReach = maxReach;
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

            float maxSeconds = 0f, minFactor = 1f, maxReach = 0f;
            foreach (var ability in pool)
            {
                if (ability == null) continue;
                var e = ability.EmittedControl;
                if (e.Kind != kind || !(e.Seconds > 0f)) continue;
                maxSeconds = Mathf.Max(maxSeconds, e.Seconds);
                if (kind == ControlKind.Slow) minFactor = Mathf.Min(minFactor, e.SlowFactor);
                maxReach = Mathf.Max(maxReach, Reach(ability));
            }
            return new Ceiling(true, maxSeconds, minFactor, maxReach);
        }

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
