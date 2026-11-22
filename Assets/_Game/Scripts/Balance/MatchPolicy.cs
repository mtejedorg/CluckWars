using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>Which rival a policy goes for when it hunts. <see cref="Profile"/> keeps the game bot's own rule.</summary>
    public enum TargetRule : byte { Profile, Nearest, Richest, Leader }

    /// <summary>The knobs of one seat's policy. A float left <c>NaN</c> means "use the bot profile's value".</summary>
    public struct PolicyParams
    {
        /// <summary>Seconds from a think to its intent taking effect.</summary>
        public float ReactionDelay;
        public float ReturnThreshold;
        public float FleeRadius;
        public float HuntDetour;
        public TargetRule Target;
        public FarmRoute Route;

        public static PolicyParams Default => new PolicyParams
        {
            ReactionDelay = 0.3f,
            ReturnThreshold = float.NaN,
            FleeRadius = float.NaN,
            HuntDetour = float.NaN,
            Target = TargetRule.Profile,
            Route = FarmRoute.Nearest,
        };
    }

    /// <summary>
    /// What a policy asks of its chicken. The chicken walks to <see cref="Goal"/> (stopping at the edge of its own base zone when the
    /// goal is its own base); with <see cref="PeckPile"/> set it walks to that pile instead and pecks it from inside its reach whenever
    /// the Peck is ready. <see cref="CastSlot"/> and <see cref="CastTarget"/> ask for an ability on a rival seat (slice S3). -1 means none.
    /// </summary>
    public struct MatchIntent
    {
        public Vector2 Goal;
        public int PeckPile;
        public int CastSlot;
        public int CastTarget;

        public static MatchIntent Idle(Vector2 at) => new MatchIntent { Goal = at, PeckPile = -1, CastSlot = -1, CastTarget = -1 };

        public bool SameAs(in MatchIntent o) =>
            Goal == o.Goal && PeckPile == o.PeckPile && CastSlot == o.CastSlot && CastTarget == o.CastTarget;
    }

    /// <summary>What a policy knows about one seat. For a rival it holds what the policy may see (slice S3 narrows it through the bot sight rule).</summary>
    public struct SeatView
    {
        public bool Present;
        public bool Seen;
        public Vector2 Pos;
        /// <summary>Food in the beak.</summary>
        public float Cargo;
        /// <summary>Food in the Spoiler's bounty bag.</summary>
        public float Bag;
        public float Banked;
        public bool Pecking;
        public bool Stunned;
        public bool Removed;
        public bool Shelled;
        public bool Hidden;
        /// <summary>Seconds until each slot is ready. Slot 0 is the Peck; slice S2 adds the four ability slots after it.</summary>
        public float[] CooldownLeft;
    }

    /// <summary>The snapshot a policy thinks on. One instance per match, refreshed every tick; a policy must not keep it.</summary>
    public sealed class MatchView
    {
        public int Self;
        public float Time;
        public float TimeLeft;
        public SeatView[] Seats;
        /// <summary>Food left in each of the nine piles.</summary>
        public float[] PileFood;
        public MatchKit[] Kits;
        public OracleMapSO Map;
        public OracleNav Nav;
        public MatchRules Rules;
    }

    public interface IMatchPolicy
    {
        MatchIntent Think(MatchView view, ref MatchRng rng);
    }

    public delegate IMatchPolicy PolicyFactory(int seat, PolicyParams p);
}
