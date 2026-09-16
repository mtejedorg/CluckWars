namespace CluckWars.Progression
{
    /// <summary>
    /// The port to the live ability categorization (decision D4, progression slice 4). Ramp and goal
    /// evaluation need "which category is this ability's key in" (for the "land N control abilities"
    /// goal template), but <c>Progression/</c> may not import <c>CluckWars.Abilities</c> — so this
    /// interface is the boundary, and its one implementation (<c>Installers.AbilityCategoryIndex</c>,
    /// the composition root) is the only place that actually walks <c>AbilityRegistrySO.All</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not on <see cref="ProgressionBoundaryTests"/>'s gameplay contract surface: nothing
    /// outside <c>Progression/</c> and <c>Installers/</c> needs it. It is resolved against the live
    /// registry every time, never a scene-baked or cached mapping, so a roster or category change
    /// never goes stale — the same bug class as the bot-loadout staleness fix.
    /// </remarks>
    public interface IAbilityCategoryIndex
    {
        /// <summary>
        /// The category key (<see cref="UnlockKeyTable.AbilityCategoryKey"/> shape, e.g.
        /// <c>"category.control"</c>) for <paramref name="abilityKey"/>, or false when the key is
        /// unknown to the live registry. Never throws.
        /// </summary>
        bool TryGetCategory(string abilityKey, out string categoryKey);
    }
}
