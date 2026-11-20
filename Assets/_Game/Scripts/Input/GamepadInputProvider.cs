using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CluckWars.Input
{
    /// <summary>
    /// Gamepad input (Phase 6 chunk 5, A8). Left stick moves (the right stick does nothing: no aim, as on touch), and the four ability slots sit on the
    /// shoulders and triggers AND on the face buttons (A, Y, B, X = slots 1-4). Clicking the left stick (L3) cancels a hold, Start opens the Leave sheet,
    /// and B backs out of menus, the Leave sheet and the podium (in a match B is only slot 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Binding table.</b> <see cref="AbilityButtons"/> is the one slot-to-buttons table (every slot has its shoulder or trigger and its face button), as an enum table for the same
    /// reason as <see cref="KeyboardInputProvider.AbilityKeys"/>: a live <see cref="Gamepad"/> does not exist in an
    /// EditMode run, so an enum table is the difference between the mapping being pinned by tests and being
    /// unverifiable. Layout: RB, RT, LB, LT = slots 1, 2, 3, 4, and A, Y, B, X = slots 1, 2, 3, 4 (the diamond mirrors the slot roles: A bottom, Y top, B right, X left). The triggers are read as buttons through the Input
    /// System's press point (0.5 by default), so a half pull is a press.
    /// </para>
    /// <para>
    /// <b>Movement</b> uses <see cref="JoystickRules"/>' 0.12 dead zone and rescaled ramp, the same as the touch stick.
    /// </para>
    /// </remarks>
    public sealed class GamepadInputProvider : IInputProvider
    {
        /// <summary><c>AbilityButtons[slot]</c> fires that slot. The FIRST entry is the one the HUD badge prints (the face button).</summary>
        public static readonly GamepadButton[][] AbilityButtons =
        {
            new[] { GamepadButton.South, GamepadButton.RightShoulder },
            new[] { GamepadButton.North, GamepadButton.RightTrigger },
            new[] { GamepadButton.East,  GamepadButton.LeftShoulder },
            new[] { GamepadButton.West,  GamepadButton.LeftTrigger },
        };

        /// <summary>Clicking the left stick (L3) cancels the hold in progress.</summary>
        public const GamepadButton CancelButton = GamepadButton.LeftStick;

        /// <summary>B backs out of menu pages, the Leave sheet and the podium. In a match it is slot 3 and nothing else.</summary>
        public const GamepadButton MenuBackButton = GamepadButton.East;

        /// <summary>Start opens the Leave sheet in a match.</summary>
        public const GamepadButton BackButton = GamepadButton.Start;

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

        public bool GetAbility1Pressed() => Pressed(0);
        public bool GetAbility2Pressed() => Pressed(1);
        public bool GetAbility3Pressed() => Pressed(2);
        public bool GetAbility4Pressed() => Pressed(3);

        public bool GetAbilityHeld(int slot)
        {
            var gp = Gamepad.current;
            if (gp == null || slot < 0 || slot >= AbilityButtons.Length) return false;
            foreach (var button in AbilityButtons[slot])
                if (gp[button].isPressed) return true;
            return false;
        }

        public bool GetAbilityCancelPressed()
        {
            var gp = Gamepad.current;
            return gp != null && gp[CancelButton].wasPressedThisFrame;
        }

        /// <summary>A pad cancels with a button, not a screen band.</summary>
        public bool IsAbilityCancelArmed() => false;

        /// <summary>Start or B: back out of a menu page, the Leave sheet or the podium. In a match only Start opens the
        /// sheet (<see cref="GetPausePressed"/>); B there fires slot 3 and <see cref="MatchBackRules"/> ignores it.</summary>
        public bool GetBackPressed()
        {
            var gp = Gamepad.current;
            return gp != null && (gp[BackButton].wasPressedThisFrame || gp[MenuBackButton].wasPressedThisFrame);
        }

        public bool GetPausePressed()
        {
            var gp = Gamepad.current;
            return gp != null && gp[BackButton].wasPressedThisFrame;
        }

        private static bool Pressed(int slot)
        {
            var gp = Gamepad.current;
            if (gp == null || slot < 0 || slot >= AbilityButtons.Length) return false;
            foreach (var button in AbilityButtons[slot])
                if (gp[button].wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Once per frame: stamps activity when the left stick leaves its dead zone or a mapped button is down.</summary>
        private void Poll()
        {
            int frame = Time.frameCount;
            if (frame == _polledFrame) return;
            _polledFrame = frame;

            var gp = Gamepad.current;
            if (gp == null) return;

            bool active = gp.leftStick.ReadValue().magnitude > JoystickRules.DeadZone ||
                          gp[CancelButton].isPressed || gp[BackButton].isPressed;
            for (int slot = 0; slot < AbilityButtons.Length && !active; slot++)
                active = GetAbilityHeld(slot);

            if (active) _lastActiveTime = Time.unscaledTime;
        }
    }
}
