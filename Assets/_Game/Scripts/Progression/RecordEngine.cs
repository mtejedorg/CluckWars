using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>Where one record stands: earned or not, and if earned, on which round and day.</summary>
    public sealed class RecordStanding
    {
        public RecordStanding(string key, bool earned, string earnedRoundId, string earnedLocalDay,
            bool hasProgress, float progress, float target, string problem)
        {
            Key = key;
            Earned = earned;
            EarnedRoundId = earnedRoundId;
            EarnedLocalDay = earnedLocalDay;
            HasProgress = hasProgress;
            Progress = progress;
            Target = target;
            Problem = problem;
        }

        /// <summary>The definition's key, or a placeholder when it had none.</summary>
        public string Key { get; }

        public bool Earned { get; }

        /// <summary>The first round that earned it (for a career record, the round it became true on), or null.</summary>
        public string EarnedRoundId { get; }

        /// <summary>That round's local day, <c>yyyy-MM-dd</c>, or null.</summary>
        public string EarnedLocalDay { get; }

        /// <summary>True when <see cref="Progress"/> toward <see cref="Target"/> is meaningful to draw.</summary>
        public bool HasProgress { get; }

        /// <summary>The best single round so far against <see cref="Target"/>.</summary>
        public float Progress { get; }

        public float Target { get; }

        /// <summary>Why this definition could not be used, or null. An unusable definition is never earned.</summary>
        public string Problem { get; }

        public bool IsValid => Problem == null;
    }

    /// <summary>
    /// Evaluates every record definition against the journal's rounds. Pure, total, and free of any
    /// state of its own: it reads the rounds and never writes to them.
    /// </summary>
    /// <remarks>
    /// <b>Records are re-derived, never stored.</b> Nothing about a record is written to the journal,
    /// so a record added in a later build credits play that already happened, and a record whose
    /// definition changes re-evaluates against the same history. That is the whole reason the journal
    /// records facts rather than earnings.
    /// <para>
    /// <b>Order is the ledger's.</b> The rounds come from
    /// <see cref="ProgressionLedger.EvaluableRounds"/>, already deduplicated and in canonical order, so
    /// the same history always produces the same standings — including which round earned a record.
    /// </para>
    /// </remarks>
    public static class RecordEngine
    {
        /// <summary>The key a standing gets when its definition had none, so a report can still name it.</summary>
        public const string UnknownKey = "record.<unnamed>";

        /// <summary>
        /// One standing per definition, in the order the definitions were given. Never throws: a null,
        /// destroyed, mis-authored or duplicate definition produces a standing carrying its
        /// <see cref="RecordStanding.Problem"/> instead of stopping the evaluation.
        /// </summary>
        /// <param name="rounds">The ledger's evaluable rounds, in canonical order.</param>
        /// <param name="definitions">The shipped record definitions.</param>
        /// <param name="masteryThresholds">Feeds the career mastery question; see <see cref="MasteryRules"/>.</param>
        /// <param name="zone">Whose calendar days name an earning day; null means the device's.</param>
        public static IReadOnlyList<RecordStanding> Evaluate(IReadOnlyList<RoundOutcome> rounds,
            IReadOnlyList<RecordDefinitionSO> definitions, IReadOnlyList<int> masteryThresholds, TimeZoneInfo zone)
        {
            var standings = new List<RecordStanding>(definitions?.Count ?? 0);
            if (definitions == null) return standings;

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];

                // Unity's overloaded == so a destroyed asset reads as missing too.
                if (definition == null)
                {
                    standings.Add(Unusable(UnknownKey, $"definition {i} is missing (an empty or destroyed slot)"));
                    continue;
                }

                string problem = definition.Problem();
                if (problem != null)
                {
                    standings.Add(Unusable(string.IsNullOrEmpty(definition.Key) ? UnknownKey : definition.Key,
                        $"'{definition.name}' cannot be evaluated: {problem}"));
                    continue;
                }

                if (!seenKeys.Add(definition.Key))
                {
                    standings.Add(Unusable(definition.Key,
                        $"'{definition.name}' repeats the key '{definition.Key}', which an earlier definition already claims"));
                    continue;
                }

                standings.Add(definition.Scope == RecordScope.Career
                    ? EvaluateCareer(definition, rounds, masteryThresholds, zone)
                    : EvaluateSingleRound(definition, rounds, zone));
            }

            return standings;
        }

        // ---- Single round ---------------------------------------------------------------

        private static RecordStanding EvaluateSingleRound(RecordDefinitionSO definition, IReadOnlyList<RoundOutcome> rounds,
            TimeZoneInfo zone)
        {
            bool tracksProgress = TryReadTarget(definition, out float target);
            float best = float.NegativeInfinity;

            for (int i = 0; rounds != null && i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null) continue;
                if (!string.IsNullOrEmpty(definition.RoleKey) &&
                    !string.Equals(round.RoleKey, definition.RoleKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (tracksProgress)
                {
                    float value = MetricOf(round, definition.Conditions[0].Metric);
                    if (!float.IsNaN(value) && value > best) best = value;
                }

                if (!AllConditionsHold(definition, round)) continue;

                // The FIRST qualifying round earns it: a record's date is when the player did it, not
                // when the build learned to notice.
                // `best` already includes this round, so an earned record's progress is at its target.
                return new RecordStanding(definition.Key, true, round.RoundId, DayOf(round, zone),
                    tracksProgress, tracksProgress ? best : 0f, target, null);
            }

            return new RecordStanding(definition.Key, false, null, null,
                tracksProgress, float.IsNegativeInfinity(best) ? 0f : best, target, null);
        }

        private static bool AllConditionsHold(RecordDefinitionSO definition, RoundOutcome round)
        {
            foreach (var condition in definition.Conditions)
            {
                if (!condition.Holds(MetricOf(round, condition.Metric))) return false;
            }

            return true;
        }

        /// <summary>
        /// The "reach N" target to draw progress against, for the single-condition <c>AtLeast</c> shape
        /// only. Anything else (several conditions, or a comparison where "best so far" would read
        /// backwards, like <c>LessThan</c>) shows no bar rather than a misleading one.
        /// </summary>
        private static bool TryReadTarget(RecordDefinitionSO definition, out float target)
        {
            target = 0f;
            if (definition.Conditions.Length != 1) return false;

            var only = definition.Conditions[0];
            if (only.Comparison != RecordComparison.AtLeast || !(only.Value > 0f)) return false;

            target = only.Value;
            return true;
        }

        private static float MetricOf(RoundOutcome round, RecordMetric metric) => metric switch
        {
            RecordMetric.BankedTotal => round.BankedTotal,
            RecordMetric.StolenTotal => round.StolenTotal,
            RecordMetric.RivalsRobbed => round.RivalsRobbed,
            RecordMetric.OpponentsDisabled => round.OpponentsDisabled,
            RecordMetric.Placement => round.Placement,
            RecordMetric.StolenMinusBanked => round.StolenTotal - round.BankedTotal,

            // A metric added to the enum and not here. NaN fails every comparison, so the record stays
            // unearned rather than being handed to everyone.
            _ => float.NaN,
        };

        // ---- Career ---------------------------------------------------------------------

        private static RecordStanding EvaluateCareer(RecordDefinitionSO definition, IReadOnlyList<RoundOutcome> rounds,
            IReadOnlyList<int> masteryThresholds, TimeZoneInfo zone)
        {
            var winningRoles = new HashSet<string>(StringComparer.Ordinal);
            var roundsPerRole = new Dictionary<string, int>(StringComparer.Ordinal);
            int wantedLevel = (int)Math.Ceiling(definition.CareerValue);

            for (int i = 0; rounds != null && i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null) continue;

                switch (definition.CareerKind)
                {
                    case CareerRecordKind.WinWithEveryRole:
                        if (round.Placement == 1 && !string.IsNullOrEmpty(round.RoleKey)) winningRoles.Add(round.RoleKey);
                        if (!CoversEveryRole(winningRoles)) continue;
                        break;

                    case CareerRecordKind.MasteryLevelOnAnyRole:
                        if (string.IsNullOrEmpty(round.RoleKey)) continue;
                        roundsPerRole.TryGetValue(round.RoleKey, out int played);
                        roundsPerRole[round.RoleKey] = ++played;
                        if (MasteryRules.LevelFor(played, masteryThresholds) < wantedLevel) continue;
                        break;

                    default:
                        // A career kind added to the enum and not here: unearned and reported, never
                        // silently true.
                        return Unusable(definition.Key, $"'{definition.name}' asks {definition.CareerKind}, which this build cannot evaluate");
                }

                return new RecordStanding(definition.Key, true, round.RoundId, DayOf(round, zone), false, 0f, 0f, null);
            }

            return new RecordStanding(definition.Key, false, null, null, false, 0f, 0f, null);
        }

        private static bool CoversEveryRole(HashSet<string> roles)
        {
            var wanted = UnlockKeyTable.RoleKeys;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!roles.Contains(wanted[i])) return false;
            }

            return true;
        }

        // ---- Helpers --------------------------------------------------------------------

        private static string DayOf(RoundOutcome round, TimeZoneInfo zone) =>
            ProgressionCalendar.TryParseUtc(round.EndedAtUtc, out var utc)
                ? ProgressionCalendar.FormatDay(ProgressionCalendar.DayOfRound(round.LocalDay, utc, zone))
                : null;

        private static RecordStanding Unusable(string key, string problem) =>
            new RecordStanding(key, false, null, null, false, 0f, 0f, problem);
    }
}
