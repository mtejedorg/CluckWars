using System;
using System.Collections.Generic;
using System.Globalization;
using CluckWars.Logging;

namespace CluckWars.Progression
{
    /// <summary>
    /// The real <see cref="IMatchEventSink"/>: aggregates each actor's events for the round in
    /// progress and, when the round ends, hands the local actor's <see cref="RoundOutcome"/> on.
    /// It knows nothing about files; <see cref="ProgressionService"/> subscribes and persists.
    /// </summary>
    /// <remarks>
    /// <b>Counters, not event lists.</b> The Ability Lab emits <c>RoundStarted</c> and casts forever
    /// and never a <c>RoundEnded</c>, so memory must stay O(actors × ability keys) however long a round
    /// runs.
    /// <para>
    /// <b>Every actor is bucketed, bots included</b> (in solo the local peer simulates them).
    /// Which bucket is the local human's is named once, by <see cref="RoundEnded"/>; no other bucket
    /// ever reaches an outcome. <c>MatchActorId.None</c> (0) is never a bucket.
    /// </para>
    /// <para>
    /// <b>Must not throw.</b> Every announcement is an inline call in the middle of a gameplay
    /// effect. <c>ProjectInstaller</c> wraps this in <see cref="GuardedMatchEventSink"/>, but the
    /// tracker does not lean on that: subscriber exceptions are caught here.
    /// </para>
    /// </remarks>
    public sealed class MatchTracker : IMatchEventSink
    {
        private const string Source = "MatchTracker";
        private const int NoActor = 0; // MatchActorId.None, which progression may not name

        private sealed class AbilityCount
        {
            public int Casts;
            public int Connected;
        }

        private sealed class ActorBucket
        {
            public float Stolen;
            public int Disabled;
            public readonly HashSet<int> Victims = new HashSet<int>();
            public readonly Dictionary<string, AbilityCount> Abilities = new Dictionary<string, AbilityCount>(StringComparer.Ordinal);
        }

        private readonly ILogService _log;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<string> _newRoundId;
        private readonly Dictionary<int, ActorBucket> _buckets = new Dictionary<int, ActorBucket>();

        private TimeZoneInfo _zone;
        private bool _roundOpen;
        private RoundRuleset _ruleset;
        private int _droppedOutsideRound;
        private bool _reportedMissingAbilityKey;
        private int _roundOpenedFailures;
        private int _outcomeFailures;

        /// <param name="log">Where the tracker reports.</param>
        /// <param name="utcNow">The clock stamped on each outcome; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="newRoundId">Issues round ids; defaults to a new GUID in <c>"N"</c> form.</param>
        /// <param name="zone">
        /// The zone that names each round's <see cref="RoundOutcome.LocalDay"/>. Null means the device's zone,
        /// read lazily when the first round ends — never here, because this constructor runs while
        /// <c>ProjectContext</c> is being built.
        /// </param>
        public MatchTracker(ILogService log, Func<DateTime> utcNow = null, Func<string> newRoundId = null,
            TimeZoneInfo zone = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _newRoundId = newRoundId ?? (() => Guid.NewGuid().ToString("N"));
            _zone = zone;
        }

        /// <summary>A round began. Subscribers clear anything that describes the previous round.</summary>
        public event Action RoundOpened;

        /// <summary>A round ended with the local actor in the standings. Raised at most once per round.</summary>
        public event Action<RoundOutcome> OutcomeReady;

        /// <summary>True between a <c>RoundStarted</c> and the next <c>RoundEnded</c>.</summary>
        public bool IsRoundOpen => _roundOpen;

        /// <summary>How many times a <see cref="RoundOpened"/> / <see cref="OutcomeReady"/> subscriber has thrown.</summary>
        public int SubscriberFailureCount => _roundOpenedFailures + _outcomeFailures;

        // ---- IMatchEventSink ---------------------------------------------------------

