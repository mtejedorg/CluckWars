using System;
using CluckWars.Diagnostics;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The subscribers of one local event, kept so that a raise can call each one separately (a subscriber that
    /// throws must not cost the others the beat) without allocating: <see cref="Items"/> is an array that is
    /// replaced on subscribe / unsubscribe and never mutated, so a raise in progress keeps iterating the array it
    /// started with, exactly as a multicast delegate would.
    /// </summary>
    public sealed class ListenerList<TDelegate> where TDelegate : Delegate
    {
        private TDelegate[] _items = Array.Empty<TDelegate>();

        /// <summary>The current subscribers, in subscription order. Read it once per raise; never write to it.</summary>
        public TDelegate[] Items => _items;

        public int Count => _items.Length;

        public void Add(TDelegate listener)
        {
            if (listener == null) return;
            var next = new TDelegate[_items.Length + 1];
            Array.Copy(_items, next, _items.Length);
            next[_items.Length] = listener;
            _items = next;
        }

        /// <summary>Removes the most recent subscription equal to <paramref name="listener"/> (delegate <c>-=</c> semantics).</summary>
        public void Remove(TDelegate listener)
        {
            if (listener == null) return;
            for (int i = _items.Length - 1; i >= 0; i--)
            {
                if (!_items[i].Equals(listener)) continue;
                var next = new TDelegate[_items.Length - 1];
                Array.Copy(_items, 0, next, 0, i);
                Array.Copy(_items, i + 1, next, i, _items.Length - i - 1);
                _items = next;
                return;
            }
        }
    }

    /// <summary>
    /// Raises a local event over a <see cref="ListenerList{TDelegate}"/>: every subscriber is called on its own, so
    /// one that throws is reported (<see cref="ILogService.Error"/>, rate-limited per event and listener type) and
    /// the next still runs. Allocates nothing unless a listener fails.
    /// </summary>
    public static class LocalRaise
    {
        private const string Source = "ChickenEvents";

        /// <summary>At most one report per event and listener type every this many seconds; the rest are counted.</summary>
        public const float ErrorIntervalSeconds = 5f;

        private static readonly PerKeyRateLimiter ErrorLimiter = new PerKeyRateLimiter(ErrorIntervalSeconds);
        private static readonly ILogService FallbackLog = new UnityLogService();

        /// <summary>False when nobody listens.</summary>
        public static bool Invoke(ListenerList<Action> listeners, string eventName, ILogService log, UnityEngine.Object owner)
        {
            var items = listeners.Items;
            for (int i = 0; i < items.Length; i++)
            {
                try { items[i](); }
                catch (Exception e) { Report(eventName, items[i], e, log, owner); }
            }
            return items.Length > 0;
        }

        /// <summary>False when nobody listens.</summary>
        public static bool Invoke<T>(ListenerList<Action<T>> listeners, string eventName, ILogService log, UnityEngine.Object owner, T payload)
        {
            var items = listeners.Items;
            for (int i = 0; i < items.Length; i++)
            {
                try { items[i](payload); }
                catch (Exception e) { Report(eventName, items[i], e, log, owner); }
            }
            return items.Length > 0;
        }

        private static void Report(string eventName, Delegate listener, Exception exception, ILogService log, UnityEngine.Object owner)
        {
            var type = listener.Target != null ? listener.Target.GetType() : listener.Method.DeclaringType;
            string typeName = type != null ? type.Name : "?";
            int key = unchecked(eventName.GetHashCode() * 397 ^ typeName.GetHashCode());
            if (!ErrorLimiter.Allow(key, Time.realtimeSinceStartup, out int held)) return;
            string ownerName = owner != null ? owner.name : "(no chicken)";
            (log ?? FallbackLog).Error(Source,
                $"A {eventName} listener ({typeName}.{listener.Method.Name}) threw on '{ownerName}'. That listener missed the " +
                "beat; the other listeners still got it." + (held > 0 ? $" (+{held} similar suppressed)" : string.Empty),
                exception);
        }
    }
}
