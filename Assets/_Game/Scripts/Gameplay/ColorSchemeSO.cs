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

        // Phase 10 redesign: warm Clash Royale / Supercell palette (cluckwars-tokens-v2).
        // If you have a pre-existing ColorScheme.asset, use "Reset" in the Inspector
        // to apply these new defaults.  Key consumers: CharacterSelectController, TouchControlsHud.

        [Header("HUD: panel + buttons")]
        public Color PanelBackground = new Color(0.23f, 0.13f, 0.06f, 0.96f); // #3a2210 warm dark wood
        public Color ButtonNormal    = new Color(0.31f, 0.18f, 0.08f, 1.00f); // #4f2e14 medium brown
        public Color ButtonHover     = new Color(0.42f, 0.25f, 0.12f, 1.00f); // #6b401f lighter hover
        public Color ButtonActive    = new Color(0.83f, 0.63f, 0.13f, 1.00f); // #d4a020 gold accent
        public Color StartButton     = new Color(0.20f, 0.64f, 0.20f, 1.00f); // #33a332 green CTA
        public Color TextOnButton    = new Color(1.00f, 0.96f, 0.88f, 1.00f); // #fef5e0 warm white
        public Color TextOnActive    = new Color(0.10f, 0.08f, 0.05f, 1.00f); // dark text on gold

        [Header("Touch HUD: joystick + cooldown")]
        public Color JoystickBase   = new Color(1f,    1f,    1f,    0.22f);
        public Color JoystickKnob   = new Color(1f,    1f,    1f,    0.55f);
        public Color AttackNormal   = new Color(0.91f, 0.28f, 0.16f, 0.60f); // warm red-orange
        public Color AttackPressed  = new Color(0.95f, 0.46f, 0.20f, 0.95f);
        public Color AbilityNormal  = new Color(0.10f, 0.50f, 0.77f, 0.55f); // P2-blue base
        public Color AbilityPressed = new Color(0.30f, 0.70f, 0.95f, 0.95f);
        public Color CooldownDim    = new Color(0f,    0f,    0f,    0.55f);
    }
}
