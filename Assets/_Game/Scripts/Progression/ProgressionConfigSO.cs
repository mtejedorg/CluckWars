using System;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>How a banner can come to be owned.</summary>
    /// <remarks>
    /// <see cref="Purchasable"/> ships with no banner using it and there is no store: the value exists
    /// so "at least one part of a nameplate can never be bought" is a property the tests can actually
    /// check, rather than one that is true only because nothing is for sale yet.
    /// </remarks>
    public enum BannerSource
    {
        /// <summary>Owned from the first launch.</summary>
        Starter,

        /// <summary>Owned once <see cref="BannerDefinition.UnlockRecordKey"/> is earned.</summary>
        Earned,

        /// <summary>Would have to be bought. Nothing in this build can own one.</summary>
        Purchasable,
    }

    /// <summary>One banner: the plate the nameplate is drawn on.</summary>
    [Serializable]
    public struct BannerDefinition
    {
        [Tooltip("Stable key, e.g. banner.barnwood. Never change one that has shipped.")]
        public string Key;

        [Tooltip("What the picker calls it.")]
        public string DisplayName;

        [Tooltip("The USS class that paints it, e.g. cw-plate--barnwood.")]
        public string UssClass;

        public BannerSource Source;

        [Tooltip("Earned banners only: the record key that unlocks it.")]
        public string UnlockRecordKey;
    }

    /// <summary>
    /// The Grain earn constants. One asset, <c>Assets/_Game/Data/Progression/ProgressionConfig.asset</c>,
    /// bound project-wide by <c>ProjectInstaller</c> from its one inspector slot.
    /// </summary>
    /// <remarks>
    /// The field initialisers mirror the shipped asset: the installer falls back to a default
    /// instance (with a loud warning) when the slot is empty.
    /// <para>
    /// <b>The wallet is re-derived from this asset every time the journal is folded.</b> Changing a
    /// value here changes every past round's Grain, and so the balance. That is intended while Grain
    /// is only earned; the first slice that lets the player spend it must record spending in the
    /// journal as its own facts.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "ProgressionConfig", menuName = "Progression/Progression Config")]
    public sealed class ProgressionConfigSO : ScriptableObject
    {
        [Header("Per round")]
        [Tooltip("Grain for finishing a round at all.")]
        [Min(0)] public int Participation = 20;

        [Tooltip("Grain per unit of the banked total in the round's standings.")]
        [Min(0f)] public float PerBank = 0.40f;

        [Tooltip("Grain per unit taken from rivals, before the cap below.")]
        [Min(0f)] public float PerSteal = 1.20f;

        [Tooltip("Most Grain a round's steals can earn.")]
        [Min(0)] public int StealCap = 12;

        [Tooltip("Grain by finishing position: element 0 is 1st place. Must cover MatchConfig.MaxPlayers " +
                 "positions, or a round finished below the last entry earns nothing.")]
        public int[] PlacementGrain = { 14, 8, 5, 3 };

        [Header("Rested bonus")]
        [Tooltip("Multiplier on a round's whole Grain while it is one of the first RestedRounds of the local day.")]
        [Min(1f)] public float RestedMultiplier = 1.5f;

        [Tooltip("How many rounds per local calendar day are rested.")]
        [Min(0)] public int RestedRounds = 3;

        [Header("Daily task")]
        [Tooltip("Grain for the daily task. Unused until slice 4 adds the task; nothing reads it yet.")]
        [Min(0)] public int DailyTaskBonus = 15;

        [Header("Identity")]
        [Tooltip("Evaluable rounds played as a role for each mastery level. Strictly increasing; element 0 is level 1.")]
        public int[] MasteryRoundThresholds = { 1, 3, 6, 10, 15, 21, 28, 36, 45, 55 };

        [Tooltip("The banner catalogue, in picker order.")]
        public BannerDefinition[] Banners =
        {
            new BannerDefinition { Key = "banner.barnwood", DisplayName = "Barnwood", UssClass = "cw-plate--barnwood", Source = BannerSource.Starter, UnlockRecordKey = "" },
            new BannerDefinition { Key = "banner.harvest",  DisplayName = "Harvest",  UssClass = "cw-plate--harvest",  Source = BannerSource.Earned,  UnlockRecordKey = "record.full_coop" },
            new BannerDefinition { Key = "banner.midnight", DisplayName = "Midnight", UssClass = "cw-plate--midnight", Source = BannerSource.Earned,  UnlockRecordKey = "record.highway_hen" },
        };

        /// <summary>
        /// Every record this build defines. Assets live in
        /// <c>Assets/_Game/Data/Progression/Records/</c> and are listed here in display order.
        /// </summary>
        /// <remarks>
        /// The code default is deliberately empty: asset references cannot be written as a field
        /// initialiser, so the "code defaults mirror the asset" test exempts this one field (and says
        /// so) while <c>RecordAssetTests</c> checks the shipped list separately. A build whose slot is
        /// empty shows an empty records page — never a wrong one.
        /// </remarks>
        [Tooltip("Every record, in display order. Assets live in Assets/_Game/Data/Progression/Records/.")]
        public RecordDefinitionSO[] Records = Array.Empty<RecordDefinitionSO>();
    }
}
