using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CluckWars.Logging;
using Zenject;

namespace CluckWars.Progression
{
    /// <summary>
    /// Owns the journal for the session: loads it at startup, records each round the
    /// <see cref="MatchTracker"/> hands over, and serves the folded totals to UI.
    /// </summary>
    /// <remarks>
    /// <b>Write before show.</b> A round's award exists only once its outcome is flushed to disk: on a
    /// failed write there is no award and no balance change, because the journal is the wallet.
    /// <para>
    /// <b>Memory equals disk.</b> The record folded after an append is the one <see cref="JournalStore.Append"/>
    /// parsed back from the exact line it wrote, so the session's balance is what a reload computes.
    /// </para>
    /// <para>
    /// <b>Lifecycle.</b> The constructor does no I/O and reads no time zone. <see cref="Initialize"/>
    /// (called by Zenject's project kernel at startup, before any match) resolves the zone and loads the
    /// journal. Bound <c>NonLazy</c>, so it exists — and is subscribed to the tracker — before the first
    /// round can end.
    /// </para>
    /// <para>
    /// <b>Every append refolds the whole journal</b> (O(rounds ever played)). That keeps the award
    /// identical to what a reload would compute, even if the device clock moved backwards and the new
    /// round sorts before others of its day.
    /// </para>
    /// </remarks>
    public sealed class ProgressionService : IProgressionService, IInitializable, IDisposable
    {
        private const string Source = "Progression";
        private const int MaxSkippedLinesListed = 10;

        private readonly ILogService _log;
        private readonly MatchTracker _tracker;
        private readonly ProgressionConfigSO _config;
        private readonly JournalStore _store;
        private readonly List<JournalRecord> _records = new List<JournalRecord>();
        private readonly List<ProfileEvent> _profileEvents = new List<ProfileEvent>();

        /// <summary>Problems already logged, so a refold does not repeat them every round.</summary>
        private readonly HashSet<string> _reportedProblems = new HashSet<string>(StringComparer.Ordinal);

        private readonly Func<DateTime> _utcNow;
        private readonly Func<string> _newEventId;
        private readonly System.Random _random;

        private TimeZoneInfo _zone;
        private ProgressionLedger _ledger = ProgressionLedger.Empty;
        private bool _initialized;
        private int _notReadyRounds;

        /// <param name="zone">
        /// Whose calendar days bound the rested bonus for journal lines without a stamped day. Null means
        /// the device's zone, read in <see cref="Initialize"/> (never here).
        /// </param>
        /// <param name="utcNow">The clock profile events are stamped with; null means <c>DateTime.UtcNow</c>.</param>
        /// <param name="newEventId">Makes a profile event's id; null means a fresh GUID.</param>
        /// <param name="random">Drives <see cref="NameGenerator"/>; null means an unseeded one.</param>
        public ProgressionService(ILogService log, MatchTracker tracker, ProgressionConfigSO config, JournalStore store,
            TimeZoneInfo zone = null, Func<DateTime> utcNow = null, Func<string> newEventId = null, System.Random random = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _zone = zone;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _newEventId = newEventId ?? (() => Guid.NewGuid().ToString("N"));
            _random = random ?? new System.Random();

            _tracker.RoundOpened += HandleRoundOpened;
            _tracker.OutcomeReady += HandleOutcome;
        }

        public bool IsReady { get; private set; }
        public ProgressionProfile Profile { get; private set; } = ProgressionProfile.Empty;
        public ProgressionIdentity Identity { get; private set; } = ProgressionIdentity.Empty;
        public RoundAward? LatestAward { get; private set; }

        /// <summary>Where the journal lives, for diagnostics.</summary>
        public string JournalPath => _store.JournalPath;

        public event Action<ProgressionProfile> OnProfileChanged;
        public event Action<ProgressionIdentity> OnIdentityChanged;
        public event Action<RoundAward> OnRoundAwarded;
        public event Action<ProgressionFault> OnFault;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ResolveZone();

            try
            {
                Load();
            }
            catch (Exception e)
            {
                // Load reports its own failures; this is a bug. Kept off the project kernel, which would
                // otherwise stop initialising everything bound after this service.
                IsReady = false;
                Fault(ProgressionFaultKind.LoadFailed, LogLevel.Error,
                    $"Loading the journal at {_store.JournalPath} threw {e.GetType().Name}: {e.Message} " +
                    "Progression is off for this session: rounds are not recorded and no Grain is shown.", e);
            }
        }

