using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Centralized palette for HUD, food piles, and other shared visual elements.
    /// Replaces inline Color literals scattered across <c>TouchControlsHud</c>,
    /// <c>CharacterSelectController</c>, and <c>FoodPileVisuals</c>. Bound
    /// app-wide in <c>ProjectInstaller</c>.
    /// </summary>
    /// <remarks>
    /// Per-class chicken tints stay on <c>ChickenClassRegistrySO</c> (where they're
    /// paired with the rest of the class definition). Per-ability accent colors
    /// stay on each <c>AbilityBaseSO</c> asset. ColorScheme owns the rest —
    /// generic HUD theming and world-element states.
    ///
    /// Sibling-SO to <c>PrefabRegistrySO</c> and <c>AudioRegistrySO</c>; see
    /// TDD §6.6 for the consolidation rationale.
    /// </remarks>
    [CreateAssetMenu(fileName = "ColorScheme", menuName = "Cluck Wars/Color Scheme", order = 7)]
    public sealed class ColorSchemeSO : ScriptableObject
    {
        [Header("Food piles (full → empty lerp)")]
        public Color FoodPileFull  = new Color(1f,    0.85f, 0.30f, 1f);   // ripe corn
        public Color FoodPileEmpty = new Color(0.40f, 0.30f, 0.15f, 1f);   // husk

        [Header("HUD: panel + buttons")]
        public Color PanelBackground = new Color(0.08f, 0.08f, 0.10f, 0.92f);
        public Color ButtonNormal    = new Color(0.18f, 0.18f, 0.22f, 1f);
        public Color ButtonHover     = new Color(0.28f, 0.28f, 0.34f, 1f);
        public Color ButtonActive    = new Color(0.95f, 0.65f, 0.20f, 1f);  // food-orange
        public Color StartButton     = new Color(0.30f, 0.65f, 0.30f, 1f);
        public Color TextOnButton    = new Color(0.95f, 0.95f, 0.95f, 1f);
        public Color TextOnActive    = new Color(0.10f, 0.08f, 0.05f, 1f);

        [Header("Touch HUD: joystick + cooldown")]
        public Color JoystickBase   = new Color(1f, 1f, 1f, 0.20f);
        public Color JoystickKnob   = new Color(1f, 1f, 1f, 0.55f);
        public Color AttackNormal   = new Color(0.95f, 0.30f, 0.25f, 0.55f);
        public Color AttackPressed  = new Color(1.00f, 0.50f, 0.25f, 0.95f);
        public Color AbilityNormal  = new Color(0.30f, 0.55f, 0.95f, 0.55f);
        public Color AbilityPressed = new Color(0.55f, 0.80f, 1.00f, 0.95f);
        public Color CooldownDim    = new Color(0f, 0f, 0f, 0.55f);
    }
}
