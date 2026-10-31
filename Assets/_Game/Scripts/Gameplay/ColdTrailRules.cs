using System;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The anti-chase rule for Death Mark, pure: after a mark ends (paid or fizzled) that rival cannot be marked again by this
    /// Assassin until he has marked someone else or the lockout passes, so an Assassin cannot camp one bird. Off when
    /// that bird is the only rival in the match, or a duel would stop being playable.
    /// </summary>
    /// <remarks>
    /// <b>Decoys:</b> a Doppelganger decoy cannot start a trail (a fizzle on a fake says nothing about the real bird) and
    /// marking one is not "someone else" (or a Thief's decoy would hand the Assassin his victim back).
    /// One trail is enough: the only way to clear it early is a new mark, whose end starts the next trail.
    /// Local to the Assassin's state authority, never networked: an authority migration just drops the trail.
    /// </remarks>
    public sealed class ColdTrail<TRival> where TRival : struct, IEquatable<TRival>
    {
        private bool _active;
        private TRival _rival;
        private double _until;

        /// <summary>A mark was placed on <paramref name="rival"/>: marking someone real clears the trail.</summary>
        public void OnMarked(TRival rival, bool isDecoy)
        {
            if (!isDecoy && !rival.Equals(_rival)) _active = false;
        }

        /// <summary>A mark on <paramref name="rival"/> ended, however it ended.</summary>
        public void OnMarkEnded(TRival rival, bool isDecoy, double now, float lockoutSeconds)
        {
            if (isDecoy) return;
            _active = lockoutSeconds > 0f;
            _rival = rival;
            _until = now + lockoutSeconds;
        }

        /// <summary>Does the trail refuse <paramref name="candidate"/> now? <paramref name="nonDecoyRivals"/> counts the Assassin's real rivals.</summary>
        public bool Refuses(TRival candidate, double now, int nonDecoyRivals) =>
            _active && nonDecoyRivals > 1 && now < _until && candidate.Equals(_rival);

        /// <summary>New round: nothing carries over.</summary>
        public void Reset() => _active = false;
    }

    /// <summary>The other half of the anti-chase rule: home is safe. Pure geometry.</summary>
    public static class HomeBreakRules
    {
        /// <summary>Planar, inclusive: true when <paramref name="chicken"/> stands inside its own base's deposit radius.</summary>
        public static bool IsHome(Vector3 chicken, Vector3 ownBase, float radius)
        {
            float dx = chicken.x - ownBase.x, dz = chicken.z - ownBase.z;
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
