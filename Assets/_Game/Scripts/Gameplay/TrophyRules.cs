using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Trophy Hunter's trophies as numbers (design 2026-11-25): each trophy adds <see cref="Room"/> food to his beak. A <see cref="Room"/> of 0
    /// adds nothing (the shipped value); <see cref="IsTrophyHunter"/> is true whatever the numbers, so a report can find the kit.
    /// </summary>
    public struct TrophyFacts
    {
        public bool IsTrophyHunter;
        /// <summary>Beak capacity one trophy adds.</summary>
        public int Room;
        /// <summary>Most trophies held at once.</summary>
        public int Cap;
        /// <summary>Trophies are cleared when the holder banks (a streak), instead of kept through banking (slow growth).</summary>
        public bool ClearOnBank;

        public static TrophyFacts None => default;
    }

    /// <summary>The pure trophy rules, shared by the match simulator and, later, the game.</summary>
    public static class TrophyRules
    {
        /// <summary>One more trophy, stopping at the cap (a cap of 0 or less earns none).</summary>
        public static int Earn(int held, int cap) => held < cap ? held + 1 : Mathf.Max(held, 0);

        /// <summary>One trophy lost to a rival's take, never below zero.</summary>
        public static int LoseOne(int held) => held > 0 ? held - 1 : 0;

        /// <summary>The beak capacity the held trophies add (0 for a kit that is not the Trophy Hunter's).</summary>
        public static float Room(int held, in TrophyFacts f) => f.IsTrophyHunter ? Mathf.Max(held, 0) * Mathf.Max(f.Room, 0) : 0f;

        /// <summary>A Steal cast earns a trophy when its takes add up to 1 food or more.</summary>
        public static bool StealEarns(float takenThisCast) => takenThisCast >= 1f;

        /// <summary>The food above the capacity: what leaves the beak when the capacity falls below the cargo.</summary>
        public static float Overflow(float cargo, float capacity) => Mathf.Max(0f, cargo - capacity);
    }
}
