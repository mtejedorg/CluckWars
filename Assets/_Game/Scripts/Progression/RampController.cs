using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// The ramp's state: pure and total, folded fresh from <see cref="ProgressionLedger.EvaluableRounds"/>
    /// every time — nothing about "which step" or "what is unlocked" is ever stored. A step, once
    /// cleared, stays cleared for the same reason a record, once earned, stays earned: the underlying
    /// sums (banked, stolen) and role-played facts are monotonic over the journal's history.
    /// </summary>
    public sealed class RampState
    {
        public static readonly RampState Empty = new RampState(1, false,
            Array.Empty<string>(), Array.Empty<string>());

        public RampState(int stepNumber, bool isComplete, IReadOnlyList<string> unlockedAbilityKeys,
            IReadOnlyList<string> unlockedRoleKeys)
        {
            StepNumber = stepNumber;
            IsComplete = isComplete;
            UnlockedAbilityKeys = unlockedAbilityKeys ?? Array.Empty<string>();
            UnlockedRoleKeys = unlockedRoleKeys ?? Array.Empty<string>();
        }

        /// <summary>1-based current step, capped at <see cref="RampController.StepCount"/> even once <see cref="IsComplete"/>.</summary>
        public int StepNumber { get; }

        /// <summary>True once the last step's objective has been met — the ramp no longer gates anything.</summary>
        public bool IsComplete { get; }

        public IReadOnlyList<string> UnlockedAbilityKeys { get; }
        public IReadOnlyList<string> UnlockedRoleKeys { get; }
    }

    /// <summary>
    /// Folds the ramp's four fixed steps over the journal's rounds. See <see cref="RampStepSO"/>'s
    /// remarks for why the advance conditions are hard-coded here rather than data on the asset.
    /// </summary>
    public static class RampController
    {
        public const int StepCount = 4;

        private const float Step1BankTarget = 10f;
        private const float Step2BankTarget = 20f;
        private const int Step2HeadbuttConnectsInARound = 2;
        private const float Step3StealTarget = 5f;
        private const string HeadbuttKey = "ability.headbutt";

        /// <summary>
        /// The ramp's state for <paramref name="rounds"/> (the ledger's evaluable rounds, in canonical
        /// order) against the shipped <paramref name="steps"/>. Never throws: fewer than
        /// <see cref="StepCount"/> usable steps caps the ramp at whatever is actually available, and a
        /// null or empty round list is simply step 1, not started.
        /// </summary>
        public static RampState Fold(IReadOnlyList<RoundOutcome> rounds, IReadOnlyList<RampStepSO> steps)
        {
            var usableSteps = UsableSteps(steps);
            if (usableSteps.Count == 0) return RampState.Empty;

            float bankedTotal = 0f;
            float stolenTotal = 0f;
            bool headbuttTwiceInARound = false;
            var rolesPlayed = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; rounds != null && i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null) continue;

                bankedTotal += round.BankedTotal;
                stolenTotal += round.StolenTotal;
                if (!string.IsNullOrEmpty(round.RoleKey)) rolesPlayed.Add(round.RoleKey);

                if (!headbuttTwiceInARound && round.Abilities != null)
                {
                    foreach (var tally in round.Abilities)
                    {
                        if (string.Equals(tally.Key, HeadbuttKey, StringComparison.Ordinal) &&
                            tally.Connected >= Step2HeadbuttConnectsInARound)
                        {
                            headbuttTwiceInARound = true;
                            break;
                        }
                    }
                }
            }

            bool step1Cleared = bankedTotal >= Step1BankTarget;
            bool step2Cleared = step1Cleared && (headbuttTwiceInARound || bankedTotal >= Step2BankTarget);
            bool step3Cleared = step2Cleared && stolenTotal >= Step3StealTarget;

            int reachedStep = 1;
            if (step1Cleared && usableSteps.Count >= 2) reachedStep = 2;
            if (step2Cleared && usableSteps.Count >= 3) reachedStep = 3;
            if (step3Cleared && usableSteps.Count >= 4) reachedStep = 4;

            var abilityKeys = new List<string>();
            var roleKeys = new List<string>();
            bool complete = false;

            for (int stepIndex = 0; stepIndex < reachedStep; stepIndex++)
            {
                var step = usableSteps[stepIndex];
                foreach (string key in step.AbilityKeysGranted) if (!string.IsNullOrEmpty(key)) abilityKeys.Add(key);

                if (!step.GrantRolesSequentially)
                {
                    foreach (string key in step.RoleKeysGranted) if (!string.IsNullOrEmpty(key)) roleKeys.Add(key);
                    continue;
                }

                // Sequential grant: RoleKeysGranted[0] the moment the step is active; each next entry
                // only once a round exists with the PREVIOUS entry's role key. The step (and the whole
                // ramp, since this is the last step by construction) completes once a round exists with
                // the LAST entry's role key.
                bool isLastStep = stepIndex == reachedStep - 1;
                for (int j = 0; j < step.RoleKeysGranted.Length; j++)
                {
                    string key = step.RoleKeysGranted[j];
                    if (string.IsNullOrEmpty(key)) continue;
                    if (j > 0 && !rolesPlayed.Contains(step.RoleKeysGranted[j - 1])) break;
                    roleKeys.Add(key);
                }

                if (isLastStep && step.RoleKeysGranted.Length > 0)
                {
                    string finalKey = step.RoleKeysGranted[step.RoleKeysGranted.Length - 1];
                    complete = !string.IsNullOrEmpty(finalKey) && rolesPlayed.Contains(finalKey);
                }
            }

            // The ramp is only ever "complete" by clearing the LAST usable step's sequential chain.
            // A ramp shorter than 4 usable steps (a misconfigured build) never claims completion from a
            // non-sequential step alone — nothing else in this fold sets `complete`.
            if (reachedStep < usableSteps.Count) complete = false;

            return new RampState(Math.Min(reachedStep, StepCount), complete, abilityKeys, roleKeys);
        }

        /// <summary>
        /// The steps <see cref="Fold"/> actually used, in order: null slots and steps with a
        /// <see cref="RampStepSO.Problem"/> are dropped. Public so <see cref="UnlocksFold"/> can show
        /// the same step's display text it advanced against, rather than re-deriving the filter.
        /// </summary>
        public static List<RampStepSO> UsableSteps(IReadOnlyList<RampStepSO> steps)
        {
            var usable = new List<RampStepSO>();
            if (steps == null) return usable;

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step != null && step.Problem() == null) usable.Add(step);
            }

            return usable;
        }
    }
}
