using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>What a record is about. Also the filter the profile page offers over the list.</summary>
    public enum RecordGroup
    {
        Foraging,
        Thievery,
        Denial,
        Mastery,
    }

    /// <summary>Whether a record is earned inside one round or over a career.</summary>
    public enum RecordScope
    {
        /// <summary>Every <see cref="RecordDefinitionSO.Conditions"/> must hold on one round.</summary>
        SingleRound,

        /// <summary>The whole history is asked one question — <see cref="RecordDefinitionSO.CareerKind"/>.</summary>
        Career,
    }

    /// <summary>A fact of one round a condition can test. Every one of these is already in the journal.</summary>
    public enum RecordMetric
    {
        BankedTotal,
        StolenTotal,
        RivalsRobbed,
        OpponentsDisabled,
        Placement,

        /// <summary><c>StolenTotal - BankedTotal</c>: the "took more than I earned" shape.</summary>
        StolenMinusBanked,
    }

    /// <summary>How a metric is compared with a condition's value.</summary>
    public enum RecordComparison
    {
        AtLeast,
        MoreThan,
        LessThan,
        AtMost,
        Equal,
    }

    /// <summary>The questions a <see cref="RecordScope.Career"/> record can ask of the whole history.</summary>
    public enum CareerRecordKind
    {
        /// <summary>Rounds finished at placement 1 cover every role key in <see cref="UnlockKeyTable.RoleKeys"/>.</summary>
        WinWithEveryRole,

        /// <summary>Some role reached mastery level <see cref="RecordDefinitionSO.CareerValue"/> or higher.</summary>
        MasteryLevelOnAnyRole,
    }

    /// <summary>One test against one round's facts. All of a record's conditions must hold, on the same round.</summary>
    [Serializable]
    public struct RecordCondition
    {
        public RecordMetric Metric;
        public RecordComparison Comparison;
        public float Value;

        public bool Holds(float actual) => Comparison switch
        {
            RecordComparison.AtLeast => actual >= Value,
            RecordComparison.MoreThan => actual > Value,
            RecordComparison.LessThan => actual < Value,
            RecordComparison.AtMost => actual <= Value,
            RecordComparison.Equal => Mathf.Approximately(actual, Value),

            // Unreachable while the enum and this switch agree; a new comparison that forgot a row
            // must not silently award the record to everyone.
            _ => false,
        };
    }

    /// <summary>
    /// One record: a named thing the player either has or has not done, and the nameplate title it
    /// awards. Assets live in <c>Assets/_Game/Data/Progression/Records/</c> and are listed on
    /// <see cref="ProgressionConfigSO.Records"/>.
    /// </summary>
    /// <remarks>
    /// <b>The predicate is data, not code.</b> A record is a list of conditions (or one career
    /// question), so adding one is authoring an asset rather than writing a subclass — and, because
    /// records are re-evaluated over the whole journal every fold, a record added later credits play
    /// that already happened. Nothing about a record is ever written to the journal.
    /// <para>
    /// <b><see cref="Key"/> is filled once and never regenerated</b>, exactly like
    /// <c>AbilityBaseSO.UnlockKey</c>: renaming the asset must not change what a player has earned.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Record", menuName = "Progression/Record Definition")]
    public sealed class RecordDefinitionSO : ScriptableObject
    {
        /// <summary>The shape every key must have: <c>record.</c> then lower-case words and underscores.</summary>
        public static readonly Regex KeyShape = new Regex(@"^record\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

        [Tooltip("Stable id for save data. Filled once from the asset name, never overwritten — renaming " +
                 "the asset must NOT change it. Don't edit by hand.")]
        [SerializeField] private string _key;

        /// <summary>Stable key, e.g. <c>"record.full_coop"</c>. Never derived at runtime.</summary>
        public string Key => _key;

        [Header("Presentation")]
        [Tooltip("The record's name, as the profile page lists it.")]
        public string DisplayName;

        [Tooltip("What the player has to do. Shown whether or not it is earned — the unearned list is the content.")]
        [TextArea(2, 3)] public string Description;

        [Tooltip("The nameplate title earning this grants. Leave empty for a record that grants no title.")]
        public string Title;

        public RecordGroup Group;

        [Tooltip("True when a player can earn this in a round they lose. Drives the 'achievable while losing' filter.")]
        public bool AchievableWhileLosing;

        [Header("Predicate")]
        public RecordScope Scope = RecordScope.SingleRound;

        [Tooltip("SingleRound only: every condition must hold on the same round.")]
        public RecordCondition[] Conditions = Array.Empty<RecordCondition>();

        [Tooltip("SingleRound only: restrict to one role key (UnlockKeyTable). Empty means any role.")]
        public string RoleKey = string.Empty;

        [Tooltip("Career only: which question the whole history is asked.")]
        public CareerRecordKind CareerKind = CareerRecordKind.WinWithEveryRole;

        [Tooltip("Career only: the level MasteryLevelOnAnyRole asks for. Ignored by WinWithEveryRole.")]
        public float CareerValue;

        /// <summary>
        /// Why this definition cannot be evaluated, or null when it can. Reported per record rather
        /// than thrown: one mis-authored asset must not blank the whole profile page.
        /// </summary>
        public string Problem()
        {
            if (string.IsNullOrEmpty(_key)) return "it has no key";
            if (!KeyShape.IsMatch(_key)) return $"its key '{_key}' does not match {KeyShape}";
            if (string.IsNullOrEmpty(DisplayName)) return "it has no display name";

            if (Scope == RecordScope.SingleRound)
            {
                if (Conditions == null || Conditions.Length == 0) return "it is a single-round record with no conditions";
                foreach (var condition in Conditions)
                {
                    if (float.IsNaN(condition.Value) || float.IsInfinity(condition.Value))
                        return $"condition on {condition.Metric} has a non-finite value";
                }

                return null;
            }

            if (CareerKind == CareerRecordKind.MasteryLevelOnAnyRole && !(CareerValue >= 1f))
                return "MasteryLevelOnAnyRole needs a level of at least 1";

            return null;
        }

#if UNITY_EDITOR
        // Editor-only, like AbilityBaseSO's: runtime code only ever READS Key, so a renamed asset can
        // never re-derive a different key on a player's device.

        /// <summary>The key a fresh asset gets from its name: <c>"FullCoop"</c> → <c>"record.full_coop"</c>. Null when nothing usable is left.</summary>
        public static string DeriveKey(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return null;

            var snake = new System.Text.StringBuilder(assetName.Length + 8);
            for (int i = 0; i < assetName.Length; i++)
            {
                char c = assetName[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(assetName[i - 1]) || char.IsDigit(assetName[i - 1]))) snake.Append('_');
                if (char.IsLetterOrDigit(c)) snake.Append(char.ToLowerInvariant(c));
                else snake.Append('_');
            }

            string body = snake.ToString().Trim('_');
            while (body.Contains("__")) body = body.Replace("__", "_");
            return body.Length == 0 ? null : "record." + body;
        }

        /// <summary>Fill-once: assigns <see cref="Key"/> only when it is empty. Returns true when it did.</summary>
        public bool AssignKeyIfMissing()
        {
            if (!string.IsNullOrEmpty(_key)) return false;

            string derived = DeriveKey(name);
            if (derived == null) return false;

            _key = derived;
            return true;
        }

        private void OnValidate() => AssignKeyIfMissing();
#endif
    }
}