        public void Dispose()
        {
            _tracker.RoundOpened -= HandleRoundOpened;
            _tracker.OutcomeReady -= HandleOutcome;
        }

        // ---- Loading -----------------------------------------------------------------

        /// <summary>The device's zone, unless one was injected; UTC (with a warning) if it cannot be read.</summary>
        private void ResolveZone()
        {
            if (_zone != null) return;
            try
            {
                _zone = TimeZoneInfo.Local;
            }
            catch (Exception e)
            {
                _zone = TimeZoneInfo.Utc;
                _log.Warn(Source, $"Could not read the device's time zone ({e.GetType().Name}: {e.Message}). Journal " +
                    "lines without a stamped day are grouped by UTC day this session.");
            }
        }

        private void Load()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var load = _store.Load();
            if (!load.Succeeded)
            {
                Fault(ProgressionFaultKind.LoadFailed, LogLevel.Error,
                    $"Could not load the journal at {_store.JournalPath}: {load.Error?.GetType().Name}: {load.Error?.Message} " +
                    "Progression is off for this session: rounds are not recorded and no Grain is shown.", load.Error);
                return;
            }

            if (load.TornTail == TornTail.Repaired)
            {
                Fault(ProgressionFaultKind.TornTailRepaired, LogLevel.Warn,
                    $"The journal's last line had no newline but was a complete record; kept it and wrote the newline ({_store.JournalPath}).", null);
            }
            else if (load.TornTail == TornTail.Dropped)
            {
                Fault(ProgressionFaultKind.TornTailDropped, LogLevel.Warn,
                    $"The journal ended in an incomplete line ({load.TornTailDetail}); it was moved to {_store.TornPath} " +
                    "and cut from the journal. That round (if it was one) earns nothing.", null);
            }

            if (load.SkippedLines.Count > 0)
            {
                string listed = string.Join("; ", load.SkippedLines.Take(MaxSkippedLinesListed));
                string more = load.SkippedLines.Count > MaxSkippedLinesListed
                    ? $" (+{load.SkippedLines.Count - MaxSkippedLinesListed} more)" : string.Empty;
                Fault(ProgressionFaultKind.MalformedLines, LogLevel.Warn,
                    $"Skipped {load.SkippedLines.Count} malformed journal line(s) in {_store.JournalPath}; the file is " +
                    $"left as it is. {listed}{more}", null);
            }

            _records.AddRange(load.Records);
            _profileEvents.AddRange(load.ProfileEvents);
            var ledger = ProgressionLedger.Fold(_records, _config, _zone);

            if (ledger.ConflictingRoundIds.Count > 0)
            {
                Fault(ProgressionFaultKind.ConflictingDuplicates, LogLevel.Warn,
                    $"{ledger.ConflictingRoundIds.Count} round id(s) appear more than once with different contents; " +
                    $"one version of each was chosen deterministically: {string.Join(", ", ledger.ConflictingRoundIds)}", null);
            }

            if (ledger.NotEvaluable > 0)
            {
                Fault(ProgressionFaultKind.NotEvaluable, LogLevel.Warn,
                    $"{ledger.NotEvaluable} journal round(s) cannot be evaluated (unknown schema, invalid placement or " +
                    "facts, or the config rejects them). They earn no Grain and count toward no total.", null);
            }

            _ledger = ledger;
            Profile = ProfileOf(ledger);
            IsReady = true;
            RefreshIdentity();

            _log.Info(Source, $"Journal loaded from {_store.JournalPath}: {load.Records.Count} round line(s), " +
                $"{load.ProfileEvents.Count} profile line(s), {ledger.RoundsPlayed} round(s), " +
                $"{ledger.GrainBalance} Grain, {ledger.Wins} win(s) " +
                $"({clock.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms).");

            Raise(OnProfileChanged, Profile, nameof(OnProfileChanged));
            Raise(OnIdentityChanged, Identity, nameof(OnIdentityChanged));

