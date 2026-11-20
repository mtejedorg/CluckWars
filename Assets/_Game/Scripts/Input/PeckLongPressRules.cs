namespace CluckWars.Input
{
    /// <summary>What one frame of a Peck slot's press means (<see cref="PeckLongPressRules.Step"/>).</summary>
    public enum PeckPressAction : byte
    {
        /// <summary>Nothing to send, nothing to flip.</summary>
        None = 0,
        /// <summary>A tap ended: send one manual Peck press.</summary>
        FirePeck = 1,
        /// <summary>The hold crossed the long-press threshold: flip Auto-Peck (once, while the player is still holding).</summary>
        ToggleAutoPeck = 2,
    }

    /// <summary>The state of one Peck press across frames. Default = idle.</summary>
    public struct PeckHoldState
    {
        public bool Active;
        public bool Toggled;
        public bool Cancelled;
        public float Seconds;
    }

    /// <summary>
    /// Pure rules of "long press on the Peck slot toggles Auto-Peck" (Maestro, 2026-11-20), the same on every device
    /// (touch hex, the slot's keys, the slot's pad buttons). Peck has no range preview, so it is exempt from "press
    /// previews, release casts": a tap pecks when it ends, a hold that reaches <see cref="PeckLongPressSeconds"/> flips the
    /// setting once, and the release after a flip pecks nothing. A cancel (Esc, right mouse, L3, the touch edge band)
    /// before the release kills the press: no peck, and no flip if it came before the threshold. Slots that do not hold a
    /// Peck are not touched by any of this (the caller never feeds them in). No Unity types, so every branch is EditMode-tested.
    /// </summary>
    public static class PeckLongPressRules
    {
        /// <summary>CODE-UI-08: the one home of the long-press threshold. A hold of exactly this long is a long press (>=).</summary>
        public const float PeckLongPressSeconds = 0.5f;

        /// <summary>
        /// One frame. <paramref name="down"/> is "the slot's input is down this frame" (held, or a press edge seen);
        /// <paramref name="cancel"/> is the one-shot cancel gesture. A slot that stopped holding a Peck mid-press (the loadout changed)
        /// drops the press with no action.
        /// </summary>
        public static PeckPressAction Step(ref PeckHoldState state, bool slotHoldsPeck, bool down, bool cancel, float deltaSeconds)
        {
            if (!slotHoldsPeck)
            {
                state = default;
                return PeckPressAction.None;
            }

            if (down)
            {
                if (!state.Active)
                {
                    state = new PeckHoldState { Active = true };
                    return PeckPressAction.None;
                }

                state.Seconds += deltaSeconds;
                if (cancel) state.Cancelled = true;
                if (!state.Cancelled && !state.Toggled && state.Seconds >= PeckLongPressSeconds)
                {
                    state.Toggled = true;
                    return PeckPressAction.ToggleAutoPeck;
                }
                return PeckPressAction.None;
            }

            if (!state.Active) return PeckPressAction.None;

            // The release frame: a cancel that lands on it (the touch edge band fires on lift) still counts.
            bool fire = !state.Toggled && !state.Cancelled && !cancel;
            state = default;
            return fire ? PeckPressAction.FirePeck : PeckPressAction.None;
        }

        /// <summary>The "AUTO" tag on a hex: only on a slot holding Peck, only while Auto-Peck is on.</summary>
        public static bool AutoPeckBadgeVisible(bool slotHoldsPeck, bool autoPeckOn) => slotHoldsPeck && autoPeckOn;

        /// <summary>
        /// Whether a hex is hidden. Only the third slot is ever hidden, and only when nothing is equipped there; whatever
        /// the kit puts in a slot, a Peck (the income button) is an equipped ability and so never hides.
        /// </summary>
        public static bool HexHidden(int slot, CluckWars.Abilities.AbilityBaseSO equipped) => slot == 2 && equipped == null;

        /// <summary>USS class of the "AUTO" tag the HUD builds inside every hex (CODE-UI-06: it has a rule in TouchControls.uss).</summary>
        public const string AutoTagClass = "cw-hex-autotag";
    }
}
