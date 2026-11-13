using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using UnityEngine;

namespace CluckWars.Visuals
{
    using Badge = ChickenStatusBadges.Badge;

    /// <summary>
    /// Where a badge row's TEXT colour comes from. A name, not a colour: the row is resolved once, in
    /// <see cref="StatusEffects.TextColor"/>, so the table stays a table of facts and the palette lookup
    /// (which is live and must not be captured at static-init time) stays in one place.
    /// </summary>
    public enum BadgeTextColor : byte
    {
        /// <summary>No colour of its own; the row draws white.</summary>
        None = 0,
        Stun,
        Root,
        Slow,
        EggShell,
        Hidden,
        Speed,
    }

    /// <summary>
    /// One status effect, as every enum in the project names it. The project has five vocabularies for
    /// the same handful of effects and they are ordered differently on purpose (the networked ones are
    /// pinned by ordinal, the ladder is severity-ascending, the badge order is the HUD stacking order):
    /// <list type="bullet">
    /// <item><see cref="ControlKind"/> — what one chicken sends another (<c>ControlClaimRules</c>);</item>
    /// <item><see cref="ControlState"/> — the resolved severity ladder (<c>ControlRules</c>);</item>
    /// <item><see cref="ControlVfx"/> — the replicated flag byte every peer reads;</item>
    /// <item><see cref="Badge"/> — the HUD row;</item>
    /// <item><see cref="ZoneEffect"/> — what an <c>AbilityZone</c> applies (networked).</item>
    /// </list>
    /// A row relates them. <c>Default</c> on a column means "this effect has no counterpart in that
    /// vocabulary" — <c>ControlKind.None</c>, <c>ControlState.Free</c>, <c>ControlVfx.None</c>,
    /// <c>Badge.None</c>, and <see cref="HasZone"/> <c>false</c> for the zone column.
    /// </summary>
    public readonly struct StatusEffectRow
    {
        /// <summary>The HUD row this effect draws; <see cref="Badge.None"/> only on the empty row.</summary>
        public readonly Badge Badge;
        /// <summary>What a caster sends to produce it; <see cref="ControlKind.None"/> when nothing sends it.</summary>
        public readonly ControlKind Kind;
        /// <summary>Its rung on the control ladder; <see cref="ControlState.Free"/> when it is not a control state.</summary>
        public readonly ControlState State;
        /// <summary>The replicated bit the state authority raises while the effect runs.</summary>
        public readonly ControlVfx StateFlag;
        /// <summary>
        /// The replicated bit that makes the HUD row appear. Same as <see cref="StateFlag"/> except for Slow,
        /// where only an enemy-agency slow (<see cref="ControlVfx.Snared"/>) earns a row — pile drag and bump
        /// drag raise <see cref="ControlVfx.Slowed"/> alone and stay ambient (E5a, 2026-11-08).
        /// </summary>
        public readonly ControlVfx BadgeFlag;
        /// <summary>True when an <c>AbilityZone</c> can apply this effect.</summary>
        public readonly bool HasZone;
        /// <summary>Meaningful only when <see cref="HasZone"/>.</summary>
        public readonly ZoneEffect Zone;
        /// <summary>True when the badge draws a state sprite; Root and the empty row draw text only.</summary>
        public readonly bool HasGlyph;
        /// <summary>Meaningful only when <see cref="HasGlyph"/>.</summary>
        public readonly StateGlyphStyle.StateGlyph Glyph;
        /// <summary>Text glyph drawn only when the badge's sprite field is unwired; empty when it has none.</summary>
        public readonly string TextGlyph;
        /// <summary>Glyph printed before the word, in the row text itself (Root; it has no sprite).</summary>
        public readonly string LeadGlyph;
        /// <summary>Localisation key for the row word; empty when the row has no word.</summary>
        public readonly string TextKey;
        /// <summary>The row prints "WORD 1.2s"; false when the effect has no deadline worth showing.</summary>
        public readonly bool CountdownInText;
        /// <summary>
        /// The row's number is a magnitude (the slow multiplier), not seconds, so it quantizes to a bare
        /// "shown / not shown" instead of tenths.
        /// </summary>
        public readonly bool ValueIsMagnitude;
        /// <summary>Which colour the row text takes.</summary>
        public readonly BadgeTextColor Color;

        public StatusEffectRow(Badge badge, ControlKind kind, ControlState state, ControlVfx stateFlag,
            ControlVfx badgeFlag, bool hasZone, ZoneEffect zone, bool hasGlyph, StateGlyphStyle.StateGlyph glyph,
            string textGlyph, string leadGlyph, string textKey, bool countdownInText, bool valueIsMagnitude,
            BadgeTextColor color)
        {
            Badge = badge;
            Kind = kind;
            State = state;
            StateFlag = stateFlag;
            BadgeFlag = badgeFlag;
            HasZone = hasZone;
            Zone = zone;
            HasGlyph = hasGlyph;
            Glyph = glyph;
            TextGlyph = textGlyph;
            LeadGlyph = leadGlyph;
            TextKey = textKey;
            CountdownInText = countdownInText;
            ValueIsMagnitude = valueIsMagnitude;
            Color = color;
        }
    }

