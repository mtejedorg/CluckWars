using System;
using System.Collections.Generic;
using CluckWars.Gameplay;
using CluckWars.Progression;

namespace CluckWars.Installers
{
    /// <summary>
    /// The one implementation of <see cref="IAbilityCategoryIndex"/> (decision D4): walks the live
    /// <see cref="AbilityRegistrySO.All"/> and maps every ability's stable unlock key to its category
    /// key. Lives in <c>Installers/</c> — the composition root — because that is the one place already
    /// exempt from the gameplay contract-surface scan and already allowed to name concrete progression
    /// and gameplay types together.
    /// </summary>
    /// <remarks>
    /// Built once from whatever the registry holds at bind time, not re-scanned per query — the
    /// registry is static data (an asset), so this is a snapshot of it, not a live view. If the
    /// registry changes at runtime (it does not, in this build) a new index would need to be bound;
    /// nothing here claims otherwise.
    /// </remarks>
    public sealed class AbilityCategoryIndex : IAbilityCategoryIndex
    {
        private readonly Dictionary<string, string> _categoryByAbilityKey = new Dictionary<string, string>(StringComparer.Ordinal);

        public AbilityCategoryIndex(AbilityRegistrySO registry)
        {
            if (registry?.All == null) return;

            foreach (var ability in registry.All)
            {
                if (ability == null || string.IsNullOrEmpty(ability.UnlockKey)) continue;
                _categoryByAbilityKey[ability.UnlockKey] = UnlockKeyTable.AbilityCategoryKey(ability.Category);
            }
        }

        public bool TryGetCategory(string abilityKey, out string categoryKey)
        {
            categoryKey = null;
            return !string.IsNullOrEmpty(abilityKey) && _categoryByAbilityKey.TryGetValue(abilityKey, out categoryKey);
        }
    }
}
