using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Builds <see cref="ProgressionUnlocks"/> from the journal: the ramp, this week's three goals and
    /// today's task. Pure and total, in the same style as <see cref="IdentityFold"/> — everything here
    /// is a function of <see cref="ProgressionLedger.EvaluableRounds"/>, never a second canonical walk
    /// of the journal, and nothing it meets throws.
    /// </summary>
    public static class UnlocksFold
    {
        /// <param name="rounds">The ledger's evaluable rounds, in canonical order.</param>
        /// <param name="categoryIndex">Resolves an ability key to its category for the "land N control abilities" goal. Null is tolerated — that metric then contributes nothing, never throws.</param>
        /// <param name="zone">Whose calendar bounds a week/day; null means the device's.</param>
        /// <param name="nowUtc">The instant "this week" / "today" are read relative to.</param>
        public static ProgressionUnlocks Build(IReadOnlyList<RoundOutcome> rounds, ProgressionConfigSO config,
            IAbilityCategoryIndex categoryIndex, TimeZoneInfo zone, DateTime nowUtc, List<string> problems = null)
        {
            if (config == null)
            {
                Report(problems, "There is no progression config, so the ramp, goals and daily task cannot be derived.");
                return ProgressionUnlocks.Empty;
            }

            zone = zone ?? TimeZoneInfo.Local;
            var localNow = ProgressionCalendar.LocalDay(nowUtc, zone);

            var rampState = RampController.Fold(rounds, config.RampSteps);
            var rampView = BuildRampView(rampState, config.RampSteps);

            if (!rampState.IsComplete)
            {
                // Hidden while the ramp runs — the plan's own words.
                return new ProgressionUnlocks(rampView, Array.Empty<ProgressionUnlocks.GoalView>(), null);
            }

            var goals = BuildGoals(rounds, config, categoryIndex, zone, localNow, problems);
            var dailyTask = BuildDailyTask(rounds, zone, localNow);
            return new ProgressionUnlocks(rampView, goals, dailyTask);
        }

        /// <summary>
        /// The Grain every completed goal-week and daily-task-day has ever paid, lifetime, re-derived
        /// from the same rounds. A separate figure from <see cref="ProgressionLedger.GrainBalance"/>
        /// (decision D6) — nothing about a round's own Grain changes meaning.
        /// </summary>
        public static long ComputeBonusGrain(IReadOnlyList<RoundOutcome> rounds, ProgressionConfigSO config,
            IAbilityCategoryIndex categoryIndex, TimeZoneInfo zone)
        {
            if (config == null || rounds == null || rounds.Count == 0) return 0;
            zone = zone ?? TimeZoneInfo.Local;

            var byWeek = new Dictionary<string, List<RoundOutcome>>(StringComparer.Ordinal);
            var byDay = new Dictionary<string, List<RoundOutcome>>(StringComparer.Ordinal);
            foreach (var round in rounds)
            {
                if (!TryDayOf(round, zone, out var day)) continue;

                string weekKey = ProgressionCalendar.IsoWeekKey(day);
                if (!byWeek.TryGetValue(weekKey, out var weekList)) byWeek[weekKey] = weekList = new List<RoundOutcome>();
                weekList.Add(round);

                string dayKey = ProgressionCalendar.FormatDay(day);
                if (!byDay.TryGetValue(dayKey, out var dayList)) byDay[dayKey] = dayList = new List<RoundOutcome>();
                dayList.Add(round);
            }

            long total = 0;
            foreach (var pair in byWeek)
            {
                foreach (var selection in GoalRotation.ForWeek(pair.Key, config.GoalTemplates))
                {
                    var tier = selection.Template.Tiers[selection.TierIndex];
                    float progress = MetricSum(pair.Value, selection.Template.Metric, categoryIndex);
                    if (progress >= tier.Threshold) total += tier.GrainReward;
                }
            }

            foreach (var pair in byDay)
            {
                var task = DailyTask.ForDay(pair.Key);
                bool met = false;
                foreach (var round in pair.Value)
                {
                    if (DailyTask.IsSatisfiedBy(task, round)) { met = true; break; }
                }
                if (met) total += Math.Max(0, config.DailyTaskBonus);
            }

            return total;
        }

        // ---- Ramp -------------------------------------------------------------------------

        private static ProgressionUnlocks.RampView BuildRampView(RampState state, IReadOnlyList<RampStepSO> steps)
        {
            var usable = RampController.UsableSteps(steps);
            string name = string.Empty, objective = string.Empty;
            int displayIndex = state.StepNumber - 1;
            if (!state.IsComplete && displayIndex >= 0 && displayIndex < usable.Count)
            {
                name = usable[displayIndex].DisplayName;
                objective = usable[displayIndex].ObjectiveDescription;
            }

            return new ProgressionUnlocks.RampView(state.StepNumber, state.IsComplete,
                state.UnlockedAbilityKeys, state.UnlockedRoleKeys, name, objective, string.Empty);
        }

        // ---- Goals ------------------------------------------------------------------------

        private static IReadOnlyList<ProgressionUnlocks.GoalView> BuildGoals(IReadOnlyList<RoundOutcome> rounds,
            ProgressionConfigSO config, IAbilityCategoryIndex categoryIndex, TimeZoneInfo zone, DateTime localNow,
            List<string> problems)
        {
            string weekKey = ProgressionCalendar.IsoWeekKey(localNow);
            string refreshesOn = ProgressionCalendar.FormatDay(ProgressionCalendar.IsoWeekStart(localNow).AddDays(7));

            var thisWeekRounds = new List<RoundOutcome>();
            for (int i = 0; rounds != null && i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round != null && TryDayOf(round, zone, out var day) && ProgressionCalendar.IsoWeekKey(day) == weekKey)
                {
                    thisWeekRounds.Add(round);
                }
            }

            var selections = GoalRotation.ForWeek(weekKey, config.GoalTemplates);
            var views = new List<ProgressionUnlocks.GoalView>(selections.Count);
            foreach (var selection in selections)
            {
                var template = selection.Template;
                var tier = template.Tiers[selection.TierIndex];
                float progress = MetricSum(thisWeekRounds, template.Metric, categoryIndex);
                bool completed = progress >= tier.Threshold;

                views.Add(new ProgressionUnlocks.GoalView(template.Key, template.NameFor(selection.TierIndex),
                    template.DescriptionFor(selection.TierIndex), progress, tier.Threshold, completed,
                    tier.GrainReward, refreshesOn));
            }

            if (selections.Count == 0 && config.GoalTemplates != null && config.GoalTemplates.Length > 0)
            {
                Report(problems, "Every goal template in ProgressionConfig.GoalTemplates has a Problem(), so no " +
                    "weekly goals could be selected this week.");
            }

            return views;
        }

        // ---- Daily task ---------------------------------------------------------------------

        private static ProgressionUnlocks.DailyTaskView BuildDailyTask(IReadOnlyList<RoundOutcome> rounds,
            TimeZoneInfo zone, DateTime localNow)
        {
            string dayKey = ProgressionCalendar.FormatDay(localNow);
            var task = DailyTask.ForDay(dayKey);

            bool completed = false;
            for (int i = 0; rounds != null && i < rounds.Count && !completed; i++)
            {
                var round = rounds[i];
                if (round == null || !TryDayOf(round, zone, out var day)) continue;
                if (ProgressionCalendar.FormatDay(day) != dayKey) continue;
                completed = DailyTask.IsSatisfiedBy(task, round);
            }

            return new ProgressionUnlocks.DailyTaskView(task.Key, task.DisplayName, task.Description, completed, 0);
        }

        // ---- Helpers ------------------------------------------------------------------------

        private static float MetricSum(IReadOnlyList<RoundOutcome> rounds, GoalMetric metric, IAbilityCategoryIndex categoryIndex)
        {
            float sum = 0f;
            for (int i = 0; i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null) continue;

                switch (metric)
                {
                    case GoalMetric.BankedTotal:
                        sum += round.BankedTotal;
                        break;
                    case GoalMetric.RivalsRobbed:
                        sum += round.RivalsRobbed;
                        break;
                    case GoalMetric.RoundsStolenExceededBanked:
                        if (round.StolenTotal > round.BankedTotal) sum += 1f;
                        break;
                    case GoalMetric.ControlAbilitiesConnected:
                        sum += ControlConnectedIn(round, categoryIndex);
                        break;
                    case GoalMetric.RoundsPlayed:
                        sum += 1f;
                        break;
                    default:
                        // A metric added to the enum and not here contributes nothing rather than throwing.
                        break;
                }
            }

            return sum;
        }

        private static float ControlConnectedIn(RoundOutcome round, IAbilityCategoryIndex categoryIndex)
        {
            if (round.Abilities == null || categoryIndex == null) return 0f;

            float sum = 0f;
            foreach (var tally in round.Abilities)
            {
                if (categoryIndex.TryGetCategory(tally.Key, out string categoryKey) &&
                    string.Equals(categoryKey, UnlockKeyTable.CategoryControl, StringComparison.Ordinal))
                {
                    sum += tally.Connected;
                }
            }

            return sum;
        }

        private static bool TryDayOf(RoundOutcome round, TimeZoneInfo zone, out DateTime day)
        {
            day = default;
            if (round == null) return false;
            if (!ProgressionCalendar.TryParseUtc(round.EndedAtUtc, out var utc)) return false;
            day = ProgressionCalendar.DayOfRound(round.LocalDay, utc, zone);
            return true;
        }

        private static void Report(List<string> problems, string message)
        {
            if (message != null) problems?.Add(message);
        }
    }
}
