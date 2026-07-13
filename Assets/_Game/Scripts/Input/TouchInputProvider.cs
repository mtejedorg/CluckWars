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
    }
}
