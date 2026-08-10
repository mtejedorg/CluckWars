using CluckWars.Gameplay;

namespace CluckWars.UI
{
    /// <summary>
    /// What ART §6.6's <b>layer 9</b> is showing on an ability hex right now. Stage 5
    /// generalises that layer from "the cooldown seconds" to "the state indicator": the
    /// refusal reasons are made mutually exclusive by
    /// <see cref="AbilityRefusalRules.Evaluate"/>, so exactly one of these can apply and
    /// the centre of the hex never has to arbitrate between two marks. This is why the
    /// four distinguishable refusals need no tenth layer.
    /// </summary>
    public enum HexCenterMark : byte
    {
        /// <summary>Nothing centred (ready, or the whole hex is hidden).</summary>
        None = 0,
        /// <summary>Seconds-remaining integer — the shipped cooldown behaviour.</summary>
        CooldownSeconds = 1,
        /// <summary>The ⃠ "nothing will happen" mark (§6 case 25).</summary>
        NoTarget = 2,
        /// <summary>The ✕ "you cannot cast" cross (§6 case 26).</summary>
        StunnedCross = 3,
    }

    /// <summary>One row of the §5.2 HUD status strip.</summary>
    public enum HudStatusKind : byte { None = 0, Stun = 1, Root = 2, Slow = 3 }

    /// <summary>
    /// A single active control status on the local player, in severity order.
    /// <see cref="Value"/> is seconds-remaining for <see cref="HudStatusKind.Stun"/> and
    /// <see cref="HudStatusKind.Root"/>, and the movement <i>multiplier</i> for
    /// <see cref="HudStatusKind.Slow"/> — see
    /// <see cref="HudFeedbackStyle.CollectStatusRows"/> for why those are different kinds
    /// of number.
    /// </summary>
    public readonly struct HudStatusRow
    {
        public readonly HudStatusKind Kind;
        public readonly float Value;

        public HudStatusRow(HudStatusKind kind, float value)
        {
            Kind = kind;
            Value = value;
        }
    }

    /// <summary>
    /// Pure mapping layer between gameplay state and the touch HUD's USS vocabulary
    /// (FEEDBACK.md §5.2 + §6, cases 17–19 and 24–29). Deliberately holds no Unity types
    /// and touches no <c>NetworkBehaviour</c>, so every branch here is EditMode-testable
    /// without a <c>NetworkRunner</c> — <c>TouchControlsController</c> is then reduced to
    /// "ask this class what to show, then write the style".
    /// </summary>
    /// <remarks>
    /// The USS class names below are the contract with
    /// <c>Assets/UI/Styles/TouchControls.uss</c>; that sheet owns everything that is pure
    /// styling (geometry, icon dimming, which shape is visible), while
    /// <c>TouchControlsController</c> owns everything that is a tuned *value* and
    /// therefore has to come from <c>FeedbackTuning</c> (alphas, semantic colours,
    /// durations). Nothing is drawn procedurally and no colour is hard-coded in USS for a
    /// state that <c>FeedbackTuning</c> already names.
    /// </remarks>
    public static class HudFeedbackStyle
    {
        // ---- Ability-hex refusal classes (ART §6.6 layers 2–4 + 7) --------------

        public const string HexCooldownClass    = "cw-hex--cooldown";
        public const string HexNoTargetClass    = "cw-hex--no-target";
        public const string HexStunnedClass     = "cw-hex--stunned";
        public const string HexOtherActiveClass = "cw-hex--other-active";

        // ---- Joystick control-consequence classes (§5.2's table) ----------------

        public const string JoyStunnedClass = "cw-joy--stunned";
        public const string JoyRootedClass  = "cw-joy--rooted";
        public const string JoySlowedClass  = "cw-joy--slowed";

        // ---- Status glyphs ------------------------------------------------------

        /// <summary>
        /// The three control-status glyphs, shared verbatim with the world-space badge
        /// stack — <c>ChickenStatusBadges</c> aliases these exact consts, so the HUD strip
        /// and the nameplate badges cannot drift into speaking two languages for the same
        /// state (FEEDBACK.md §5.1/§5.2 are explicitly one vocabulary).
        ///
        /// Font: these are NOT in <c>LilitaOne-Regular.ttf</c> (225 codepoints, checked
        /// against its cmap — it has no glyph above U+02DC except a handful of punctuation)
        /// and TMP Settings ships an empty <c>m_fallbackFontAssets</c>, so any element
        /// showing one of these must select the project's tintable glyph font,
        /// <c>Assets/_Game/Resources/Fonts/NotoEmoji-Regular.ttf</c> (the same font
        /// <c>UiGfx.EmojiFont</c> already uses), via the <c>cw-glyph-font</c> USS class.
        /// All three are confirmed present in that font's cmap. Noto Emoji is monochrome,
        /// so each row still tints to its canonical <c>FeedbackTuning</c> colour.
        /// </summary>
        public const string StunGlyph = "⚡";        // HIGH VOLTAGE SIGN
        public const string RootGlyph = "⛓";        // CHAINS
        public const string SlowGlyph = "\U0001F40C";    // SNAIL

