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
        /// <summary>
        /// A random extra delay per intent, uniform from 0 to this many seconds (rounded to ticks), drawn from the seat's own stream after the think.
        /// Zero (the default of <see cref="Default"/>) draws nothing. Without it a match of fixed policies is deterministic, so replicates of one
        /// cell would repeat each other and a mirror would be won by the same seat every time (spec 5.1: "a small reaction jitter").
        /// </summary>
        public float ReactionJitter;
        public float ReturnThreshold;
        public float FleeRadius;
        public float HuntDetour;
        public TargetRule Target;
        public FarmRoute Route;

        public static PolicyParams Default => new PolicyParams
        {
            ReactionDelay = 0.3f,
            ReactionJitter = 0f,
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
    /// the Peck is ready. <see cref="CastSlot"/> is an index into <c>MatchKit.Abilities</c> (a Peck slot is refused: pecking is <see cref="PeckPile"/>);
    /// <see cref="CastTarget"/> names a rival seat for an ability that picks one (Death Mark). -1 means none. Area abilities hit whoever is in range.
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

    /// <summary>
    /// What a policy knows about one seat. For a rival it holds what the policy may see: its position comes from the game's bot sight rule
    /// (<see cref="MatchSight"/>), never from the rival's true position while it is hidden.
    /// </summary>
    public struct SeatView
    {
        public bool Present;
        /// <summary>The rival is visible this tick (always true for the seat itself).</summary>
        public bool Seen;
        /// <summary>False when the observer has given a hidden rival up (it searched the last seen spot, or ran out of patience): <see cref="Pos"/> then means nothing.</summary>
        public bool Known;
        public Vector2 Pos;
        /// <summary>Food in the beak.</summary>
        public float Cargo;
        public float Banked;
        public bool Pecking;
        public bool Stunned;
        public bool Removed;
        public bool Shelled;
        public bool Hidden;
        /// <summary>The seat this seat's live Death Mark is on, -1 for none (replicated in the game, so every seat sees it).</summary>
        public int MarkTarget;
        /// <summary>The seat whose live mark is on this one, -1 for none.</summary>
        public int MarkedBy;
        /// <summary>Seconds until this seat's live mark arms (0 once armed or with no mark).</summary>
        public float ArmLeft;
        /// <summary>Seconds until this seat's live mark runs out (0 with no mark).</summary>
        public float WindowLeft;
        /// <summary>The rival this seat's loot mark (the Thief's) is on, -1 for none. Only the Thief and the marked rival see it in the game; a policy reads its own.</summary>
        public int LootTarget;
        /// <summary>The seat (a Thief) whose loot mark is on this one, -1 for none. For a policy's own seat only; P0 and P1 do not read it for a rival.</summary>
        public int LootMarkedBy;
        /// <summary>Seconds until each slot is ready. Index 0 is the Peck, index 1 + i is <c>MatchKit.Abilities[i]</c>.</summary>
        public float[] CooldownLeft;
        /// <summary>Beak capacity now. Policies read this, never the kit: it is the one capacity the simulator computes.</summary>
        public float Capacity;
        /// <summary>Seconds until this seat's Peck cast ends; 0 unless it is pecking. A rival's is read only while it is seen.</summary>
        public float PeckCastLeft;
        /// <summary>Seconds since this seat's own Peck last landed (a large number before the first landing).</summary>
        public float SinceLanded;
        /// <summary>That landing took food (a landing that took 0 opens no fresh state).</summary>
        public bool LandedWithFood;
        /// <summary>The seat's current fresh state has been bitten already (cleared by its next cast start or landing).</summary>
        public bool FreshBitten;
        /// <summary>Trophies this seat holds on each rival, by rival seat (the Champion's; zeros for every other kit). Meaningful for the policy's own seat.</summary>
        public int[] TrophyOn;
        /// <summary>Seconds until each of those counts decays, by rival seat (0 with no count).</summary>
        public float[] TrophyLeft;
        /// <summary>Seconds each rival stays locked against this seat's hit-steal, by rival seat. Meaningful for the policy's own seat.</summary>
        public float[] HitLockLeft;

        /// <summary>Seconds <paramref name="rival"/> stays locked against this seat's hit-steal (0 when nothing is known about it).</summary>
        public float LockLeftOn(int rival) => HitLockLeft != null && rival >= 0 && rival < HitLockLeft.Length ? HitLockLeft[rival] : 0f;
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
        /// <summary>The comeback event's Golden Pile is on the map (its place index is <see cref="MatchGeometry.GoldenPile"/>).</summary>
        public bool GoldenActive;
        public Vector2 GoldenPos;
        public float GoldenFood;
        public MatchKit[] Kits;
        public OracleMapSO Map;
        public OracleNav Nav;
        public MatchRules Rules;

        /// <summary>Trophies <paramref name="seat"/> holds on <paramref name="rival"/>.</summary>
        public int TrophiesOn(int seat, int rival)
        {
            var on = Seats[seat].TrophyOn;
            return on != null && rival >= 0 && rival < on.Length ? on[rival] : 0;
        }
    }

    public interface IMatchPolicy
    {
        MatchIntent Think(MatchView view, ref MatchRng rng);
    }

    public delegate IMatchPolicy PolicyFactory(int seat, PolicyParams p);
}
