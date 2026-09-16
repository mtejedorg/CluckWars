using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// What the ramp, the week's three goals and today's task look like, as UI reads them. Immutable
    /// snapshot, rebuilt on every refold — the same shape <see cref="ProgressionIdentity"/> uses.
    /// </summary>
    /// <remarks>
    /// <b>Nested on purpose</b> (decision D5): UI reads progression only through
    /// <see cref="IProgressionService"/>, and the boundary scan denies every top-level progression
    /// type it has not been told about. Nesting every view inside this one type keeps the allowlist
    /// addition to exactly one entry, the same trick slice 3 used for <see cref="ProgressionIdentity"/>.
    /// </remarks>
    public sealed class ProgressionUnlocks
    {
        public static readonly ProgressionUnlocks Empty = new ProgressionUnlocks(RampView.Empty, Array.Empty<GoalView>(), null);

        public ProgressionUnlocks(RampView ramp, IReadOnlyList<GoalView> goals, DailyTaskView todaysTask)
        {
            Ramp = ramp ?? RampView.Empty;
            Goals = goals ?? Array.Empty<GoalView>();
            TodaysTask = todaysTask;
        }

        /// <summary>Where the new-player ramp stands. Never null.</summary>
        public RampView Ramp { get; }

        /// <summary>The week's three goals, in rotation order. Empty while the ramp is active.</summary>
        public IReadOnlyList<GoalView> Goals { get; }

        /// <summary>
        /// Today's task, or null. Null both while the journal has not loaded and, deliberately, while
        /// <see cref="Ramp"/> is still active — the plan hides the daily task until the ramp finishes.
        /// Named <c>TodaysTask</c> rather than <c>DailyTask</c> on purpose: the UI boundary scan denies
        /// the top-level <see cref="Progression.DailyTask"/> type by name, textually, and a
        /// same-named property would trip it on every read even though it is not that type.
        /// </summary>
        public DailyTaskView TodaysTask { get; }

        /// <summary>What the ramp unlocks and whether it is still gating anything.</summary>
        public sealed class RampView
        {
            public static readonly RampView Empty = new RampView(1, false, Array.Empty<string>(), Array.Empty<string>(),
                string.Empty, string.Empty, string.Empty);

            public RampView(int stepNumber, bool isComplete, IReadOnlyList<string> unlockedAbilityKeys,
                IReadOnlyList<string> unlockedRoleKeys, string stepName, string objectiveText, string refreshesOnDay)
            {
                StepNumber = stepNumber;
                IsComplete = isComplete;
                StepName = stepName ?? string.Empty;
                ObjectiveText = objectiveText ?? string.Empty;
                RefreshesOnDay = refreshesOnDay ?? string.Empty;
                _abilityKeys = new HashSet<string>(unlockedAbilityKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
                _roleKeys = new HashSet<string>(unlockedRoleKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
            }

            private readonly HashSet<string> _abilityKeys;
            private readonly HashSet<string> _roleKeys;

            /// <summary>1-based current step, capped at <see cref="RampController.StepCount"/>.</summary>
            public int StepNumber { get; }

            /// <summary>True once the ramp no longer gates anything — every ability and class is available.</summary>
            public bool IsComplete { get; }

            public string StepName { get; }
            public string ObjectiveText { get; }

            /// <summary>Unused while the ramp runs (there is no weekly rotation yet); kept for symmetry with <see cref="GoalView"/>.</summary>
            public string RefreshesOnDay { get; }

            /// <summary>
            /// True for every ability once <see cref="IsComplete"/>, so a caller never has to check
            /// completion separately before asking "is this locked".
            /// </summary>
            public bool IsAbilityUnlocked(string abilityKey) =>
                IsComplete || (!string.IsNullOrEmpty(abilityKey) && _abilityKeys.Contains(abilityKey));

            /// <summary>True for every role once <see cref="IsComplete"/>; see <see cref="IsAbilityUnlocked"/>.</summary>
            public bool IsRoleUnlocked(string roleKey) =>
                IsComplete || (!string.IsNullOrEmpty(roleKey) && _roleKeys.Contains(roleKey));
        }

        /// <summary>One of the week's three goals, and how close the player is to it.</summary>
        public sealed class GoalView
        {
            public GoalView(string key, string displayName, string description, float progress, float target,
                bool completed, int grainReward, string refreshesOnDay)
            {
                Key = key;
                DisplayName = displayName;
                Description = description;
                Progress = progress;
                Target = target;
                Completed = completed;
                GrainReward = grainReward;
                RefreshesOnDay = refreshesOnDay;
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string Description { get; }

            /// <summary>The week's cumulative progress toward <see cref="Target"/>.</summary>
            public float Progress { get; }
            public float Target { get; }
            public bool Completed { get; }

            /// <summary>Grain this goal has already paid (once) if <see cref="Completed"/>; the reward on offer otherwise.</summary>
            public int GrainReward { get; }

            /// <summary>The local day (<c>yyyy-MM-dd</c>) the week's three goals next rotate — never a ticking countdown.</summary>
            public string RefreshesOnDay { get; }
        }

        /// <summary>Today's single-round task.</summary>
        public sealed class DailyTaskView
        {
            public DailyTaskView(string key, string displayName, string description, bool completed, int grainReward)
            {
                Key = key;
                DisplayName = displayName;
                Description = description;
                Completed = completed;
                GrainReward = grainReward;
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string Description { get; }

            /// <summary>True once any evaluable round today satisfied it — a bad first round never burns it.</summary>
            public bool Completed { get; }
            public int GrainReward { get; }
        }
    }
}
