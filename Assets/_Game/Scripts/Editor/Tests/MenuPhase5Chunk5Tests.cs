using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Localization;
using CluckWars.UI;
using NUnit.Framework;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu overhaul Phase 5 chunk 5 (round-2 findings 7, 10, 16 + leftovers): the GEAR UP doorway slot,
    /// the join-code pill and Enter-submit, the page-centred Coop bar, the centred PICK hero, the
    /// invite-code tile fit and the leaderboard's unclaimed-corner rule.
    /// </summary>
    public sealed class MenuPhase5Chunk5Tests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), rel));

        private static string Rule(string css, string selector)
        {
            var m = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(m.Success, "missing USS rule " + selector);
            return m.Groups[1].Value;
        }

        // ---- Finding 7: GEAR UP doorway bird ------------------------------------------------------

        [Test]
        public void GearDoorway_ShowsAWholeBirdOrNone()
        {
            Assert.IsFalse(GearDoorway.Fits(float.NaN), "not laid out yet");
            Assert.IsFalse(GearDoorway.Fits(0f));
            Assert.IsFalse(GearDoorway.Fits(76f), "4:3 tablet: the class cards wrap, no room");
            Assert.IsFalse(GearDoorway.Fits(GearDoorway.MinBirdHeight - 1f));
            Assert.IsTrue(GearDoorway.Fits(GearDoorway.MinBirdHeight));
            Assert.IsTrue(GearDoorway.Fits(120f), "20:9 phone");
            Assert.IsTrue(GearDoorway.Fits(250f), "16:9 desktop");
        }

        [Test]
        public void GearUp_TheDeckKeepsItsHeight_AndTheDoorwayTakesTheRest()
        {
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            StringAssert.Contains("flex-grow: 0", Rule(css, ".cw-deck"), "the deck no longer grows over the bird");
            StringAssert.Contains("flex-shrink: 0", Rule(css, ".cw-deck"));
            StringAssert.Contains("flex-grow: 1", Rule(css, ".cw-gear-doorway"));
            StringAssert.DoesNotContain("position: absolute", Rule(css, ".cw-gear-chicken"), "in flow, not laid over the deck");
            StringAssert.Contains("display: none", Rule(css, ".cw-gear-chicken--nofit"));
        }

        // ---- Finding 16: PICK YOUR BIRD hero ------------------------------------------------------

        [Test]
        public void PickHero_FigureIsCentredOnAPaintedBackdrop()
        {
            string xml = Read("Assets/UI/CharacterSelectClass.uxml");
            int stage = xml.IndexOf("name=\"HeroStage\"");
            int figure = xml.IndexOf("name=\"HeroFigure\"");
            Assert.Greater(figure, stage, "light + bale + bird grouped inside the stage");
            Assert.Greater(xml.IndexOf("name=\"PreviewChicken\""), figure);
            Assert.Greater(xml.IndexOf("name=\"PreviewDisc\""), figure);

            string stageRule = Rule(Read("Assets/UI/Styles/CluckWarsTheme.uss"), ".cw-hero__stage");
            StringAssert.Contains("justify-content: center", stageRule, "vertically centred, not sat on the bottom");
            StringAssert.Contains("Backgrounds/Bg_", stageRule, "a painted backdrop, not a flat wash");
        }

        // ---- Finding 10: THE COOP bottom bar + join -----------------------------------------------

        [Test]
        public void JoinCode_PillSaysNeedKeepTypingReady()
        {
            Assert.AreEqual(JoinCodeState.Empty, JoinCodeEntry.StateOf(null));
            Assert.AreEqual(JoinCodeState.Empty, JoinCodeEntry.StateOf("   "));
            Assert.AreEqual(JoinCodeState.Partial, JoinCodeEntry.StateOf("A"));
            Assert.AreEqual(JoinCodeState.Partial, JoinCodeEntry.StateOf(" ABC12 "));
            Assert.AreEqual(JoinCodeState.Complete, JoinCodeEntry.StateOf("ABC123"));
            Assert.AreEqual(UiKeys.LobbyStatusEnterCode, JoinCodeEntry.PillKey(JoinCodeState.Empty));
            Assert.AreEqual(UiKeys.LobbyStatusKeepTyping, JoinCodeEntry.PillKey(JoinCodeState.Partial));
            Assert.AreEqual(UiKeys.LobbyStatusCodeReady, JoinCodeEntry.PillKey(JoinCodeState.Complete));
            StringAssert.Contains("lobby.status.keepTyping,Keep typing…", Read("Assets/_Game/Resources/Text/UiText.csv"));
        }

        [Test]
        public void JoinField_EnterSubmits_OnlyWithAWholeCode()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            var m = Regex.Match(src, @"RegisterCallback<KeyDownEvent>\(evt =>(.*?)\}, TrickleDown\.TrickleDown\);", RegexOptions.Singleline);
            Assert.IsTrue(m.Success, "LobbyJoinField listens for Enter / Done");
            StringAssert.Contains("KeyCode.Return", m.Groups[1].Value);
            StringAssert.Contains("KeyCode.KeypadEnter", m.Groups[1].Value);
            StringAssert.Contains("JoinCodeEntry.IsComplete(f.value)", m.Groups[1].Value, "a half code never submits");
            StringAssert.Contains("OnStartMatch()", m.Groups[1].Value, "the same path as JOIN MATCH");
        }

        [Test]
        public void CoopBar_CentresOnThePage_InTheMenuOnly()
        {
            StringAssert.Contains("class=\"cw-coop-bottom cw-coop-bottom--centred\"", Read("Assets/UI/Lobby.uxml"));
            StringAssert.DoesNotContain("cw-coop-bottom--centred", Read("Assets/UI/MatchOverlays.uxml"),
                "the waiting room's invite card + two planks need the plain bar");
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            string left = Rule(css, ".cw-coop-bottom--centred > .cw-coop-left");
            string right = Rule(css, ".cw-coop-bottom--centred > .cw-coop-right");
            StringAssert.Contains("flex-grow: 1; flex-basis: 0", left);
            StringAssert.Contains("flex-grow: 1; flex-basis: 0", right, "equal side columns: the centre is the page centre");
        }

        // ---- Leftover: invite-code tiles ----------------------------------------------------------

        [Test]
        public void CodeTiles_ShrinkToFit_ThenWrap()
        {
            Assert.AreEqual((CodeTileFit.MaxFont, false), CodeTileFit.Fit(2000f, 6), "room to spare: full size");
            Assert.AreEqual((CodeTileFit.MaxFont, false), CodeTileFit.Fit(float.NaN, 6), "not laid out yet");

            // The offline host's 9-letter "cluck-lan" in a 560 px row: one line of smaller tiles that fit.
            var (f, wrap) = CodeTileFit.Fit(560f, 9);
            Assert.IsFalse(wrap);
            Assert.Less(f, CodeTileFit.MaxFont);
            Assert.GreaterOrEqual(f, CodeTileFit.MinFont);
            Assert.LessOrEqual(9 * CodeTileFit.TileWidth(f), 560.01f, "nine tiles fit the row");

            // The waiting room's squeezed row (~330 px on desktop): two lines of five, not tiny tiles.
            (f, wrap) = CodeTileFit.Fit(330f, 9);
            Assert.IsTrue(wrap);
            Assert.Greater(f, CodeTileFit.MinFont, "wrapping buys back size");
            Assert.LessOrEqual(f, CodeTileFit.WrapMaxFont, "two lines stay about one full line tall");
            Assert.LessOrEqual(5 * CodeTileFit.TileWidth(f), 330.01f, "five tiles fit each line");

            // A 16-letter code (the field cap) in 300 px: smallest tiles, wrapping.
            Assert.AreEqual((CodeTileFit.MinFont, true), CodeTileFit.Fit(300f, 16));
        }

        [Test]
        public void BothCodeRows_UseTheFit()
        {
            StringAssert.Contains("CodeTileFit.Fill(", Read("Assets/_Game/Scripts/UI/MenuUiController.cs"));
            StringAssert.Contains("CodeTileFit.Fill(", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        // ---- Leftover: leaderboard rows for unclaimed corners -------------------------------------

        [Test]
        public void Standings_ListOnlyCornersSomeonePlays()
        {
            Assert.IsFalse(MatchStandings.Listed(false, 0f), "unclaimed corner in a 1-3 player session");
            Assert.IsTrue(MatchStandings.Listed(true, 0f), "a chicken with nothing banked yet");
            Assert.IsTrue(MatchStandings.Listed(false, 3f), "a player who left keeps their score");
            StringAssert.Contains("MatchStandings.Listed(", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"), "HUD");
            StringAssert.Contains("MatchStandings.Listed(", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"), "post-match");
        }
    }
}
