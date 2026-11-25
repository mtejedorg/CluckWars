using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.Balance
{
    /// <summary>
    /// The what-if roster swap (design 2026-11-25, section 5): a copy of the shipped ability registry in which one passive is replaced by another,
    /// so a passive that is not registered (the Trophy Hunter) can take a seat in the match simulator. The shipped registry, the game and
    /// <see cref="BalanceBook"/> are untouched; the book and the simulator are simply built over the copy.
    /// </summary>
    public static class OracleRoster
    {
        /// <summary>
        /// A new registry holding every entry of <paramref name="shipped"/> with <paramref name="outgoing"/> replaced by <paramref name="incoming"/>.
        /// Refused (throws) when either passive is null, the outgoing one is not in the registry, the incoming one already is, or the two do not
        /// allow the same single class. The caller owns the copy and destroys it.
        /// </summary>
        public static AbilityRegistrySO Swap(AbilityRegistrySO shipped, PassiveAbilitySO outgoing, PassiveAbilitySO incoming)
        {
            if (shipped == null) throw new ArgumentNullException(nameof(shipped));
            if (outgoing == null) throw new ArgumentNullException(nameof(outgoing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            int at = shipped.All == null ? -1 : Array.IndexOf(shipped.All, outgoing);
            if (at < 0) throw new InvalidOperationException($"Roster swap: '{outgoing.name}' is not in the ability registry, so it has no seat to give up.");
            if (shipped.All.Contains(incoming)) throw new InvalidOperationException($"Roster swap: '{incoming.name}' is already in the ability registry.");
            if (!PreEquippedLoadout.TryGetSingleClass(outgoing.AllowedClasses, out var from) || !PreEquippedLoadout.TryGetSingleClass(incoming.AllowedClasses, out var to))
                throw new InvalidOperationException($"Roster swap: '{outgoing.name}' and '{incoming.name}' must each allow exactly one class.");
            if (from != to) throw new InvalidOperationException($"Roster swap: '{incoming.name}' is a {to} passive and '{outgoing.name}' a {from} one; a seat is swapped within its class.");

            var copy = ScriptableObject.CreateInstance<AbilityRegistrySO>();
            copy.name = shipped.name + " (what-if roster)";
            copy.All = (AbilityBaseSO[])shipped.All.Clone();
            copy.All[at] = incoming;
            return copy;
        }
    }
}
