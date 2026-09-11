using System;

// Plain data shaped for JsonUtility: [Serializable], public fields, arrays rather than
// dictionaries, no properties, no methods. Anything JsonUtility cannot see would silently
// vanish from the journal.
namespace CluckWars.Progression
{
    /// <summary>The rules a round is played under, captured once when it starts.</summary>
    /// <remarks>
    /// Slice 1 captures this from <c>MatchConfigSO</c> at round start
    /// (<c>FoodTargetToWin</c>, <c>MatchDurationSeconds</c>, <c>MaxPlayers</c>).
    /// </remarks>
    [Serializable]
    public struct RoundRuleset
    {
        /// <summary>Banked total that wins the round outright.</summary>
        public float ResourceTargetToWin;

        /// <summary>Round length in seconds; when it runs out the highest total wins.</summary>
        public float RoundDurationSeconds;

        /// <summary>Most actors the round can hold.</summary>
        public int MaxActors;
    }

    /// <summary>One actor's result in a finished round.</summary>
    [Serializable]
    public struct RoundStandingEntry
    {
        /// <summary>Opaque actor identity, the same value the sink's events carried.</summary>
        public int ActorId;

        /// <summary>The actor's role key from <see cref="UnlockKeyTable"/>, so per-role records need no extra event.</summary>
        public string RoleKey;

        /// <summary>1-based finishing position; tied actors share the better placement.</summary>
        public int Placement;

        /// <summary>The actor's banked total when the round ended.</summary>
        public float ResourceTotal;
    }

    /// <summary>Every actor's result in a finished round.</summary>
    [Serializable]
    public sealed class RoundStandings
    {
        /// <summary>One entry per actor that took part.</summary>
        public RoundStandingEntry[] Entries;
    }
}
