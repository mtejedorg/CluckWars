using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP7 (PORTRAIT_AND_LANDSCAPE_SPEC 1.5): the match HUD's portrait overrides and the goal pill's move under the timer
    /// (decision C5, both orientations). Pinned as text because UI Toolkit layout needs a live panel; the Editor captures at
    /// 540x960 / 1080x2400 / 960x540 are the visual check. The zone numbers that mirror these sheets are pinned in
    /// <see cref="Phase8ChunkDRivalArrowTests"/>.
    /// </summary>
    public class MatchHudPortraitTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), rel));

        /// <summary>The body of the rule whose selector is exactly <paramref name="selector"/> (at a line start).</summary>
        private static string Block(string css, string selector)
        {
            var m = Regex.Match(css, @"^" + Regex.Escape(selector) + @"\s*\{([^}]*)\}", RegexOptions.Multiline);
            Assert.IsTrue(m.Success, $"rule '{selector}' not found");
            return m.Groups[1].Value;
        }

        private static void Has(string block, string declaration, string selector) =>
            Assert.IsTrue(Regex.IsMatch(block, @"(?<![\w-])" + Regex.Escape(declaration).Replace(@"\ ", @"\s*") + @"\s*;"),
                $"{selector} must declare '{declaration}'");

        // ---- The goal pill, both orientations --------------------------------------------------------------

        [Test]
        public void GoalPill_SitsUnderTheTimer_RightAligned_NotUnderTheBoard()
        {
            string xml = Read("Assets/UI/MatchTopBar.uxml");
            var timerCol = Regex.Match(xml, "name=\"TimerContainer\".*?</ui:VisualElement>", RegexOptions.Singleline).Value;
            StringAssert.Contains("name=\"MatchTimer\"", timerCol);
            StringAssert.Contains("name=\"WinTargetBadge\"", timerCol, "the pill is in the timer column (USS cannot reparent)");
            Assert.Less(timerCol.IndexOf("MatchTimer"), timerCol.IndexOf("WinTargetBadge"), "under the timer, not above it");
            var board = Regex.Match(xml, "name=\"LeaderboardContainer\".*?name=\"TimerContainer\"", RegexOptions.Singleline).Value;
            StringAssert.DoesNotContain("WinTargetBadge", board, "the pill no longer hangs under the board into the arena (CW-212)");

            string css = Read("Assets/UI/Styles/MatchTopBar.uss");
            string col = Block(css, ".cw-timer-container");
            Has(col, "flex-direction: column", ".cw-timer-container");
            Has(col, "align-items: flex-end", ".cw-timer-container");
            Has(Block(css, ".cw-target-badge"), "align-self: flex-end", ".cw-target-badge");
        }

        // ---- Portrait overrides (spec 1.5 table) -----------------------------------------------------------

        [Test]
        public void PortraitTopBar_BoardGrowsAndShrinks_TimerNeverDoes()
        {
            string css = Read("Assets/UI/Styles/MatchTopBar.uss");
            Has(Block(css, ".cw-topbar.layout--portrait"), "height: auto", "portrait band");

            string lb = Block(css, ".layout--portrait .cw-lb-container");
            foreach (var d in new[] { "flex-grow: 1", "flex-shrink: 1", "min-width: 0", "max-width: 640px" })
                Has(lb, d, "portrait leaderboard container");

            string timerCol = Block(css, ".layout--portrait .cw-timer-container");
            Has(timerCol, "flex-shrink: 0", "portrait timer column");
            Has(timerCol, "margin-left: 16px", "portrait timer column");

            Has(Block(css, ".layout--portrait .cw-timer"), "font-size: 44px", "portrait timer");
        }

        [Test]
        public void PortraitBoardRows_NameAndBarShrink_TheScoreNever()
        {
            string css = Read("Assets/UI/Styles/MatchTopBar.uss");
            Has(Block(css, ".layout--portrait .cw-lb-row"), "height: 52px", "portrait row");

            string name = Block(css, ".layout--portrait .cw-lb-name");
            foreach (var d in new[] { "flex-grow: 1", "flex-shrink: 1", "min-width: 0" }) Has(name, d, "portrait name");
            string baseName = Block(css, ".cw-lb-name");
            Has(baseName, "text-overflow: ellipsis", ".cw-lb-name (the shrunk name ellipsizes)");
            Has(baseName, "overflow: hidden", ".cw-lb-name");

            string bar = Block(css, ".layout--portrait .cw-lb-bar-trough");
            foreach (var d in new[] { "flex-basis: 72px", "flex-shrink: 1", "min-width: 40px" }) Has(bar, d, "portrait bar");

            string score = Block(css, ".layout--portrait .cw-lb-score");
            Has(score, "width: 64px", "portrait score");
            Has(score, "flex-shrink: 0", "portrait score: the score column is never shrunk");
            Has(Block(css, ".layout--portrait .cw-lb-ord"), "flex-shrink: 0", "portrait ordinal");
        }

        [Test]
        public void PortraitHudType_StaysAtOrAboveTheHudFloor()
        {
            // U4 of spec 3.5: HUD text >= 28 px. Every font-size in a portrait override of the match HUD sheets.
            foreach (var file in new[] { "Assets/UI/Styles/MatchTopBar.uss", "Assets/UI/Styles/TouchControls.uss" })
            {
                string css = Read(file);
                foreach (Match rule in Regex.Matches(css, @"\.layout--portrait[^{]*\{([^}]*)\}"))
                foreach (Match fs in Regex.Matches(rule.Groups[1].Value, @"font-size:\s*([\d.]+)px"))
                    Assert.GreaterOrEqual(float.Parse(fs.Groups[1].Value, CultureInfo.InvariantCulture), 28f, $"{file}: {rule.Value.Trim()}");
            }
        }

        [Test]
        public void PortraitStatusStrip_IsTheJoysticksWidth()
        {
            string css = Read("Assets/UI/Styles/TouchControls.uss");
            Has(Block(css, ".layout--portrait .cw-status-strip"), "width: 280px", "portrait status strip");
            Has(Block(css, ".layout--portrait .cw-status-value"), "min-width: 72px", "portrait status value");
        }

        [Test]
        public void PortraitEventBanner_SitsAboveTheBottomBand_OnItsOwnShapeClasses()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            Has(Block(css, ".cw-eventbanner.layout--portrait"), "bottom: 600px", "portrait banner");
            Has(Block(css, ".cw-eventbanner.layout--portrait .cw-eventbanner__plaque"), "max-width: 90%", "portrait banner plaque");
            // The classes go on #EventBanner only (measured on #OverlayRoot), so the GET READY plate never sees them.
            StringAssert.Contains("LayoutClasses.Bind(overlayHost, _eventBanner);", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        [Test]
        public void PortraitEventBanner_ClearsTheHexArc_DerivedFromTouchControlsUss()
        {
            // The 600 px comes from "the hexes top out at ~531 px": re-derive it from the hex rules so a re-layout of the arc
            // fails here instead of putting the banner over a button.
            string touch = Read("Assets/UI/Styles/TouchControls.uss");
            float hex = float.Parse(Regex.Match(touch, @"^\.cw-hex\s*\{[^}]*height:\s*(\d+)px", RegexOptions.Multiline).Groups[1].Value,
                CultureInfo.InvariantCulture);
            float top = 0f;
            var rules = Regex.Matches(touch, @"^\.cw-hex--a\d\s*\{\s*right:\s*\d+px;\s*bottom:\s*(\d+)px;", RegexOptions.Multiline);
            Assert.AreEqual(4, rules.Count, "four .cw-hex--aN rules");
            foreach (Match m in rules)
                top = Mathf.Max(top, float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + hex + CluckWars.Visuals.HudControlZones.ControlPadPx);
            Assert.AreEqual(531f, top, "the arc's top (hex + its chain ring) the banner rule was derived from");

            string overlays = Read("Assets/UI/Styles/MatchOverlays.uss");
            float banner = float.Parse(Regex.Match(Block(overlays, ".cw-eventbanner.layout--portrait"), @"bottom:\s*(\d+)px").Groups[1].Value,
                CultureInfo.InvariantCulture);
            // The hexes sit inside the safe area and the banner does not: allow the spec's simulated gesture bar (2 % of a
            // 9:20 panel, 1724 px high: UI Toolkit's linear scale blend, WP8) plus the 8 px U1 gap.
            float gestureBar = 0.02f * 1724f;
            Assert.GreaterOrEqual(banner, top + gestureBar + 8f, "the portrait banner must clear the hex arc");
        }

        // ---- Landscape is unchanged apart from the goal pill --------------------------------------------------

        [Test]
        public void Landscape_BaseRules_AreTheShippedLook()
        {
            string top = Read("Assets/UI/Styles/MatchTopBar.uss");
            Has(Block(top, ".cw-topbar"), "height: 330px", ".cw-topbar");
            Has(Block(top, ".cw-lb-row"), "height: 64px", ".cw-lb-row");
            Has(Block(top, ".cw-lb-name"), "width: 150px", ".cw-lb-name");
            Has(Block(top, ".cw-lb-bar-trough"), "width: 120px", ".cw-lb-bar-trough");
            Has(Block(top, ".cw-lb-score"), "width: 76px", ".cw-lb-score");
            StringAssert.Contains("font-size: 52px", Block(top, ".cw-timer"));

            string touch = Read("Assets/UI/Styles/TouchControls.uss");
            Has(Block(touch, ".cw-status-strip"), "width: 380px", ".cw-status-strip");
            Has(Block(touch, ".cw-status-value"), "min-width: 110px", ".cw-status-value");

            string overlays = Read("Assets/UI/Styles/MatchOverlays.uss");
            Has(Block(overlays, ".cw-eventbanner"), "bottom: 150px", ".cw-eventbanner");
        }
    }
}
