using System;
using System.Collections.Generic;
using CluckWars.Progression;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Ranks a finished round's per-actor totals into <see cref="RoundStandings"/>. Pure, so
    /// the placement rules can be tested without a runner.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>Order:</b> total descending, ties broken by actor id ascending, so the result
    ///   is the same whatever order the input arrives in.</item>
    ///   <item><b>Ties share the better placement</b> (competition ranking: totals 10, 10, 5 place
    ///   1, 1, 3).</item>
    ///   <item><b>Tie test:</b> <c>Mathf.Approximately</c> against the previous entry in sorted
    ///   order — the same notion of "equal totals" <c>GameManager.EndOnTimerExpiry</c> uses. The two
    ///   still answer different questions: standings let tied actors <i>share</i> a placement, so two
    ///   actors can both be P1, while <c>EndOnTimerExpiry</c> names exactly one winner by breaking the
    ///   tie on kills, then corner. <c>Entries[0]</c> is therefore not necessarily that winner.</item>
    ///   <item><b>Orphan entries</b> (<c>MatchActorId.None</c>, a claimed base whose actor left) rank
    ///   by total like any other, and share a placement with any actor on an equal total. Their
    ///   position among equal totals is just the actor-id tie-break with id 0 — after any actor
    ///   whose id wrapped negative (<c>MatchActorId</c> is an unchecked cast), before positive ids —
    ///   so never read meaning into it.</item>
    /// </list>
    /// Not the HUD's display sorts (<c>MatchOverlaysController</c>, <c>MatchHudController</c>):
    /// those use an unstable <c>List.Sort</c>, which may order equal totals differently from one
    /// call to the next. This uses a stable insertion sort — the input is at most a handful of
    /// actors, and it runs once per round.
    /// </remarks>
    public static class RoundStandingsBuilder
    {
        /// <summary>One entry per element of <paramref name="results"/>, ranked. Never null.</summary>
        public static RoundStandings Build(IReadOnlyList<(int actorId, string roleKey, float total)> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            var entries = new RoundStandingEntry[results.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                var (actorId, roleKey, total) = results[i];
                var entry = new RoundStandingEntry { ActorId = actorId, RoleKey = roleKey, ResourceTotal = total };

                // Stable insertion sort: shift every entry that ranks after this one.
                int j = i - 1;
                while (j >= 0 && RanksBefore(entry, entries[j]))
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = entry;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                bool tiedWithPrevious = i > 0 &&
                    Mathf.Approximately(entries[i].ResourceTotal, entries[i - 1].ResourceTotal);
                entries[i].Placement = tiedWithPrevious ? entries[i - 1].Placement : i + 1;
            }

            return new RoundStandings { Entries = entries };
        }

        private static bool RanksBefore(in RoundStandingEntry a, in RoundStandingEntry b)
        {
            if (a.ResourceTotal != b.ResourceTotal) return a.ResourceTotal > b.ResourceTotal;
            return a.ActorId < b.ActorId;
        }
    }
}
