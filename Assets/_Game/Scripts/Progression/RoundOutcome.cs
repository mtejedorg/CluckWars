using System;

// Plain data shaped for JsonUtility: [Serializable], public fields, arrays rather than
// dictionaries, no properties. Anything JsonUtility cannot see would silently vanish from the
// journal. One RoundOutcome is one line of the journal.
namespace CluckWars.Progression
{
    /// <summary>
    /// What the local actor did in one finished round: <b>facts only</b>. There is deliberately no
    /// currency field. Grain is derived from these facts by <see cref="ProgressionRules.Evaluate"/>
    /// every time the journal is folded, so the journal never stores a number that a config change
    /// could make wrong.
    /// </summary>
    /// <remarks>
    /// Built by <see cref="MatchTracker"/> when a round ends and appended to the journal by
    /// <see cref="JournalStore"/>. Every number written or parsed by hand goes through
    /// <c>CultureInfo.InvariantCulture</c>; JsonUtility itself is culture-independent.
    /// </remarks>
    [Serializable]
    public sealed class RoundOutcome
    {
        /// <summary>The shape this build writes, and the newest it reads (see <see cref="IsReadableSchema"/>).</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// True for schema versions 1..<see cref="CurrentSchemaVersion"/>. The journal loader and
        /// <see cref="ProgressionRules.Evaluate"/> both use this one check: 0 is JsonUtility's default for
        /// <c>{}</c> (not a record), and a newer version was written by a build this one does not understand.
        /// </summary>
        public static bool IsReadableSchema(int version) => version >= 1 && version <= CurrentSchemaVersion;

        /// <summary>Shape version of this record; 0 means "not a record" (JsonUtility's default for <c>{}</c>).</summary>
        public int SchemaVersion;

        /// <summary>Unique id of the round on this device. The journal folds duplicates of it once.</summary>
        public string RoundId;

        /// <summary>When the round ended, UTC, ISO-8601 round-trip format (<c>"o"</c>, invariant culture).</summary>
        public string EndedAtUtc;

        /// <summary>
        /// The local calendar day the round ended on, <c>yyyy-MM-dd</c> (<see cref="ProgressionCalendar.FormatDay"/>),
        /// stamped by <see cref="MatchTracker"/> in the device's zone at that moment. The rested bonus groups
        /// rounds by this day, so a later change of zone cannot move a past round to another day and change
        /// the balance. Absent on lines written before it existed; the fold then falls back to the day of
        /// <see cref="EndedAtUtc"/> in the current zone.
        /// </summary>
        public string LocalDay;

        /// <summary>The rules the round was played under.</summary>
        public RoundRuleset Ruleset;

        /// <summary>How long the round actually ran, in seconds.</summary>
        public float DurationSeconds;

        /// <summary>The local actor's role key (<see cref="UnlockKeyTable"/>).</summary>
        public string RoleKey;

        /// <summary>1-based finishing position from the round's standings; tied actors share it.</summary>
        public int Placement;

        /// <summary>The local actor's banked total, read from the round's standings — never a sum of banked events.</summary>
        public float BankedTotal;

        /// <summary>Resource the local actor was credited for taking from rivals (the thief's optimistic credit).</summary>
        public float StolenTotal;

        /// <summary>How many distinct rivals the local actor took resource from.</summary>
        public int RivalsRobbed;

        /// <summary>How many times the local actor took an opponent out of play.</summary>
        public int OpponentsDisabled;

        /// <summary>Per-ability cast counts, sorted ordinally by key so the line is deterministic.</summary>
        public AbilityTally[] Abilities;
    }

    /// <summary>How often the local actor resolved one ability in a round, and how many of those connected.</summary>
    /// <remarks>
    /// <see cref="Connected"/> is a lower bound: families that cannot tell whether they reached anyone
    /// (self-buffs, placed zones) always report "not connected".
    /// </remarks>
    [Serializable]
    public struct AbilityTally
    {
        /// <summary>The ability's stable unlock key.</summary>
        public string Key;

        /// <summary>Times it was resolved.</summary>
        public int Casts;

        /// <summary>Times it reached at least one target.</summary>
        public int Connected;
    }
}
