using CluckWars.Gameplay;
using CluckWars.Visuals;

namespace CluckWars.Input
{
    /// <summary>What the hunt does to a hex's centre label this frame (the same ownership contract as the egg and caster-stun digits).</summary>
    public enum HuntDigitStep : byte
    {
        /// <summary>Not ours, nothing to undo.</summary>
        Idle = 0,
        /// <summary>The hunt takes the label over: set the colour and display, force a text write.</summary>
        Acquire = 1,
        /// <summary>The hunt still owns the label: write the text when it changed.</summary>
        Hold = 2,
        /// <summary>The hunt gives the label back: restore the stylesheet colour and force the cooldown readout to repaint.</summary>
        HandBack = 3,
    }

    /// <summary>
    /// The Hunt hex while a Death Mark is on (spec 2026-11-17 H2a, H3a): pure rules for which slot carries the hunt, how bright
    /// the hex is, which glyph names the blocker and who owns the centre label. No Unity object; the HUD
    /// (<see cref="TouchControlsController"/>, touch arc and desktop / pad strip alike) feeds it <see cref="MarkFeedback.Current"/>.
    /// <c>MarkKillAbilitySO.IsUsable</c> is not touched: the look is an override keyed on the mark, like KILL.
    /// </summary>
    public static class HuntHexRules
    {
        /// <summary>USS class on the hex while it carries the hunt.</summary>
        public const string HuntClass = "cw-hex--hunt";

        public const string GlyphStarClass = "cw-hex-huntglyph--star";
        public const string GlyphLockClass = "cw-hex-huntglyph--lock";
        public const string GlyphEggClass  = "cw-hex-huntglyph--egg";
        public const string GlyphFarClass  = "cw-hex-huntglyph--far";

        /// <summary>This slot carries the hunt: the local player's own live mark, on the Death Mark slot.</summary>
        public static bool Applies(in MarkOutputs o, bool slotIsDeathMark) =>
            slotIsDeathMark && o.Role == MarkRole.Assassin && o.AssassinMarkActive;

        /// <summary>
        /// The hex tint's alpha, the table of <c>TouchControlsController.RefreshSlot</c>: Cooldown dims, Stunned wears the illegal-cast
        /// wash's own alpha, everything else rests at <see cref="FeedbackTuning.HexAlphaReady"/>. While hunting, "no target" (what
        /// <c>MarkKillAbilitySO.IsUsable</c> yields until the pounce is legal) is not a refusal and stays at full alpha, never the
        /// dimmer <see cref="FeedbackTuning.HexAlphaNoTarget"/> the joystick uses.
        /// </summary>
        public static float TintAlpha(AbilityRefusal refusal, bool hunting)
        {
            if (hunting && (refusal == AbilityRefusal.NoTarget || refusal == AbilityRefusal.None)) return FeedbackTuning.HexAlphaReady;
            switch (refusal)
            {
                case AbilityRefusal.Cooldown: return FeedbackTuning.HexAlphaCooldown;
                case AbilityRefusal.Stunned: return FeedbackTuning.IllegalCastTintColor.a;
                default: return FeedbackTuning.HexAlphaReady;
            }
        }

        /// <summary>The glyph class for what blocks the pounce, or null when nothing does. TooFar is the metres chip.</summary>
        public static string GlyphClass(HuntBlocker blocker)
        {
            switch (blocker)
            {
                case HuntBlocker.NotStunned: return GlyphStarClass;
                case HuntBlocker.Walled: return GlyphLockClass;
                case HuntBlocker.Shelled: return GlyphEggClass;
                case HuntBlocker.TooFar: return GlyphFarClass;
                default: return null;
            }
        }

        /// <summary>The label hand-over for this frame, from whether the hunt owns the label now and whether it should.</summary>
        public static HuntDigitStep DigitStep(bool owned, bool show)
        {
            if (show) return owned ? HuntDigitStep.Hold : HuntDigitStep.Acquire;
            return owned ? HuntDigitStep.HandBack : HuntDigitStep.Idle;
        }
    }
}
