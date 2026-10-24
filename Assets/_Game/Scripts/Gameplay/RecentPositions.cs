using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The last ~<see cref="WindowSeconds"/> of a chicken's own positions on its state authority (local, never
    /// networked). Used by the root-claim check: a Root Egg's authority decides on its (lagged) view of the victim, so a
    /// victim that stood in the egg and then jumped away (Doppelganger blink, session 20261023-173009) was refused
    /// although the trigger was honest. The victim now also asks "was I inside a zone this caster owns within the window
    /// the RPC could have been in flight?" - its own past position is local truth, and the zone must still vouch, so no
    /// evidence rule is loosened.
    /// </summary>
    /// <remarks>
    /// Cleared on the round-reset teleport, so a position from the previous round can never vouch for anything.
    /// </remarks>
    public sealed class RecentPositions
    {
        /// <summary>How far back a claim may look: the latency cap the control bounds already accept (<see cref="ExecuteRules.MaxLatencySeconds"/>).</summary>
        public const float WindowSeconds = ExecuteRules.MaxLatencySeconds;

        // 32 Hz x 0.5 s = 16 samples; 24 leaves headroom for a faster tick rate or a frame of slack.
        private const int Capacity = 24;

        private readonly float[] _t = new float[Capacity];
        private readonly Vector3[] _p = new Vector3[Capacity];
        private int _head;   // next write
        private int _count;

        public int Count => _count;

        public void Record(float time, Vector3 position)
        {
            _t[_head] = time;
            _p[_head] = position;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// True when a sample recorded within <see cref="WindowSeconds"/> before <paramref name="now"/> satisfies
        /// <paramref name="inside"/>. Samples from the future (clock reset) or older than the window never count.
        /// </summary>
        public bool AnyWithinWindow(float now, System.Func<Vector3, bool> inside) =>
            AnyWithinWindow(now, (_, p) => inside(p));

        /// <summary>
        /// As above, but the predicate also receives the time the sample was recorded, so a claim can refuse samples
        /// from before the evidence it leans on existed (a zone this peer had not yet seen).
        /// </summary>
        public bool AnyWithinWindow(float now, System.Func<float, Vector3, bool> inside)
        {
            for (int i = 0; i < _count; i++)
            {
                int k = (_head - 1 - i + Capacity) % Capacity;
                float age = now - _t[k];
                if (age < 0f || age > WindowSeconds) continue;
                if (inside(_t[k], _p[k])) return true;
            }
            return false;
        }
    }
}
