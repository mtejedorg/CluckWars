using CluckWars.Gameplay;

namespace CluckWars.Balance
{
    /// <summary>One chicken of the match simulator: its farming numbers (part 1's <see cref="OracleKit"/>) and its class. Slice S2 adds the abilities and passive hooks.</summary>
    public sealed class MatchKit
    {
        public string Label;
        public ChickenClass Class;
        public OracleKit Farm;
    }

    /// <summary>The rules of one simulated match. Slice S2 adds the combat constants.</summary>
    public struct MatchRules
    {
        /// <summary>Seconds between two thinks of a policy when nothing happened; used when <see cref="ThinkSeconds"/> is not positive.</summary>
        public const float DefaultThinkSeconds = 0.25f;

        /// <summary>The farm rules of part 1: win target, clock, tick, pile slow. <see cref="FarmRules.StopAtTarget"/> is ignored: a match always ends at the target.</summary>
        public FarmRules Farm;
        /// <summary>Half the side of the box a chicken's centre may use (<c>OracleMapBaker.ArenaHalf()</c>).</summary>
        public float ArenaHalf;
        public float ThinkSeconds;
    }
}
