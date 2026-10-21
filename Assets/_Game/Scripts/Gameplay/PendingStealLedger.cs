using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The thief's local record of optimistic steal credits that the victim's authority has not
    /// yet confirmed, so <c>ChickenCargo.RPC_RefundRefusedSteal</c> can only undo a credit that
    /// actually happened. Pure and Fusion-free (the key is generic) so the EditMode suite can
    /// exercise every branch.
    /// </summary>
    /// <remarks>
    /// <b>Why it exists.</b> The refund RPC is <c>RpcSources.All</c> and its sender check only
    /// proves "this peer owns the named victim". Without a pending record, any client that owns
    /// some chicken could name it as a victim and remove a plausible steal from any other
    /// chicken, as often as it liked. With the ledger a refund is honoured only against a credit
    /// this thief really made, for at most the amount it credited, once.
    /// <para>
    /// Local to the thief's state authority and never replicated (Shared Mode trusts clients by
    /// design; this only closes the unsolicited-call gap). A fixed ring: when full, the oldest
    /// record is overwritten, which can only make a genuine refund fail after eight unconfirmed
    /// steals inside one window, a rate no ability set can produce.
    /// </para>
    /// </remarks>
    public sealed class PendingStealLedger<TKey> where TKey : struct
    {
        /// <summary>
        /// How long a credit stays refundable, in seconds. The refund travels credit, then
        /// RPC_DrainStolen to the victim's authority, then RPC_RefundRefusedSteal back: one full
        /// round trip plus two tick boundaries. At a pessimistic 250 ms RTT that is about 0.3 s,
        /// so 1.0 s covers it with margin for jitter and a missed snapshot, while still being
        /// short enough that a forged refund must be timed to a steal that just happened.
        /// </summary>
        public const float WindowSeconds = 1.0f;

        /// <summary>Slots in the ring. Snatch can rob up to three rivals in one cast.</summary>
        public const int Capacity = 8;

        // Tolerance when comparing a requested refund to a recorded amount (float noise only).
        private const float AmountEpsilon = 1e-4f;

        private struct Entry
        {
            public TKey Key;
            public float Amount;
            public float Time;
            public bool Live;
        }

        private readonly Entry[] _entries = new Entry[Capacity];
        private readonly float _windowSeconds;
        private int _next;

        /// <param name="windowSeconds">
        /// How long a record stays consumable. Defaults to <see cref="WindowSeconds"/> (the steal
        /// refund's one round trip); the execute-loot ledger passes a longer one because its
        /// report makes two relayed trips in Shared Mode (see
        /// <c>StealRules.ExecuteLootWindowSeconds</c>).
        /// </param>
        public PendingStealLedger(float windowSeconds = WindowSeconds)
        {
            if (!(windowSeconds > 0f))
                throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds, "window must be positive");
            _windowSeconds = windowSeconds;
        }

        /// <summary>This ledger's consumable window, in seconds.</summary>
        public float Window => _windowSeconds;

        /// <summary>Records an optimistic credit of <paramref name="amount"/> taken from <paramref name="key"/>.</summary>
        public void Record(TKey key, float amount, float now)
        {
            if (amount <= 0f) return;
            _entries[_next] = new Entry { Key = key, Amount = amount, Time = now, Live = true };
            _next = (_next + 1) % Capacity;
        }

        /// <summary>
        /// Matches a refund claim against a pending record from <paramref name="key"/> and consumes
        /// it. <paramref name="allowed"/> is the most the caller may take back:
        /// <c>min(recorded, requested)</c>. Expired records are dropped on the way.
        /// </summary>
        /// <returns>False (and 0) when there is no unexpired record from that key.</returns>
        public bool TryConsume(TKey key, float requested, float now, out float allowed)
        {
            allowed = 0f;
            if (requested <= 0f) return false;

            int best = -1;      // oldest record that covers the request
            int fallback = -1;  // oldest record from this key at all
            for (int i = 0; i < Capacity; i++)
            {
                if (!_entries[i].Live) continue;
                if (now - _entries[i].Time > _windowSeconds)
                {
                    _entries[i].Live = false;
                    continue;
                }
                if (!EqualityComparer<TKey>.Default.Equals(_entries[i].Key, key)) continue;

                if (fallback < 0 || _entries[i].Time < _entries[fallback].Time) fallback = i;
                if (_entries[i].Amount + AmountEpsilon >= requested &&
                    (best < 0 || _entries[i].Time < _entries[best].Time))
                    best = i;
            }

            int pick = best >= 0 ? best : fallback;
            if (pick < 0) return false;

            allowed = Math.Min(_entries[pick].Amount, requested);
            _entries[pick].Live = false;
            return true;
        }

        /// <summary>Drops every record (new round).</summary>
        public void Clear() => Array.Clear(_entries, 0, Capacity);
    }
}
