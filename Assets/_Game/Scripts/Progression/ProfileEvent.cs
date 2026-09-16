using System;
using System.Text.RegularExpressions;

// Plain data shaped for JsonUtility, exactly like RoundOutcome: [Serializable], public fields, no
// properties. One ProfileEvent is one line of the journal — the same file the rounds live in.
namespace CluckWars.Progression
{
    /// <summary>
    /// A choice the player made about how they present themselves: the generated name, or the title,
    /// emblem or banner on their nameplate. <b>Facts only</b>, like <see cref="RoundOutcome"/>: the line
    /// says what was chosen and when, never what that choice currently resolves to.
    /// </summary>
    /// <remarks>
    /// Profile lines share the journal with round lines and are told apart by <see cref="Kind"/>: a
    /// round line has no <c>Kind</c> at all (JsonUtility's default for an absent string is null), so
    /// every line slice 2 wrote still reads back byte-identically.
    /// <para>
    /// Appended and flushed <b>before</b> anything shows it, exactly as a round's award is.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class ProfileEvent
    {
        /// <summary>The <see cref="Kind"/> value that marks a line as a profile event.</summary>
        public const string ProfileKind = "profile";

        /// <summary>The shape this build writes, and the newest it reads (see <see cref="IsReadableSchema"/>).</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Longest a displayable name may be, counting the <c>#</c> and its four digits.</summary>
        public const int MaxNameLength = 24;

        /// <summary>A name as <see cref="NameGenerator"/> makes them: ASCII letters, one <c>#</c>, four digits.</summary>
        private static readonly Regex NameShape = new Regex(@"^[A-Za-z]+#[0-9]{4}$", RegexOptions.CultureInvariant);

        /// <summary>True for schema versions 1..<see cref="CurrentSchemaVersion"/>; 0 is JsonUtility's default for <c>{}</c>.</summary>
        public static bool IsReadableSchema(int version) => version >= 1 && version <= CurrentSchemaVersion;

        /// <summary>Always <see cref="ProfileKind"/>. Absent or empty on a line means "a round".</summary>
        public string Kind;

        /// <summary>Shape version of this event; 0 means "not an event".</summary>
        public int SchemaVersion;

        /// <summary>Unique id of this event on this device (a GUID, "N" format). The fold folds duplicates of it once.</summary>
        public string EventId;

        /// <summary>When the choice was made, UTC, ISO-8601 round-trip (<see cref="ProgressionCalendar.FormatUtc"/>).</summary>
        public string AtUtc;

        /// <summary>Which part of the nameplate this event sets — one of <see cref="ProfileEventTypes"/>.</summary>
        public string Type;

        /// <summary>
        /// The chosen value: the generated name, or the key of the chosen part. An empty value on a
        /// title or banner event means "cleared".
        /// </summary>
        public string Value;

        /// <summary>
        /// A new event, stamped now. <paramref name="eventId"/> and <paramref name="atUtc"/> are passed
        /// in rather than read here so the type stays pure data and tests stay deterministic.
        /// </summary>
        public static ProfileEvent Of(string type, string value, string eventId, DateTime atUtc) =>
            new ProfileEvent
            {
                Kind = ProfileKind,
                SchemaVersion = CurrentSchemaVersion,
                EventId = eventId,
                AtUtc = ProgressionCalendar.FormatUtc(atUtc),
                Type = type,
                Value = value ?? string.Empty,
            };

        /// <summary>
        /// True if <paramref name="name"/> is one this build will show. A name that fails this is
        /// ignored (with a warning) rather than displayed: it was written by a build whose generator
        /// this one does not know, or by hand.
        /// </summary>
        public static bool IsDisplayableName(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && NameShape.IsMatch(name);
    }

    /// <summary>The <see cref="ProfileEvent.Type"/> values this build understands.</summary>
    /// <remarks>
    /// Strings, not an enum, for the same reason unlock keys are strings: the journal is a promise, and
    /// an enum member is a refactoring target. An unknown type is kept in the journal and ignored, so a
    /// newer build's events survive a downgrade.
    /// </remarks>
    public static class ProfileEventTypes
    {
        public const string Name = "name";
        public const string Title = "title";
        public const string Emblem = "emblem";
        public const string Banner = "banner";

        /// <summary>The types this build folds, in no particular order.</summary>
        public static readonly string[] All = { Name, Title, Emblem, Banner };

        public static bool IsKnown(string type) => Array.IndexOf(All, type) >= 0;

        /// <summary>The nameplate part a <paramref name="slot"/> is stored under.</summary>
        public static string Of(NameplateSlot slot) => slot switch
        {
            NameplateSlot.Title => Title,
            NameplateSlot.Emblem => Emblem,
            NameplateSlot.Banner => Banner,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), $"Slot {slot} has no profile event type."),
        };
    }

    /// <summary>A part of the nameplate the player can choose. The name is not here: it is re-rolled, not picked.</summary>
    public enum NameplateSlot
    {
        Title,
        Emblem,
        Banner,
    }
}
