using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Who the player is, as UI reads it: their nameplate, their mastery per role, every record
    /// (earned or not) and their career. An immutable snapshot — a new one is built on every refold,
    /// never edited in place.
    /// </summary>
    /// <remarks>
    /// Everything here is derived from the journal by <see cref="IdentityFold"/>: the rounds give the
    /// mastery, records and career; the profile events give the choices. Nothing in it is a fact of its
    /// own, so nothing here can disagree with a reload.
    /// <para>
    /// The display types are <b>nested on purpose</b>. UI reads progression through
    /// <see cref="IProgressionService"/> alone, and the boundary scan denies every top-level
    /// progression type it has not been told about; nesting the views keeps that allowlist at two
    /// entries (this type and <see cref="NameplateSlot"/>) instead of one per view.
    /// </para>
    /// </remarks>
    public sealed class ProgressionIdentity
    {
        /// <summary>What UI shows before the journal has loaded: no name, no roles, no records.</summary>
        public static readonly ProgressionIdentity Empty = new ProgressionIdentity(
            new Nameplate(null, null, null, null, 0, null, null, null),
            Array.Empty<RoleMastery>(), Array.Empty<RecordView>(), Array.Empty<BannerView>(), Career.Empty);

        public ProgressionIdentity(Nameplate plate, IReadOnlyList<RoleMastery> roles, IReadOnlyList<RecordView> records,
            IReadOnlyList<BannerView> banners, Career summary)
        {
            Plate = plate;
            Roles = roles;
            Records = records;
            Banners = banners;
            Summary = summary;
        }

        /// <summary>The composed nameplate: name, title, emblem, banner.</summary>
        public Nameplate Plate { get; }

        /// <summary>One entry per role, in roster order: how much it has been played and what that is worth.</summary>
        public IReadOnlyList<RoleMastery> Roles { get; }

        /// <summary>Every record this build defines, earned or not. The unearned ones are the content.</summary>
        public IReadOnlyList<RecordView> Records { get; }

        /// <summary>The banner catalogue, with what the player owns marked.</summary>
        public IReadOnlyList<BannerView> Banners { get; }

        /// <summary>Lifetime and per-role totals, recent placements and personal bests.</summary>
        public Career Summary { get; }

        /// <summary>The record groups, in their authored order — the filters the profile page offers.</summary>
        public static readonly IReadOnlyList<string> RecordGroups = Enum.GetNames(typeof(RecordGroup));

        /// <summary>The composed nameplate. Any part but the emblem may be absent.</summary>
        public sealed class Nameplate
        {
            public Nameplate(string name, string titleKey, string titleText, string emblemRoleKey, int emblemMasteryLevel,
                string bannerKey, string bannerName, string bannerUssClass)
            {
                Name = name;
                TitleKey = titleKey;
                TitleText = titleText;
                EmblemRoleKey = emblemRoleKey;
                EmblemMasteryLevel = emblemMasteryLevel;
                BannerKey = bannerKey;
                BannerName = bannerName;
                BannerUssClass = bannerUssClass;
            }

            /// <summary>The generated name, or null when the journal holds none this build can show.</summary>
            public string Name { get; }

            /// <summary>True when there is a name to draw. UI hides the whole line when there is not.</summary>
            public bool HasName => !string.IsNullOrEmpty(Name);

            /// <summary>The record key whose title is worn, or null.</summary>
            public string TitleKey { get; }

            /// <summary>The title's text, or null.</summary>
            public string TitleText { get; }

            /// <summary>The role the emblem shows. Always present once any role is known.</summary>
            public string EmblemRoleKey { get; }

            /// <summary>The mastery level drawn inside the emblem.</summary>
            public int EmblemMasteryLevel { get; }

            public string BannerKey { get; }
            public string BannerName { get; }

            /// <summary>The USS class that paints the banner, or null for the plain plate.</summary>
            public string BannerUssClass { get; }
        }

        /// <summary>What one role is worth to this player. Mastery is display only: it grants nothing.</summary>
        public sealed class RoleMastery
        {
            public RoleMastery(string roleKey, int rounds, int wins, float bestBanked, int level, int maxLevel, int roundsToNextLevel)
            {
                RoleKey = roleKey;
                Rounds = rounds;
                Wins = wins;
                BestBanked = bestBanked;
                Level = level;
                MaxLevel = maxLevel;
                RoundsToNextLevel = roundsToNextLevel;
            }

            public string RoleKey { get; }

            /// <summary>Evaluable rounds played as this role — the mastery points.</summary>
            public int Rounds { get; }

            public int Wins { get; }
            public float BestBanked { get; }

            /// <summary>How many mastery thresholds <see cref="Rounds"/> has reached.</summary>
            public int Level { get; }

            public int MaxLevel { get; }

            /// <summary>Rounds still needed for the next level; 0 at <see cref="MaxLevel"/>.</summary>
            public int RoundsToNextLevel { get; }

            /// <summary>True once the role has been played at all — the emblem can only show a role that has.</summary>
            public bool Playable => Rounds > 0;
        }

        /// <summary>One record as the profile page lists it.</summary>
        public sealed class RecordView
        {
            public RecordView(string key, string name, string description, string title, string group,
                bool achievableWhileLosing, bool earned, string earnedOnDay, bool hasProgress, float progress, float target)
            {
                Key = key;
                Name = name;
                Description = description;
                Title = title;
                Group = group;
                AchievableWhileLosing = achievableWhileLosing;
                Earned = earned;
                EarnedOnDay = earnedOnDay;
                HasProgress = hasProgress;
                Progress = progress;
                Target = target;
            }

            public string Key { get; }
            public string Name { get; }
            public string Description { get; }

            /// <summary>The nameplate title earning this grants, or empty.</summary>
            public string Title { get; }

            /// <summary>The record's group, as one of <see cref="RecordGroups"/>.</summary>
            public string Group { get; }

            public bool AchievableWhileLosing { get; }
            public bool Earned { get; }

            /// <summary>The local day the first qualifying round happened on (<c>yyyy-MM-dd</c>), or null.</summary>
            public string EarnedOnDay { get; }

            /// <summary>True when <see cref="Progress"/> and <see cref="Target"/> are worth drawing.</summary>
            public bool HasProgress { get; }

            /// <summary>The best the player has managed toward <see cref="Target"/>.</summary>
            public float Progress { get; }

            public float Target { get; }
        }

        /// <summary>One banner in the catalogue, and whether the player has it.</summary>
        public sealed class BannerView
        {
            public BannerView(string key, string displayName, string ussClass, bool owned, bool purchasable)
            {
                Key = key;
                DisplayName = displayName;
                UssClass = ussClass;
                Owned = owned;
                Purchasable = purchasable;
            }

            public string Key { get; }
            public string DisplayName { get; }
            public string UssClass { get; }

            /// <summary>True when it may be worn. Nothing in this build can own a purchasable banner: there is no store.</summary>
            public bool Owned { get; }

            /// <summary>True when the only way it could ever be owned is money.</summary>
            public bool Purchasable { get; }
        }

        /// <summary>
        /// The career page: what the player has done, never how they compare. No shaming stats and no
        /// population comparisons (Maestro's ruling) — every number here is their own.
        /// </summary>
        public sealed class Career
        {
            public static readonly Career Empty = new Career(0, 0, 0, 0, 0, Array.Empty<int>(), 0, 0, 0);

            public Career(int rounds, int wins, double banked, double stolen, int rivalsRobbed,
                IReadOnlyList<int> recentPlacements, float bestBankedInARound, float bestStolenInARound, int bestRivalsRobbedInARound)
            {
                Rounds = rounds;
                Wins = wins;
                Banked = banked;
                Stolen = stolen;
                RivalsRobbed = rivalsRobbed;
                RecentPlacements = recentPlacements;
                BestBankedInARound = bestBankedInARound;
                BestStolenInARound = bestStolenInARound;
                BestRivalsRobbedInARound = bestRivalsRobbedInARound;
            }

            public int Rounds { get; }
            public int Wins { get; }
            public double Banked { get; }
            public double Stolen { get; }
            public int RivalsRobbed { get; }

            /// <summary>The last twenty placements, most recent last.</summary>
            public IReadOnlyList<int> RecentPlacements { get; }

            public float BestBankedInARound { get; }
            public float BestStolenInARound { get; }
            public int BestRivalsRobbedInARound { get; }
        }
    }
}
