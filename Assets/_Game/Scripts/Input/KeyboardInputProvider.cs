using UnityEngine;
using UnityEngine.InputSystem;

namespace CluckWars.Input
{
    /// <summary>
    /// Standalone-Windows input. WASD = movement, Q = Ability1, E = Ability2, R = Ability3.
    /// Uses Unity Input System (already shipped in Packages/manifest.json).
    /// </summary>
    /// <remarks>
    /// v0.3: LMB attack removed. R-key added for Ability3 (Assassin slot only).
    ///
    /// Phase 1 caveat: <c>wasPressedThisFrame</c> is sampled at Unity Update rate,
    /// while Fusion calls <c>OnInput</c> at the simulation tick rate (32 Hz). For
    /// edge-triggered abilities we will need to latch presses between OnInput calls.
    /// Not a problem until Phase 6 — flagged here for future work.
    /// </remarks>
    public sealed class KeyboardInputProvider : IInputProvider
    {
        public Vector2 GetMovement()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = 0f, y = 0f;
            if (kb.wKey.isPressed) y += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            return new Vector2(x, y);
        }

        public bool GetAbility1Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.qKey.wasPressedThisFrame;
        }

        public bool GetAbility2Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.eKey.wasPressedThisFrame;
        }

        public bool GetAbility3Pressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame;
        }

        public bool GetAbilityHeld(int slot)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return slot switch
            {
                0 => kb.qKey.isPressed,
                1 => kb.eKey.isPressed,
                2 => kb.rKey.isPressed,
                _ => false,
            };
        }

        public bool GetAbilityCancelPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }
    }
}