        /// <summary>
        /// Short row names for the status strip. Kept to four characters so the glyph, the
        /// name and the quantity all fit one 26px-floor row without wrapping at the mobile
        /// reference — §1.4, nothing mid-fight is a sentence.
        /// </summary>
        public const string StunName = "STUN";
        public const string RootName = "ROOT";
        public const string SlowName = "SLOW";

        // ---- Layer-9 refusal marks: drawn shapes vs. real glyphs ----------------

        /// <summary>
        /// Whether the layer-9 ⃠ / ✕ marks are drawn as USS shapes (a bordered circle plus
        /// a rotated bar; two rotated bars) instead of typed as characters.
        ///
        /// <b>Verified true, do not flip blindly.</b> U+20E0 (COMBINING ENCLOSING CIRCLE
        /// BACKSLASH) and U+2715 (MULTIPLICATION X) are both absent from
        /// <c>LilitaOne-Regular.ttf</c> <i>and</i> from <c>NotoEmoji-Regular.ttf</c>, and
        /// TMP Settings' global fallback list is empty — both would have drawn tofu on
        /// every platform, not just Android. This is the same tofu risk
        /// <c>ChickenStatusBadges</c> documents on its own glyph consts, resolved the same
        /// way: one named switch, so the decision is reversible in one line if a glyph font
        /// that carries them is ever added.
        ///
        /// Flip to <c>false</c> and the controller types
        /// <see cref="NoTargetGlyph"/>/<see cref="StunnedCrossGlyph"/> into the existing
        /// layer-9 label instead; nothing else changes. Kept <c>static readonly</c> rather
        /// than <c>const</c> so both branches stay live code and neither rots.
        ///
        /// Half-measure on record, deliberately not taken: NotoEmoji <i>does</i> carry
        /// U+2716 ✖ (HEAVY MULTIPLICATION X), so the stun cross alone could be a real
        /// glyph. It is drawn anyway, because the no-target mark has no glyph on any font
        /// the project ships — and one drawn mark plus one typed mark would put the two
        /// refusal states on different rendering paths, with different optical weights and
        /// different metrics, for no gain. Both drawn keeps them a matched pair.
        /// </summary>
        public static readonly bool UseDrawnRefusalMarks = true;

        /// <summary>Character form of the "no valid target" mark, used only when
        /// <see cref="UseDrawnRefusalMarks"/> is false. See that field first.</summary>
        public const string NoTargetGlyph = "⃠";

        /// <summary>Character form of the "cannot cast, stunned" mark, used only when
        /// <see cref="UseDrawnRefusalMarks"/> is false. See that field first.</summary>
        public const string StunnedCrossGlyph = "✕";

        /// <summary>
        /// Movement multiplier at or above which the local player is NOT considered
        /// slowed. Mirrors <c>ChickenController.CurrentControlState</c>'s own threshold
        /// exactly — the strip and the greyed/tinted stick both have to agree with the
        /// ladder that decides <c>ControlRules.CanMove</c>, or the HUD would show a SLOW
        /// row on a stick it is simultaneously painting as Free.
        /// </summary>
        public const float SlowThreshold = 0.99f;

        // ---- Mappings -----------------------------------------------------------

        /// <summary>
        /// The USS modifier class for a hex in <paramref name="refusal"/>, or
        /// <c>null</c> when the hex needs none (ready, or hidden entirely because the slot
        /// is unavailable for this class — ART §6.6's "slot 3 visibility" rule already
        /// removes that hex from the layout, so painting it would be dead work).
        /// </summary>
        public static string HexClass(AbilityRefusal refusal)
        {
            switch (refusal)
            {
                case AbilityRefusal.Cooldown:           return HexCooldownClass;
                case AbilityRefusal.NoTarget:           return HexNoTargetClass;
                case AbilityRefusal.Stunned:            return HexStunnedClass;
                case AbilityRefusal.OtherAbilityActive: return HexOtherActiveClass;
                default:                                return null;
            }
        }

