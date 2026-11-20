using CluckWars.Localization;
using CluckWars.Settings;
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

    /// <summary>How the four hexes of the desktop / pad information strip are arranged (<see cref="HudLayoutRules.StripArrangement"/>).</summary>
    public enum HudStripArrangement : byte
    {
        /// <summary>The touch arc (the strip does not exist).</summary>
        None = 0,
        /// <summary>One row, left to right Q W E R = slots 1, 2, 4, 3 (keyboard scheme ArrowsQwer).</summary>
        Row = 1,
        /// <summary>Inverted T like the arrow cluster and the pad's face diamond: slot 2 on top, slots 4, 1, 3 along the bottom.</summary>
        Tee = 2,
    }

    /// <summary>Which way an arrow-key badge points; the badge shows a drawn arrow because the HUD fonts have no arrow glyphs.</summary>
    public enum BadgeArrow : byte { None = 0, Up = 1, Right = 2, Down = 3, Left = 4 }

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
        /// What the slot badge prints: the slot number on touch, the slot's first key on keyboard (a letter for ArrowsQwer;
        /// <see cref="string.Empty"/> for an arrow key, which <see cref="ArrowFor"/> draws instead), the slot's face button on a
        /// controller (A / Y / B / X). Derived from the REAL binding tables, so the badge can never advertise a key the
        /// provider does not read.
        /// </summary>
        public static string BadgeLabel(HudDeviceMode mode, int slot, KeyboardScheme scheme)
        {
            if (slot < 0 || slot >= 4) return string.Empty;
            switch (mode)
            {
                case HudDeviceMode.KeyboardMouse:
                {
                    var key = KeyboardInputProvider.AbilityKeysFor(scheme)[slot][0];
                    return ArrowOf(key) != BadgeArrow.None ? string.Empty : key.ToString();
                }
                case HudDeviceMode.Gamepad:
                    return PadLabel(GamepadInputProvider.AbilityButtons[slot][0]);
                default:
                    return (slot + 1).ToString();
            }
        }

        /// <summary>The arrow the slot's badge draws (keyboard, arrow-key slot); <see cref="BadgeArrow.None"/> everywhere else.</summary>
        public static BadgeArrow ArrowFor(HudDeviceMode mode, int slot, KeyboardScheme scheme) =>
            mode == HudDeviceMode.KeyboardMouse && slot >= 0 && slot < 4
                ? ArrowOf(KeyboardInputProvider.AbilityKeysFor(scheme)[slot][0])
                : BadgeArrow.None;

        /// <summary>The arrow a key draws, keyed on the Key enum.</summary>
        public static BadgeArrow ArrowOf(Key key) => key switch
        {
            Key.UpArrow    => BadgeArrow.Up,
            Key.RightArrow => BadgeArrow.Right,
            Key.DownArrow  => BadgeArrow.Down,
            Key.LeftArrow  => BadgeArrow.Left,
            _              => BadgeArrow.None,
        };

        /// <summary>USS class of the badge's arrow element for <paramref name="arrow"/> (none for <see cref="BadgeArrow.None"/>).</summary>
        public static string ArrowClass(BadgeArrow arrow) => arrow switch
        {
            BadgeArrow.Up    => "cw-hex-badge-arrow--up",
            BadgeArrow.Right => "cw-hex-badge-arrow--right",
            BadgeArrow.Down  => "cw-hex-badge-arrow--down",
            BadgeArrow.Left  => "cw-hex-badge-arrow--left",
            _                => null,
        };

        // ---- Strip arrangement -----------------------------------------------------------------------

        /// <summary>
        /// How the strip reads like the keys: ArrowsQwer is one row (Q W E R); WasdArrows and the pad share the inverted T of the
        /// arrow cluster / face diamond. Touch has no strip.
        /// </summary>
        public static HudStripArrangement StripArrangement(HudDeviceMode mode, KeyboardScheme scheme) => mode switch
        {
            HudDeviceMode.KeyboardMouse => scheme == KeyboardScheme.ArrowsQwer ? HudStripArrangement.Row : HudStripArrangement.Tee,
            HudDeviceMode.Gamepad       => HudStripArrangement.Tee,
            _                           => HudStripArrangement.None,
        };

        /// <summary>USS modifier class on the HUD root for <paramref name="arrangement"/> (none for <see cref="HudStripArrangement.None"/>).</summary>
        public static string ArrangementClass(HudStripArrangement arrangement) => arrangement switch
        {
            HudStripArrangement.Row => "cw-hud--strip-row",
            HudStripArrangement.Tee => "cw-hud--strip-tee",
            _                       => null,
        };

        /// <summary>Pad button as printed on the pad.</summary>
        public static string PadLabel(GamepadButton button) => button switch
        {
            GamepadButton.South         => "A",
            GamepadButton.North         => "Y",
            GamepadButton.East          => "B",
            GamepadButton.West          => "X",
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

        /// <summary>
        /// Touch-hex seconds readout: whole seconds rounded up with an <c>s</c> suffix (<c>4s</c>), and empty (no digit)
        /// when nothing is left, so a held hex with nobody in reach never prints a bare 0. The suffix (I11) keeps the
        /// countdown from twinning the slot badge numeral (<c>4</c>, top-left) on the touch arc.
        /// </summary>
        public static string TouchCooldownText(float remainingSeconds)
        {
            if (remainingSeconds <= 0f) return string.Empty;
            return UnityEngine.Mathf.CeilToInt(remainingSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + "s";
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

        /// <summary>Phrase separator in the hint copy.</summary>
        public const string PhraseSeparator = " · ";

        /// <summary>
        /// Display text for the hint. Touch shows each phrase on its own line (no dots), so a narrow plate never wraps
        /// mid-phrase or leaves a dangling dot; desktop / pad keep the one-line dotted copy.
        /// </summary>
        public static string Format(HudDeviceMode mode, string copy) =>
            mode == HudDeviceMode.Gamepad || mode == HudDeviceMode.KeyboardMouse
                ? copy
                : copy.Replace(PhraseSeparator, "\n");

        /// <summary>Hint copy for the layout the player is using.</summary>
        public static string KeyFor(HudDeviceMode mode) => mode switch
        {
            HudDeviceMode.Gamepad       => UiKeys.HudHintHoldPad,
            HudDeviceMode.KeyboardMouse => UiKeys.HudHintHoldKeys,
            _                           => UiKeys.HudHintHoldTouch,
        };
    }
}
