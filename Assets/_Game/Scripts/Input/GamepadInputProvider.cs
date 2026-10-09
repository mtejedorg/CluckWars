using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CluckWars.Input
{
    /// <summary>
    /// Gamepad input (Phase 6 chunk 5, A8). Left stick moves, right stick aims, and the four ability slots sit on the
    /// shoulders and triggers (the face buttons fight the right stick). B cancels a hold, Start is back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Binding table.</b> <see cref="AbilityButtons"/> is the one slot-to-button table, as an enum table for the same
    /// reason as <see cref="KeyboardInputProvider.AbilityKeys"/>: a live <see cref="Gamepad"/> does not exist in an
    /// EditMode run, so an enum table is the difference between the mapping being pinned by tests and being
    /// unverifiable. Layout: RB, RT, LB, LT = slots 1, 2, 3, 4. The triggers are read as buttons through the Input
    /// System's press point (0.5 by default), so a half pull is a press.
    /// </para>
    /// <para>
    /// <b>Movement</b> uses <see cref="JoystickRules"/>' 0.12 dead zone and rescaled ramp, the same as the touch stick.
    /// <b>Aim</b> is the right stick through <see cref="StickAimFilter"/> (dead zone 0.25, outer 0.95, 150 ms hold),
    /// returned in the same camera-relative space as movement.
    /// </para>
    /// </remarks>
    public sealed class GamepadInputProvider : IInputProvider
    {
        /// <summary><c>AbilityButtons[slot]</c> fires that slot: RB, RT, LB, LT.</summary>
        public static readonly GamepadButton[] AbilityButtons =
        {
            GamepadButton.RightShoulder,
            GamepadButton.RightTrigger,
            GamepadButton.LeftShoulder,
            GamepadButton.LeftTrigger,
        };

        /// <summary>B cancels the hold in progress.</summary>
        public const GamepadButton CancelButton = GamepadButton.East;

        /// <summary>Start is UI back (the leave sheet, later in Part B).</summary>
        public const GamepadButton BackButton = GamepadButton.Start;

        private readonly StickAimFilter _aimFilter = new StickAimFilter();
        private float _lastActiveTime = float.NegativeInfinity;
        private int _polledFrame = -1;

        public InputDeviceKind Device => InputDeviceKind.Gamepad;

        public float LastActiveTime
        {
            get
            {
                Poll();
                return _lastActiveTime;
            }
        }

        public Vector2 GetMovement()
        {
            var gp = Gamepad.current;
            return gp == null ? Vector2.zero : JoystickRules.ApplyDeadZone(gp.leftStick.ReadValue());
        }

        public AimInput GetAim(float groundY)
        {
            var gp = Gamepad.current;
            if (gp == null) return AimInput.None;

            var aim = _aimFilter.Update(gp.rightStick.ReadValue(), Time.unscaledTime);
            return aim == Vector2.zero ? AimInput.None : AimInput.FromStick(aim);
        }

        public bool GetAbility1Pressed() => Pressed(0);
        public bool GetAbility2Pressed() => Pressed(1);
        public bool GetAbility3Pressed() => Pressed(2);
        public bool GetAbility4Pressed() => Pressed(3);

        public bool GetAbilityHeld(int slot)
        {
            var gp = Gamepad.current;
            return gp != null && slot >= 0 && slot < AbilityButtons.Length && gp[AbilityButtons[slot]].isPressed;
        }

        public bool GetAbilityCancelPressed()
        {
            var gp = Gamepad.current;
            return gp != null && gp[CancelButton].wasPressedThisFrame;
        }

        /// <summary>A pad cancels with a button, not a screen band.</summary>
        public bool IsAbilityCancelArmed() => false;

        public bool GetBackPressed()
        {
            var gp = Gamepad.current;
            return gp != null && gp[BackButton].wasPressedThisFrame;
        }

        private static bool Pressed(int slot)
        {
            var gp = Gamepad.current;
            return gp != null && slot >= 0 && slot < AbilityButtons.Length && gp[AbilityButtons[slot]].wasPressedThisFrame;
        }

        /// <summary>Once per frame: stamps activity when a stick leaves its dead zone or a mapped button is down.</summary>
        private void Poll()
        {
            int frame = Time.frameCount;
            if (frame == _polledFrame) return;
            _polledFrame = frame;

            var gp = Gamepad.current;
            if (gp == null) return;

            bool active = gp.leftStick.ReadValue().magnitude > JoystickRules.DeadZone ||
                          gp.rightStick.ReadValue().magnitude > StickAimFilter.DeadZone ||
                          gp[CancelButton].isPressed || gp[BackButton].isPressed;
            for (int slot = 0; slot < AbilityButtons.Length && !active; slot++)
                active = gp[AbilityButtons[slot]].isPressed;

            if (active) _lastActiveTime = Time.unscaledTime;
        }
    }
}
