using CluckWars.Abilities;
using CluckWars.Input;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// The ONE answer to "which way will this move point when the player lets go?" (Phase 7 B3, round-4 finding 3).
    /// The state authority uses it at fire time (<see cref="AbilityController"/>); the ground preview, the dashed /
    /// solid state, the target brackets and the HUD's in-range pip all use it before the release, so what the player
    /// is shown is the shape the move will actually take.
    /// <para>Order: the touch / pad soft-lock pick (<see cref="AimSoftLock"/>), else the bird's facing.</para>
    /// </summary>
    public static class AbilityAimForward
    {
        /// <summary>Pure core: the soft-lock pick, else the facing. A zero vector means "none".</summary>
        public static Vector3 Choose(Vector2 softLockPick, Vector3 facing, out bool aimed)
        {
            aimed = softLockPick != Vector2.zero;
            return aimed ? new Vector3(softLockPick.x, 0f, softLockPick.y) : facing;
        }

        /// <summary>
        /// The direction the shape of <paramref name="ability"/> is tested along on release. A move that does not turn the
        /// bird to the aim (self shapes, single-target picks by angle, ...) keeps the facing on every device.
        /// </summary>
        /// <param name="softLockHalfAngle">Magnetism cone for this device (<see cref="AimSoftLock.HalfAngleFor"/>), 0 = off.</param>
        /// <param name="offsetScratch">Reusable buffer for the soft-lock candidates (at least 4 long).</param>
        public static Vector3 Resolve(AbilityBaseSO ability, ChickenController caster,
                                      float softLockHalfAngle, Vector2[] offsetScratch, out bool aimed)
        {
            Vector3 facing = caster != null ? caster.transform.forward : Vector3.forward;
            aimed = false;
            if (ability == null || caster == null || !ability.UsesAim || !ability.RotatesToAim) return facing;

            Vector2 pick = Vector2.zero;
            if (softLockHalfAngle > 0f)
                pick = PickSoftLock(ability, caster, softLockHalfAngle, offsetScratch);
            return Choose(pick, facing, out aimed);
        }

        /// <summary>Direction to the eligible rival with the smallest angle to the bird's facing within
        /// <paramref name="halfAngleDegrees"/>, or zero. Eligible = would be hit if the bird faced them.</summary>
        public static Vector2 PickSoftLock(AbilityBaseSO ability, ChickenController caster, float halfAngleDegrees,
                                           Vector2[] offsetScratch)
        {
            var all = ChickenController.ActiveControllers;
            Vector3 origin = caster.transform.position;
            int n = 0;
            for (int i = 0; i < all.Count && n < offsetScratch.Length; i++)
            {
                var rival = all[i];
                if (rival == null || rival == caster) continue;

                Vector3 to = rival.transform.position - origin;
                to = Planar.Flat(to);
                if (to.sqrMagnitude < 1e-4f) continue;
                if (!ability.WouldAffect(caster, rival, to.normalized)) continue;

                offsetScratch[n++] = new Vector2(to.x, to.z);
            }

            Vector3 facing = caster.transform.forward;
            int best = AimSoftLock.PickClosestAngle(new Vector2(facing.x, facing.z), offsetScratch, n, halfAngleDegrees);
            return best < 0 ? Vector2.zero : offsetScratch[best].normalized;
        }
    }
}
