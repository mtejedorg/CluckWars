using UnityEngine;
using UnityEngine.InputSystem;

namespace CluckWars.Input
{
    /// <summary>
    /// Standalone-Windows input. WASD = movement; every ability slot answers to
    /// <b>three</b> keys — a letter, a digit and an arrow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The binding table lives in one place</b> (<see cref="BindingsFor"/>) and both the
    /// edge-triggered and level-triggered readers derive from it. Previously each slot was
    /// spelled out twice — once in <c>GetAbilityNPressed</c> and again in
    /// <c>GetAbilityHeld</c> — which is two lists to keep in step; at three keys per slot
    /// that would have been 24 hand-maintained references and a near-certain drift between
    /// "fires" and "is held".
    /// </para>
    /// <para>
    /// <b>Layout (Maestro, 2026-08-15).</b> Movement is the left hand on WASD, so abilities
    /// get the right hand on the arrows, with the digit row as the direct route for players
    /// who read the slot number off the HUD:
    /// </para>
    /// <list type="table">
    ///   <listheader><term>Slot</term><description>Keys</description></listheader>
    ///   <item><term>1</term><description>Q · 1 · Up</description></item>
    ///   <item><term>2</term><description>E · 2 · Right</description></item>
    ///   <item><term>3</term><description>R · 3 · Down</description></item>
    ///   <item><term>4</term><description>F · 4 · Left</description></item>
    /// </list>
    /// <para>
    /// The digits are the obvious half: the HUD already prints 1–4 on the ability hexes, so
    /// pressing the number you can see is the mapping players will guess first. The arrows
    /// run <b>clockwise from Up</b> — the same order as the digits, laid onto the arrow
    /// diamond, which is also how a gamepad's face buttons sit. Q/E/R/F are kept so nobody's
    /// muscle memory breaks.
    /// </para>
    /// <para>
    /// <b>Arrow keys are also UI Toolkit's navigation keys.</b> That is safe here only
    /// because the menu and the match never own the screen at the same time — during a match
    /// no menu panel has focus, so nothing else is listening. If an in-match focusable panel
    /// is ever added (a pause menu, a scoreboard overlay), an arrow press would drive both it
    /// and an ability in the same frame, and this provider will need gating on menu focus.
    /// </para>
    /// <para>
    /// Phase 1 caveat, unchanged: <c>wasPressedThisFrame</c> is sampled at Unity Update rate
    /// while Fusion calls <c>OnInput</c> at the simulation tick rate (32 Hz). For
    /// edge-triggered abilities we will need to latch presses between OnInput calls.
    /// </para>
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

        public bool GetAbility1Pressed() => Pressed(0);
        public bool GetAbility2Pressed() => Pressed(1);
        public bool GetAbility3Pressed() => Pressed(2);
        public bool GetAbility4Pressed() => Pressed(3);

        public bool GetAbilityHeld(int slot) => Held(slot);

        public bool GetAbilityCancelPressed()
        {
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
        }

        /// <summary>
        /// The one and only slot-to-key table: <c>AbilityKeys[slot]</c> is every key that
        /// fires that slot.
        /// </summary>
        /// <remarks>
        /// Deliberately <see cref="Key"/> enums rather than resolved control references.
        /// A control table can only be built from a live <see cref="Keyboard"/>,
        /// which does not exist in an EditMode test run — so an enum table is the difference
        /// between this mapping being pinned by tests and being unverifiable. See
        /// <c>ServicesAndInputTests</c>, which asserts the table is total, has no duplicate
        /// key inside a slot, no key shared between slots, and no collision with WASD.
        ///
        /// Allocated once as <c>static readonly</c>; the per-tick readers only index it.
        /// </remarks>
        public static readonly Key[][] AbilityKeys =
        {
            new[] { Key.Q, Key.Digit1, Key.UpArrow },
            new[] { Key.E, Key.Digit2, Key.RightArrow },
            new[] { Key.R, Key.Digit3, Key.DownArrow },
            new[] { Key.F, Key.Digit4, Key.LeftArrow },
        };

        /// <summary>The movement keys, exposed so tests can prove no ability key collides.</summary>
        public static readonly Key[] MovementKeys = { Key.W, Key.A, Key.S, Key.D };

        /// <summary>Edge-triggered: did any of this slot's keys go down this frame?</summary>
        private static bool Pressed(int slot)
        {
            var kb = Keyboard.current;
            if (kb == null || slot < 0 || slot >= AbilityKeys.Length) return false;

            var keys = AbilityKeys[slot];
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].wasPressedThisFrame) return true;

            return false;
        }

        /// <summary>Level-triggered: is any of this slot's keys down right now?</summary>
        private static bool Held(int slot)
        {
            var kb = Keyboard.current;
            if (kb == null || slot < 0 || slot >= AbilityKeys.Length) return false;

            var keys = AbilityKeys[slot];
            for (int i = 0; i < keys.Length; i++)
                if (kb[keys[i]].isPressed) return true;

            return false;
        }
    }
}
