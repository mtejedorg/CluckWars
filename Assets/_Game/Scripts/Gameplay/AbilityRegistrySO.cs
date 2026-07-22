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

        /// <summary>Active abilities in the shared Common pool (legal for everyone).</summary>
        public IEnumerable<AbilityBaseSO> CommonAbilities =>
            ActiveAbilities.Where(a => a.SlotKind == AbilitySlotKind.Common);

        /// <summary>Active Character-pool abilities this class may equip.</summary>
        public IEnumerable<AbilityBaseSO> GetCharacterAbilitiesForClass(ChickenClass cls) =>
            ActiveAbilities.Where(a => a.SlotKind == AbilitySlotKind.Character && IsAllowedFor(a, cls));

        /// <summary>
        /// A legal default loadout for <paramref name="cls"/>: <b>1 Common + 2 Character</b>
        /// (ADR 0003 Decision 3). Used as the fallback whenever a selection is absent or
        /// illegal, so nothing can spawn with an off-class or malformed loadout.
        /// </summary>
        public void ComposeDefaultLoadout(ChickenClass cls,
            out AbilityBaseSO common, out AbilityBaseSO character0, out AbilityBaseSO character1)
        {
            common = CommonAbilities.FirstOrDefault();
            var chars = GetCharacterAbilitiesForClass(cls).ToList();
            character0 = chars.Count > 0 ? chars[0] : null;
            character1 = chars.Count > 1 ? chars[1] : null;
        }
    }
}
