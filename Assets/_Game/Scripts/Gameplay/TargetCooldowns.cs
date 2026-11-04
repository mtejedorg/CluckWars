using System;
using System.Collections.Generic;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// One attacker's per-victim cooldowns, pure: Sneaky Steal cannot take from the same bird again until its lock ends, while the
    /// next nearest rival stays fair game. At most a handful of rivals, so a list. Times are <c>Runner.SimulationTime</c>.
    /// A lock starts late on purpose: the caster's hit confirmation re-derives the cast's targets a frame or more after the cast,
    /// through the same filter, and a lock already biting there would hide the real victim or spark a bystander.
    /// </summary>
    public sealed class TargetCooldowns<TTarget> where TTarget : struct, IEquatable<TTarget>
    {
        private readonly List<(TTarget target, double from, double until)> _locks = new List<(TTarget, double, double)>(4);

        public void Lock(TTarget target, double from, double until)
        {
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i].target.Equals(target)) { _locks[i] = (target, from, until); return; }
            _locks.Add((target, from, until));
        }

        public bool Blocks(TTarget target, double now)
        {
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i].target.Equals(target)) return now >= _locks[i].from && now < _locks[i].until;
            return false;
        }

        /// <summary>New round: nothing carries over.</summary>
        public void Clear() => _locks.Clear();
    }
}
