using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// One attacker's per-victim cooldowns, pure: Sneaky Steal cannot take from the same bird again until its lock ends, while the
    /// next nearest rival stays fair game. At most a handful of rivals, so a list. Times are <c>Runner.SimulationTime</c>.
    /// </summary>
    public sealed class TargetCooldowns<TTarget> where TTarget : struct, IEquatable<TTarget>
    {
        private readonly List<(TTarget target, double until)> _locks = new List<(TTarget, double)>(4);

        public void Lock(TTarget target, double until)
        {
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i].target.Equals(target)) { _locks[i] = (target, until); return; }
            _locks.Add((target, until));
        }

        public bool Blocks(TTarget target, double now)
        {
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i].target.Equals(target)) return now < _locks[i].until;
            return false;
        }

        /// <summary>New round: nothing carries over.</summary>
        public void Clear() => _locks.Clear();
    }
}