        public void RoundStarted(RoundRuleset ruleset)
        {
            if (_roundOpen)
            {
                // A Started with no Ended: the previous round was abandoned. Its buckets must not
                // leak into this one.
                _log.Debug(Source, $"Round started while another was open: discarding the abandoned round's " +
                    $"buckets ({_buckets.Count} actors).");
            }

            if (_droppedOutsideRound > 0)
            {
                _log.Debug(Source, $"Dropped {_droppedOutsideRound} event(s) that arrived outside a round.");
                _droppedOutsideRound = 0;
            }

            _buckets.Clear();
            _ruleset = ruleset;
            _roundOpen = true;

            RaiseRoundOpened();
        }

        /// <remarks>
        /// Deliberately not tallied: bank rewards read the round's standings, because a flush can be
        /// refused and a round-end bonus is never announced here (<see cref="IMatchEventSink.ResourceBanked"/>).
        /// </remarks>
        public void ResourceBanked(int actorId, float amount) { }

        public void ResourceStolen(int actorId, int victimActorId, float amount)
        {
            var bucket = BucketFor(actorId);
            if (bucket == null) return;

            // A zero (or NaN) take robs nobody; only a positive one counts, as a total and as a rival.
            if (!(amount > 0f)) return;
            bucket.Stolen += amount;
            if (victimActorId != NoActor && victimActorId != actorId) bucket.Victims.Add(victimActorId);
        }

        public void OpponentDisabled(int actorId)
        {
            var bucket = BucketFor(actorId);
            if (bucket != null) bucket.Disabled++;
        }

        public void AbilityResolved(int actorId, string abilityKey, bool connected)
        {
            var bucket = BucketFor(actorId);
            if (bucket == null) return;

            if (string.IsNullOrEmpty(abilityKey))
            {
                // Every shipped ability carries a key (slice 0, UnlockKeyTests), so this is a wiring bug.
                // Once is enough: it would otherwise repeat on every cast of that ability.
                if (!_reportedMissingAbilityKey)
                {
                    _reportedMissingAbilityKey = true;
                    _log.Error(Source, $"AbilityResolved from actor {actorId} carried no ability key, so the cast " +
                        "is missing from the round's ability tallies (and any goal counting it). Check the " +
                        "ability asset's unlock key. Reported once per session.");
                }
                return;
            }

            if (!bucket.Abilities.TryGetValue(abilityKey, out var count))
            {
                count = new AbilityCount();
                bucket.Abilities.Add(abilityKey, count);
            }

            count.Casts++;
            if (connected) count.Connected++;
        }

        public void RoundEnded(RoundStandings standings, int localActorId, float durationSeconds)
        {
            if (!_roundOpen)
            {
                // RoundAnnouncer never ends a round it did not start (its exhaustive oracle test), so
                // this is an invariant violation — but not one worth throwing mid-frame for.
                _log.Warn(Source, "RoundEnded arrived with no round open; nothing recorded.");
                return;
            }

            RoundOutcome outcome = null;
            try
            {
                if (localActorId == NoActor)
                {
                    // A legal state: no local non-decoy actor on this peer (e.g. a spectating peer).
                    _log.Debug(Source, "Round ended with no local actor; nothing recorded.");
                }
                else if (!TryFindEntry(standings, localActorId, out var entry))
                {
                    _log.Warn(Source, $"Round ended but the local actor {localActorId} is not in the standings " +
                        $"({(standings?.Entries == null ? "no entries" : standings.Entries.Length + " entries")}); " +
                        "nothing recorded for this round.");
                }
                else
                {
                    outcome = BuildOutcome(entry, durationSeconds);
                }
            }
            finally
            {
                // Closed before subscribers run, so a subscriber reacting to the outcome sees no open round.
                CloseRound();
            }

            if (outcome != null) RaiseOutcome(outcome);
        }

        // ---- Internals -----------------------------------------------------------------

        private ActorBucket BucketFor(int actorId)
        {
            if (actorId == NoActor) return null;
            if (!_roundOpen)
            {
                _droppedOutsideRound++;
                return null;
            }

            if (!_buckets.TryGetValue(actorId, out var bucket))
            {
                bucket = new ActorBucket();
                _buckets.Add(actorId, bucket);
            }
            return bucket;
        }

