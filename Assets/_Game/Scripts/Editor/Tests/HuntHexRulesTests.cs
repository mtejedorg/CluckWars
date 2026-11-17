using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Visuals;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>The Hunt hex (<see cref="HuntHexRules"/>, spec 2026-11-17 H2a / H3a): which slot carries the hunt, full alpha, the
    /// glyph per blocker, the label hand-over and the stylesheet / UXML the hex needs.</summary>
    public class HuntHexRulesTests
    {
        private const string Uss  = "Assets/UI/Styles/TouchControls.uss";
        private const string Uxml = "Assets/UI/TouchControls.uxml";
        private const string HudPath = "Assets/_Game/Scripts/Input/TouchControlsController.cs";

        private static MarkOutputs Mark(MarkRole role, bool active) => new MarkOutputs { Role = role, AssassinMarkActive = active };

        [Test]
        public void OnlyTheDeathMarkSlotTakesTheHunt()
        {
            Assert.IsTrue(HuntHexRules.Applies(Mark(MarkRole.Assassin, true), true));
            Assert.IsFalse(HuntHexRules.Applies(Mark(MarkRole.Assassin, true), false), "another slot never carries the hunt");
            Assert.IsFalse(HuntHexRules.Applies(Mark(MarkRole.Assassin, false), true), "the end fade (AssassinMarkActive false) gives the hex back");
            Assert.IsFalse(HuntHexRules.Applies(Mark(MarkRole.Victim, true), true), "the victim never sees a hunt hex");
            Assert.IsFalse(HuntHexRules.Applies(Mark(MarkRole.ThirdParty, true), true));
            Assert.IsFalse(HuntHexRules.Applies(Mark(MarkRole.None, false), true));
        }

        [Test]
        public void TintAlpha_FullWhileHunting_ExistingTableOtherwise()
        {
            foreach (var r in new[] { AbilityRefusal.None, AbilityRefusal.NoTarget })
            {
                Assert.AreEqual(FeedbackTuning.HexAlphaReady, HuntHexRules.TintAlpha(r, true), $"{r} while hunting");
                Assert.Greater(HuntHexRules.TintAlpha(r, true), FeedbackTuning.HexAlphaNoTarget, "never the joystick's dimmer value");
                Assert.AreEqual(FeedbackTuning.HexAlphaReady, HuntHexRules.TintAlpha(r, false), $"{r} at rest is full too");
            }
            foreach (var hunting in new[] { true, false })
            {
                Assert.AreEqual(FeedbackTuning.HexAlphaCooldown, HuntHexRules.TintAlpha(AbilityRefusal.Cooldown, hunting), "Cooldown keeps its dim");
                Assert.AreEqual(FeedbackTuning.IllegalCastTintColor.a, HuntHexRules.TintAlpha(AbilityRefusal.Stunned, hunting), "Stunned keeps its wash");
            }
        }

        [Test]
        public void GlyphClass_TotalOverHuntBlocker()
        {
            var seen = new HashSet<string>();
            foreach (HuntBlocker b in System.Enum.GetValues(typeof(HuntBlocker)))
            {
                string cls = HuntHexRules.GlyphClass(b);
                if (b == HuntBlocker.None) { Assert.IsNull(cls, "nothing blocks, no glyph"); continue; }
                Assert.IsNotNull(cls, $"{b} has no glyph class");
                Assert.IsTrue(seen.Add(cls), $"{b}: {cls} is shared with another blocker");
            }
            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void EveryEmittedHuntClass_HasARuleInTheStylesheet_AndEverySpriteExists()
        {
            string uss = File.ReadAllText(Uss);
            var emitted = new List<string> { HuntHexRules.HuntClass, "cw-hex-huntdot", "cw-hex-huntglyph", "cw-hex-huntfar" };
            foreach (HuntBlocker b in System.Enum.GetValues(typeof(HuntBlocker)))
                if (HuntHexRules.GlyphClass(b) != null) emitted.Add(HuntHexRules.GlyphClass(b));
            foreach (string cls in emitted)
                Assert.IsTrue(Regex.IsMatch(uss, @"\." + Regex.Escape(cls) + @"[\s,{]"), $"no rule for .{cls} in TouchControls.uss");

            // The badges that draw a sprite name it, and every project:// url in the hex stylesheet resolves to a file on disk.
            foreach (string cls in new[] { HuntHexRules.GlyphStarClass, HuntHexRules.GlyphLockClass, HuntHexRules.GlyphEggClass })
                Assert.IsTrue(Regex.IsMatch(uss, @"\." + Regex.Escape(cls) + @"\s*\{[^}]*background-image:\s*url\("), $".{cls} draws no sprite");
            foreach (Match m in Regex.Matches(uss, @"url\(""project://database/([^""]+)""\)"))
                Assert.IsTrue(File.Exists(m.Groups[1].Value), $"TouchControls.uss points at a missing file: {m.Groups[1].Value}");
        }

        [Test]
        public void TouchControlsUxml_HasTheHuntElementsOnAllFourSlots_WithNoBakedText()
        {
            string uxml = File.ReadAllText(Uxml);
            for (int n = 1; n <= 4; n++)
            {
                StringAssert.Contains($"name=\"HuntDot{n}\" class=\"cw-hex-huntdot\"", uxml);
                StringAssert.Contains($"name=\"HuntGlyph{n}\" class=\"cw-hex-huntglyph\"", uxml);
                StringAssert.Contains($"name=\"HuntFar{n}\" text=\"\" class=\"cw-hex-huntfar\"", uxml);   // CODE-TEXT-04: the text comes from UiText
            }
        }

        [Test]
        public void DigitStep_IsATotalHandOverTable()
        {
            Assert.AreEqual(HuntDigitStep.Idle, HuntHexRules.DigitStep(false, false));
            Assert.AreEqual(HuntDigitStep.Acquire, HuntHexRules.DigitStep(false, true));
            Assert.AreEqual(HuntDigitStep.Hold, HuntHexRules.DigitStep(true, true));
            Assert.AreEqual(HuntDigitStep.HandBack, HuntHexRules.DigitStep(true, false));
        }

        [Test]
        public void DigitStep_HandsTheLabelBackExactlyOnce_WhenTheHuntEnds()
        {
            // Drive the controller's ownership flag the way RefreshHuntDigit does: arm, live, an end, then quiet frames.
            var shows = new[] { true, true, true, true, false, false, false };
            bool owned = false;
            int acquires = 0, handBacks = 0;
            foreach (bool show in shows)
            {
                switch (HuntHexRules.DigitStep(owned, show))
                {
                    case HuntDigitStep.Acquire: owned = true; acquires++; break;
                    case HuntDigitStep.HandBack: owned = false; handBacks++; break;
                }
            }
            Assert.AreEqual(1, acquires);
            Assert.AreEqual(1, handBacks, "a stale hunt digit would mean no hand-back");
            Assert.IsFalse(owned, "after the hunt the cooldown readout owns the label again");

            // A stunned cross mid-hunt (show false) hands back, and the hunt takes the label again when the cross clears.
            owned = false; acquires = 0; handBacks = 0;
            foreach (bool show in new[] { true, false, true })
            {
                switch (HuntHexRules.DigitStep(owned, show))
                {
                    case HuntDigitStep.Acquire: owned = true; acquires++; break;
                    case HuntDigitStep.HandBack: owned = false; handBacks++; break;
                }
            }
            Assert.AreEqual(2, acquires);
            Assert.AreEqual(1, handBacks);
        }

        [Test]
        public void Hud_HuntDigitOutranksTheOtherOwners_AndHandsBackLikeTheEggDigit_SourceGuard()
        {
            string src = TestSources.ReadClass(HudPath);
            int method = src.IndexOf("private void RefreshCenterMark(", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(method, 0);
            int hunt = src.IndexOf("RefreshHuntDigit(slot", method, System.StringComparison.Ordinal);
            int stun = src.IndexOf("RefreshCasterStunDigit(slot", method, System.StringComparison.Ordinal);
            int egg = src.IndexOf("RefreshEggDigit(slot", method, System.StringComparison.Ordinal);
            Assert.Greater(hunt, method, "RefreshCenterMark must call RefreshHuntDigit");
            Assert.Less(hunt, stun, "the hunt outranks the caster-stun digit");
            Assert.Less(hunt, egg, "the hunt outranks the egg digit");

            int body = src.IndexOf("private bool RefreshHuntDigit(", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(body, 0);
            string fn = src.Substring(body, src.IndexOf("private bool RefreshEggDigit(", body, System.StringComparison.Ordinal) - body);
            StringAssert.Contains("mark != HexCenterMark.StunnedCross", fn);            // the cross outranks the hunt
            StringAssert.Contains("case HuntDigitStep.HandBack:", fn);
            StringAssert.Contains("label.style.color = new StyleColor(StyleKeyword.Null);", fn);   // stylesheet colour restored
            StringAssert.Contains("_appliedCdSeconds[slot] = int.MinValue;", fn);       // cooldown readout repaints
            StringAssert.Contains("_appliedCdText[slot] = null;", fn);
            StringAssert.Contains("_stunDigitShown[slot] = false;", fn);                 // the other owners re-initialise
            StringAssert.Contains("_eggDigitShown[slot] = false;", fn);
            StringAssert.Contains("HuntRules.CountText(", fn);
        }

        // ---- K2b: the hex's own geometry, as relationships in the stylesheet ------------------------------------------

        private static float HexPx(string selector, string property)
        {
            string uss = File.ReadAllText(Uss);
            var m = Regex.Match(uss, "(?:^|\n)" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(m.Success, $"{selector} has no rule in TouchControls.uss");
            var v = Regex.Match(m.Groups[1].Value, @"(?<![\w-])" + Regex.Escape(property) + @"\s*:\s*(-?[0-9.]+)px");
            Assert.IsTrue(v.Success, $"{selector} has no {property} in px");
            return float.Parse(v.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        [Test]
        public void HuntCount_IsAtLeast1p4xTheCooldownDigit_AndTheCooldownRuleIsUntouched()
        {
            float cooldown = HexPx(".cw-hex-cdnum", "font-size");
            Assert.AreEqual(36f, cooldown, "the cooldown readout after the hunt is unchanged");
            Assert.GreaterOrEqual(HexPx(".cw-hex--hunt .cw-hex-cdnum", "font-size"), cooldown * 1.4f);
            Assert.GreaterOrEqual(HexPx(".cw-hud--pad .cw-hex--hunt .cw-hex-cdnum", "font-size"), cooldown * 1.4f);
        }

        [Test]
        public void HuntIcon_ShrinksAndRisesClearOfTheCount()
        {
            float hex = HexPx(".cw-hex", "height");
            float iconBottom = HexPx(".cw-hex--hunt .cw-hex-icon", "top") + HexPx(".cw-hex--hunt .cw-hex-icon", "height");
            float countTop = hex - HexPx(".cw-hex--hunt .cw-hex-cdnum", "bottom") - HexPx(".cw-hex--hunt .cw-hex-cdnum", "height");
            Assert.Less(HexPx(".cw-hex--hunt .cw-hex-icon", "width"), HexPx(".cw-hex-icon", "width"), "the paw shrinks while hunting");
            Assert.LessOrEqual(iconBottom, countTop + 6f, "the icon ends where the count box starts (the count's ink starts below its box top)");
        }

        [Test]
        public void BlockerBadge_IsAtLeast36px_WithAnInkBacking_AndTheFarChipClearsTheSlotBadge()
        {
            Assert.GreaterOrEqual(HexPx(".cw-hex-huntglyph", "width"), 36f);
            Assert.GreaterOrEqual(HexPx(".cw-hex-huntglyph", "height"), 36f);
            string uss = File.ReadAllText(Uss);
            StringAssert.Contains("background-color: rgb(20, 10, 26);", Regex.Match(uss, @"\.cw-hex-huntglyph\s*\{([^}]*)\}").Groups[1].Value, "ink backing");
            float slotBadgeBottom = HexPx(".cw-hex-badge", "top") + HexPx(".cw-hex-badge", "height");
            Assert.GreaterOrEqual(HexPx(".cw-hex-huntglyph", "top"), slotBadgeBottom, "the badge sits below the slot badge");
            Assert.GreaterOrEqual(HexPx(".cw-hex-huntfar", "top"), slotBadgeBottom - 2f, "the chip does not cover the slot badge number");
        }

        [Test]
        public void KillWord_IsSmallerThanBefore_AndFitsInsideTheHexBelowTheBadges()
        {
            const float fontBeforeK2b = 42f;
            Assert.Less(HexPx(".cw-hex-killtext", "font-size"), fontBeforeK2b);
            float hexWidth = HexPx(".cw-hex", "width");
            float content = hexWidth - HexPx(".cw-hex-killtext", "left") - HexPx(".cw-hex-killtext", "right");
            // POUNCE is ~3.76 em wide with its letter-spacing at 42 px / 2 px (158 px measured); with margin it must fit the content.
            float font = HexPx(".cw-hex-killtext", "font-size");
            float wordWidth = 158f / 42f * font;
            Assert.LessOrEqual(wordWidth + 12f, content, "the word fits the content width with margin");
            float badgeBottom = HexPx(".cw-hex-badge", "top") + HexPx(".cw-hex-badge", "height");
            Assert.GreaterOrEqual(HexPx(".cw-hex-killtext", "top"), badgeBottom - 4f, "the word starts below the slot badge");
            float tenthsInkTop = hexWidth - HexPx(".cw-hex--hunt .cw-hex-cdnum", "bottom") - HexPx(".cw-hex--hunt .cw-hex-cdnum", "height") + 6f;
            Assert.LessOrEqual(HexPx(".cw-hex-killtext", "top") + HexPx(".cw-hex-killtext", "height"), tenthsInkTop, "the tenths do not touch the word");
        }
    }
}
