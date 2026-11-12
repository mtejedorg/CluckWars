namespace CluckWars.Gameplay
{
    /// <summary>
    /// The cursor over one replicated one-shot counter (a <c>[Networked] byte XEventId</c> the state authority
    /// bumps). Pure, tested; <see cref="ChickenLocalEvents"/> holds one per counter, and nothing else keeps one.
    /// </summary>
    /// <remarks>
    /// The rules, in one place:
    /// <list type="bullet">
    ///   <item>The first observation only seeds: a late joiner (or a recycled object) inheriting a non-zero id never
    ///   replays a moment that happened before it could see it.</item>
    ///   <item>A changed id fires once. The value does not matter, so the byte wrap 255 to 0 is a change like any other.</item>
    ///   <item>Several bumps between two observations fire ONCE: the fields written beside the id describe only the
    ///   latest moment, so there is nothing truthful to say about the ones in between. (Exactly 256 bumps between two
    ///   observations would read as no change; observations run every frame, bumps at most every tick.)</item>
    ///   <item><see cref="Reset"/> forgets the baseline, so the next observation seeds again (spawn, despawn, pool reuse).</item>
    /// </list>
    /// </remarks>
    public struct EventEdge
    {
        private bool _seeded;
        private byte _last;

        /// <summary>True once the baseline is taken.</summary>
        public bool Seeded => _seeded;

        /// <summary>True when <see cref="Observe"/> would fire for <paramref name="id"/> (does not move the cursor).</summary>
        public bool WouldFire(byte id) => _seeded && id != _last;

        /// <summary>Moves the cursor to <paramref name="id"/>; true exactly when it changed since the last observation.</summary>
        public bool Observe(byte id)
        {
            if (!_seeded)
            {
                _seeded = true;
                _last = id;
                return false;
            }
            if (id == _last) return false;
            _last = id;
            return true;
        }

        /// <summary>Forgets the baseline: the next observation seeds again and fires nothing.</summary>
        public void Reset()
        {
            _seeded = false;
            _last = 0;
        }
    }
}
