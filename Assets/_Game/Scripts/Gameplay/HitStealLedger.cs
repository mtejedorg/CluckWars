using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The Bully's per-victim lock (design 2026-11-25, 16.2): a take on a victim locks that victim against every hitter of the same caster for
    /// <c>HitStealLockSeconds</c>, and a take the victim's authority settles at 0 gives the lock back. Authority-local and never replicated, like
    /// <see cref="PendingStealLedger{TKey}"/>; pure and Fusion-free (the key is generic) so EditMode exercises every branch. The lock itself is
    /// <see cref="HitStealRules.Locked"/>, the rule the match simulator calls too.
    /// </summary>
    public sealed class HitStealLedger<TKey> where TKey : struct
    {
        private struct Lock
        {
            public TKey Victim;
            public ushort RequestId;
            public float Until;
        }

        private readonly List<Lock> _locks = new List<Lock>(4);

        /// <summary>Locks still held (running or not yet pruned).</summary>
        public int Count => _locks.Count;

        /// <summary>Is <paramref name="victim"/> locked at <paramref name="now"/>? The lock holds against every hitter of the caster.</summary>
        public bool Locked(TKey victim, float now)
        {
            int i = IndexOf(victim);
            return i >= 0 && HitStealRules.Locked(now, _locks[i].Until);
        }

        /// <summary>Seconds left on <paramref name="victim"/>'s lock; 0 when none runs.</summary>
        public float LockLeft(TKey victim, float now)
        {
            int i = IndexOf(victim);
            if (i < 0) return 0f;
            float left = _locks[i].Until - now;
            return left > 0f ? left : 0f;
        }

        /// <summary>A take of request <paramref name="requestId"/> was asked of <paramref name="victim"/> at <paramref name="now"/>: it locks him for <paramref name="lockSeconds"/>.</summary>
        public void Ask(TKey victim, ushort requestId, float now, float lockSeconds)
        {
            Prune(now);
            var entry = new Lock { Victim = victim, RequestId = requestId, Until = now + lockSeconds };
            int i = IndexOf(victim);
            if (i >= 0) _locks[i] = entry;
            else _locks.Add(entry);
        }

        /// <summary>
        /// The victim's authority answered request <paramref name="requestId"/> with <paramref name="credited"/>: a 0 releases the lock that ask started
        /// (nothing was taken, so nothing is locked); a positive answer keeps it. Returns true when the request was one this ledger asked.
        /// </summary>
        public bool Settled(TKey victim, ushort requestId, float credited)
        {
            int i = IndexOf(victim);
            if (i < 0 || _locks[i].RequestId != requestId) return false;
            if (!(credited > 0f)) _locks.RemoveAt(i);
            return true;
        }

        public void Clear() => _locks.Clear();

        private int IndexOf(TKey victim)
        {
            var comparer = EqualityComparer<TKey>.Default;
            for (int i = 0; i < _locks.Count; i++)
                if (comparer.Equals(_locks[i].Victim, victim)) return i;
            return -1;
        }

        // An ended lock is dropped when the next one is asked, so the list never outgrows the rivals a chicken has.
        private void Prune(float now)
        {
            for (int i = _locks.Count - 1; i >= 0; i--)
                if (!HitStealRules.Locked(now, _locks[i].Until)) _locks.RemoveAt(i);
        }
    }
}
