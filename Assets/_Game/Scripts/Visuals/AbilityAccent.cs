using CluckWars.Abilities;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// An ability's accent colour, resolved by presentation. The ability asset only names its accent
    /// (<see cref="AbilityBaseSO.AccentKey"/>, or its own <see cref="AbilityBaseSO.CustomAccentColor"/>); the
    /// colour comes from the live <see cref="FeedbackPalette"/>. This was the property
    /// <c>AbilityBaseSO.AccentColor</c> until audit batch 4: abilities do not depend on presentation.
    /// </summary>
    public static class AbilityAccent
    {
        /// <summary>Accent colour for VFX / range guides / telegraphs: the palette accent for the ability's
        /// <see cref="AbilityBaseSO.AccentKey"/>, or its <see cref="AbilityBaseSO.CustomAccentColor"/> when the key is Custom.</summary>
        public static Color AccentColor(this AbilityBaseSO ability) =>
            ability.AccentKey == AccentKey.Custom ? ability.CustomAccentColor : FeedbackPalette.Current.Accent(ability.AccentKey);
    }
}
