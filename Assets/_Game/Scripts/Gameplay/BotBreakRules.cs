using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The bot's decisions about the Champion's Peck break (design 2026-11-25, section 7), pure functions over facts. Called by the match
    /// simulator's P0 (<c>ScriptedPolicy</c>) now and by <c>BotController</c> in the game slice.
    /// </summary>
    public static class BotBreakRules
    {
        /// <summary>
        /// A rival whose Peck the bot can break now and will still be able to break after its reaction: casting, with more of the cast left than the
        /// reaction takes, inside <paramref name="reach"/>, no base wall between, not break-immune.
        /// </summary>
        public static bool Candidate(bool rivalCasting, float castLeft, float dist, float reach, bool walled, bool immune, float reaction) =>
            PeckBreakRules.CanBreak(rivalCasting && castLeft > reaction, dist, reach, walled, false, immune);

        /// <summary>Outside reach, but walking the gap plus the reaction ends before the rival's cast does.</summary>
        public static bool Approach(float dist, float reach, float castLeft, float moveSpeed, float reaction) =>
            moveSpeed > 0f && reach > 0f && dist > reach && (dist - reach) / moveSpeed + reaction < castLeft;

        /// <summary>
        /// Break when there is a candidate and either the bot cannot peck a pile itself now (<paramref name="ownPileBite"/> 0) or a trophy is still to
        /// win (<paramref name="trophies"/> below <paramref name="cap"/>).
        /// </summary>
        public static bool ShouldBreak(bool candidate, float ownPileBite, int trophies, int cap) =>
            candidate && (ownPileBite <= 0f || trophies < cap);

        /// <summary>
        /// The cargo fraction at which the bot heads home: the profile's fraction, or the share of the beak that the return amount plus the room its
        /// trophies added is, whichever is lower. Without a return amount the profile's fraction. Without it the bot banks at its amount and the
        /// trophy room is never used.
        /// </summary>
        public static float ReturnFraction(float profileFraction, float returnAmount, int trophies, int room, float capacity)
        {
            if (returnAmount <= 0f || capacity <= 0f) return profileFraction;
            return Mathf.Min(profileFraction, (returnAmount + Mathf.Max(trophies, 0) * Mathf.Max(room, 0)) / capacity);
        }
    }
}
