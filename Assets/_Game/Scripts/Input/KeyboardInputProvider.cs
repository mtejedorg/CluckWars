using CluckWars.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CluckWars.Input
{
    /// <summary>
    /// Standalone-Windows input, in one of two schemes the player picks in Settings
    /// (<see cref="PlayerPreferences.KeyboardSchemeChoice"/>, live: read on every poll). Every ability slot answers to
    /// <b>two</b> keys: the scheme's key and the digit. There is no aim: the mouse only counts as device activity and
    /// its right button cancels a hold, so abilities cast along the facing exactly as on touch. Esc cancels a hold and is
    /// still menu back / the Leave sheet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The binding tables live in one place</b> (<see cref="AbilityKeysFor"/> / <see cref="MovementKeysFor"/>, one pair per
    /// scheme) and both the edge-triggered and level-triggered readers derive from them, so "fires" and "is held" cannot drift.
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Slot</term><description>WasdArrows (default) / ArrowsQwer</description></listheader>
    ///   <item><term>1</term><description>Down · 1 / Q · 1</description></item>
    ///   <item><term>2</term><description>Up · 2 / W · 2</description></item>
    ///   <item><term>3</term><description>Right · 3 / R · 3</description></item>
    ///   <item><term>4</term><description>Left · 4 / E · 4</description></item>
    /// </list>
    /// <para>
    /// WasdArrows: WASD moves with the left hand, the arrows are the abilities (the inverted-T cluster the HUD strip mirrors).
    /// ArrowsQwer (MOBA): the arrows move and Q W E R are abilities in slot order 1, 2, 4, 3 along the row (R, the rarely used
    /// flex slot, sits like an ultimate). The digits are the direct route for players who read the slot number off the HUD.
    /// </para>
    /// <para>
    /// <b>Arrow keys are also UI Toolkit's navigation keys.</b> That is safe here only
    /// because the menu and the match never own the screen at the same time: during a match
    /// no menu panel has focus, so nothing else is listening. If an in-match focusable panel
    /// is ever added (a pause menu, a scoreboard overlay), an arrow press would drive both it
    /// and an ability (WasdArrows) or the walk (ArrowsQwer) in the same frame, and this provider will need gating on menu focus.
    /// </para>
    /// <para>
    /// Phase 1 caveat, unchanged: <c>wasPressedThisFrame</c> is sampled at Unity Update rate
    /// while Fusion calls <c>OnInput</c> at the simulation tick rate (32 Hz). For
    /// edge-triggered abilities we will need to latch presses between OnInput calls.
    /// </para>
    /// </remarks>
    public sealed class KeyboardInputProvider : IInputProvider
    {
        private float _lastActiveTime = float.NegativeInfinity;
        private int _polledFrame = -1;

        public InputDeviceKind Device => InputDeviceKind.KeyboardMouse;

        public float LastActiveTime
        {
            get
            {
                Poll();
                return _lastActiveTime;
            }
        }

        /// <summary>Once per frame: stamps activity (a key, a cursor move or a mouse button) for device selection.</summary>
        private void Poll()
        {
            int frame = Time.frameCount;
            if (frame == _polledFrame) return;
            _polledFrame = frame;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.01f;
            bool keyDown = kb != null && DesktopInputRules.KeyDownIsActivity(
                kb.anyKey.isPressed, kb.escapeKey.isPressed, PressedKeyCount(kb), Application.isMobilePlatform);
            bool mouseButton = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
            if (mouseMoved || keyDown || mouseButton) _lastActiveTime = Time.unscaledTime;
        }

        /// <summary>Keys down, counted only when Escape is one of them on a phone (the one case the rule needs it).</summary>
        private static int PressedKeyCount(Keyboard kb)
        {
            if (!Application.isMobilePlatform || !kb.escapeKey.isPressed) return 0;
            int n = 0;
            var keys = kb.allKeys;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].isPressed) n++;
            return n;
        }

        public Vector2 GetMovement()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = 0f, y = 0f;
            var m = MovementKeysFor(PlayerPreferences.KeyboardSchemeChoice);   // up, down, left, right
            if (kb[m[0]].isPressed) y += 1f;
            if (kb[m[1]].isPressed) y -= 1f;
            if (kb[m[2]].isPressed) x -= 1f;
            if (kb[m[3]].isPressed) x += 1f;
            return new Vector2(x, y);
        }

        public bool GetAbility1Pressed() => Pressed(0);
        public bool GetAbility2Pressed() => Pressed(1);
        public bool GetAbility3Pressed() => Pressed(2);
        public bool GetAbility4Pressed() => Pressed(3);

        public bool GetAbilityHeld(int slot) => Held(slot);

        /// <summary>Esc or the right mouse button. The right button is read here (devices are only read in
        /// providers); the hold state machine ignores a cancel when nothing is being held, so a stray right
        /// click in idle does nothing.</summary>
        public bool GetAbilityCancelPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
        }

        /// <summary>Keyboard / mouse cancel with a button, not a screen band.</summary>
        public bool IsAbilityCancelArmed() => false;

        /// <summary>Esc on desktop; on Android the system back button arrives as the Escape key.</summary>
        public bool GetBackPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        /// <summary>Esc opens the Leave sheet.</summary>
        public bool GetPausePressed() => GetBackPressed();

        /// <summary>
        /// The slot-to-key table of a scheme: <c>AbilityKeysFor(scheme)[slot]</c> is every key that fires that slot. Entry [0] is
        /// the key the HUD badge prints.
        /// </summary>
        /// <remarks>
        /// Deliberately <see cref="Key"/> enums rather than resolved control references.
        /// A control table can only be built from a live <see cref="Keyboard"/>,
        /// which does not exist in an EditMode test run, so an enum table is the difference
        /// between this mapping being pinned by tests and being unverifiable. See
        /// <c>ServicesAndInputTests</c>, which asserts for each scheme that the table is total, has no duplicate
        /// key inside a slot, no key shared between slots, and no collision with that scheme's movement keys.
        /// Allocated once as <c>static readonly</c>; the per-tick readers only index it.
        /// </remarks>
        public static Key[][] AbilityKeysFor(KeyboardScheme scheme) =>
            scheme == KeyboardScheme.ArrowsQwer ? ArrowsQwerAbilityKeys : WasdArrowsAbilityKeys;

        /// <summary>The scheme's movement keys in the order up, down, left, right (exposed so tests can prove no ability key collides).</summary>
        public static Key[] MovementKeysFor(KeyboardScheme scheme) =>
            scheme == KeyboardScheme.ArrowsQwer ? ArrowsQwerMovementKeys : WasdArrowsMovementKeys;

        private static readonly Key[][] WasdArrowsAbilityKeys =
        {
            new[] { Key.DownArrow,  Key.Digit1 },
            new[] { Key.UpArrow,    Key.Digit2 },
            new[] { Key.RightArrow, Key.Digit3 },
            new[] { Key.LeftArrow,  Key.Digit4 },
        };

        private static readonly Key[][] ArrowsQwerAbilityKeys =
        {
            new[] { Key.Q, Key.Digit1 },
            new[] { Key.W, Key.Digit2 },
            new[] { Key.R, Key.Digit3 },
            new[] { Key.E, Key.Digit4 },
        };

        private static readonly Key[] WasdArrowsMovementKeys = { Key.W, Key.S, Key.A, Key.D };
        private static readonly Key[] ArrowsQwerMovementKeys = { Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow };

        /// <summary>Edge-triggered: did any of this slot's keys go down this frame?</summary>
        private static bool Pressed(int slot)
        {
            var kb = Keyboard.current;
            var table = AbilityKeysFor(PlayerPreferences.KeyboardSchemeChoice);
            if (kb == null || slot < 0 || slot >= table.Length) return false;

            var keys = table[slot];
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].wasPressedThisFrame) return true;

            return false;
        }

        /// <summary>Level-triggered: is any of this slot's keys down right now?</summary>
        private static bool Held(int slot)
        {
            var kb = Keyboard.current;
            var table = AbilityKeysFor(PlayerPreferences.KeyboardSchemeChoice);
            if (kb == null || slot < 0 || slot >= table.Length) return false;

            var keys = table[slot];
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].isPressed) return true;

            return false;
        }
    }
}
