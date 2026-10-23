using Fusion;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The tick at which THIS peer joined the current session (qa W1, security review). Local and unforgeable: a remote
    /// peer can write a zone's timer, never when we arrived. Read by <see cref="AbilityZone"/>'s root evidence to tell
    /// "this egg was in my initial snapshot" from "I saw it spawn".
    /// </summary>
    /// <remarks>
    /// The join tick is <c>Runner.Tick.Raw</c> at the first <c>Spawned</c> this runner delivers to a noting behaviour: a
    /// joiner receives every live object (chickens, zones) in its first snapshot, so <see cref="ChickenController"/> and
    /// <see cref="AbilityZone"/> both note it. A zone is in the initial snapshot when THIS peer's <c>Spawned</c> for it
    /// ran at a tick &lt;= the join tick; anything spawned after the join is seen later and gets no late-joiner
    /// extension. If Fusion delivered the initial snapshot's <c>Spawned</c> calls over several ticks, a pre-join egg
    /// seen on a later tick would simply not get the extension (fail closed). A new runner (reconnect, next session)
    /// starts a new clock. Integer raw ticks only (never <c>Fusion.Tick</c> in state).
    /// </remarks>
    public static class LocalJoinClock
    {
        private static NetworkRunner _runner;
        private static int _joinTick;

        /// <summary>Records the join tick on the first call for <paramref name="runner"/>; later calls are no-ops.</summary>
        public static void Note(NetworkRunner runner)
        {
            if (runner == null || ReferenceEquals(runner, _runner)) return;
            _runner = runner;
            _joinTick = runner.Tick.Raw;
        }

        /// <summary>True when an object this peer first saw at <paramref name="firstSeenTick"/> was part of its initial snapshot.</summary>
        public static bool InJoinSnapshot(NetworkRunner runner, int firstSeenTick) =>
            runner != null && ReferenceEquals(runner, _runner) && IsInJoinSnapshot(firstSeenTick, _joinTick);

        /// <summary>Pure: first seen at or before the join tick.</summary>
        public static bool IsInJoinSnapshot(int firstSeenTick, int joinTick) => firstSeenTick <= joinTick;
    }
}
