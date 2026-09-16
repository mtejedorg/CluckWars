using System;

namespace CluckWars.Progression
{
    /// <summary>
    /// The player's progression, as UI reads it. Project-scoped, main-thread only, synchronous.
    /// </summary>
    /// <remarks>
    /// There is no Null implementation: when the journal cannot be loaded, <see cref="IsReady"/> stays
    /// false and progression UI stays hidden. Nothing here blocks or waits.
    /// </remarks>
    public interface IProgressionService
    {
        /// <summary>True once the journal has loaded. A recovered torn line still counts as loaded (and is reported).</summary>
        bool IsReady { get; }

        /// <summary>The current totals. <see cref="ProgressionProfile.Empty"/> until loaded.</summary>
        ProgressionProfile Profile { get; }

        /// <summary>
        /// The award of the most recently recorded round, or null. Two mechanisms keep a results screen from
        /// showing an earlier round's award: the service clears this when the next round opens
        /// (<c>RoundOpened</c>), and a results panel ignores an award that already existed when the panel was
        /// created — a player who quits during results and joins a session already on its results screen
        /// hears no round start, so the previous award is still here.
        /// </summary>
        RoundAward? LatestAward { get; }

        /// <summary>
        /// Who the player is: nameplate, mastery, records and career.
        /// <see cref="ProgressionIdentity.Empty"/> until loaded.
        /// </summary>
        ProgressionIdentity Identity { get; }

        /// <summary>
        /// Where the new-player ramp stands, and this week's three goals plus today's task once it has
        /// finished. <see cref="ProgressionUnlocks.Empty"/> until loaded.
        /// </summary>
        ProgressionUnlocks Unlocks { get; }

        /// <summary>The totals changed (after a load or a recorded round).</summary>
        event Action<ProgressionProfile> OnProfileChanged;

        /// <summary>
        /// The identity changed: after a load, after a recorded round (records and mastery move with
        /// play) and after any profile event.
        /// </summary>
        event Action<ProgressionIdentity> OnIdentityChanged;

        /// <summary>
        /// <see cref="Unlocks"/> changed: after a load and after a recorded round (a step may clear, or
        /// a goal's progress moves).
        /// </summary>
        event Action<ProgressionUnlocks> OnUnlocksChanged;

        /// <summary>A round's Grain was written to the journal. Raised after <see cref="OnProfileChanged"/>.</summary>
        event Action<RoundAward> OnRoundAwarded;

        /// <summary>
        /// Something went wrong with the journal. Every fault is also logged, because nothing may be
        /// subscribed yet when it happens (loading runs at startup).
        /// </summary>
        event Action<ProgressionFault> OnFault;

        /// <summary>
        /// Rolls a new display name and writes it to the journal. Returns false, changing nothing, when
        /// the journal is not loaded or the write failed (an <c>Error</c> and <c>OnFault</c> say which).
        /// </summary>
        /// <remarks>
        /// Names are generated, never typed: there is no free-text entry anywhere in the game. The new
        /// name is never the current one, so the button always visibly does something.
        /// </remarks>
        bool TryRerollName();

        /// <summary>
        /// Wears <paramref name="key"/> in <paramref name="slot"/>, or clears the slot when it is empty.
        /// Returns false, writing nothing, for a part the player does not own, or when the journal is
        /// not loaded or the write failed.
        /// </summary>
        /// <remarks>
        /// Written and flushed before <see cref="Identity"/> changes, exactly as a round's award is: a
        /// nameplate the journal does not back is one the next launch would take away.
        /// </remarks>
        bool TrySelectNameplatePart(NameplateSlot slot, string key);
    }

    /// <summary>An immutable snapshot of the player's progression totals.</summary>
    public sealed class ProgressionProfile
    {
        public static readonly ProgressionProfile Empty = new ProgressionProfile(0, 0, 0, 0, 0, 0);

        public ProgressionProfile(long grainBalance, int roundsPlayed, int wins, double totalBanked, double totalStolen,
            long bonusGrain)
        {
            GrainBalance = grainBalance;
            RoundsPlayed = roundsPlayed;
            Wins = wins;
            TotalBanked = totalBanked;
            TotalStolen = totalStolen;
            BonusGrain = bonusGrain;
        }

        public long GrainBalance { get; }
        public int RoundsPlayed { get; }

        /// <summary>
        /// Rounds finished at placement 1. A shared first place counts, so this can differ from the
        /// winner the round announced: that tie breaks on other rules (kills, then corner).
        /// </summary>
        public int Wins { get; }

        public double TotalBanked { get; }
        public double TotalStolen { get; }

        /// <summary>
        /// Grain every completed weekly goal and daily task has ever paid, lifetime — derived fresh
        /// from the same rounds every fold (decision D6), never journaled as its own amount. Separate
        /// from <see cref="GrainBalance"/> on purpose: no existing number changes meaning.
        /// </summary>
        public long BonusGrain { get; }
    }

    /// <summary>The Grain one round earned. <see cref="Grain"/> is exactly the balance delta it caused.</summary>
    public readonly struct RoundAward
    {
        public readonly string RoundId;
        public readonly int Grain;
        public readonly bool Rested;

        public RoundAward(string roundId, int grain, bool rested)
        {
            RoundId = roundId;
            Grain = grain;
            Rested = rested;
        }
    }

    /// <summary>What kind of journal problem a <see cref="ProgressionFault"/> reports.</summary>
    public enum ProgressionFaultKind
    {
        /// <summary>The journal could not be read or made safe; progression is off this session.</summary>
        LoadFailed,
        /// <summary>The final line had no newline but was complete: kept, newline repaired.</summary>
        TornTailRepaired,
        /// <summary>The final line was incomplete: moved to the <c>.torn</c> file.</summary>
        TornTailDropped,
        /// <summary>Middle lines were malformed and skipped.</summary>
        MalformedLines,
        /// <summary>Several records share a round id but disagree.</summary>
        ConflictingDuplicates,
        /// <summary>Records exist that cannot be evaluated; they earn nothing and count for nothing.</summary>
        NotEvaluable,
        /// <summary>A round could not be written; it earned nothing.</summary>
        WriteFailed,
        /// <summary>A round ended while the journal was not loaded; it was not recorded.</summary>
        NotReady,
    }

    /// <summary>One journal problem, as raised by <see cref="IProgressionService.OnFault"/>.</summary>
    public sealed class ProgressionFault
    {
        public ProgressionFault(ProgressionFaultKind kind, string message, Exception exception)
        {
            Kind = kind;
            Message = message;
            Exception = exception;
        }

        public ProgressionFaultKind Kind { get; }
        public string Message { get; }
        public Exception Exception { get; }
    }
}
