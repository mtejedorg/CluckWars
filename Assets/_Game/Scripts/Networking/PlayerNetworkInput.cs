using Fusion;
using UnityEngine;

namespace CluckWars.Networking
{
    /// <summary>
    /// The Fusion input struct sampled once per simulation tick on the input authority
    /// and replicated to every peer. Keep this small — it goes over the wire each tick.
    /// </summary>
    public struct PlayerNetworkInput : INetworkInput
    {
        public Vector2 Movement;
        public NetworkButtons Buttons;
    }

    /// <summary>
    /// Bit indices used inside <see cref="PlayerNetworkInput.Buttons"/>.
    /// Cast to <c>int</c> when calling <c>NetworkButtons.IsSet</c> / <c>Set</c>.
    /// </summary>
    public enum InputButton
    {
        // Attack removed in v0.3 — all combat is ability-driven.
        Ability1 = 0,
        Ability2 = 1,
        Ability3 = 2, // Assassin (Combo passive) only.

        // v0.6 hold-to-aim (FEEDBACK.md §2, Stage 2). AbilityN above stays the
        // latched press edge (tap / sub-tick-tap fallback); the three bits below
        // are the live, level-triggered held state read fresh every tick — do NOT
        // renumber the block above, these are additive.
        AbilityHold1 = 3,
        AbilityHold2 = 4,
        AbilityHold3 = 5,

        /// <summary>Edge-triggered hold-cancel gesture (desktop Esc; touch drag-off,
        /// surfaced through <c>TouchControlsController.ConsumeAbilityCancelled</c>).
        /// One bit is enough — only one slot can be charging at a time.</summary>
        AbilityCancel = 6,
    }
}
