using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Global ability pool. v0.3 removes class-based ability restrictions —
    /// every class draws from the same pool. Bind via <c>ProjectInstaller</c>
    /// and inject wherever ability selection is needed.
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
    }
}
