using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Global ability pool. ADR 0003 restructures abilities into Common, Character, and Passive pools.
    /// Bind via <c>ProjectInstaller</c> and inject wherever ability selection is needed.
    /// </summary>
    /// <remarks>
    /// Populate in the Inspector: create an asset via
    /// <c>Cluck Wars / Ability Registry</c>, then drag all ability assets
    /// (from <c>Assets/_Game/Data/Abilities/</c>) into the <c>All</c> array.
    /// Assign the asset in <c>ProjectInstaller._abilityRegistry</c>.
    /// </remarks>
    [CreateAssetMenu(fileName = "AbilityRegistry", menuName = "Cluck Wars/Ability Registry", order = 2)]
    public sealed class AbilityRegistrySO : ScriptableObject
    {
        [Tooltip("Every ability available for selection. Order determines display order in the picker.")]
        public AbilityBaseSO[] All;

        /// <summary>Returns all non-passive active abilities in the registry.</summary>
        public IEnumerable<AbilityBaseSO> ActiveAbilities =>
            All != null ? All.Where(a => a != null && !(a is PassiveAbilitySO)) : Enumerable.Empty<AbilityBaseSO>();

        /// <summary>Returns all passive abilities in the registry.</summary>
        public IEnumerable<PassiveAbilitySO> Passives =>
            All != null ? All.OfType<PassiveAbilitySO>() : Enumerable.Empty<PassiveAbilitySO>();

        /// <summary>Returns passives allowed for a specific class.</summary>
        public IEnumerable<PassiveAbilitySO> GetPassivesForClass(ChickenClass cls)
        {
            var flag = cls switch
            {
                ChickenClass.Warrior  => ChickenClassFlags.Warrior,
                ChickenClass.Speedy   => ChickenClassFlags.Speedy,
                ChickenClass.Fatty    => ChickenClassFlags.Fatty,
                ChickenClass.Assassin => ChickenClassFlags.Assassin,
                _ => ChickenClassFlags.None,
            };
            return Passives.Where(p => (p.AllowedClasses & flag) != 0);
        }

        /// <summary>
        /// The class's <b>signature</b> passive — its default. Prefers the entry flagged
        /// <see cref="PassiveAbilitySO.IsSignature"/>; only falls back to registry order when
        /// no signature is authored (which is an authoring bug worth surfacing, not a
        /// legitimate state).
        /// </summary>
        /// <remarks>
        /// Previously this returned <c>list[0]</c> — i.e. whatever sat first in <see cref="All"/>.
        /// That is authoring order, so Warrior defaulted to Bracer and Speedy to Second Wind:
        /// both alternatives, and both currently inert. Verified live 2026-07-22.
        /// </remarks>
        public PassiveAbilitySO GetDefaultPassiveForClass(ChickenClass cls)
        {
            var list = GetPassivesForClass(cls).ToList();
            if (list.Count == 0) return null;
            return list.FirstOrDefault(p => p.IsSignature) ?? list[0];
        }

        // ---- Class-gated pools (ADR 0003 Decision 3) -------------------------

        /// <summary>Bit flag for a class, so callers don't re-derive the mapping.</summary>
        public static ChickenClassFlags FlagOf(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => ChickenClassFlags.Warrior,
            ChickenClass.Speedy   => ChickenClassFlags.Speedy,
            ChickenClass.Fatty    => ChickenClassFlags.Fatty,
            ChickenClass.Assassin => ChickenClassFlags.Assassin,
            _ => ChickenClassFlags.None,
        };

        /// <summary>
        /// Is this ability legal for <paramref name="cls"/>? The single authority on class
        /// gating — bots and the player picker must both route through it, or they drift
        /// (bots were spawning Warrior-only Flying Peck on Speedy/Fatty/Assassin).
        /// </summary>
        public static bool IsAllowedFor(AbilityBaseSO ability, ChickenClass cls) =>
            ability != null && (ability.AllowedClasses & FlagOf(cls)) != 0;

        /// <summary>
        /// True for an ability every class may pick (<c>AllowedClasses == All</c>). Derived —
        /// the retired Common/Character slot kind no longer exists; AllowedClasses decides.
        /// </summary>
        public static bool IsShared(AbilityBaseSO ability) =>
            ability != null && ability.AllowedClasses == ChickenClassFlags.All;

        /// <summary>
        /// True for an ability nobody can hand-pick (<c>AllowedClasses == None</c>): obtainable only as a
        /// subclass pre-equip (Peck and Mark/Kill today). Never appears in the picker, bot presets,
        /// sanitiser or backfill.
        /// </summary>
        public static bool IsPreEquipOnly(AbilityBaseSO ability) =>
            ability != null && ability.AllowedClasses == ChickenClassFlags.None;

        /// <summary>Active abilities every class may pick.</summary>
        public IEnumerable<AbilityBaseSO> SharedAbilities => ActiveAbilities.Where(IsShared);

        /// <summary>Active abilities this class may pick that are not shared with everyone.</summary>
        public IEnumerable<AbilityBaseSO> GetClassAbilitiesForClass(ChickenClass cls) =>
            ActiveAbilities.Where(a => !IsShared(a) && IsAllowedFor(a, cls));

        /// <summary>
        /// A legal default loadout for <paramref name="cls"/>: one shared ability plus two class
        /// abilities. Used as the fallback whenever a selection is absent or illegal, so nothing
        /// can spawn with an off-class or malformed loadout. Pre-equip-only abilities never appear.
        /// </summary>
        public void ComposeDefaultLoadout(ChickenClass cls,
            out AbilityBaseSO shared, out AbilityBaseSO class0, out AbilityBaseSO class1)
        {
            shared = SharedAbilities.FirstOrDefault();
            var chars = GetClassAbilitiesForClass(cls).ToList();
            class0 = chars.Count > 0 ? chars[0] : null;
            class1 = chars.Count > 1 ? chars[1] : null;
        }
    }
}
