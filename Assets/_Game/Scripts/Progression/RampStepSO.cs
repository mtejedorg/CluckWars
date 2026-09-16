using System;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>
    /// One step of the fixed 4-step new-player ramp: what it unlocks and how it explains itself.
    /// <b>Asset references only, never scene-baked</b> — the four shipped assets live under
    /// <c>Assets/_Game/Data/Progression/Ramp/</c> and are listed on
    /// <see cref="ProgressionConfigSO.RampSteps"/>, the one place anything resolves them from, exactly
    /// like <see cref="RecordDefinitionSO"/> and <see cref="ProgressionConfigSO.Records"/>. Every key
    /// here is a stable unlock key (<see cref="AbilityBaseSO.UnlockKey"/> / <see cref="UnlockKeyTable.RoleKey"/>
    /// shape) resolved against the live registry by whoever reads <see cref="ProgressionUnlocks"/> —
    /// never against this asset's prose, and never baked into a scene or prefab (the exact bug class
    /// <c>memory/bot-loadout-staleness-bug.md</c> documents: a scene-baked preset goes stale the moment
    /// the roster changes under it).
    /// </summary>
    /// <remarks>
    /// <b>What advances a step is not on this asset.</b> The ramp is exactly four fixed, hand-designed
    /// steps for this pitch milestone (never designer-extended without a code change — the plan pins
    /// the table), so <see cref="RampController"/> hard-codes each step's advance condition (bank 10,
    /// bank 20 or two connected Headbutts, steal 5, one round per sequential role) the same way
    /// <see cref="RecordEngine"/> hard-codes <c>CareerRecordKind</c>'s two questions rather than
    /// modelling a general predicate language for two cases. This asset supplies the two things that
    /// genuinely are data: what the step unlocks, and what it says about itself.
    /// </remarks>
    [CreateAssetMenu(fileName = "RampStep", menuName = "Progression/Ramp Step")]
    public sealed class RampStepSO : ScriptableObject
    {
        [Header("Presentation")]
        public string DisplayName;

        [Tooltip("What the player must do to clear this step and reach the next one (or, on the last step, finish the ramp).")]
        [TextArea(2, 3)] public string ObjectiveDescription;

        [Header("Grants")]
        [Tooltip("Stable ability unlock keys (AbilityBaseSO.UnlockKey) granted the moment this step becomes active.")]
        public string[] AbilityKeysGranted = Array.Empty<string>();

        [Tooltip("Stable role keys (UnlockKeyTable.RoleKeys) granted by this step. See GrantRolesSequentially for how many are granted at once.")]
        public string[] RoleKeysGranted = Array.Empty<string>();

        [Tooltip("False (default): every RoleKeysGranted key unlocks the moment this step becomes active. " +
                 "True (step 4 only): RoleKeysGranted[0] unlocks on entry, and each next entry unlocks only " +
                 "once a round has been played as the previous one — see RampController.")]
        public bool GrantRolesSequentially;

        /// <summary>Why this step cannot be used, or null when it can.</summary>
        public string Problem()
        {
            if (string.IsNullOrEmpty(DisplayName)) return "it has no display name";
            if (string.IsNullOrEmpty(ObjectiveDescription)) return "it has no objective description";
            return null;
        }
    }
}
