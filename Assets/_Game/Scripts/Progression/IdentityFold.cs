using System;
using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Progression
{
    /// <summary>
    /// Builds the whole of <see cref="ProgressionIdentity"/> from the journal: the rounds give mastery,
    /// records and the career; the profile events give the choices. Pure, total, and the only place
    /// those two halves meet.
    /// </summary>
    /// <remarks>
    /// <b>The rounds come from the ledger, already folded.</b> Taking
    /// <see cref="ProgressionLedger.EvaluableRounds"/> rather than the raw records is what makes a
    /// career total and the matching <c>ProgressionProfile</c> total the same number by construction:
    /// they are two sums over one list, not two independent walks of the journal.
    /// <para>
    /// Every problem it meets — a mis-authored record, an unreadable name, an event type from a newer
    /// build — is appended to the caller's list and the rest of the identity is still built. Nothing
    /// here throws, because a broken asset must cost the player a line on a page, not their profile.
    /// </para>
    /// </remarks>
    public static class IdentityFold
    {
        /// <summary>How many recent placements the career strip shows.</summary>
        public const int RecentPlacementCount = 20;

        /// <summary>
        /// The identity for <paramref name="rounds"/> and <paramref name="profileEvents"/>.
        /// </summary>
        /// <param name="rounds">The ledger's evaluable rounds, in canonical order.</param>
        /// <param name="profileEvents">Every profile event read from the journal, in file order.</param>
        /// <param name="zone">Whose calendar days name an earning day; null means the device's.</param>
        /// <param name="problems">Appended to, never read. May be null.</param>
        public static ProgressionIdentity Build(IReadOnlyList<RoundOutcome> rounds, IReadOnlyList<ProfileEvent> profileEvents,
            ProgressionConfigSO config, TimeZoneInfo zone, List<string> problems = null)
        {
            if (config == null)
            {
                Report(problems, "There is no progression config, so no identity can be derived from the journal.");
                return ProgressionIdentity.Empty;
            }

            var selections = FoldSelections(profileEvents, problems);
            var roles = BuildRoles(rounds, config.MasteryRoundThresholds);
            var records = BuildRecords(rounds, config, zone, problems);
            var banners = BuildBanners(config.Banners, records, problems);
            var career = BuildCareer(rounds);

            string name = Selected(selections, ProfileEventTypes.Name);
            if (!string.IsNullOrEmpty(name) && !ProfileEvent.IsDisplayableName(name))
            {
                Report(problems, $"The journal's current name '{name}' is not a shape this build can show, so no name is " +
                    "displayed. Re-rolling writes a new one.");
                name = null;
            }

            var plate = NameplateComposer.Compose(
                name,
                Selected(selections, ProfileEventTypes.Title),
                Selected(selections, ProfileEventTypes.Emblem),
                Selected(selections, ProfileEventTypes.Banner),
                records, roles, banners);

            return new ProgressionIdentity(plate, roles, records, banners, career);
        }

        // ---- Profile events --------------------------------------------------------------

        /// <summary>
        /// The current value of every event type: duplicates folded by <c>EventId</c>, then the last
        /// event per type in canonical order — (<c>AtUtc</c> instant, <c>EventId</c> ordinal) — wins.
        /// </summary>
        /// <remarks>
        /// Order-independent for the same reason the ledger is: two devices' clocks, or a reordered
        /// file, must not change what the nameplate says. An event type this build does not know is
        /// kept in the journal and ignored here, so a downgrade cannot destroy a newer build's choices.
        /// </remarks>
        public static Dictionary<string, string> FoldSelections(IReadOnlyList<ProfileEvent> profileEvents, List<string> problems)
        {
            var selections = new Dictionary<string, string>(StringComparer.Ordinal);
            if (profileEvents == null) return selections;

            var byId = new Dictionary<string, (DateTime At, ProfileEvent Event, string Canonical)>(StringComparer.Ordinal);
            foreach (var profileEvent in profileEvents)
            {
                if (profileEvent == null || string.IsNullOrEmpty(profileEvent.EventId)) continue;
                if (!ProgressionCalendar.TryParseUtc(profileEvent.AtUtc, out var at))
                {
                    Report(problems, $"Profile event {profileEvent.EventId} has no readable instant ('{profileEvent.AtUtc}'), so it is ignored.");
                    continue;
                }

                string canonical = JsonUtility.ToJson(profileEvent);
                if (!byId.TryGetValue(profileEvent.EventId, out var kept))
                {
                    byId.Add(profileEvent.EventId, (at, profileEvent, canonical));
                    continue;
                }

                // Same id, different content: resolve to the ordinal-smallest serialization, which does
                // not depend on the order the lines were read in.
                if (string.CompareOrdinal(canonical, kept.Canonical) < 0)
                    byId[profileEvent.EventId] = (at, profileEvent, canonical);
            }

            var ordered = new List<(DateTime At, ProfileEvent Event)>(byId.Count);
            foreach (var entry in byId.Values) ordered.Add((entry.At, entry.Event));
            ordered.Sort((a, b) =>
            {
                int byTime = a.At.Ticks.CompareTo(b.At.Ticks);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Event.EventId, b.Event.EventId);
            });

            foreach (var (_, profileEvent) in ordered)
            {
                if (!ProfileEventTypes.IsKnown(profileEvent.Type))
                {
                    Report(problems, $"Profile event {profileEvent.EventId} sets '{profileEvent.Type}', which this build does " +
                        "not know. It is kept in the journal and ignored.");
                    continue;
                }

                selections[profileEvent.Type] = profileEvent.Value ?? string.Empty;
            }

            return selections;
        }

        private static string Selected(Dictionary<string, string> selections, string type) =>
            selections.TryGetValue(type, out string value) ? value : string.Empty;

        // ---- Roles -----------------------------------------------------------------------

        private static IReadOnlyList<ProgressionIdentity.RoleMastery> BuildRoles(IReadOnlyList<RoundOutcome> rounds,
            IReadOnlyList<int> thresholds)
        {
            // Every roster role is listed even at zero rounds: the unplayed ones are the reason to play
            // them. Role keys in the journal that the roster no longer has (an older build's) follow,
            // so a player never loses sight of play they actually did.
            var order = new List<string>(UnlockKeyTable.RoleKeys);
            var counts = new Dictionary<string, (int Rounds, int Wins, float BestBanked)>(StringComparer.Ordinal);
            foreach (string key in order) counts[key] = (0, 0, 0f);

            var extra = new List<string>();
            for (int i = 0; rounds != null && i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null || string.IsNullOrEmpty(round.RoleKey)) continue;

                if (!counts.TryGetValue(round.RoleKey, out var tally))
                {
                    tally = (0, 0, 0f);
                    extra.Add(round.RoleKey);
                }

                counts[round.RoleKey] = (tally.Rounds + 1,
                    tally.Wins + (round.Placement == 1 ? 1 : 0),
                    Math.Max(tally.BestBanked, round.BankedTotal));
            }

            extra.Sort(StringComparer.Ordinal);
            order.AddRange(extra);

            var roles = new List<ProgressionIdentity.RoleMastery>(order.Count);
            foreach (string key in order)
            {
                var tally = counts[key];
                roles.Add(new ProgressionIdentity.RoleMastery(key, tally.Rounds, tally.Wins, tally.BestBanked,
                    MasteryRules.LevelFor(tally.Rounds, thresholds),
                    MasteryRules.MaxLevel(thresholds),
                    MasteryRules.RoundsToNextLevel(tally.Rounds, thresholds)));
            }

            return roles;
        }

        // ---- Records and banners ---------------------------------------------------------

        private static IReadOnlyList<ProgressionIdentity.RecordView> BuildRecords(IReadOnlyList<RoundOutcome> rounds,
            ProgressionConfigSO config, TimeZoneInfo zone, List<string> problems)
        {
            var definitions = config.Records;
            var standings = RecordEngine.Evaluate(rounds, definitions, config.MasteryRoundThresholds, zone);
            var views = new List<ProgressionIdentity.RecordView>(standings.Count);

            for (int i = 0; i < standings.Count; i++)
            {
                var standing = standings[i];
                if (!standing.IsValid)
                {
                    // Listed nowhere rather than listed wrong: a record with no key or no name has
                    // nothing a player could act on.
                    Report(problems, standing.Problem);
                    continue;
                }

                var definition = definitions[i];
                views.Add(new ProgressionIdentity.RecordView(
                    definition.Key,
                    definition.DisplayName,
                    definition.Description,
                    definition.Title ?? string.Empty,
                    definition.Group.ToString(),
                    definition.AchievableWhileLosing,
                    standing.Earned,
                    standing.EarnedLocalDay,
                    standing.HasProgress,
                    standing.Progress,
                    standing.Target));
            }

            return views;
        }

        private static IReadOnlyList<ProgressionIdentity.BannerView> BuildBanners(IReadOnlyList<BannerDefinition> definitions,
            IReadOnlyList<ProgressionIdentity.RecordView> records, List<string> problems)
        {
            var banners = new List<ProgressionIdentity.BannerView>(definitions?.Count ?? 0);
            if (definitions == null) return banners;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (string.IsNullOrEmpty(definition.Key) || !keys.Add(definition.Key))
                {
                    Report(problems, $"Banner '{definition.DisplayName}' has a missing or repeated key ('{definition.Key}'), so it is not offered.");
                    continue;
                }

                bool owned = definition.Source switch
                {
                    BannerSource.Starter => true,
                    BannerSource.Earned => IsEarned(records, definition.UnlockRecordKey),

                    // Purchasable: nothing in this build can own one, because there is nothing to buy
                    // it with. Kept in the catalogue so the picker can show what a store would sell.
                    _ => false,
                };

                if (definition.Source == BannerSource.Earned && string.IsNullOrEmpty(definition.UnlockRecordKey))
                {
                    Report(problems, $"Banner '{definition.Key}' is earned but names no record, so it can never be owned.");
                }

                banners.Add(new ProgressionIdentity.BannerView(definition.Key, definition.DisplayName, definition.UssClass,
                    owned, definition.Source == BannerSource.Purchasable));
            }

            return banners;
        }

        private static bool IsEarned(IReadOnlyList<ProgressionIdentity.RecordView> records, string key)
        {
            if (string.IsNullOrEmpty(key) || records == null) return false;
            for (int i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].Key, key, StringComparison.Ordinal)) return records[i].Earned;
            }

            return false;
        }

        // ---- Career ----------------------------------------------------------------------

        private static ProgressionIdentity.Career BuildCareer(IReadOnlyList<RoundOutcome> rounds)
        {
            if (rounds == null || rounds.Count == 0) return ProgressionIdentity.Career.Empty;

            int wins = 0, rivalsRobbed = 0, bestRivals = 0, played = 0;
            double banked = 0, stolen = 0;
            float bestBanked = 0f, bestStolen = 0f;
            var placements = new List<int>(Math.Min(rounds.Count, RecentPlacementCount));

            for (int i = 0; i < rounds.Count; i++)
            {
                var round = rounds[i];
                if (round == null) continue;

                played++;
                if (round.Placement == 1) wins++;
                banked += round.BankedTotal;
                stolen += round.StolenTotal;
                rivalsRobbed += round.RivalsRobbed;
                bestBanked = Math.Max(bestBanked, round.BankedTotal);
                bestStolen = Math.Max(bestStolen, round.StolenTotal);
                bestRivals = Math.Max(bestRivals, round.RivalsRobbed);

                // The rounds are in canonical order, so the tail is the most recent play.
                if (rounds.Count - i <= RecentPlacementCount) placements.Add(round.Placement);
            }

            return new ProgressionIdentity.Career(played, wins, banked, stolen, rivalsRobbed,
                placements, bestBanked, bestStolen, bestRivals);
        }

        private static void Report(List<string> problems, string message)
        {
            if (message != null) problems?.Add(message);
        }
    }
}
