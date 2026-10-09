using CluckWars.Localization;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CluckWars.Input
{
    /// <summary>Which control scheme the in-match HUD is laid out for (Phase 6, A10).</summary>
    public enum HudDeviceMode : byte
    {
        /// <summary>Joystick + tappable hexes in the thumb arc.</summary>
        Touch = 0,
        /// <summary>No joystick; the hexes are an information strip at the bottom centre, badged with the keys.</summary>
        KeyboardMouse = 1,
        /// <summary>As KeyboardMouse, badged with the pad buttons.</summary>
        Gamepad = 2,
    }

    /// <summary>
    /// Pure rules behind the desktop / controller HUD: which layout the last-used device wants, what each hex's badge
    /// says on it, and the one-line "hold to aim" hint (how often it shows and what it says). No Unity objects beyond
    /// the Input System's key / button enums, so EditMode tests cover it.
    /// </summary>
    public static class HudLayoutRules
    {
        /// <summary>
        /// The layout for the most recently used device. Touch wins whenever it is the device in hand (a touchscreen
        /// laptop gets its thumb layout back the moment a finger lands); a device nobody has used yet falls back to the
        /// platform's natural one: phones start on touch, everything else on keyboard / mouse.
        /// </summary>
        public static HudDeviceMode FromDevice(InputDeviceKind lastUsed, bool isMobilePlatform) => lastUsed switch
        {
            InputDeviceKind.Touch         => HudDeviceMode.Touch,
            InputDeviceKind.Gamepad       => HudDeviceMode.Gamepad,
            InputDeviceKind.KeyboardMouse => HudDeviceMode.KeyboardMouse,
            _                             => isMobilePlatform ? HudDeviceMode.Touch : HudDeviceMode.KeyboardMouse,
        };

        /// <summary>USS class on the HUD root for <paramref name="mode"/> (none for the touch layout).</summary>
        public static string RootClass(HudDeviceMode mode) => mode switch
        {
            HudDeviceMode.KeyboardMouse => "cw-hud--desktop",
            HudDeviceMode.Gamepad       => "cw-hud--pad",
            _                           => null,
        };

        /// <summary>The hexes take taps only in the touch layout; elsewhere they are information.</summary>
        public static bool HexesAreInteractive(HudDeviceMode mode) => mode == HudDeviceMode.Touch;

        /// <summary>Scale of a resting hex: the information strip is smaller than the thumb arc.</summary>
        public static float HexRestScale(HudDeviceMode mode) => mode == HudDeviceMode.Touch ? 1f : 0.75f;

        // ---- Badges ----------------------------------------------------------------------------------

        /// <summary>
        /// What the slot badge shows: the slot number on touch, the slot's first key on keyboard (Q / E / R / F), the
        /// slot's pad button on a controller (RB / RT / LB / LT). Derived from the REAL binding tables, so the badge
        /// can never advertise a key the provider does not read.
        /// </summary>
        public static string BadgeLabel(HudDeviceMode mode, int slot)
        {
            if (slot < 0 || slot >= 4) return string.Empty;
            switch (mode)
            {
                case HudDeviceMode.KeyboardMouse:
                    return KeyboardInputProvider.AbilityKeys[slot][0].ToString();
                case HudDeviceMode.Gamepad:
                    return PadLabel(GamepadInputProvider.AbilityButtons[slot]);
                default:
                    return (slot + 1).ToString();
            }
        }

        /// <summary>Shoulder / trigger button as printed on the pad.</summary>
        public static string PadLabel(GamepadButton button) => button switch
        {
            GamepadButton.RightShoulder => "RB",
            GamepadButton.RightTrigger  => "RT",
            GamepadButton.LeftShoulder  => "LB",
            GamepadButton.LeftTrigger   => "LT",
            _                           => button.ToString(),
        };

        // ---- Cooldown number -------------------------------------------------------------------------

        /// <summary>
        /// The seconds readout on a cooling hex: whole seconds rounded UP (so it never shows 0 while the move is still
        /// locked), and under one second a single decimal (0.9 ... 0.1) so the last beat is readable on the strip.
        /// </summary>
        public static string CooldownText(float remainingSeconds)
        {
            if (remainingSeconds <= 0f) return string.Empty;
            if (remainingSeconds >= 1f) return ((int)System.Math.Ceiling(remainingSeconds)).ToString();
            int tenths = (int)System.Math.Ceiling(remainingSeconds * 10f);
            return tenths >= 10 ? "1" : "0." + tenths;
        }
    }

    /// <summary>
    /// The one-line "Hold to aim" hint above the ability cluster (Phase 6, A1): shown for the first
    /// <see cref="MaxHolds"/> real holds, then never again.
    /// </summary>
    public static class HoldHintRules
    {
        public const int MaxHolds = 5;

        /// <summary>
        /// A hold just became a real hold (it outlived the tap / hold threshold). Returns whether the hint shows for it
        /// and advances the persisted counter when it does. Taps never reach this, so a tap-only player keeps the hint
        /// until they actually hold.
        /// </summary>
        public static bool OnHoldQualified(ref int holdsCounted)
        {
            if (holdsCounted >= MaxHolds) return false;
            holdsCounted++;
            return true;
        }

        /// <summary>Hint copy for the layout the player is using.</summary>
        public static string KeyFor(HudDeviceMode mode) => mode switch
        {
            HudDeviceMode.Gamepad       => UiKeys.HudHintHoldPad,
            HudDeviceMode.KeyboardMouse => UiKeys.HudHintHoldKeys,
            _                           => UiKeys.HudHintHoldTouch,
        };
    }
}
