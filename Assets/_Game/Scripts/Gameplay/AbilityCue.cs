namespace CluckWars.Gameplay
{
    /// <summary>
    /// A moment of a chicken's own ability use that only the player driving it is told about. An id, not a sound:
    /// presentation resolves what it sounds like (<c>HitFeedback</c>, by <c>SfxId</c>).
    /// </summary>
    /// <remarks>
    /// None of these is replicated, on purpose: each happens in the tick of the chicken's state authority, which for
    /// a human's chicken (Shared Mode) is that human's own peer, and nobody else is meant to hear it.
    /// </remarks>
    public enum AbilityCue : byte
    {
        /// <summary>The player cancelled an aim on purpose (Esc / right mouse / touch edge band).</summary>
        Cancelled = 0,
        /// <summary>A release with nobody in range: no effect, no cooldown.</summary>
        Fizzled = 1,
        /// <summary>The peck chain stopped because the cargo is full.</summary>
        CargoFull = 2,
        /// <summary>A timed ability ran out on its own.</summary>
        Expired = 3,
    }

    /// <summary>
    /// Carries <see cref="AbilityCue"/>s from the tick that notices them to the next <c>Render</c>, which raises
    /// them as local events: nothing is played from <c>FixedUpdateNetwork</c> (CONVENTIONS "Replicated moments",
    /// rule 4). <c>Render</c> follows the tick in the same frame, so the cue is not late. A cue noticed twice
    /// before one <c>Render</c> is raised once.
    /// </summary>
    public struct AbilityCueLatch
    {
        public const int CueCount = 4;

        private byte _pending;

        public bool Any => _pending != 0;

        public void Queue(AbilityCue cue) => _pending |= (byte)(1 << (int)cue);

        /// <summary>Takes the lowest pending cue; false when none is left.</summary>
        public bool TryTake(out AbilityCue cue)
        {
            for (int i = 0; i < CueCount; i++)
            {
                byte bit = (byte)(1 << i);
                if ((_pending & bit) == 0) continue;
                _pending &= (byte)~bit;
                cue = (AbilityCue)i;
                return true;
            }
            cue = default;
            return false;
        }

        public void Clear() => _pending = 0;
    }
}
