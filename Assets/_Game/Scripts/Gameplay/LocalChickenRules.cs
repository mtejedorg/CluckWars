using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>What the local-chicken selection needs to know about one spawned chicken.</summary>
    public readonly struct LocalChickenCandidate
    {
        /// <summary>The network object is live (between Spawned and Despawned).</summary>
        public readonly bool Valid;
        public readonly bool HasInputAuthority;
        /// <summary>A Doppelganger decoy: it shares its caster's input authority, so input authority alone is not enough.</summary>
        public readonly bool IsDecoy;
        public readonly bool IsBot;

        public LocalChickenCandidate(bool valid, bool hasInputAuthority, bool isDecoy, bool isBot)
        {
            Valid = valid;
            HasInputAuthority = hasInputAuthority;
            IsDecoy = isDecoy;
            IsBot = isBot;
        }
    }

    /// <summary>
    /// A10 (architecture audit 2026-11-12): the one rule for "which chicken does this peer drive". Pure, tested;
    /// <see cref="ChickenController.Local"/> applies it to the spawned chickens.
    /// </summary>
    public static class LocalChickenRules
    {
        /// <summary>A live chicken this peer holds input authority over, that is not a decoy.</summary>
        public static bool IsEligible(in LocalChickenCandidate c) => c.Valid && c.HasInputAuthority && !c.IsDecoy;

        /// <summary>
        /// Index of the local chicken in <paramref name="candidates"/>, or -1. The first eligible chicken that is not
        /// a bot wins; an eligible bot is returned only when no such chicken exists (bots are spawned without input
        /// authority, so that case is a guard, not a path the shipped game takes).
        /// </summary>
        public static int Pick(IReadOnlyList<LocalChickenCandidate> candidates)
        {
            int bot = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!IsEligible(in c)) continue;
                if (!c.IsBot) return i;
                if (bot < 0) bot = i;
            }
            return bot;
        }
    }
}
