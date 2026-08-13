using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// <see cref="IInputProvider"/> backed by the on-screen
    /// <see cref="TouchControlsController"/> (UI Toolkit touch controls, Stage 2b).
    /// Returns zero / false until the controls are bound — safe to bind app-wide
    /// before the Game scene is loaded.
    /// </summary>
    public sealed class TouchInputProvider : IInputProvider
    {
        public Vector2 GetMovement()
        {
            var hud = TouchControlsController.Instance;
            return hud != null ? hud.Movement : Vector2.zero;
        }

        public bool GetAbility1Pressed()
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.Ability1Pressed;
        }

        public bool GetAbility2Pressed()
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.Ability2Pressed;
        }

        public bool GetAbility3Pressed()
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.Ability3Pressed;
        }

        public bool GetAbility4Pressed()
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.Ability4Pressed;
        }

        public bool GetAbilityHeld(int slot)
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.IsAbilityHeld(slot);
        }

        /// <summary>
        /// One flag covers all four slots: at most one hex can be mid-hold at a
        /// time, so ORing (and thereby consuming) every slot's cancel latch here is
        /// equivalent to — and simpler than — threading a slot index through
        /// <see cref="IInputProvider.GetAbilityCancelPressed"/>.
        /// </summary>
        public bool GetAbilityCancelPressed()
        {
            var hud = TouchControlsController.Instance;
            if (hud == null) return false;

            bool any = false;
            any |= hud.ConsumeAbilityCancelled(0);
            any |= hud.ConsumeAbilityCancelled(1);
            any |= hud.ConsumeAbilityCancelled(2);
            any |= hud.ConsumeAbilityCancelled(3);
            return any;
        }
    }
}
