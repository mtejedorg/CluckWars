using CluckWars.Gameplay;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Pure decisions behind the Phase 6 "press ALWAYS previews" model, kept free of Unity and Fusion
    /// types so every branch is EditMode-testable. Three small rules:
    /// <list type="bullet">
    ///   <item>which slot the LOCAL player's held button previews (<see cref="SelectLocalPreviewSlot"/>);</item>
    ///   <item>which refusals wash the preview to the illegal tint (<see cref="IsRealRefusal"/>);</item>
    ///   <item>when the HUD's in-range pip shows (<see cref="InRangePipVisible"/>).</item>
    /// </list>
    /// </summary>
    public static class AbilityPreviewRules
    {
        public const int NoSlot = -1;

        /// <summary>
        /// The slot whose preview the local player's held buttons should show right now, or
        /// <see cref="NoSlot"/>. Mirrors <c>AbilityHoldStateMachine</c>'s "lowest held slot wins"
        /// exactly: the lowest held slot claims the gesture whether or not it can fire, so if that
        /// slot is unavailable (empty, or cooling) there is NO preview — previewing a higher held
        /// slot would show an aim the state machine will never fire.
        /// </summary>
        /// <param name="held">One level-triggered hold bit per slot (the local input provider).</param>
        /// <param name="available">Same length: the slot has an ability and is off cooldown.</param>
        public static int SelectLocalPreviewSlot(bool[] held, bool[] available)
        {
            if (held == null || available == null) return NoSlot;

            int n = held.Length < available.Length ? held.Length : available.Length;
            for (int slot = 0; slot < n; slot++)
            {
                if (!held[slot]) continue;
                return available[slot] ? slot : NoSlot;
            }
            return NoSlot;
        }

        /// <summary>
        /// Does this refusal wash the live preview to <c>FeedbackTuning.IllegalCastTintColor</c>?
        /// Only REAL refusals do: stunned, cooling down, unavailable.
        /// <see cref="AbilityRefusal.NoTarget"/> deliberately does not — pressing with nobody in the
        /// shape is a legal aim (you are lining the move up), drawn as the dashed no-target preview.
        /// </summary>
        /// <remarks>
        /// <see cref="AbilityRefusal.OtherAbilityActive"/> stays classified so the exhaustiveness test keeps
        /// every enum member decided, but it is retired and unreachable: abilities run concurrently as of
        /// Phase 6 chunk 3, so nothing produces it any more.
        /// </remarks>
        public static bool IsRealRefusal(AbilityRefusal refusal) =>
            refusal == AbilityRefusal.Stunned ||
            refusal == AbilityRefusal.Cooldown ||
            refusal == AbilityRefusal.SlotUnavailable ||
            refusal == AbilityRefusal.OtherAbilityActive;

        /// <summary>Is the preview in its dashed "nothing in the shape yet" state?</summary>
        public static bool IsNoTargetPreview(AbilityRefusal refusal) => refusal == AbilityRefusal.NoTarget;

        /// <summary>
        /// The HUD's in-range pip: lit while a rival is inside the slot's shape (the same "hot"
        /// verdict the ground guide brightens on) and the slot is not cooling down. Presence or
        /// absence is the cue, not colour.
        /// </summary>
        public static bool InRangePipVisible(bool hot, bool onCooldown) => hot && !onCooldown;
    }
}
