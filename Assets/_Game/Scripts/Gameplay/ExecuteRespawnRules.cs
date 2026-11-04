using System.Collections.Generic;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>What an executed chicken's authority does this tick while it is out.</summary>
    public enum ExecuteRespawnStep : byte
    {
        /// <summary>Still out.</summary>
        None = 0,
        /// <summary>Out time over: queue the teleport home (it stays removed for the tick it lands).</summary>
        QueueTeleport = 1,
        /// <summary>The teleport has landed: stand up at the base.</summary>
        StandUp = 2,
    }

    /// <summary>
    /// The executed chicken's way back, pure (Maestro 2026-11): out for <c>MarkKill.asset RemovalSeconds</c>, then back at its
    /// OWN corner's spawn point (the round reset's teleport, so nobody sees it slide), empty-handed, cooldowns still running.
    /// </summary>
    public static class ExecuteRespawnRules
    {
        public static ExecuteRespawnStep Next(bool outTimeOver, bool teleportQueued) =>
            !outTimeOver ? ExecuteRespawnStep.None : teleportQueued ? ExecuteRespawnStep.StandUp : ExecuteRespawnStep.QueueTeleport;

        /// <summary>The spawn point of <paramref name="homeCorner"/> (the same list the round reset teleports to, indexed by
        /// corner whether or not that base is claimed). False with no corner or no point for it.</summary>
        public static bool TrySpawnPoint(int homeCorner, IReadOnlyList<Vector3> spawnPoints, out Vector3 point)
        {
            point = default;
            if (homeCorner < 0 || spawnPoints == null || spawnPoints.Count == 0) return false;
            point = spawnPoints[homeCorner % spawnPoints.Count];
            return true;
        }
    }
}
