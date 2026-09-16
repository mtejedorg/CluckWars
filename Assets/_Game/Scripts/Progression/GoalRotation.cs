using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Picks the week's three weekly goals from the shipped <see cref="GoalTemplateSO"/> catalogue.
    /// Pure and total: the same <paramref name="weekKey"/> and catalogue always produce the same three
    /// goals, on any device, with nothing stored — that is what lets the rotation "expose the refresh
    /// day, never a ticking countdown" (the plan's own words): the UI just formats
    /// <see cref="ProgressionCalendar.IsoWeekStart"/> of next week, it never counts down to it.
    /// </summary>
    public static class GoalRotation
    {
        public const int GoalsPerWeek = 3;

        /// <summary>One selected goal for the week: which template, and which of its tiers.</summary>
        public readonly struct Selection
        {
            public Selection(GoalTemplateSO template, int tierIndex)
            {
                Template = template;
                TierIndex = tierIndex;
            }

            public GoalTemplateSO Template { get; }
            public int TierIndex { get; }
        }

        /// <summary>
        /// Up to <see cref="GoalsPerWeek"/> distinct templates for <paramref name="weekKey"/>
        /// (<see cref="ProgressionCalendar.IsoWeekKey"/> shape), each with one seeded tier. Fewer than
        /// <see cref="GoalsPerWeek"/> only when the usable catalogue itself is smaller. Never throws: a
        /// null catalogue, or one where every template has a <see cref="GoalTemplateSO.Problem"/>,
        /// returns an empty list.
        /// </summary>
        public static IReadOnlyList<Selection> ForWeek(string weekKey, IReadOnlyList<GoalTemplateSO> templates)
        {
            var usable = new List<GoalTemplateSO>();
            if (templates != null)
            {
                foreach (var template in templates)
                {
                    if (template != null && template.Problem() == null) usable.Add(template);
                }
            }

            if (usable.Count == 0) return Array.Empty<Selection>();

            // Definition order first, so the shuffle is reproducible independent of however the
            // caller's array happens to be ordered in memory.
            var rng = new Random(StableHash(weekKey ?? string.Empty));
            var indices = new List<int>(usable.Count);
            for (int i = 0; i < usable.Count; i++) indices.Add(i);
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            int count = Math.Min(GoalsPerWeek, usable.Count);
            var result = new List<Selection>(count);
            for (int i = 0; i < count; i++)
            {
                var template = usable[indices[i]];
                int tierIndex = rng.Next(template.Tiers.Length);
                result.Add(new Selection(template, tierIndex));
            }

            return result;
        }

        /// <summary>Small ordinal string hash — deterministic across runs and platforms, unlike <see cref="string.GetHashCode()"/>.</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < s.Length; i++) hash = hash * 31 + s[i];
                return hash;
            }
        }
    }
}
