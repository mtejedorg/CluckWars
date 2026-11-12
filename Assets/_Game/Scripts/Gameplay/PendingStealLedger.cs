using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// A chicken's local record of the food transfers it has opened and that the other authority
    /// has not answered yet: steals and pecks it asked for, deposit flushes it sent, executes whose
    /// loot it expects. The settle RPCs on <c>ChickenCargo</c> honour a report only against a record
    /// here. Pure and Fusion-free (the key is generic) so the EditMode suite can exercise every branch.
    /// </summary>
    /// <remarks>
    /// <b>Why it exists.</b> Every settle RPC is <c>RpcSources.All</c> and its sender check only
    /// proves "this peer owns the named pile, victim or base". Without a pending record, any client
    /// that owns some chicken could name it as a victim and report a plausible steal to any other
    /// chicken, as often as it liked. With the ledger a report is honoured only against a request
    /// this chicken really made, for at most the amount it asked for, once.
    /// <para>
    /// Local to the requesting chicken's state authority and never replicated (Shared Mode trusts clients by
    /// design; this only closes the unsolicited-call gap). A fixed ring: when full, the oldest
    /// record is overwritten, which can only make a genuine report fail after eight unanswered
    /// requests inside one window: no ability set produces that, and deposit flushes (4 a second)
    /// reach it only when no acknowledgement has come back for two seconds.
    /// </para>
    /// </remarks>
    public sealed class PendingStealLedger<TKey> where TKey : struct
    {
        /// <summary>
        /// The default window, in seconds: one full round trip plus two tick boundaries (about 0.3 s
        /// at a pessimistic 250 ms RTT) with margin for jitter and a missed snapshot, while still
        /// short enough that a forged report must be timed to a request that was just made. The
        /// ledgers in production pass their own, longer window (two relayed trips):
        /// <c>FoodTransferRules.SettleWindowSeconds</c> and <c>StealRules.ExecuteLootWindowSeconds</c>.
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

        /// <summary>Records a transfer of <paramref name="amount"/> opened with <paramref name="key"/>.</summary>
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

        /// <summary>
        /// Consumes the oldest unexpired record from <paramref name="key"/>, whatever its amount, and returns that amount.
        /// The settle half of a food transfer (<see cref="FoodTransferRules"/>): reports from one owner arrive in the
        /// order the requests were sent, and a report of zero must still close its request.
        /// </summary>
        /// <returns>False (and 0) when there is no unexpired record from that key.</returns>
        public bool TryConsumeOldest(TKey key, float now, out float recorded)
        {
            recorded = 0f;
            int pick = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (!IsLive(i, now)) continue;
                if (!EqualityComparer<TKey>.Default.Equals(_entries[i].Key, key)) continue;
                if (pick < 0 || _entries[i].Time < _entries[pick].Time) pick = i;
            }
            if (pick < 0) return false;

            recorded = _entries[pick].Amount;
            _entries[pick].Live = false;
            return true;
        }

        /// <summary>The sum of every unexpired record: what is still asked for and not yet settled.</summary>
        public float Outstanding(float now)
        {
            float total = 0f;
            for (int i = 0; i < Capacity; i++)
                if (IsLive(i, now)) total += _entries[i].Amount;
            return total;
        }

        // Expired records are dropped on the way, as TryConsume does.
        private bool IsLive(int i, float now)
        {
            if (!_entries[i].Live) return false;
            if (now - _entries[i].Time <= _windowSeconds) return true;
            _entries[i].Live = false;
            return false;
        }

        /// <summary>Drops every record (new round).</summary>
        public void Clear() => Array.Clear(_entries, 0, Capacity);
    }
}
