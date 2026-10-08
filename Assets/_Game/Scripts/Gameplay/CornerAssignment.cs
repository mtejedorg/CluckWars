using CluckWars.Services;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Which spawn corner each player gets, decided before the match so the menu can show it
    /// (round-2 finding 1: a player is the same colour in the lobby, the HUD and the results).
    /// <c>MatchBootstrapper</c> spawns from <see cref="Permutation"/> of <see cref="SeedFor"/>; the menu
    /// Coop colours its seats from the same two calls through <see cref="LobbySeatCorner"/>.
    /// </summary>
    /// <remarks>
    /// Solo: the seed is <see cref="ISessionSelectionService.SoloCornerSeed"/>, rolled by the menu each
    /// time THE COOP opens, so every session still gets a fresh layout but the menu knows it; the player
    /// takes permutation slot 0 and the three bots slots 1..3 in the Coop's seat order.
    /// Host / Join: the seed is the session name (every peer computes the same permutation) and a player
    /// takes the slot of their join order. The host is the first to join, so its corner (and, in a fresh
    /// session, each open seat's) is known once the join code exists; a guest's is not known before it
    /// joins, so a guest's seats stay neutral in the menu.
    /// </remarks>
    public static class CornerAssignment
    {
        public const int Corners = 4;

        /// <summary>The corner of each slot: a Fisher-Yates shuffle of [0,1,2,3] driven by <paramref name="seed"/>.</summary>
        public static int[] Permutation(int seed)
        {
            var perm = new[] { 0, 1, 2, 3 };
            var rng = new System.Random(seed);
            for (int i = perm.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            return perm;
        }

        /// <summary>The permutation seed of a session: the menu-rolled solo seed, or the session name's hash.</summary>
        public static int SeedFor(SessionMode mode, string sessionName, int soloSeed) =>
            mode == SessionMode.Solo ? soloSeed : SessionNameSeed(sessionName ?? string.Empty);

        /// <summary>
        /// The corner the menu Coop's <paramref name="seat"/> will spawn on, or -1 when it cannot be known
        /// yet: a guest (its join order is decided by the session), or a host before its join code exists
        /// (<paramref name="permutation"/> null).
        /// </summary>
        public static int LobbySeatCorner(SessionMode mode, int seat, int[] permutation)
        {
            if (mode == SessionMode.Join || permutation == null || seat < 0) return -1;
            return permutation[seat % permutation.Length];
        }

        /// <summary>Stable 31-polynomial hash of the session name (string.GetHashCode is randomised per process).
        /// <c>MapGenerator</c> keeps an identical copy; <c>ContractsAndEnumsTests</c> keeps them in sync.</summary>
        public static int SessionNameSeed(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }
    }
}
