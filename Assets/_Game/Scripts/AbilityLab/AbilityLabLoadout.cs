using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.AbilityLab
{
    /// <summary>
    /// The Ability Lab's own loadout selection rules. Pure, static, and deliberately
    /// NOT <c>MatchBootstrapper.ResolveLegalLoadout</c>.
    /// </summary>
    /// <remarks>
    /// The match sanitiser force-inserts Peck (or forcibly strips it) and forces the
    /// specialization's signature into slot 0. Those rules exist so a live match can
    /// never spawn a chicken that is unable to score — a correct invariant for a match
    /// and the exact opposite of what a lab is for. Asking to look at Wing Slam on its
    /// own and receiving Peck plus a signature in two of the four slots makes the tool
    /// useless, so the lab honours the four picks it is given and backfills only what
    /// is genuinely missing.
    /// <para>
    /// The one match rule the lab keeps is class legality, because "does this ability
    /// feel right on this class" is usually the question being asked — but
    /// <c>ignoreLegality</c> turns it off, which is the whole point of a lab: trying the
    /// combinations the lobby refuses.
    /// </para>
    /// <para>
    /// <b>Every resolved slot is non-null on purpose.</b> <see cref="AbilityController.SetSlots"/>
    /// ignores null arguments (a null means "keep the prefab default"), so the lab cannot
    /// express an empty slot through it and must always hand over four real abilities.
    /// Changing that would mean a new setter on shipped code; nothing in the lab needs one.
    /// </para>
    /// </remarks>
    public static class AbilityLabLoadout
    {
        /// <summary>
        /// Every active (non-passive) ability the lab will offer for <paramref name="cls"/>.
        /// Registry order is preserved so the picker list doesn't reshuffle between frames.
        /// </summary>
        public static List<AbilityBaseSO> FilterActives(AbilityRegistrySO registry, ChickenClass cls, bool ignoreLegality)
        {
            var result = new List<AbilityBaseSO>();
            if (registry == null) return result;

            foreach (var ability in registry.ActiveAbilities)
            {
                if (ability == null) continue;
                if (!ignoreLegality && !AbilityRegistrySO.IsAllowedFor(ability, cls)) continue;
                result.Add(ability);
            }
            return result;
        }

        /// <summary>Every passive the lab will offer for <paramref name="cls"/>.</summary>
        public static List<PassiveAbilitySO> FilterPassives(AbilityRegistrySO registry, ChickenClass cls, bool ignoreLegality)
        {
            var result = new List<PassiveAbilitySO>();
            if (registry == null) return result;

            foreach (var passive in registry.Passives)
            {
                if (passive == null) continue;
                if (!ignoreLegality && !AbilityRegistrySO.IsAllowedFor(passive, cls)) continue;
                result.Add(passive);
            }
            return result;
        }

        /// <summary>
        /// Turns a possibly-incomplete set of picks into exactly
        /// <see cref="AbilityController.SlotCount"/> distinct, non-null abilities.
        /// Honours the order supplied, drops picks that are null / duplicated / (unless
        /// <paramref name="ignoreLegality"/>) illegal for the class, then backfills from
        /// the filtered pool. No ability is ever forced in ahead of a pick the caller made.
        /// </summary>
        /// <returns>
        /// False when the pool could not fill every slot — the caller has an
        /// under-equipped loadout and should say so rather than spawn silently.
        /// </returns>
        public static bool ResolveSlots(AbilityRegistrySO registry, ChickenClass cls, bool ignoreLegality,
            IReadOnlyList<AbilityBaseSO> picks, AbilityBaseSO[] resolved)
        {
            int slotCount = AbilityController.SlotCount;
            if (resolved == null || resolved.Length < slotCount) return false;

            var pool = FilterActives(registry, cls, ignoreLegality);
            var chosen = new List<AbilityBaseSO>(slotCount);

            if (picks != null)
            {
                for (int i = 0; i < picks.Count && chosen.Count < slotCount; i++)
                {
                    var pick = picks[i];
                    if (pick == null || chosen.Contains(pick)) continue;
                    if (!ignoreLegality && !AbilityRegistrySO.IsAllowedFor(pick, cls)) continue;
                    chosen.Add(pick);
                }
            }

            for (int i = 0; i < pool.Count && chosen.Count < slotCount; i++)
            {
                if (!chosen.Contains(pool[i])) chosen.Add(pool[i]);
            }

            for (int i = 0; i < slotCount; i++)
                resolved[i] = i < chosen.Count ? chosen[i] : null;

            return chosen.Count >= slotCount;
        }

        /// <summary>
        /// The passive the lab should equip: the pick when it is usable, otherwise the
        /// class default. Null only when the registry has no passive for the class at all.
        /// </summary>
        public static PassiveAbilitySO ResolvePassive(AbilityRegistrySO registry, ChickenClass cls, bool ignoreLegality,
            PassiveAbilitySO pick)
        {
            if (registry == null) return pick;
            if (pick != null && (ignoreLegality || AbilityRegistrySO.IsAllowedFor(pick, cls))) return pick;
            return registry.GetDefaultPassiveForClass(cls);
        }
    }
}
