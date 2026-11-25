using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The bot's decisions about the Champion's bite and trophies (design 2026-11-25, section 7), pure functions over facts. Called by the match
    /// simulator's P0 (<c>ScriptedPolicy</c>) now and by <c>BotController</c> in the game slice.
    /// </summary>
    public static class BotChampionRules
    {
        public const int Stun = 0;
        public const int Shove = 1;
        public const int None = -1;

        /// <summary>
        /// How long the rival stays biteable: its cast left plus the window mid-cast (assuming the cast lands), the window minus the time since it landed
        /// just after a landing with food, 0 when bitten, not fresh or the window is 0.
        /// </summary>
        public static float FreshLeft(bool casting, float castLeft, float sinceLanded, bool landedWithFood, bool bitten, float freshSeconds)
        {
            if (!BiteRules.Fresh(casting, sinceLanded, landedWithFood, bitten, freshSeconds)) return 0f;
            return casting ? Mathf.Max(0f, castLeft) + freshSeconds : Mathf.Max(0f, freshSeconds - sinceLanded);
        }

        /// <summary>A fresh rival in a ready hitter's reach is worth a hit now when it stays fresh longer than the bot's reaction.</summary>
        public static bool HitFresh(float freshLeft, float reaction) => freshLeft > reaction;

        /// <summary>The hitter that builds a trophy: <see cref="Stun"/> or <see cref="Shove"/>, the shorter cooldown first (a tie goes to the Shove), <see cref="None"/> when neither is ready.</summary>
        public static int PickBuilder(bool stunReady, bool shoveReady, float stunCooldown, float shoveCooldown)
        {
            if (!stunReady && !shoveReady) return None;
            if (!stunReady) return Shove;
            if (!shoveReady) return Stun;
            return stunCooldown < shoveCooldown ? Stun : Shove;
        }

        /// <summary>
        /// Steal that rival now: it would take at least 1 food, a trophy is on it, and either the count is at the cap or no builder is back before the
        /// count decays. A Steal at count 0 stays the ordinary Steal rule.
        /// </summary>
        public static bool Cash(int count, int cap, float decayLeft, float nextBuilderReadyIn, float stealGain) =>
            stealGain >= 1f && count >= 1 && (count >= cap || nextBuilderReadyIn >= decayLeft);

        /// <summary>What a Steal on a rival holding <paramref name="count"/> trophies would take: the inflated amount, held to the free room and the rival's food.</summary>
        public static float StealGain(float stealAmount, float stealFactor, float perTrophy, int count, float freeRoom, float victimStealable) =>
            StealMath.Clamp(TrophyRules.StealAmount(stealAmount, stealFactor, perTrophy, count), freeRoom, victimStealable);

        /// <summary>
        /// Leave the pile for a fight when the fight's food per second (the bite plus the Steal gain over the walk and the casts) beats the Peck's
        /// (its amount over its cycle). A slower or smaller Peck makes more fights worth it: this is where the foraging lever bites.
        /// </summary>
        public static bool Fight(float fightGain, float fightSeconds, float peckAmount, float peckCycleSeconds)
        {
            if (!(fightGain > 0f)) return false;
            float peckRate = peckCycleSeconds > 0f ? peckAmount / peckCycleSeconds : float.MaxValue;
            return fightGain / Mathf.Max(0.05f, fightSeconds) > peckRate;
        }
    }
}
