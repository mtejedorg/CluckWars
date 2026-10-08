using System;
using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Pure bookkeeping for concurrently running ability slots (Phase 6 chunk 3 review fixes): the
    /// active-mask arithmetic, which slots expired, which the next cast ends, which ran most recently, and the
    /// ONE teardown sequence for a slot. <see cref="AbilityController"/> stores the mask in a <c>[Networked]</c>
    /// byte and the timers in <c>TickTimer</c>s and routes every decision through here, so the rules are tested
    /// without a NetworkRunner.
    /// </summary>
    public static class AbilityRunRules
    {
        public static bool IsActive(byte mask, int slot) => slot >= 0 && slot < 8 && (mask & (1 << slot)) != 0;

        public static byte Begin(byte mask, int slot) => (byte)(mask | (1 << slot));

        public static byte End(byte mask, int slot) => (byte)(mask & ~(1 << slot));

        /// <summary>Mask of the RUNNING slots whose duration has run out (<paramref name="timerExpired"/>[i]).</summary>
        public static byte ExpiredMask(byte mask, bool[] timerExpired)
        {
            byte expired = 0;
            for (int i = 0; i < timerExpired.Length; i++)
                if (IsActive(mask, i) && timerExpired[i]) expired = Begin(expired, i);
            return expired;
        }

        /// <summary>
        /// Mask of the running slots a successful cast of <paramref name="castSlot"/> ends: those whose ability
        /// <paramref name="endsOnNextMove"/> (stealth, Peck's lock). The cast's own slot is never included.
        /// </summary>
        public static byte EndedByCast(byte mask, int castSlot, bool[] endsOnNextMove)
        {
            byte ended = 0;
            for (int i = 0; i < endsOnNextMove.Length; i++)
                if (i != castSlot && IsActive(mask, i) && endsOnNextMove[i]) ended = Begin(ended, i);
            return ended;
        }

        /// <summary>
        /// The running slot with the smallest elapsed time (cast most recently), or <paramref name="none"/>
        /// when nothing runs. Ties keep the lower slot.
        /// </summary>
        public static int MostRecent(byte mask, float[] elapsedSeconds, int none = -1)
        {
            int best = none;
            float bestElapsed = float.MaxValue;
            for (int i = 0; i < elapsedSeconds.Length; i++)
            {
                if (!IsActive(mask, i)) continue;
                if (elapsedSeconds[i] < bestElapsed) { bestElapsed = elapsedSeconds[i]; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The one teardown of a slot: runs <paramref name="ability"/>'s OnDeactivate with the slot as its source
        /// key, then ALWAYS removes every modifier the slot holds and ends its traversal hold, whatever the ability
        /// did or didn't clean up itself and even when the slot's asset was swapped mid-run (a loadout swap would
        /// otherwise call the NEW asset's OnDeactivate and leak the old one's modifier). Both removals are no-ops
        /// when nothing is held. The context's slot is reset to -1 afterwards so a stray later write is caught.
        /// </summary>
        public static void ReleaseSlot(int slot, AbilityBaseSO ability, AbilityContext ctx,
            AbilityEffectStack effects, Action<int> endTraversal)
        {
            try
            {
                if (ability != null && ctx != null)
                {
                    ctx.Slot = slot;
                    ability.OnDeactivate(ctx);
                }
            }
            finally
            {
                // Runs even if OnDeactivate throws (the exception still propagates and is logged by Unity).
                if (ctx != null) ctx.Slot = -1;
                effects?.RemoveSource(slot);
                endTraversal?.Invoke(slot);
            }
        }
    }
}
