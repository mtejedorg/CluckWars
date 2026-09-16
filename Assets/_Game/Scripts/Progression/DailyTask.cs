using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// The one single-round objective offered for the local day: shown before the day's first round,
    /// <b>open all day</b> — a bad first round never burns it, because completion is "any evaluable
    /// round today satisfied it", not "the first round did". Hidden entirely while the ramp runs
    /// (<see cref="ProgressionUnlocks.DailyTask"/> is null then).
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="RecordCondition"/>/<see cref="RecordMetric"/> rather than inventing a second
    /// per-round predicate shape: a daily task and a single-round record ask the exact same kind of
    /// question ("did one round's facts satisfy this"), so <see cref="RecordEngine.MetricOf"/> is the
    /// one place that reads a <see cref="RoundOutcome"/> into a number. Unlike
    /// <see cref="GoalTemplateSO"/>, there is no asset catalogue for these: the pool is small, fixed
    /// for this pitch milestone, and not designer-authored per the plan, so it is a plain in-code
    /// table rather than a folder of near-identical assets.
    /// </remarks>
    public static class DailyTask
    {
        /// <summary>One candidate daily task: a stable key, display text and the conditions a round must meet.</summary>
        public sealed class Template
        {
            public Template(string key, string displayName, string description, RecordCondition[] conditions)
            {
                Key = key;
                DisplayName = displayName;
                Description = description;
                Conditions = conditions ?? Array.Empty<RecordCondition>();
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string Description { get; }

            /// <summary>Every condition must hold on the same round. Empty means "finish any round".</summary>
            public RecordCondition[] Conditions { get; }
        }

        /// <summary>
        /// The fixed daily-task catalogue. Every one is completable while losing (Maestro's ruling,
        /// restated for slice 4): none of these conditions touch <see cref="RecordMetric.Placement"/>.
        /// </summary>
        public static readonly IReadOnlyList<Template> Templates = new[]
        {
            new Template("daily.play_a_round", "Play a Round", "Finish one round — any placement.",
                Array.Empty<RecordCondition>()),
            new Template("daily.bank_5", "Bank 5", "Bank at least 5 in one round.",
                new[] { new RecordCondition { Metric = RecordMetric.BankedTotal, Comparison = RecordComparison.AtLeast, Value = 5f } }),
            new Template("daily.rob_2_rivals", "Rob 2 Rivals", "Take from 2 different rivals in one round.",
                new[] { new RecordCondition { Metric = RecordMetric.RivalsRobbed, Comparison = RecordComparison.AtLeast, Value = 2f } }),
            new Template("daily.steal_3", "Steal 3", "Take at least 3 total from rivals in one round.",
                new[] { new RecordCondition { Metric = RecordMetric.StolenTotal, Comparison = RecordComparison.AtLeast, Value = 3f } }),
            new Template("daily.disable_1", "Disable a Rival", "Take an opponent out of play once in a round.",
                new[] { new RecordCondition { Metric = RecordMetric.OpponentsDisabled, Comparison = RecordComparison.AtLeast, Value = 1f } }),
        };

        /// <summary>
        /// The day's task, seeded by <paramref name="dayKey"/> (<see cref="ProgressionCalendar.FormatDay"/>
        /// shape) so every device and every reload picks the same one without storing a choice. Never
        /// null and never throws: an empty or unrecognised key still resolves to a real template.
        /// </summary>
        public static Template ForDay(string dayKey) =>
            Templates[(int)((uint)StableHash(dayKey ?? string.Empty) % (uint)Templates.Count)];

        /// <summary>True when every condition of <paramref name="template"/> holds on <paramref name="round"/>. Total: a null template or round is not satisfied.</summary>
        public static bool IsSatisfiedBy(Template template, RoundOutcome round)
        {
            if (template == null || round == null) return false;
            for (int i = 0; i < template.Conditions.Length; i++)
            {
                var condition = template.Conditions[i];
                float actual = RecordEngine.MetricOf(round, condition.Metric);
                if (!condition.Holds(actual)) return false;
            }

            return true;
        }

        /// <summary>Same small ordinal string hash <see cref="GoalRotation"/> uses, kept independent so neither's shuffle depends on the other's seed derivation.</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < s.Length; i++) hash = hash * 31 + s[i];
                return hash;
            }
        }
    }
}
