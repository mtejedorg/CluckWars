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
    }
}
