using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Everything derived from the journal: the Grain balance and the career totals. Built only by
    /// <see cref="Fold"/>, and never persisted — the journal is the wallet.
    /// </summary>
    /// <remarks>
    /// <b>Deterministic and order-independent.</b> The same set of records gives the same ledger in any
    /// order: duplicates are folded by <c>RoundId</c> first, then records are walked in one canonical
    /// order, (<c>EndedAtUtc</c> instant, <c>RoundId</c> ordinal).
    /// <para>
    /// <b>Re-derived from config.</b> Each record is evaluated against the current
    /// <see cref="ProgressionConfigSO"/>, so a config change changes past rounds' Grain too.
    /// </para>
    /// </remarks>
    public sealed class ProgressionLedger
    {
        private readonly Dictionary<string, GrainAward> _awards;

        private ProgressionLedger(Dictionary<string, GrainAward> awards) => _awards = awards;

        /// <summary>The ledger of an empty journal.</summary>
        public static readonly ProgressionLedger Empty = new ProgressionLedger(new Dictionary<string, GrainAward>(StringComparer.Ordinal))
        {
            ConflictingRoundIds = Array.Empty<string>(),
        };

        /// <summary>Sum of every evaluable round's Grain.</summary>
        public long GrainBalance { get; private set; }

        /// <summary>Evaluable rounds.</summary>
        public int RoundsPlayed { get; private set; }

        /// <summary>Evaluable rounds finished at placement 1. A shared first place counts for everyone in it.</summary>
        public int Wins { get; private set; }

        /// <summary>Sum of evaluable rounds' banked totals.</summary>
        public double TotalBanked { get; private set; }

        /// <summary>Sum of evaluable rounds' stolen totals.</summary>
        public double TotalStolen { get; private set; }

        /// <summary>Distinct rounds that could not be evaluated. They are excluded from every figure above.</summary>
        public int NotEvaluable { get; private set; }

        /// <summary>Records beyond the first for a <c>RoundId</c>.</summary>
        public int DuplicateRecords { get; private set; }

        /// <summary>Round ids whose duplicates disagree in content, ordinal-sorted. The ordinal-smallest serialization was used.</summary>
        public IReadOnlyList<string> ConflictingRoundIds { get; private set; }

        /// <summary>The evaluation of one folded round; false if the ledger has no such round.</summary>
        public bool TryGetAward(string roundId, out GrainAward award)
        {
            award = default;
            return roundId != null && _awards.TryGetValue(roundId, out award);
        }

        /// <summary>Folds <paramref name="records"/> into a ledger. Total: never throws for any input.</summary>
        /// <param name="zone">The zone whose calendar days bound the rested bonus; null means <see cref="TimeZoneInfo.Local"/>.</param>
        public static ProgressionLedger Fold(IReadOnlyList<JournalRecord> records, ProgressionConfigSO config, TimeZoneInfo zone)
        {
            zone = zone ?? TimeZoneInfo.Local;
            var ledger = new ProgressionLedger(new Dictionary<string, GrainAward>(StringComparer.Ordinal));
            if (records == null)
            {
                ledger.ConflictingRoundIds = Array.Empty<string>();
                return ledger;
            }

            // 1. One record per RoundId. Differing duplicates resolve to the ordinal-smallest
            //    serialization, which does not depend on the order they were read in.
            var byId = new Dictionary<string, JournalRecord>(StringComparer.Ordinal);
            var conflicts = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record.Outcome == null || string.IsNullOrEmpty(record.Outcome.RoundId))
                {
                    ledger.NotEvaluable++; // no identity to fold by, so nothing to count it against
                    continue;
                }

                string id = record.Outcome.RoundId;
                if (!byId.TryGetValue(id, out var kept))
                {
                    byId.Add(id, record);
                    continue;
                }

                ledger.DuplicateRecords++;
                if (string.Equals(kept.Canonical, record.Canonical, StringComparison.Ordinal)) continue;
                conflicts.Add(id);
                if (string.CompareOrdinal(record.Canonical, kept.Canonical) < 0) byId[id] = record;
            }
            ledger.ConflictingRoundIds = new List<string>(conflicts);

            // 2. Canonical order. A record whose instant does not parse has no place in a day.
            var ordered = new List<(DateTime Utc, JournalRecord Record)>(byId.Count);
            foreach (var pair in byId)
            {
                if (ProgressionCalendar.TryParseUtc(pair.Value.Outcome.EndedAtUtc, out var utc))
                {
                    ordered.Add((utc, pair.Value));
                }
                else
                {
                    ledger.NotEvaluable++;
                    ledger._awards[pair.Key] = GrainAward.NotEvaluable(EvaluationStatus.InvalidFacts);
                }
            }
            ordered.Sort((a, b) =>
            {
                int byTime = a.Utc.Ticks.CompareTo(b.Utc.Ticks);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Record.Outcome.RoundId, b.Record.Outcome.RoundId);
            });

            // 3. Walk. priorRoundsToday counts only earlier *evaluable* rounds on the same local day: the
            //    day stamped on the round when it has one (so a later zone change cannot move it), else
            //    the day of its instant in `zone` (lines written before the stamp existed).
            var roundsPerDay = new Dictionary<DateTime, int>();
            foreach (var (utc, record) in ordered)
            {
                var outcome = record.Outcome;
                var day = ProgressionCalendar.DayOfRound(outcome.LocalDay, utc, zone);
                roundsPerDay.TryGetValue(day, out int prior);

                var award = ProgressionRules.Evaluate(outcome, config, prior);
                ledger._awards[outcome.RoundId] = award;
                if (!award.IsEvaluable)
                {
                    ledger.NotEvaluable++;
                    continue;
                }

                roundsPerDay[day] = prior + 1;
                ledger.GrainBalance += award.Grain;
                ledger.RoundsPlayed++;
                if (outcome.Placement == 1) ledger.Wins++;
                ledger.TotalBanked += outcome.BankedTotal;
                ledger.TotalStolen += outcome.StolenTotal;
            }

            return ledger;
        }
    }
}