        /// <summary>
        /// Which single mark ART §6.6's layer 9 carries for <paramref name="refusal"/>.
        /// <c>OtherAbilityActive</c> deliberately gets <see cref="HexCenterMark.None"/>:
        /// its signal is the top-down accent drain on the *other* slot (the one actually
        /// running), and stamping a mark on every suppressed hex as well would say "four
        /// things are wrong" when only one thing is happening.
        /// </summary>
        public static HexCenterMark CenterMark(AbilityRefusal refusal)
        {
            switch (refusal)
            {
                case AbilityRefusal.Cooldown: return HexCenterMark.CooldownSeconds;
                case AbilityRefusal.NoTarget: return HexCenterMark.NoTarget;
                case AbilityRefusal.Stunned:  return HexCenterMark.StunnedCross;
                default:                      return HexCenterMark.None;
            }
        }

        /// <summary>
        /// The USS modifier class marking what <paramref name="state"/> does to the move
        /// stick, or <c>null</c> for <see cref="ControlState.Free"/>. Communication only —
        /// the stick stays draggable in every state, because <c>ControlRules.CanMove</c> is
        /// enforced in <c>ChickenController</c> and a HUD that also swallowed the input
        /// would double-gate it and hide input bugs.
        /// </summary>
        public static string JoystickClass(ControlState state)
        {
            switch (state)
            {
                case ControlState.Stunned: return JoyStunnedClass;
                case ControlState.Rooted:  return JoyRootedClass;
                case ControlState.Slowed:  return JoySlowedClass;
                default:                   return null;
            }
        }

        /// <summary>Glyph for a strip row, or the empty string for
        /// <see cref="HudStatusKind.None"/>.</summary>
        public static string GlyphFor(HudStatusKind kind)
        {
            switch (kind)
            {
                case HudStatusKind.Stun: return StunGlyph;
                case HudStatusKind.Root: return RootGlyph;
                case HudStatusKind.Slow: return SlowGlyph;
                default:                 return string.Empty;
            }
        }

        /// <summary>Short name for a strip row, or the empty string for
        /// <see cref="HudStatusKind.None"/>.</summary>
        public static string NameFor(HudStatusKind kind)
        {
            switch (kind)
            {
                case HudStatusKind.Stun: return StunName;
                case HudStatusKind.Root: return RootName;
                case HudStatusKind.Slow: return SlowName;
                default:                 return string.Empty;
            }
        }

        /// <summary>
        /// Whether a row of <paramref name="kind"/> gets a draining bar.
        ///
        /// <b>Slow does not, and this is the accepted deferral, not an omission.</b> Stun
        /// and Root each have a real <c>[Networked] TickTimer</c> with a deadline, so a bar
        /// showing remaining/total is true. <c>SlowMultiplier</c> is re-derived from
        /// scratch every tick out of whatever is currently touching the chicken (zones,
        /// auras, piles, collisions) and has no end time at all — two overlapping sources
        /// would make any single "time left" meaningless. A bar there would be a lie, so
        /// the slow row shows its <i>magnitude</i> (<c>×0.45</c>) instead. See
        /// <c>ChickenController.SlowMultiplier</c>'s remarks: there is no slow-end timer to
        /// invent.
        /// </summary>
        public static bool HasDrainBar(HudStatusKind kind) =>
            kind == HudStatusKind.Stun || kind == HudStatusKind.Root;

        /// <summary>
        /// Fills <paramref name="into"/> with the local player's active control statuses in
        /// severity order (stun → root → slow) and returns how many were written.
        ///
        /// Stun / root / slow are independent bits that can all be set at once, so this
        /// reports the whole set rather than the single dominant
        /// <c>ChickenController.CurrentControlState</c> — §5.1's "one small icon per active
        /// status." The dominant state is still what drives the move stick, since the stick
        /// has exactly one appearance. Writes at most <paramref name="into"/>.Length rows,
        /// which the caller sizes from <c>FeedbackTuning.StatusBadgeMaxRows</c>.
        /// </summary>
        /// <param name="stunRemaining">Seconds left; may legitimately be 0 on the frame a
        /// stun is applied but its timer has not been read yet — the row still appears, it
        /// just renders without a number rather than with a wrong one.</param>
        public static int CollectStatusRows(
            bool stunned, float stunRemaining,
            bool rooted, float rootRemaining,
            float slowMultiplier,
            HudStatusRow[] into)
        {
            if (into == null || into.Length == 0) return 0;

            int n = 0;
            if (stunned && n < into.Length)
                into[n++] = new HudStatusRow(HudStatusKind.Stun, stunRemaining > 0f ? stunRemaining : 0f);
            if (rooted && n < into.Length)
                into[n++] = new HudStatusRow(HudStatusKind.Root, rootRemaining > 0f ? rootRemaining : 0f);
            if (slowMultiplier < SlowThreshold && n < into.Length)
                into[n++] = new HudStatusRow(HudStatusKind.Slow, slowMultiplier);

            return n;
        }
    }
}
