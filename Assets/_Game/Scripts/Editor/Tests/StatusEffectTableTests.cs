using System;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    using Badge = ChickenStatusBadges.Badge;

    /// <summary>
    /// A16 part 2: <see cref="StatusEffects"/> is the one table relating the project's five status-effect
    /// vocabularies (<see cref="ControlKind"/>, <see cref="ControlState"/>, <see cref="ControlVfx"/>,
    /// <see cref="Badge"/>, <see cref="ZoneEffect"/>) and the one place a badge's glyph, word, countdown
    /// rule and text colour live.
    ///
    /// Two jobs. <b>Totality</b>: every value of every one of the five enums either has exactly one row or
    /// is named here as having no counterpart, so a new enum value cannot be forgotten. <b>Equivalence</b>:
    /// the five per-badge lookups the table replaced are re-stated here as they read before the change, and
    /// compared against the table for every badge — the old answers are the specification.
    /// </summary>
    public sealed class StatusEffectTableTests
    {
        private static Badge[] AllBadges => (Badge[])Enum.GetValues(typeof(Badge));

        // ---- Totality -----------------------------------------------------------------------

        [Test]
        public void EveryBadge_HasExactlyOneRow()
        {
            foreach (var badge in AllBadges)
            {
                int rows = StatusEffects.All.Count(r => r.Badge == badge);
                Assert.AreEqual(1, rows, $"{badge} must appear in exactly one StatusEffects row");
                Assert.AreEqual(badge, StatusEffects.For(badge).Badge);
            }
            Assert.AreEqual(AllBadges.Length, StatusEffects.All.Length, "no row without a badge of its own");
        }

        [Test]
        public void EveryControlKind_MapsOrIsExplicitlyNone()
        {
            foreach (ControlKind kind in Enum.GetValues(typeof(ControlKind)))
            {
                bool found = StatusEffects.TryForKind(kind, out var row);
                if (kind == ControlKind.None)
                {
                    Assert.IsFalse(found, "ControlKind.None is the explicit 'no effect' value and has no row");
                    continue;
                }
                Assert.IsTrue(found, $"{kind} has no StatusEffects row");
                Assert.AreEqual(kind, row.Kind);
                Assert.AreEqual(1, StatusEffects.All.Count(r => r.Kind == kind), $"{kind} maps to one effect only");
            }
        }

        [Test]
        public void EveryControlState_MapsOrIsExplicitlyFree()
        {
            foreach (ControlState state in Enum.GetValues(typeof(ControlState)))
            {
                bool found = StatusEffects.TryForState(state, out var row);
                if (state == ControlState.Free)
                {
                    Assert.IsFalse(found, "ControlState.Free is the explicit 'no effect' rung and has no row");
                    continue;
                }
                Assert.IsTrue(found, $"{state} has no StatusEffects row");
                Assert.AreEqual(state, row.State);
                Assert.AreEqual(1, StatusEffects.All.Count(r => r.State == state), $"{state} maps to one effect only");
            }
        }

        [Test]
        public void EveryZoneEffect_MapsToARow()
        {
            foreach (ZoneEffect zone in Enum.GetValues(typeof(ZoneEffect)))
            {
                Assert.IsTrue(StatusEffects.TryForZone(zone, out var row), $"{zone} has no StatusEffects row");
                Assert.AreEqual(zone, row.Zone);
                Assert.AreEqual(1, StatusEffects.All.Count(r => r.HasZone && r.Zone == zone), $"{zone} maps to one effect only");
            }
        }

        [Test]
        public void EveryControlVfxBit_IsSomeRowsStateOrBadgeFlag()
        {
            foreach (ControlVfx bit in Enum.GetValues(typeof(ControlVfx)))
            {
                if (bit == ControlVfx.None) continue;
                bool covered = StatusEffects.All.Any(r => (r.StateFlag & bit) != 0 || (r.BadgeFlag & bit) != 0);
                Assert.IsTrue(covered, $"ControlVfx.{bit} is not related to any status effect");
            }
        }

        // ---- The relations the table asserts -------------------------------------------------

        [Test]
        public void TheThreeControlEffects_AgreeAcrossAllFiveVocabularies()
        {
            // The whole point of the table: one row per effect, and these three rows are the ones
            // every vocabulary has a word for. Stun is the one no zone can apply.
            var stun = StatusEffects.For(Badge.Stun);
            Assert.AreEqual(ControlKind.Stun, stun.Kind);
            Assert.AreEqual(ControlState.Stunned, stun.State);
            Assert.AreEqual(ControlVfx.Stunned, stun.StateFlag);
            Assert.IsFalse(stun.HasZone, "no zone stuns");

            var root = StatusEffects.For(Badge.Root);
            Assert.AreEqual(ControlKind.Root, root.Kind);
            Assert.AreEqual(ControlState.Rooted, root.State);
            Assert.AreEqual(ControlVfx.Rooted, root.StateFlag);
            Assert.IsTrue(root.HasZone);
            Assert.AreEqual(ZoneEffect.Root, root.Zone);

            var slow = StatusEffects.For(Badge.Slow);
            Assert.AreEqual(ControlKind.Slow, slow.Kind);
            Assert.AreEqual(ControlState.Slowed, slow.State);
            Assert.AreEqual(ControlVfx.Slowed, slow.StateFlag);
            Assert.IsTrue(slow.HasZone);
            Assert.AreEqual(ZoneEffect.Slow, slow.Zone);
        }

        [Test]
        public void OnlySlow_ShowsItsRowOnADifferentBitThanItsState()
        {
            // E5a: a pile or bump drag raises Slowed with no enemy behind it and earns no row;
            // Snared is the bit that does. Every other effect's row follows its own state bit.
            foreach (var row in StatusEffects.All)
            {
                if (row.Badge == Badge.Slow)
                    Assert.AreEqual(ControlVfx.Snared, row.BadgeFlag, "the Slow row answers to Snared, not Slowed");
                else
                    Assert.AreEqual(row.StateFlag, row.BadgeFlag, $"{row.Badge}: the row follows its own state bit");
            }
        }

        [Test]
        public void TheLadderRungs_AgreeWithControlRules()
        {
            // ControlRules is the gameplay side of the same facts; the table must not disagree with it.
            Assert.IsFalse(ControlRules.CanCast(StatusEffects.For(Badge.Stun).State), "a stunned chicken cannot cast");
            Assert.IsFalse(ControlRules.CanMove(StatusEffects.For(Badge.Root).State), "a rooted chicken cannot move");
            Assert.IsTrue(ControlRules.CanMove(StatusEffects.For(Badge.Slow).State), "a slowed chicken still moves");
            Assert.IsTrue(ControlRules.CanCollect(StatusEffects.For(Badge.Slow).State), "a slowed chicken still collects");

            foreach (var row in StatusEffects.All)
            {
                if (row.Kind != ControlKind.None) continue;
                Assert.AreEqual(ControlState.Free, row.State, $"{row.Badge} is not sent by anyone, so it is not on the ladder");
                Assert.AreEqual(ControlVfx.None, row.StateFlag, $"{row.Badge} raises no replicated control bit");
            }
        }

        [Test]
        public void EveryAbilityTheGameShips_EmitsAKindTheTableKnows()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            foreach (var ability in registry.All)
            {
                if (ability == null) continue;
                var kind = ability.EmittedControl.Kind;
                if (kind == ControlKind.None) continue;
                Assert.IsTrue(StatusEffects.TryForKind(kind, out _), $"{ability.name} emits {kind}, which has no row");
            }
        }

        // ---- Equivalence with the mapping that was replaced ----------------------------------

        /// <summary>The glyph switch exactly as <c>ChickenStatusBadges.TryGlyphFor</c> read it before A16 part 2.</summary>
        private static bool OldTryGlyphFor(Badge badge, out StateGlyphStyle.StateGlyph glyph)
        {
            switch (badge)
            {
                case Badge.Stun:   glyph = StateGlyphStyle.StateGlyph.Stunned;    return true;
                case Badge.Slow:   glyph = StateGlyphStyle.StateGlyph.Slowed;     return true;
                case Badge.Waddle: glyph = StateGlyphStyle.StateGlyph.ShieldWalk; return true;
                case Badge.Egg:    glyph = StateGlyphStyle.StateGlyph.ShieldEgg;  return true;
                case Badge.Hidden: glyph = StateGlyphStyle.StateGlyph.Hidden;     return true;
                case Badge.Speed:  glyph = StateGlyphStyle.StateGlyph.Speeding;   return true;
                default:           glyph = default;                               return false;
            }
        }

        private static string OldFallbackGlyph(Badge badge)
        {
            switch (badge)
            {
                case Badge.Stun: return HudFeedbackStyle.StunGlyph;
                case Badge.Slow: return HudFeedbackStyle.SlowGlyph;
                default:         return string.Empty;
            }
        }

        private static int OldQuantize(Badge badge, float value)
        {
            if (value <= 0f) return 0;
            return badge == Badge.Slow ? 1 : Mathf.CeilToInt(value * 10f);
        }

        private static string OldWordWithCountdown(string word, string secs) =>
            secs.Length > 0 ? word + " " + secs : word;

        private static string OldCompose(Badge badge, int quantity)
        {
            string secs = quantity > 0 ? HudFeedbackStyle.Seconds(quantity) : string.Empty;
            switch (badge)
            {
                case Badge.Stun:   return OldWordWithCountdown(UiText.Get(UiKeys.HudStatusStun), secs);
                case Badge.Root:   return HudFeedbackStyle.RootGlyph + " " + OldWordWithCountdown(UiText.Get(UiKeys.HudStatusStuck), secs);
                case Badge.Slow:   return UiText.Get(UiKeys.HudStatusSlow);
                case Badge.Waddle: return OldWordWithCountdown(UiText.Get(UiKeys.HudBadgeWaddle), secs);
                case Badge.Egg:    return OldWordWithCountdown(UiText.Get(UiKeys.HudBadgeEgg), secs);
                case Badge.Hidden: return UiText.Get(UiKeys.HudBadgeHidden);
                case Badge.Speed:  return UiText.Get(UiKeys.HudBadgeFast);
                default:           return string.Empty;
            }
        }

        private static Color OldColorFor(Badge badge)
        {
            switch (badge)
            {
                case Badge.Stun:   return FeedbackTuning.CanonicalStunColor;
                case Badge.Root:   return FeedbackTuning.CanonicalRootColor;
                case Badge.Slow:   return FeedbackTuning.CanonicalSlowTextColor;
                case Badge.Waddle:
                case Badge.Egg:    return FeedbackPalette.Current.Accent(AccentKey.EggShell);
                case Badge.Hidden: return FeedbackTuning.CanonicalHideTextColor;
                case Badge.Speed:  return FeedbackPalette.Current.Accent(AccentKey.Speed);
                default:           return Color.white;
            }
        }

        [Test]
        public void Glyph_MatchesTheOldSwitch_ForEveryBadge()
        {
            foreach (var badge in AllBadges)
            {
                bool expected = OldTryGlyphFor(badge, out var expectedGlyph);
                bool actual = StatusEffects.TryGlyph(badge, out var actualGlyph);
                Assert.AreEqual(expected, actual, $"{badge}: has-glyph");
                if (expected) Assert.AreEqual(expectedGlyph, actualGlyph, $"{badge}: glyph");
            }
        }

        [Test]
        public void TextGlyph_MatchesTheOldSwitch_ForEveryBadge()
        {
            foreach (var badge in AllBadges)
                Assert.AreEqual(OldFallbackGlyph(badge), StatusEffects.TextGlyph(badge), $"{badge}: text fallback");
        }

        [Test]
        public void Quantize_MatchesTheOldRule_ForEveryBadgeAndValue()
        {
            float[] values = { -1f, 0f, 0.001f, 0.049f, 0.45f, 0.99f, 1f, 2.34f, 12.5f, 99f };
            foreach (var badge in AllBadges)
                foreach (float v in values)
                    Assert.AreEqual(OldQuantize(badge, v), StatusEffects.Quantize(badge, v), $"{badge} @ {v}");
        }

        [Test]
        public void Compose_MatchesTheOldSwitch_ForEveryBadgeAndQuantity()
        {
            int[] quantities = { 0, 1, 5, 10, 123 };
            foreach (var badge in AllBadges)
                foreach (int q in quantities)
                    Assert.AreEqual(OldCompose(badge, q), StatusEffects.Compose(badge, q), $"{badge} @ {q}");
        }

        [Test]
        public void TextColor_MatchesTheOldSwitch_ForEveryBadge()
        {
            foreach (var badge in AllBadges)
                Assert.AreEqual(OldColorFor(badge), StatusEffects.TextColor(badge), $"{badge}: row text colour");
        }

        [Test]
        public void ChickenStatusBadges_StillAnswersThroughTheTable()
        {
            // The HUD's own entry points are thin wrappers now; they must not have drifted.
            foreach (var badge in AllBadges)
            {
                Assert.AreEqual(StatusEffects.TryGlyph(badge, out var tableGlyph),
                                ChickenStatusBadges.TryGlyphFor(badge, out var hudGlyph), $"{badge}: TryGlyphFor");
                Assert.AreEqual(tableGlyph, hudGlyph, $"{badge}: TryGlyphFor glyph");
                Assert.AreEqual(StatusEffects.Quantize(badge, 1.23f), ChickenStatusBadges.Quantize(badge, 1.23f), $"{badge}: Quantize");
            }

            Assert.IsTrue(ChickenStatusBadges.ShowSlowRow(ControlVfx.Snared | ControlVfx.Slowed));
            Assert.IsFalse(ChickenStatusBadges.ShowSlowRow(ControlVfx.Slowed), "an ambient drag earns no row");
            Assert.IsFalse(ChickenStatusBadges.ShowSlowRow(ControlVfx.None));
        }
    }
}
