using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>One chicken as the loot feedback sees it, as plain values (no Unity object): who it is, whether this peer owns it and what its Thief state says.</summary>
    public struct LootSeat
    {
        /// <summary>The chicken's network id as one number (<c>LootFeedback.IdOf</c>); never 0 for a chicken.</summary>
        public long Id;
        /// <summary>This peer's own, non-decoy bird.</summary>
        public bool IsLocal;
        /// <summary>Executed and waiting for its respawn, or out of the match.</summary>
        public bool Removed;
        /// <summary>The passive carries a loot mark (<c>LootMarkFacts.Active</c>): only the Thief's.</summary>
        public bool ThiefActive;
        /// <summary>The mark is up (<c>ThiefLoot.MarkLive</c>: Live or Overdue).</summary>
        public bool MarkLive;
        /// <summary>The marked rival's <see cref="Id"/>, 0 with none.</summary>
        public long MarkedId;
    }

    /// <summary>Who the local player is in the loot mark, by index into the seats handed to <see cref="LootPresentationRules.Resolve"/>.</summary>
    public struct LootRoles
    {
        public int Local;
        /// <summary>The local player is a Thief with a live mark on a living rival.</summary>
        public bool AsThief;
        public int Target;
        /// <summary>A Thief's live mark is on the local player.</summary>
        public bool AsMarked;
        public int Thief;
    }

    /// <summary>What blocks a Steal from taking the marked rival: the design's blocked state (a wall of its base, or an egg shell).</summary>
    public enum LootBlocker : byte { None = 0, Walled = 1, Shelled = 2 }

    /// <summary>
    /// The loot mark's presentation rules (design 2026-11-23 sections 11 and 22), pure and Unity-free. <b>The one gate</b>: <see cref="Resolve"/> says who the
    /// local player is; every presenter (marker, arrow badge, brackets, victim badge, Steal-hex dot, pops, buzz) draws only from <c>LootFeedback.Current</c>,
    /// which is built from this answer, so a bystander (and a Thief whose mark is on someone else's screen) gets nothing. Owner, 2026-11-24: only the Thief
    /// and the marked rival see the mark and the reward; the identity is a bag of coins, never a crown.
    /// </summary>
    public static class LootPresentationRules
    {
        /// <summary>Resolves the local player's roles. Only the local seat can be a role: another Thief's mark on a third bird, or a mark on nobody, resolves to none.</summary>
        public static LootRoles Resolve(IReadOnlyList<LootSeat> seats)
        {
            var r = new LootRoles { Local = -1, Target = -1, Thief = -1 };
            if (seats == null) return r;
            for (int i = 0; i < seats.Count; i++)
                if (seats[i].IsLocal) { r.Local = i; break; }
            if (r.Local < 0 || seats[r.Local].Removed) return r;

            var local = seats[r.Local];
            if (local.ThiefActive && local.MarkLive && local.MarkedId != 0)
            {
                for (int j = 0; j < seats.Count; j++)
                {
                    if (j == r.Local || seats[j].Id != local.MarkedId || seats[j].Removed) continue;
                    r.AsThief = true;
                    r.Target = j;
                    break;
                }
            }
            for (int j = 0; j < seats.Count; j++)
            {
                if (j == r.Local) continue;
                var s = seats[j];
                if (!s.ThiefActive || !s.MarkLive || s.MarkedId != local.Id || s.Removed) continue;
                r.AsMarked = true;
                r.Thief = j;
                break;
            }
            return r;
        }

        /// <summary>The Thief sees the bag on the marked rival unless that rival is hidden (Hide): a hidden bird hides the mark from the Thief, as it hides the bird.</summary>
        public static bool MarkerShown(bool asThief, bool targetHidden) => asThief && !targetHidden;

        public static LootBlocker BlockerFor(bool walled, bool shelled) => shelled ? LootBlocker.Shelled : walled ? LootBlocker.Walled : LootBlocker.None;

        /// <summary>Is the marked rival inside Steal's reach (read from the Steal ability, <c>IndicatorRange</c>; 0 = no reach known, never in reach)?</summary>
        public static bool InReach(float planarDistance, float reach) => reach > 0f && planarDistance <= reach;

        /// <summary>The ground brackets stand round the marked rival while the marker shows and Steal could reach it ("fire now"; grey when blocked).</summary>
        public static bool BracketsShown(bool markerShown, bool inReach) => markerShown && inReach;

        /// <summary>The marker's pop factor after a mark moved onto a new bird: 1 at rest (and always under Reduced Motion), growing from <see cref="FeedbackTuning.LootHopFrom"/> over the hop.</summary>
        public static float HopPop(float secondsSinceMove, bool reducedMotion)
        {
            if (reducedMotion || secondsSinceMove < 0f || secondsSinceMove >= FeedbackTuning.LootHopSeconds) return 1f;
            return Mathf.Lerp(FeedbackTuning.LootHopFrom, 1f, secondsSinceMove / FeedbackTuning.LootHopSeconds);
        }

        /// <summary>The victim's badge alpha: full the frame the mark lands, then fading out over <see cref="FeedbackTuning.LootBadgeFadeSeconds"/> once it is gone (no word).</summary>
        public static float BadgeAlpha(float current, bool marked, float deltaSeconds)
        {
            if (marked) return 1f;
            return Mathf.Max(0f, current - deltaSeconds / Mathf.Max(0.0001f, FeedbackTuning.LootBadgeFadeSeconds));
        }

        /// <summary>
        /// The ordinary <c>+n</c> pop a marked steal would draw is only what the reward pop does not say: the marked steal's cargo gain is take plus bounty, the
        /// <c>LOOT +n</c> pop names the bounty, so the grab pop shows the rest (0, no pop, when the beak was empty).
        /// </summary>
        public static float GrabAfterBounty(float delta, float bounty) => bounty > 0f ? Mathf.Max(0f, delta - bounty) : delta;

        /// <summary>Do the loot pops and buzz fire for this bounty? A paid steal whose bounty did not fit says nothing (no "LOOT +0").</summary>
        public static bool RewardShown(float bounty) => Mathf.RoundToInt(bounty) > 0;
    }
}
