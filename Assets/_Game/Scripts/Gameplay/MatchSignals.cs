using System;
using CluckWars.Logging;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Local, per-peer match signals (B10 / F5). <see cref="RoundReset"/> fires exactly once per world reset on EVERY peer:
    /// on the <see cref="GameManager"/>'s state authority from inside <c>ResetWorldForNewRound</c> (before its RPC fan-out),
    /// and on every other peer when <see cref="GameManager.Render"/> observes the replicated
    /// <see cref="GameManager.ResetTickRaw"/> change (<see cref="RoundResetObservation"/>). The argument is that reset tick
    /// (<c>Tick.Raw</c>): the cut-off subscribers compare their own spawn tick against.
    /// </summary>
    /// <remarks>
    /// Why every peer and not only the master: in Shared Mode a placed zone's (or a decoy's) state authority is its caster's
    /// peer, and only that peer can <c>Runner.Despawn</c> it. Subscribers must therefore only record the request and act in
    /// their own authority-gated <c>FixedUpdateNetwork</c>: a handler can run inside another object's tick, inside
    /// <c>Spawned</c> or from a UI press (PLAY AGAIN), none of which is a safe place to despawn.
    /// <para>Not networked and never RPC'd: it is a C# event over state every peer already replicates.</para>
    /// </remarks>
    public static class MatchSignals
    {
        private const string Source = "MatchSignals";

        /// <summary>The world was reset for a new round at the given simulation tick (<c>Tick.Raw</c>).</summary>
        public static event Action<int> RoundReset;

        /// <summary>Current subscriber count (diagnostics and tests).</summary>
        public static int RoundResetSubscribers => RoundReset?.GetInvocationList().Length ?? 0;

        /// <summary>
        /// Invokes every <see cref="RoundReset"/> subscriber once with <paramref name="resetTick"/>. A throwing subscriber is
        /// reported through <paramref name="log"/> and does not stop the others (nor the reset that raised it). Returns how
        /// many ran cleanly.
        /// </summary>
        public static int RaiseRoundReset(int resetTick, ILogService log)
        {
            var handlers = RoundReset;
            if (handlers == null) return 0;
            int ok = 0;
            foreach (var d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)d)(resetTick);
                    ok++;
                }
                catch (Exception e)
                {
                    // The reset itself must go on; the subscriber that broke is named so it can be fixed.
                    log?.Error(Source, $"RoundReset subscriber {d.Method.DeclaringType?.Name}.{d.Method.Name} threw " +
                        $"({e.GetType().Name}: {e.Message}); its object may survive into the next round.", e);
                }
            }
            return ok;
        }

        // Enter Play Mode without a domain reload keeps statics: drop subscribers of a previous play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => RoundReset = null;
    }

    /// <summary>The B10 decisions over plain ticks (pure, tested).</summary>
    /// <remarks>
    /// The trigger is the replicated <see cref="GameManager.ResetTickRaw"/>, written once per <c>ResetWorldForNewRound</c>,
    /// not the <c>Ended -&gt; WaitingForPlayers</c> state edge: joiners do miss that edge (session-20261021-213935 round 3:
    /// the joiners saw <c>Ended -&gt; Starting</c>, the host pressed START in the frame it re-armed the waiting room).
    /// </remarks>
    public static class RoundResetObservation
    {
        /// <summary>
        /// <see cref="GameManager.Render"/>'s change detector saw <see cref="GameManager.ResetTickRaw"/> go from
        /// <paramref name="previous"/> to <paramref name="current"/>. The state authority already raised the signal inside
        /// the reset, so it never raises again from what it observes.
        /// </summary>
        public static bool ShouldRaiseOnChange(bool hasStateAuthority, int previous, int current) =>
            !hasStateAuthority && current != 0 && previous != current;

        /// <summary>
        /// A non-authority copy of the manager spawned with a reset already recorded: a fresh manager that cleaned a dirty
        /// world in its own <c>Spawned</c>, or a late joiner. Exact for both, because <see cref="OutlivedReset"/> only takes
        /// objects that existed at that tick (a late joiner's own objects all spawn later).
        /// </summary>
        public static bool ShouldRaiseOnSpawn(bool hasStateAuthority, int resetTick) =>
            !hasStateAuthority && resetTick != 0;

        /// <summary>
        /// A zone / decoy that spawned at <paramref name="spawnTick"/> (its authority's <c>Runner.Tick</c> in
        /// <c>Spawned</c>) belongs to the round that the reset at <paramref name="resetTick"/> ended. At or before: it was
        /// alive during that round (the reset can run in the same tick, after it was spawned). After: it was cast in the
        /// NEW round - e.g. on a stalled peer whose first snapshot already carried the reset and State=Active before its
        /// Render raised the signal - and must live. 0 = no reset recorded.
        /// </summary>
        public static bool OutlivedReset(int spawnTick, int resetTick) =>
            resetTick != 0 && spawnTick <= resetTick;
    }
}
