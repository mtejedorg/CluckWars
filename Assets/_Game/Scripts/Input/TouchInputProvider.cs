using UnityEngine;
using UnityEngine.InputSystem;

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
        private float _lastActiveTime = float.NegativeInfinity;
        private int _polledFrame = -1;

        public InputDeviceKind Device => InputDeviceKind.Touch;

        /// <summary>A finger on the stick or on a hex. Reads only level-triggered state, so it never consumes a press.</summary>
        public float LastActiveTime
        {
            get
            {
                int frame = Time.frameCount;
                if (frame != _polledFrame)
                {
                    _polledFrame = frame;
                    var hud = TouchControlsController.Instance;
                    bool active = false;
                    if (hud != null)
                    {
                        active = hud.Movement.sqrMagnitude > 0f;
                        for (int slot = 0; slot < 4 && !active; slot++) active = hud.IsAbilityHeld(slot);
                    }
                    // Any finger on the screen counts, not just one on a control: on the desktop / pad layout the
                    // joystick is hidden and the hexes take no pointer, so this is how a touchscreen laptop gets the
                    // thumb layout back.
                    var screen = Touchscreen.current;
                    if (!active && screen != null) active = screen.primaryTouch.press.isPressed;
                    if (active) _lastActiveTime = Time.unscaledTime;
                }
                return _lastActiveTime;
            }
        }

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

        public bool IsAbilityCancelArmed()
        {
            var hud = TouchControlsController.Instance;
            return hud != null && hud.IsCancelArmed;
        }

        /// <summary>The on-screen controls have no back button (Android back comes through the
        /// keyboard provider as Escape).</summary>
        public bool GetBackPressed() => false;
    }
}
