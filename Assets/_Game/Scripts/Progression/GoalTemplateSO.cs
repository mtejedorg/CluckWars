using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>What a weekly goal template counts, cumulative across every evaluable round in one ISO week.</summary>
    public enum GoalMetric
    {
        /// <summary>Sum of <see cref="RoundOutcome.BankedTotal"/> across the week.</summary>
        BankedTotal,

        /// <summary>Sum of <see cref="RoundOutcome.RivalsRobbed"/> across the week.</summary>
        RivalsRobbed,

        /// <summary>Count of the week's rounds where <c>StolenTotal &gt; BankedTotal</c>.</summary>
        RoundsStolenExceededBanked,

        /// <summary>
        /// Sum of <see cref="AbilityTally.Connected"/> across the week for every ability whose category
        /// (<see cref="IAbilityCategoryIndex"/>) is <see cref="UnlockKeyTable.CategoryControl"/>.
        /// </summary>
        ControlAbilitiesConnected,

        /// <summary>Count of evaluable rounds played in the week.</summary>
        RoundsPlayed,
    }

    /// <summary>One difficulty tier of a goal: the cumulative target and what it pays.</summary>
    [Serializable]
    public struct GoalTier
    {
        [Min(0f)] public float Threshold;
        [Min(0)] public int GrainReward;
    }

    /// <summary>
    /// One weekly-goal template: a metric plus a ladder of thresholds. <see cref="GoalRotation"/> picks
    /// three distinct templates (and one tier each) per ISO week from the shipped catalogue
    /// (<see cref="ProgressionConfigSO.GoalTemplates"/>), the same "asset is the predicate, code stays
    /// generic" shape <see cref="RecordDefinitionSO"/> uses for records.
    /// </summary>
    /// <remarks>
    /// Assets live in <c>Assets/_Game/Data/Progression/Goals/</c>. Nothing about a goal is ever
    /// written to the journal — like a record, it is re-evaluated over the ledger's rounds every
    /// fold, grouped by the ISO week they fall in, so a template added later can credit a week
    /// that already happened to have been rotated onto it (it cannot: rotation is seeded by
    /// week key and template identity together, so a new template simply never appears in a past
    /// week's three — but the completed weeks it IS rotated into from here on are still derived
    /// from history, never journaled as their own fact).
    /// </remarks>
    [CreateAssetMenu(fileName = "Goal", menuName = "Progression/Goal Template")]
    public sealed class GoalTemplateSO : ScriptableObject
    {
        /// <summary>The shape every key must have: <c>goal.</c> then lower-case words and underscores.</summary>
        public static readonly Regex KeyShape = new Regex(@"^goal\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

        [Tooltip("Stable id for save data. Filled once from the asset name, never overwritten — renaming " +
                 "the asset must NOT change it. Don't edit by hand.")]
        [SerializeField] private string _key;

        /// <summary>Stable key, e.g. <c>"goal.bank_total"</c>. Never derived at runtime.</summary>
        public string Key => _key;

        [Header("Presentation")]
        [Tooltip("The goal's name with '{0}' where the chosen tier's threshold is substituted, e.g. \"Bank {0}\".")]
        public string DisplayNameFormat;

        [Tooltip("What the player has to do, same '{0}' substitution as the name.")]
        [TextArea(2, 3)] public string DescriptionFormat;

        public GoalMetric Metric;

        [Tooltip("Difficulty ladder. GoalRotation picks one tier per week this template is rotated in; at least one is required.")]
        public GoalTier[] Tiers = Array.Empty<GoalTier>();

        /// <summary>
        /// Why this definition cannot be used, or null when it can. Reported per template, like
        /// <see cref="RecordDefinitionSO.Problem"/>: one mis-authored asset must not blank the whole
        /// goals list.
        /// </summary>
        public string Problem()
        {
            if (string.IsNullOrEmpty(_key)) return "it has no key";
            if (!KeyShape.IsMatch(_key)) return $"its key '{_key}' does not match {KeyShape}";
            if (string.IsNullOrEmpty(DisplayNameFormat)) return "it has no display name";
            if (Tiers == null || Tiers.Length == 0) return "it has no tiers";

            float previous = float.NegativeInfinity;
            foreach (var tier in Tiers)
            {
                if (float.IsNaN(tier.Threshold) || float.IsInfinity(tier.Threshold) || tier.Threshold <= 0f)
                    return "a tier's threshold is not a positive, finite number";
                if (tier.Threshold <= previous) return "tiers must be strictly increasing";
                previous = tier.Threshold;
            }

            return null;
        }

        /// <summary>The display name for one chosen tier, with its threshold substituted in.</summary>
        public string NameFor(int tierIndex) => Format(DisplayNameFormat, tierIndex);

        /// <summary>The description for one chosen tier, with its threshold substituted in.</summary>
        public string DescriptionFor(int tierIndex) => Format(DescriptionFormat, tierIndex);

        private string Format(string template, int tierIndex)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (Tiers == null || tierIndex < 0 || tierIndex >= Tiers.Length) return template;

            float threshold = Tiers[tierIndex].Threshold;
            string number = threshold == Mathf.Floor(threshold)
                ? threshold.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : threshold.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return template.Replace("{0}", number);
        }

#if UNITY_EDITOR
        // Editor-only, like AbilityBaseSO's and RecordDefinitionSO's: runtime code only ever READS
        // Key, so a renamed asset can never re-derive a different key on a player's device.

        /// <summary>The key a fresh asset gets from its name: <c>"BankTotal"</c> → <c>"goal.bank_total"</c>. Null when nothing usable is left.</summary>
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
            return body.Length == 0 ? null : "goal." + body;
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
