namespace CluckWars.Balance
{
    /// <summary>
    /// The match simulator's own random numbers: SplitMix64 turns a match seed into PCG32 streams. Not <c>System.Random</c>
    /// and not <c>UnityEngine.Random</c>, so a seed gives the same sequence on every runtime. A value type: copy it and
    /// both copies continue from the same place.
    /// </summary>
    public struct MatchRng
    {
        /// <summary>Seat streams are 0 to 3; these three are the shared ones.</summary>
        public const ulong StreamComeback = 100, StreamMark = 101, StreamSetup = 102;

        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        private ulong _state;
        private ulong _inc;

        /// <summary>The reference <c>pcg32_srandom(initState, initSeq)</c>.</summary>
        public static MatchRng Pcg32(ulong initState, ulong initSeq)
        {
            var rng = new MatchRng { _state = 0UL, _inc = (initSeq << 1) | 1UL };
            rng.NextUInt();
            rng._state += initState;
            rng.NextUInt();
            return rng;
        }

        /// <summary>The next SplitMix64 output; advances <paramref name="state"/>.</summary>
        public static ulong SplitMix64(ref ulong state)
        {
            state += Golden;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>The stream <paramref name="stream"/> of one match: every seat, the comeback draw and the mark pick get their own.</summary>
        public static MatchRng Create(ulong matchSeed, ulong stream)
        {
            ulong s = matchSeed ^ (stream + 1UL) * Golden;
            ulong initState = SplitMix64(ref s);
            ulong initSeq = SplitMix64(ref s);
            return Pcg32(initState, initSeq);
        }

        /// <summary>The seed of match <paramref name="replicate"/> of design cell <paramref name="cell"/> in a run seeded <paramref name="runSeed"/>.</summary>
        public static ulong MatchSeed(ulong runSeed, int cell, int replicate)
        {
            ulong s = runSeed;
            ulong a = SplitMix64(ref s) ^ (ulong)(uint)cell * 0xD1B54A32D192ED03UL;
            s = a;
            ulong b = SplitMix64(ref s) ^ (ulong)(uint)replicate * 0x8CB92BA72F3D8DD7UL;
            s = b;
            return SplitMix64(ref s);
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * Multiplier + _inc;
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>A float in [0, 1): 24 random bits, so 1 is never returned.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>An integer in [0, <paramref name="maxExclusive"/>); 0 when the bound is not positive.</summary>
        public int NextInt(int maxExclusive) =>
            maxExclusive <= 0 ? 0 : (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);
    }
}