        private static bool TryFindEntry(RoundStandings standings, int actorId, out RoundStandingEntry entry)
        {
            entry = default;
            if (standings?.Entries == null) return false;
            foreach (var candidate in standings.Entries)
            {
                if (candidate.ActorId != actorId) continue;
                entry = candidate;
                return true;
            }
            return false;
        }

        private RoundOutcome BuildOutcome(RoundStandingEntry entry, float durationSeconds)
        {
            _buckets.TryGetValue(entry.ActorId, out var bucket); // an actor that did nothing has no bucket

            var abilities = new List<AbilityTally>();
            if (bucket != null)
            {
                foreach (var pair in bucket.Abilities)
                    abilities.Add(new AbilityTally { Key = pair.Key, Casts = pair.Value.Casts, Connected = pair.Value.Connected });
                abilities.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            }

            DateTime endedUtc = _utcNow();
            return new RoundOutcome
            {
                SchemaVersion = RoundOutcome.CurrentSchemaVersion,
                RoundId = _newRoundId(),
                EndedAtUtc = ProgressionCalendar.FormatUtc(endedUtc),
                LocalDay = ProgressionCalendar.FormatDay(ProgressionCalendar.LocalDay(endedUtc, Zone())),
                Ruleset = _ruleset,
                DurationSeconds = durationSeconds,
                RoleKey = entry.RoleKey,
                Placement = entry.Placement,
                BankedTotal = entry.ResourceTotal,
                StolenTotal = bucket?.Stolen ?? 0f,
                RivalsRobbed = bucket?.Victims.Count ?? 0,
                OpponentsDisabled = bucket?.Disabled ?? 0,
                Abilities = abilities.ToArray(),
            };
        }

        /// <summary>
        /// The zone that names a round's local day: the injected one, else the device's, read here (lazily)
        /// the first time a round ends. A device whose zone cannot be read falls back to UTC for the session,
        /// with one warning.
        /// </summary>
        private TimeZoneInfo Zone()
        {
            if (_zone != null) return _zone;
            try
            {
                _zone = TimeZoneInfo.Local;
            }
            catch (Exception e)
            {
                _zone = TimeZoneInfo.Utc;
                _log.Warn(Source, $"Could not read the device's time zone ({e.GetType().Name}: {e.Message}). Rounds this " +
                    "session are stamped with their UTC day, so the rested bonus follows UTC days.");
            }
            return _zone;
        }

        private void CloseRound()
        {
            _roundOpen = false;
            _buckets.Clear();
        }

        private void RaiseRoundOpened()
        {
            var handlers = RoundOpened;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { OnSubscriberFailure(nameof(RoundOpened), ref _roundOpenedFailures, e); }
            }
        }

        private void RaiseOutcome(RoundOutcome outcome)
        {
            var handlers = OutcomeReady;
            if (handlers == null)
            {
                _log.Warn(Source, $"Round {outcome.RoundId} ended with nobody listening for its outcome; it is not recorded.");
                return;
            }

            foreach (Action<RoundOutcome> handler in handlers.GetInvocationList())
            {
                try { handler(outcome); }
                catch (Exception e) { OnSubscriberFailure(nameof(OutcomeReady), ref _outcomeFailures, e); }
            }
        }

        private void OnSubscriberFailure(string evt, ref int failures, Exception e)
        {
            // Same policy as GuardedMatchEventSink: the first failure is an Error with the exception,
            // later ones are only counted, so a broken subscriber cannot bury the log.
            if (++failures > 1) return;
            _log.Error(Source, $"A {evt} subscriber threw {e.GetType().Name}: {e.Message} The round carries on; " +
                $"further {evt} failures are counted (MatchTracker.SubscriberFailureCount), not logged. " +
                $"Failures so far: {failures.ToString(CultureInfo.InvariantCulture)}.", e);
        }
    }
}
