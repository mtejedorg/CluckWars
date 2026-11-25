using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Champion's Peck break as numbers (design 2026-11-25): catch a rival mid Peck-cast, cut it and take its bite from its pile. A
    /// <see cref="Reach"/> of 0 is no break at all (the shipped value).
    /// </summary>
    public struct PeckBreakFacts
    {
        /// <summary>Centre to centre metres a break reaches; 0 = no break.</summary>
        public float Reach;
        /// <summary>Seconds before the same Champion may break again. Gates breaks only, never his Peck.</summary>
        public float Cooldown;
        /// <summary>Seconds a broken rival cannot be broken again.</summary>
        public float ImmuneSeconds;

        public bool Active => Reach > 0f;
    }

    /// <summary>What a break press does when it is released.</summary>
    public enum BreakOutcome : byte { None, Break, FallbackPeck, Cancel }

    /// <summary>The pure rules of the Peck break, shared by the match simulator and, later, the game.</summary>
    public static class PeckBreakRules
    {
        /// <summary>
        /// A rival's cast can be broken now: it is casting a Peck, inside <paramref name="reach"/>, with no base wall between, not in its egg
        /// and not break-immune. A reach of 0 or less never breaks.
        /// </summary>
        public static bool CanBreak(bool casting, float dist, float reach, bool walled, bool egg, bool immune) =>
            reach > 0f && casting && dist <= reach && !walled && !egg && !immune;

        /// <summary>The food a break takes from the victim's pile: the victim's own bite, held to the pile and to the breaker's free room.</summary>
        public static float Bite(float victimPeckAmount, float pileFood, float freeRoom)
        {
            float bite = victimPeckAmount < pileFood ? victimPeckAmount : pileFood;
            if (freeRoom < bite) bite = freeRoom;
            return bite > 0f ? bite : 0f;
        }

        /// <summary>A break when one can land; else a pile Peck when a pile with food is in reach; else a visible cancel that spends nothing.</summary>
        public static BreakOutcome Resolve(bool canBreak, bool pileInReach) =>
            canBreak ? BreakOutcome.Break : pileInReach ? BreakOutcome.FallbackPeck : BreakOutcome.Cancel;

        /// <summary>The index of the smallest distance (the first on a tie), -1 for none. A NaN or infinite distance is not a candidate.</summary>
        public static int Nearest(IReadOnlyList<float> distances)
        {
            int best = -1;
            if (distances == null) return best;
            for (int i = 0; i < distances.Count; i++)
            {
                float d = distances[i];
                if (float.IsNaN(d) || float.IsInfinity(d)) continue;
                if (best < 0 || d < distances[best]) best = i;
            }
            return best;
        }
    }
}
