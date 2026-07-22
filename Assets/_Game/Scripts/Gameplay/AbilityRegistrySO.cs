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

        /// <summary>Returns default/signature passive for a class.</summary>
        public PassiveAbilitySO GetDefaultPassiveForClass(ChickenClass cls)
        {
            var list = GetPassivesForClass(cls).ToList();
            if (list.Count > 0) return list[0];
            return null;
        }
    }
}
