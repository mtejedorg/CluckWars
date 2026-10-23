using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP9 (PORTRAIT_AND_LANDSCAPE_SPEC 1.2-1.4, 1.6; DECISIONS_2026-10-21 C7/C8): every menu page and match overlay has real
    /// portrait rules, and the landscape gutters clear the UiProbe U2 floor (adjacent targets &gt;= 19 px). Pure text tests on
    /// the shipped USS / UXML, plus the width budgets on the narrowest portrait panel (9:21, 746 px wide) worked from those
    /// numbers, so a later edit that breaks a budget fails here before a capture run.
    /// </summary>
    public sealed class MenuPortraitLayoutTests
    {
        /// <summary>The narrowest portrait panel the menus must fit (9:21 phone; spec 0 table).</summary>
        private const float NarrowestPortraitPx = 746f;
        /// <summary>UiLayoutRules.TouchTargetMinGapPx (8 dp).</summary>
        private const float MinGapPx = 19f;

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        private static string Theme => Read("Assets/UI/Styles/CluckWarsTheme.uss");
        private static string Overlays => Read("Assets/UI/Styles/MatchOverlays.uss");

        /// <summary>The body of the rule whose selector is exactly <paramref name="selector"/> at the start of a line.</summary>
        private static string Block(string css, string selector)
        {
            var m = Regex.Match(css, @"^" + Regex.Escape(selector) + @"\s*\{([^}]*)\}", RegexOptions.Multiline);
            Assert.IsTrue(m.Success, "missing USS rule " + selector);
            return Regex.Replace(m.Groups[1].Value, @"\s+", " ");
        }

        private static float Px(string block, string property)
        {
            var m = Regex.Match(block, @"(?:^|[ ;])" + Regex.Escape(property) + @":\s*(-?[\d.]+)px");
            Assert.IsTrue(m.Success, $"no {property} in {{{block}}}");
            return float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static void Has(string block, string decl, string what) => StringAssert.Contains(decl, block, what);

        // ---- Landscape: gutters between adjacent targets (pure margin tweaks, U2) ---------------------------------------

        [Test]
        public void Landscape_TargetGutters_ClearTheU2Floor()
        {
            string css = Theme;
            var tile = Block(css, ".cw-class-tile");
            Assert.GreaterOrEqual(Px(tile, "margin-left") + Px(tile, "margin-right"), MinGapPx, "class tiles (were 14 px apart)");
            Assert.GreaterOrEqual(Px(tile, "margin-top") + Px(tile, "margin-bottom"), MinGapPx, "class tiles, 2x2 rows");
            Assert.GreaterOrEqual(Px(Block(css, ".cw-perk-badge"), "margin-right"), MinGapPx, "perk badges (were 12 px apart)");
            var card = Block(css, ".cw-ability-card");
            Assert.GreaterOrEqual(Px(card, "margin-left") + Px(card, "margin-right"), MinGapPx, "deck cards (were 10 px apart)");
            var slot = Block(css, ".cw-slot-box");
            Assert.GreaterOrEqual(Px(slot, "margin-left") + Px(slot, "margin-right"), MinGapPx, "slot boxes (were 16 px apart)");
            Assert.GreaterOrEqual(Px(Block(css, ".cw-lobby-btnrow .cw-mini-btn"), "margin-left"), MinGapPx, "SHARE / COPY (were 10 px apart)");
            Assert.GreaterOrEqual(Px(Block(css, ".layout--short .cw-menu-secondary-row"), "margin-top"), 16f,
                "PLAY SOLO -> HOST / JOIN on a short landscape panel (14 measured an 18 px gap)");
            Assert.GreaterOrEqual(Px(Block(css, ".cw-slot-box__empty-label"), "margin-left"), 8f, "EMPTY clear of the slot-number badge (U1)");
        }

        [Test]
        public void Landscape_ResultsActionPadding_ActuallyApplies()
        {
            // .cw-me-action's 20 px padding lost to the later .cw-btn (same specificity): BACK TO LOBBY 288 px in a 286 px box.
            var rule = Block(Overlays, ".cw-me-actions .cw-me-action");
            Assert.AreEqual(20f, Px(rule, "padding-left"));
            Assert.AreEqual(20f, Px(rule, "padding-right"));
            StringAssert.DoesNotContain("padding-left", Block(Overlays, ".cw-me-action"), "the dead single-class padding is gone");
        }

        // ---- Shared header -------------------------------------------------------------------------------------------

        [Test]
        public void Portrait_Header_CentresTheRibbonBesideHome()
        {
            string css = Theme;
            var header = Block(css, ".layout--portrait .cw-page-header");
            var home = Block(css, ".layout--portrait .cw-page-header .cw-home-btn");
            Assert.GreaterOrEqual(Px(header, "padding-left"), Px(home, "left") + Px(home, "min-width"), "the ribbon starts right of HOME");
            // "PICK YOUR BIRD" (the longest ribbon) measured 337.6 px at 44 px / 2 tracking (WP9 live run, U3 at 776): ~309.6 px of
            // glyphs over 14 gaps. At the portrait 40 px / 1 tracking it must fit the ribbon's content box at 9:21.
            var title = Block(css, ".layout--portrait .cw-page-header .cw-ribbon .cw-title");
            float size = Px(title, "font-size"), track = Px(title, "letter-spacing");
            Assert.GreaterOrEqual(size, 30f, "U4 menu floor");
            float text = (337.6f - 14f * 2f) * size / 44f + 14f * track;
            var ribbon = Block(css, ".layout--portrait .cw-page-header .cw-ribbon");
            float room = NarrowestPortraitPx - Px(header, "padding-left") - Px(header, "padding-right")
                         - Px(ribbon, "padding-left") - Px(ribbon, "padding-right");
            Assert.LessOrEqual(text + 1.5f, room, "the ribbon title fits at 9:21 (746 px)");
            Assert.GreaterOrEqual(Px(ribbon, "padding-left"), 109f, "the text clears the 109 px ribbon tails");
        }

        // ---- Main menu (spec 1.2, C8) -----------------------------------------------------------------------------------

        [Test]
        public void Portrait_MainMenu_TitleCastAndActions()
        {
            string css = Theme;
            var page = Block(css, ".layout--portrait .cw-menu");
            Assert.GreaterOrEqual(Px(page, "padding-top"), 10f + 124f, "content starts below the 124 px gear (top 10)");
            Has(Block(css, ".layout--portrait .cw-menu-title"), "font-size: 96px", "title (128 px measured 820 > 767)");
            // 820 px at 128 px / 8 tracking -> ~740 px of glyphs; at 96 px / 4 tracking ~595 px, inside the column.
            Assert.LessOrEqual(740f * 96f / 128f + 10f * 4f, NarrowestPortraitPx - 2f * Px(page, "padding-left"), "title fits 9:21");

            Has(Block(css, ".layout--portrait .cw-menu-cast"), "flex-wrap: wrap", "cast 2x2 (C8)");
            Has(Block(css, ".layout--portrait .cw-menu-cast__item"), "width: 50%", "two per row");
            Has(Block(css, ".layout--portrait .cw-menu-cast__item"), "margin-left: 0", "no 26 px landscape side margins");

            Has(Block(css, ".layout--portrait .cw-mode-row"), "flex-direction: column", "HOST / JOIN stack");
            var secondary = Block(css, ".layout--portrait .cw-menu-secondary");
            Assert.GreaterOrEqual(Px(secondary, "margin-top") + Px(secondary, "margin-bottom"), MinGapPx, "HOST -> JOIN (was 2.9 px)");
            Assert.GreaterOrEqual(Px(Block(css, ".layout--portrait .cw-menu-secondary-row"), "margin-top") + Px(secondary, "margin-top"),
                MinGapPx, "PLAY SOLO -> HOST");
            Assert.GreaterOrEqual(Px(secondary, "min-height"), 124f, "HOST measured 115 px at a 116 floor");
            Assert.GreaterOrEqual(Px(Block(css, ".layout--portrait .cw-menu-primary"), "min-height"), 124f);
            Assert.GreaterOrEqual(Px(Block(css, ".layout--portrait .cw-menu .cw-menu-primary.cw-menu-primary--demoted"), "min-height"), 124f,
                "the demoted PLAY SOLO outranks the portrait primary rule, so it needs its own floor");
        }

        [Test]
        public void Portrait_Sheets_FitTheColumn()
        {
            string css = Theme;
            Has(Block(css, ".layout--portrait .cw-sheet"), "align-self: stretch", "settings sheet full width");
            var close = Block(css, ".layout--portrait .cw-sheet__close");
            Has(close, "position: relative", "CLOSE beside the ribbon, not over it");
            Assert.GreaterOrEqual(Px(close, "min-height"), 124f);

            var dc = Block(css, ".layout--portrait .cw-dc-panel");
            Has(dc, "align-self: stretch", "disconnect panel full width");
            var dcBtn = Block(css, ".layout--portrait .cw-dc-btn");
            Assert.GreaterOrEqual(Px(dcBtn, "margin-top") + Px(dcBtn, "margin-bottom"), MinGapPx, "REJOIN -> BACK (was 17 px)");
            // "YOU WERE DISCONNECTED": 633.5 px at 52 px / 3 tracking -> ~573 px of glyphs; at 38 / 1 ~439 px.
            float title = 573.5f * 38f / 52f + 20f * 1f;
            var ribbon = Block(css, ".layout--portrait .cw-dc-panel .cw-ribbon");
            float inner = NarrowestPortraitPx - 2f * Px(dc, "margin-left") - 2f * Px(dc, "padding-left");
            Assert.LessOrEqual(title + 2f * Px(ribbon, "padding-left"), inner, "the headline fits its ribbon at 9:21");
        }

        // ---- PICK YOUR BIRD (spec 1.3) ------------------------------------------------------------------------------------

        [Test]
        public void Portrait_ClassPick_FourTilesFit_AndTheHeroIsAColumn()
        {
            string css = Theme;
            var body = Block(css, ".layout--portrait .cw-pick-body");
            Has(body, "flex-direction: column", "tiles above the hero");
            var tile = Block(css, ".cw-class-tile");
            var pTile = Block(css, ".layout--portrait .cw-class-tile");
            float tileW = (NarrowestPortraitPx - 2f * Px(body, "padding-left") - 4f * (Px(tile, "margin-left") + Px(tile, "margin-right"))) / 4f;
            float inside = tileW - 2f * 5f /* border */ - 2f * Px(pTile, "padding-left");
            // Widest texts (WP9 probe, 776 px run): "ASSASSIN" 165 px at 36 px / 1 tracking -> ~140 at 32 / 0; "Hit & Run" 138 at 30 px.
            Assert.GreaterOrEqual(inside, 140f + 1.5f, "the tile name fits");
            Assert.GreaterOrEqual(inside, 138f + 1.5f, "the tile role fits");
            Has(Block(css, ".layout--portrait .cw-class-tile__name"), "font-size: 32px", "tile name");

            Has(Block(css, ".layout--portrait .cw-hero"), "flex-direction: column", "stage above the text");
            Has(Block(css, ".layout--portrait .cw-hero__stage"), "flex-grow: 1", "the stage takes what the text leaves");
            Has(Block(css, ".layout--portrait .cw-hero__chicken"), "height: 79%", "the figure scales with the stage");
            Has(Block(css, ".layout--portrait .cw-hero__text"), "flex-grow: 0", "the text keeps its natural height");
        }

        // ---- GEAR UP (spec 1.3, C7) -----------------------------------------------------------------------------------

        [Test]
        public void Portrait_GearUp_OnlyTheDeckScrolls_AndThreeCardsFitARow()
        {
            string xml = Read("Assets/UI/CharacterSelect.uxml");
            int scroll = xml.IndexOf("name=\"DeckScroll\"", System.StringComparison.Ordinal);
            Assert.Greater(scroll, 0, "the deck sits in a ScrollView");
            Assert.Less(scroll, xml.IndexOf("name=\"Deck\"", System.StringComparison.Ordinal));
            Assert.Greater(xml.IndexOf("name=\"BottomBar\"", System.StringComparison.Ordinal), xml.IndexOf("</ui:ScrollView>", System.StringComparison.Ordinal),
                "READY is outside the scrolling region");
            Assert.AreEqual(1, Regex.Matches(xml, "<ui:ScrollView").Count, "no other region scrolls");

            string css = Theme;
            var land = Block(css, ".cw-deck-scroll");
            Has(land, "flex-grow: 0", "landscape: exactly the deck's height");
            Has(land, "flex-shrink: 0", "landscape: never scrolls");
            Has(Block(css, ".layout--portrait .cw-deck-scroll"), "flex-shrink: 1", "portrait: shrinks to the room left");
            Has(Block(css, ".layout--portrait .cw-gear-doorway"), "flex-basis: 0", "the bird only gets leftover height");
            Has(Block(css, ".layout--portrait .cw-gear-bottom"), "flex-direction: column", "details over READY");
            Assert.GreaterOrEqual(Px(Block(css, ".layout--portrait .cw-gear-ready"), "min-height"), 124f);

            var slot = Block(css, ".layout--portrait .cw-slot-box");
            Assert.GreaterOrEqual(Px(slot, "margin-top") + Px(slot, "margin-bottom"), 44f, "room for the NEXT caret between the 2x2 rows");

            var gearBody = Block(css, ".layout--portrait .cw-gear-body");
            var group = Block(css, ".layout--portrait .cw-deck-group");
            var card = Block(css, ".layout--portrait .cw-ability-card");
            var baseCard = Block(css, ".cw-ability-card");
            float inner = NarrowestPortraitPx - 2f * Px(gearBody, "padding-left") - 2f * Px(group, "padding-left");
            float cardW = inner / 3f - (Px(baseCard, "margin-left") + Px(baseCard, "margin-right"));
            float content = cardW - 2f * 6f /* border */ - 2f * Px(card, "padding-left");
            // DOPPELGANGER is ~208 px at 30 px; MenuUiController.FitCardName steps it to 28 px: ~194.
            Assert.GreaterOrEqual(content, 194f, "the longest card name fits three-per-row at 9:21");
            Assert.GreaterOrEqual(Px(card, "margin-top") + Px(card, "margin-bottom"), MinGapPx, "wrapped card rows");
            Assert.AreEqual(-Px(card, "padding-left"), Px(Block(css, ".layout--portrait .cw-ability-card__band"), "margin-left"),
                "the category band stays flush with the card edge");
        }

        // ---- THE COOP + waiting room (spec 1.4) -------------------------------------------------------------------------

        [Test]
        public void Portrait_Coop_SeatsTwoByTwo_AndTheBottomBarStacks()
        {
            string css = Theme;
            Has(Block(css, ".layout--portrait .cw-lineup"), "flex-wrap: wrap", "seats 2x2");
            var seat = Block(css, ".layout--portrait .cw-seat");
            Has(seat, "flex-basis: 40%", "two per row");
            Assert.GreaterOrEqual(Px(seat, "margin-top") + Px(seat, "margin-bottom"), MinGapPx, "seat rows");
            Has(Block(css, ".layout--portrait .cw-coop-bottom"), "flex-direction: column", "pill, centre, START stacked");
            Has(Block(css, ".layout--portrait .cw-coop-bottom > .cw-coop-center"), "min-width: 0", "the 480 px centre floor is dropped");
            Has(Block(css, ".layout--portrait .cw-coop-right .cw-page-cta"), "min-width: 0", "START full width");

            string wr = Overlays;
            Assert.GreaterOrEqual(Px(Block(wr, "#LobbyOverlay.layout--portrait .cw-wr-changebird"), "margin-right"), MinGapPx, "side by side on 9:16");
            Has(Block(wr, "#LobbyOverlay.layout--tall .cw-wr-actions"), "flex-direction: column", "CHANGE BIRD over START on a tall phone");
            Assert.GreaterOrEqual(Px(Block(wr, "#LobbyOverlay.layout--tall .cw-wr-changebird"), "margin-bottom"), MinGapPx);
            Assert.GreaterOrEqual(Px(Block(wr, "#LobbyOverlay.layout--portrait .cw-lobby-btnrow .cw-mini-btn"), "margin-left"), MinGapPx);
        }

        // ---- Results, sheets, GET READY (spec 1.6) ------------------------------------------------------------------------

        [Test]
        public void Portrait_Results_AreAColumn_AndTheBoardHasNoLandscapeFloor()
        {
            string css = Overlays;
            Has(Block(css, ".layout--portrait .cw-me-panel"), "flex-direction: column", "hero over the board");
            var board = Block(css, ".layout--portrait .cw-me-right");
            Has(board, "min-width: 0", "the 800 px board floor crushed the podium");
            Has(board, "max-width: none", "full width");
            Assert.GreaterOrEqual(Px(Block(css, ".layout--portrait .cw-me-actions .cw-me-action"), "min-height"), 124f, "PLAY AGAIN / BACK measured 115");
            Has(Block(css, ".layout--portrait .cw-leave-actions"), "flex-direction: column", "KEEP PLAYING over LEAVE");
            Has(Block(css, ".layout--portrait .cw-se-panel"), "align-self: stretch", "session-end panel full width");
            Has(Block(css, ".layout--portrait .cw-se-panel .cw-ribbon"), "min-width: 0", "the 640 px ribbon floor is dropped");
        }

        [Test]
        public void GetReady_PortraitPlateFits_AndTheShortRuleStaysOnTheMenuCard()
        {
            string css = Read("Assets/UI/Styles/GetReadyCard.uss");
            StringAssert.Contains(".layout--short #GetReadyCard .cw-getready__plate", css,
                "the intro card carries the shape classes since WP9; landscape keeps its full size");
            StringAssert.DoesNotContain("\n.layout--short .cw-getready__plate", css);
            var plate = Block(css, ".layout--portrait .cw-getready__plate");
            // "GET READY" measured 560 px at 104 px / 8 tracking -> ~488 px of glyphs; at 72 px / 4 ~374 px.
            float label = 488f * 72f / 104f + 9f * 4f;
            Assert.LessOrEqual(label + Px(plate, "padding-left") + Px(plate, "padding-right"), NarrowestPortraitPx);
            Has(Block(css, ".layout--portrait .cw-getready__label"), "font-size: 72px", "portrait GET READY");
        }

        [Test]
        public void Overlays_GetTheShapeClasses_FromTheAlwaysVisibleRoot()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("LayoutClasses.BindAll(_root, _lobbyOverlay, _matchEndOverlay, _leaveSheet, _sessionEndOverlay, _introGetReady);", src);
            StringAssert.Contains("LayoutClasses.Bind(overlayHost, _eventBanner);", src, "the event banner keeps its own measured element");
            StringAssert.Contains("deckScroll.ScrollTo(ve)", Read("Assets/_Game/Scripts/UI/MenuUiController.cs"),
                "pad / keyboard focus brings a card below the fold into view");
        }
    }
}
