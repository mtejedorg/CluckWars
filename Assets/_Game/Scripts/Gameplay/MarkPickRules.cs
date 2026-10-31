using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Who a Death Mark lands on, pure, with the roll injected. The caller lists the living, non-decoy rivals. The next mark can
    /// never be the last one unless that rival is the only rival in the match; there is no time release.
    /// </summary>
    public static class MarkPickRules
    {
        /// <summary>Removes the last mark from <paramref name="candidates"/> unless it is the only rival in the match.</summary>
        /// <param name="rivalsInMatch">Every real rival, including one out of play for the moment.</param>
        public static void Filter<T>(List<T> candidates, bool hasLast, T last, int rivalsInMatch) where T : struct, IEquatable<T>
        {
            if (!hasLast || rivalsInMatch <= 1) return;
            for (int i = candidates.Count - 1; i >= 0; i--)
                if (candidates[i].Equals(last)) candidates.RemoveAt(i);
        }

        /// <summary>An index into the filtered candidates for a roll in [0, 1); -1 when there is nobody.</summary>
        public static int Pick(int count, float roll)
        {
            if (count <= 0) return -1;
            int i = (int)(roll * count);
            return i < 0 ? 0 : i >= count ? count - 1 : i;
        }
    }
}