    /// <summary>
    /// The one mapping table between the project's five status-effect vocabularies (A16 part 2), and the
    /// one place a badge's glyph, word, countdown rule and text colour are declared.
    /// </summary>
    /// <remarks>
    /// <b>Why it lives in Presentation.</b> Four of the five enums are Gameplay; <see cref="Badge"/> is a HUD
    /// row and belongs to Presentation. Gameplay &lt; Presentation, so a Gameplay file cannot name
    /// <see cref="Badge"/> and this is the only assembly that can see all five at once. The cost is that the
    /// Gameplay side cannot read the table: <c>ChickenController.CurrentControlState</c> (facts → ladder),
    /// its <see cref="ControlVfx"/> composition (facts → flags) and <c>AbilityZone</c>'s
    /// <c>Effect == ZoneEffect.Root</c> branches stay where they are. They are pinned against this table by
    /// <c>StatusEffectTableTests</c>, which fails if either side drifts.
    ///
    /// <b>Numeric values are untouched.</b> Every enum keeps the values it shipped with; three of them are
    /// networked. This table only says which value in one enum means the same thing as which value in another.
    /// </remarks>
    public static class StatusEffects
    {
        private const string StunGlyph = HudFeedbackStyle.StunGlyph;
        private const string RootGlyph = HudFeedbackStyle.RootGlyph;
        private const string SlowGlyph = HudFeedbackStyle.SlowGlyph;

        private static readonly StatusEffectRow[] Rows =
        {
            // badge            kind              state                 stateFlag            badgeFlag            zone?                      glyph                                            textGlyph    lead        textKey                  countdown magnitude  colour
            new StatusEffectRow(Badge.None,   ControlKind.None, ControlState.Free,    ControlVfx.None,     ControlVfx.None,     false, default,            false, default,                                   "",          "",         "",                      false, false, BadgeTextColor.None),
            new StatusEffectRow(Badge.Stun,   ControlKind.Stun, ControlState.Stunned, ControlVfx.Stunned,  ControlVfx.Stunned,  false, default,            true,  StateGlyphStyle.StateGlyph.Stunned,        StunGlyph,   "",         UiKeys.HudStatusStun,    true,  false, BadgeTextColor.Stun),
            // Root prints its own text glyph before the word: it is the one badge with no sprite.
            new StatusEffectRow(Badge.Root,   ControlKind.Root, ControlState.Rooted,  ControlVfx.Rooted,   ControlVfx.Rooted,   true,  ZoneEffect.Root,    false, default,                                   "",          RootGlyph,  UiKeys.HudStatusStuck,   true,  false, BadgeTextColor.Root),
            // E6a (2026-11-09): slow has no deadline and a x0.45 means nothing to a kid, so the row is the word alone.
            new StatusEffectRow(Badge.Slow,   ControlKind.Slow, ControlState.Slowed,  ControlVfx.Slowed,   ControlVfx.Snared,   true,  ZoneEffect.Slow,    true,  StateGlyphStyle.StateGlyph.Slowed,         SlowGlyph,   "",         UiKeys.HudStatusSlow,    false, true,  BadgeTextColor.Slow),
            // The shell's two phases, Hidden and Speed come from ability slots, not from a control claim:
            // nothing sends them, they are not on the ladder and they raise no replicated control bit.
            new StatusEffectRow(Badge.Waddle, ControlKind.None, ControlState.Free,    ControlVfx.None,     ControlVfx.None,     false, default,            true,  StateGlyphStyle.StateGlyph.ShieldWalk,     "",          "",         UiKeys.HudBadgeWaddle,   true,  false, BadgeTextColor.EggShell),
            new StatusEffectRow(Badge.Egg,    ControlKind.None, ControlState.Free,    ControlVfx.None,     ControlVfx.None,     false, default,            true,  StateGlyphStyle.StateGlyph.ShieldEgg,      "",          "",         UiKeys.HudBadgeEgg,      true,  false, BadgeTextColor.EggShell),
            new StatusEffectRow(Badge.Hidden, ControlKind.None, ControlState.Free,    ControlVfx.None,     ControlVfx.None,     false, default,            true,  StateGlyphStyle.StateGlyph.Hidden,         "",          "",         UiKeys.HudBadgeHidden,   false, false, BadgeTextColor.Hidden),
            new StatusEffectRow(Badge.Speed,  ControlKind.None, ControlState.Free,    ControlVfx.None,     ControlVfx.None,     false, default,            true,  StateGlyphStyle.StateGlyph.Speeding,       "",          "",         UiKeys.HudBadgeFast,     false, false, BadgeTextColor.Speed),
        };

