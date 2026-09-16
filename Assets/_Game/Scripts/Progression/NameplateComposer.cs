using System;
using System.Collections.Generic;

namespace CluckWars.Progression
{
    /// <summary>
    /// Turns the player's four choices into the nameplate that is actually drawn. Pure: it decides
    /// nothing about what is owned, it only refuses to wear what is not.
    /// </summary>
    /// <remarks>
    /// <b>A choice is not a promise.</b> A title whose record is no longer earned, or a banner that is
    /// not owned, is dropped rather than shown — the journal keeps the choice, so re-earning the record
    /// brings the title back without the player having to pick it again.
    /// <para>
    /// <b>The emblem is always present.</b> It is the one part that cannot be absent or bought: the
    /// selected role when it has been played, else the most-played role (ties broken by
    /// <see cref="UnlockKeyTable.RoleKeys"/>), else the first role in that order. That is what makes
    /// "every nameplate holds at least one part no one could buy" true by construction rather than by
    /// there being no store yet.
    /// </para>
    /// </remarks>
    public static class NameplateComposer
    {
        /// <param name="name">The generated name, already validated; null or empty for "no name yet".</param>
        /// <param name="titleKey">The record key whose title was chosen, or empty for none.</param>
        /// <param name="emblemRoleKey">The role key chosen for the emblem, or empty for the automatic one.</param>
        /// <param name="bannerKey">The banner key chosen, or empty for the default.</param>
        public static ProgressionIdentity.Nameplate Compose(
            string name,
            string titleKey,
            string emblemRoleKey,
            string bannerKey,
            IReadOnlyList<ProgressionIdentity.RecordView> records,
            IReadOnlyList<ProgressionIdentity.RoleMastery> roles,
            IReadOnlyList<ProgressionIdentity.BannerView> banners)
        {
            var title = FindEarnedTitle(titleKey, records);
            var role = ChooseEmblemRole(emblemRoleKey, roles);
            var banner = ChooseBanner(bannerKey, banners);

            return new ProgressionIdentity.Nameplate(
                ProfileEvent.IsDisplayableName(name) ? name : null,
                title?.Key,
                title?.Title,
                role?.RoleKey,
                role?.Level ?? 0,
                banner?.Key,
                banner?.DisplayName,
                banner?.UssClass);
        }

        /// <summary>True when <paramref name="key"/> names a title the player has actually earned.</summary>
        public static bool OwnsTitle(string key, IReadOnlyList<ProgressionIdentity.RecordView> records) =>
            FindEarnedTitle(key, records) != null;

        /// <summary>True when <paramref name="roleKey"/> names a role the player has played at least once.</summary>
        public static bool OwnsEmblem(string roleKey, IReadOnlyList<ProgressionIdentity.RoleMastery> roles)
        {
            var role = Find(roles, r => string.Equals(r.RoleKey, roleKey, StringComparison.Ordinal));
            return role != null && role.Playable;
        }

        /// <summary>True when <paramref name="key"/> names a banner the player owns.</summary>
        public static bool OwnsBanner(string key, IReadOnlyList<ProgressionIdentity.BannerView> banners)
        {
            var banner = Find(banners, b => string.Equals(b.Key, key, StringComparison.Ordinal));
            return banner != null && banner.Owned;
        }

        private static ProgressionIdentity.RecordView FindEarnedTitle(string key, IReadOnlyList<ProgressionIdentity.RecordView> records)
        {
            if (string.IsNullOrEmpty(key)) return null;

            var record = Find(records, r => string.Equals(r.Key, key, StringComparison.Ordinal));
            return record != null && record.Earned && !string.IsNullOrEmpty(record.Title) ? record : null;
        }

        private static ProgressionIdentity.RoleMastery ChooseEmblemRole(string roleKey, IReadOnlyList<ProgressionIdentity.RoleMastery> roles)
        {
            if (roles == null || roles.Count == 0) return null;

            var chosen = Find(roles, r => string.Equals(r.RoleKey, roleKey, StringComparison.Ordinal));
            if (chosen != null && chosen.Playable) return chosen;

            // Most played, ties broken by the roster order the list is already in.
            var best = roles[0];
            for (int i = 1; i < roles.Count; i++)
            {
                if (roles[i].Rounds > best.Rounds) best = roles[i];
            }

            return best;
        }

        private static ProgressionIdentity.BannerView ChooseBanner(string key, IReadOnlyList<ProgressionIdentity.BannerView> banners)
        {
            if (banners == null || banners.Count == 0) return null;

            var chosen = Find(banners, b => string.Equals(b.Key, key, StringComparison.Ordinal));
            if (chosen != null && chosen.Owned) return chosen;

            // The first banner the player already owns — never simply "the first banner", which in a
            // catalogue of nothing but purchasable ones would put an unowned plate on the nameplate.
            return Find(banners, b => b.Owned);
        }

        private static T Find<T>(IReadOnlyList<T> items, Func<T, bool> match) where T : class
        {
            if (items == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && match(items[i])) return items[i];
            }

            return null;
        }
    }
}