            EnsureName();
        }

        /// <summary>
        /// Gives a player with no readable name one, writing it before anything shows it.
        /// </summary>
        /// <remarks>
        /// Done at load rather than on the first visit to the profile page, so a name exists everywhere
        /// it might be drawn. If the write fails there is simply no name — the name line hides, the
        /// fault is reported, and the next launch tries again; nothing invents one in memory.
        /// </remarks>
        private void EnsureName()
        {
            if (Identity.Plate.HasName) return;

            string name = NameGenerator.Next(_random);
            if (AppendProfile(ProfileEventTypes.Name, name))
            {
                _log.Info(Source, $"No name in the journal, so one was generated and written: {name}.");
            }
        }

        // ---- Tracker handoff -----------------------------------------------------------

        private void HandleRoundOpened() => LatestAward = null;

        private void HandleOutcome(RoundOutcome outcome)
        {
            if (!IsReady)
            {
                // Once per session is enough to see why; every later round repeats the same cause.
                _notReadyRounds++;
                Fault(ProgressionFaultKind.NotReady, _notReadyRounds == 1 ? LogLevel.Warn : LogLevel.Debug,
                    $"Round {outcome.RoundId} ended but the journal is {(_initialized ? "not loaded (see the load error above)" : "not initialised yet")}; " +
                    $"it is not recorded and earns no Grain ({_notReadyRounds.ToString(CultureInfo.InvariantCulture)} round(s) so far).", null);
                return;
            }

            var append = _store.Append(outcome);
            _log.Debug(Source, $"Journal append for round {outcome.RoundId}: " +
                $"{append.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms, ok={append.Succeeded}.");
            if (!append.Succeeded)
            {
                Fault(ProgressionFaultKind.WriteFailed, LogLevel.Error,
                    $"Could not write round {outcome.RoundId} to {_store.JournalPath}: {append.Error?.GetType().Name}: " +
                    $"{append.Error?.Message} It earns no Grain and the balance is unchanged.", append.Error);
                return;
            }

            // Written: from here on the round is part of the wallet. Fold the record as the line reads
            // back, not the in-memory outcome, so this session's balance is exactly what a reload computes.
            var record = append.Record;
            var withRound = new List<JournalRecord>(_records.Count + 1);
            withRound.AddRange(_records);
            withRound.Add(record);
            var next = ProgressionLedger.Fold(withRound, _config, _zone);

            var previous = _ledger;
            _records.Add(record);
            _ledger = next;

            if (!next.TryGetAward(outcome.RoundId, out var award))
            {
                // The fold keeps every record with a RoundId, and Append refuses one without; so a bug.
                Fault(ProgressionFaultKind.NotEvaluable, LogLevel.Error,
                    $"Round {outcome.RoundId} was written to the journal but is missing from the ledger after the " +
                    "refold. It earns no Grain this session; a reload will fold it again.", null);
                return;
            }

            if (!award.IsEvaluable)
            {
                // Recorded anyway: the journal holds facts, and evaluability depends on the config. A
                // config fix (e.g. a longer placement table) then credits the round on the next load.
                Fault(ProgressionFaultKind.NotEvaluable, LogLevel.Warn,
                    $"Round {outcome.RoundId} was recorded but cannot be evaluated ({award.Status}: placement " +
                    $"{outcome.Placement}, banked {outcome.BankedTotal.ToString(CultureInfo.InvariantCulture)}, stolen " +
                    $"{outcome.StolenTotal.ToString(CultureInfo.InvariantCulture)}). It earns no Grain under the current config.", null);
                return;
            }

            long delta = next.GrainBalance - previous.GrainBalance;
            Profile = ProfileOf(next);
            LatestAward = new RoundAward(outcome.RoundId, (int)delta, award.Rested);
            RefreshIdentity();

            _log.Info(Source, $"Round {outcome.RoundId}: +{delta} Grain{(award.Rested ? " (rested)" : string.Empty)}, " +
                $"placement {outcome.Placement}; balance {next.GrainBalance}.");

            Raise(OnProfileChanged, Profile, nameof(OnProfileChanged));
            Raise(OnIdentityChanged, Identity, nameof(OnIdentityChanged));
            Raise(OnRoundAwarded, LatestAward.Value, nameof(OnRoundAwarded));
        }

        // ---- Identity ------------------------------------------------------------------

        public bool TryRerollName()
        {
            if (!ReadyFor("roll a new name")) return false;

            string next = NameGenerator.Next(_random, Identity.Plate.Name);
            return AppendProfile(ProfileEventTypes.Name, next);
        }

        public bool TrySelectNameplatePart(NameplateSlot slot, string key)
        {
            if (!ReadyFor($"choose a {slot}")) return false;

            key = key ?? string.Empty;
            if (key.Length > 0 && !Owns(slot, key))
            {
                // Not reachable through the UI — unowned parts are not selectable — so refusing here is
                // about the journal never recording a choice the player could not have made.
                _log.Warn(Source, $"'{key}' is not a {slot} this player has, so it was not worn and nothing was written.");
                return false;
            }

            return AppendProfile(ProfileEventTypes.Of(slot), key);
        }

        private bool Owns(NameplateSlot slot, string key) => slot switch
        {
            NameplateSlot.Title => NameplateComposer.OwnsTitle(key, Identity.Records),
            NameplateSlot.Emblem => NameplateComposer.OwnsEmblem(key, Identity.Roles),
            NameplateSlot.Banner => NameplateComposer.OwnsBanner(key, Identity.Banners),

            // A slot added to the enum and not here: refused, never granted by default.
            _ => false,
        };

        private bool ReadyFor(string what)
        {
            if (IsReady) return true;

            Fault(ProgressionFaultKind.NotReady, LogLevel.Warn,
                $"The journal is not loaded, so the player cannot {what}. Nothing was written.", null);
            return false;
        }

        /// <summary>
        /// Writes one profile event, then refolds and announces the identity it produced.
        /// </summary>
        /// <remarks>
        /// Write before show, exactly like a round: on a failed write there is no change to
        /// <see cref="Identity"/> at all, because the journal is the only thing that says who the
        /// player is.
        /// </remarks>
        private bool AppendProfile(string type, string value)
        {
            var profileEvent = ProfileEvent.Of(type, value, _newEventId(), _utcNow());
            var append = _store.Append(profileEvent);
            _log.Debug(Source, $"Journal append for profile event {profileEvent.EventId} ({type}): " +
                $"{append.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms, ok={append.Succeeded}.");

            if (!append.Succeeded)
            {
                Fault(ProgressionFaultKind.WriteFailed, LogLevel.Error,
                    $"Could not write the '{type}' choice to {_store.JournalPath}: {append.Error?.GetType().Name}: " +
                    $"{append.Error?.Message} Nothing changed.", append.Error);
                return false;
            }

            // Fold the event as the line reads back, not the object that was serialized, so what is
            // shown is what a reload computes.
            _profileEvents.Add(append.Event);
            RefreshIdentity();
            Raise(OnIdentityChanged, Identity, nameof(OnIdentityChanged));
            return true;
        }

        /// <summary>
        /// Rebuilds <see cref="Identity"/> from the ledger's canonical rounds and the journal's profile
        /// events. Raises nothing — the caller decides when the change is announced.
        /// </summary>
        private void RefreshIdentity()
        {
            var problems = new List<string>();
            Identity = IdentityFold.Build(_ledger.EvaluableRounds, _profileEvents, _config, _zone, problems);

            // Every refold meets the same mis-authored asset, so each problem is logged once per
            // session rather than once per round.
            foreach (string problem in problems)
            {
                if (_reportedProblems.Add(problem)) _log.Warn(Source, problem);
            }
        }

        // ---- Helpers -------------------------------------------------------------------

        private static ProgressionProfile ProfileOf(ProgressionLedger ledger) =>
            new ProgressionProfile(ledger.GrainBalance, ledger.RoundsPlayed, ledger.Wins, ledger.TotalBanked, ledger.TotalStolen);

        /// <summary>Faults are always logged: <see cref="OnFault"/> may have no subscriber yet (loading runs at startup).</summary>
        private void Fault(ProgressionFaultKind kind, LogLevel level, string message, Exception exception)
        {
            if (level == LogLevel.Error) _log.Error(Source, message, exception);
            else if (level == LogLevel.Warn) _log.Warn(Source, message);
            else _log.Debug(Source, message);

            Raise(OnFault, new ProgressionFault(kind, message, exception), nameof(OnFault));
        }

        private void Raise<T>(Action<T> handlers, T value, string evt)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                // One broken subscriber must not stop the others, nor unwind into the tracker mid-round.
                try { handler(value); }
                catch (Exception e)
                {
                    _log.Error(Source, $"An {evt} subscriber threw {e.GetType().Name}: {e.Message} The journal and " +
                        "balance are unaffected.", e);
                }
            }
        }
    }
}