        /// <summary>Every row, in badge order. Read by the tests that prove the table is total.</summary>
        public static StatusEffectRow[] All => Rows;

        /// <summary>The row for a badge. Total: every <see cref="Badge"/> value has exactly one row.</summary>
        public static StatusEffectRow For(Badge badge)
        {
            for (int i = 0; i < Rows.Length; i++)
                if (Rows[i].Badge == badge) return Rows[i];
            // Unreachable while the totality test passes; a new enum value lands here rather than silently
            // drawing a stun.
            throw new ArgumentOutOfRangeException(nameof(badge), badge, "No status-effect row; add one to StatusEffects.Rows.");
        }

        /// <summary>The row a control claim produces. False for <see cref="ControlKind.None"/>, which has no effect.</summary>
        public static bool TryForKind(ControlKind kind, out StatusEffectRow row)
        {
            for (int i = 0; i < Rows.Length; i++)
                if (kind != ControlKind.None && Rows[i].Kind == kind) { row = Rows[i]; return true; }
            row = Rows[0];
            return false;
        }

        /// <summary>The row a ladder rung names. False for <see cref="ControlState.Free"/>, which is no effect.</summary>
        public static bool TryForState(ControlState state, out StatusEffectRow row)
        {
            for (int i = 0; i < Rows.Length; i++)
                if (state != ControlState.Free && Rows[i].State == state) { row = Rows[i]; return true; }
            row = Rows[0];
            return false;
        }

        /// <summary>The row a zone applies. Total over <see cref="ZoneEffect"/>.</summary>
        public static bool TryForZone(ZoneEffect zone, out StatusEffectRow row)
        {
            for (int i = 0; i < Rows.Length; i++)
                if (Rows[i].HasZone && Rows[i].Zone == zone) { row = Rows[i]; return true; }
            row = Rows[0];
            return false;
        }

        // ---- The columns the HUD reads -------------------------------------------------------

        /// <summary>The state sprite a badge draws, or false when it has none and stays text (Root).</summary>
        public static bool TryGlyph(Badge badge, out StateGlyphStyle.StateGlyph glyph)
        {
            var row = For(badge);
            glyph = row.HasGlyph ? row.Glyph : default;
            return row.HasGlyph;
        }

        /// <summary>Text glyph drawn only when the badge's sprite field is null; empty when it has none.</summary>
        public static string TextGlyph(Badge badge) => For(badge).TextGlyph;

        /// <summary>
        /// Reduces a raw value to the integer the label actually shows, so the string is only rebuilt when a
        /// visible digit moves: tenths of a second for the timed states, a bare 1 for slow's magnitude.
        /// </summary>
        public static int Quantize(Badge badge, float value)
        {
            if (value <= 0f) return 0;
            return For(badge).ValueIsMagnitude ? 1 : Mathf.CeilToInt(value * 10f);
        }

        /// <summary>The row text without a sprite: a lead glyph, a word, a countdown, or nothing.</summary>
        public static string Compose(Badge badge, int quantity)
        {
            var row = For(badge);
            if (row.TextKey.Length == 0) return string.Empty;

            string word = UiText.Get(row.TextKey);
            if (row.CountdownInText && quantity > 0)
                word += " " + HudFeedbackStyle.Seconds(quantity);
            return row.LeadGlyph.Length > 0 ? row.LeadGlyph + " " + word : word;
        }

        /// <summary>The row TEXT colour (the glyph keeps its own art colours).</summary>
        public static Color TextColor(Badge badge)
        {
            switch (For(badge).Color)
            {
                case BadgeTextColor.Stun:     return FeedbackTuning.CanonicalStunColor;
                case BadgeTextColor.Root:     return FeedbackTuning.CanonicalRootColor;
                case BadgeTextColor.Slow:     return FeedbackTuning.CanonicalSlowTextColor;
                case BadgeTextColor.EggShell: return FeedbackPalette.Current.Accent(AccentKey.EggShell);
                case BadgeTextColor.Hidden:   return FeedbackTuning.CanonicalHideTextColor;
                case BadgeTextColor.Speed:    return FeedbackPalette.Current.Accent(AccentKey.Speed);
                default:                      return UnityEngine.Color.white;
            }
        }
    }
}
