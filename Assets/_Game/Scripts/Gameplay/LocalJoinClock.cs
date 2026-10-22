using Fusion;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// When THIS peer joined the current session, on its own clock (qa W1). Local and unforgeable: a remote peer can
    /// write a zone's timer, never when we arrived. Read by <see cref="AbilityZone"/>'s root evidence to tell "this egg
    /// was already there when I joined" from "I saw it spawn".
    /// </summary>
    /// <remarks>
    /// The join moment is the first <c>Spawned</c> this runner delivers to a noting behaviour: a joiner receives every
    /// live object (chickens, zones) in its first snapshot, so <see cref="ChickenController"/> and
    /// <see cref="AbilityZone"/> both note it. A new runner (reconnect, next session) starts a new clock.
    /// </remarks>
    public static class LocalJoinClock
    {
        private static NetworkRunner _runner;
        private static float _joinedAt;

        /// <summary>Records the join on the first call for <paramref name="runner"/>; later calls are no-ops.</summary>
        public static void Note(NetworkRunner runner)
        {
            if (runner == null || ReferenceEquals(runner, _runner)) return;
            _runner = runner;
            _joinedAt = Time.realtimeSinceStartup;
        }

        /// <summary>Real seconds since this peer joined <paramref name="runner"/>'s session; +infinity when it never noted it.</summary>
        public static float SecondsSinceJoin(NetworkRunner runner) =>
            runner != null && ReferenceEquals(runner, _runner) ? Time.realtimeSinceStartup - _joinedAt : float.PositiveInfinity;
    }
}
