using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Services;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Entry = CluckWars.UI.MatchStandings.Entry;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 4 post-match / in-match overlay rules: names (re-audit 16), ranking + podium order,
    /// the KO column, the safe-area insets and the stylesheet guards the rebuild relies on.
    /// </summary>
    public sealed class MatchStandingsTests
    {
        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Names ------------------------------------------------------------------------------

        [Test]
        public void LocalPlayer_IsYou_WhateverCornerTheShuffleGaveThem()
        {
            for (int corner = 0; corner < 4; corner++)
                Assert.AreEqual(UiText.Get(UiKeys.LabelYou), MatchStandings.DisplayName(true, false, ChickenClass.Speedy, corner));
        }

        [TestCase(ChickenClass.Speedy, UiKeys.LobbyBot1)]
        [TestCase(ChickenClass.Fatty, UiKeys.LobbyBot2)]
        [TestCase(ChickenClass.Assassin, UiKeys.LobbyBot3)]
        public void SoloBots_GetTheCoopsBotName_FromTheirClass_NotTheirCorner(ChickenClass cls, string key)
        {
            for (int corner = 0; corner < 4; corner++)
                Assert.AreEqual(UiText.Get(key), MatchStandings.DisplayName(false, true, cls, corner));
        }

        [Test]
        public void RemoteHuman_AndAnUnlistedBotClass_FallBackToTheCornerTag()
        {
            Assert.AreEqual(UiText.Format(UiKeys.LobbyPlayerTag, ("n", 3)), MatchStandings.DisplayName(false, false, ChickenClass.Speedy, 2));
            Assert.AreEqual(UiText.Format(UiKeys.LobbyPlayerTag, ("n", 2)), MatchStandings.DisplayName(false, true, ChickenClass.Warrior, 1));
        }

        [Test]
        public void SoloBotOrder_MatchesTheSpawnerAndTheCoop()
        {
            // The name mapping is only right while all three lists agree (bot i = class i = seat i + 1).
            CollectionAssert.AreEqual(new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin }, MatchStandings.SoloBotClasses);
            StringAssert.Contains("var botClasses = new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };",
                Read("Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs"), "MatchBootstrapper.TrySpawnBots order changed");
            StringAssert.Contains("BotClasses = { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin };",
                Read("Assets/_Game/Scripts/UI/MenuUiController.cs"), "MenuUiController.BotClasses (Coop seats) order changed");
        }

        [Test]
        public void WinBanner_SaysYouWin_ForTheLocalPlayer_AndTheNameOtherwise()
        {
            var you = new Entry(2, 40, 0, isLocal: true, isBot: false, ChickenClass.Warrior);
            var bot = new Entry(0, 30, 0, isLocal: false, isBot: true, ChickenClass.Fatty);
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchYouWin), MatchStandings.WinBanner(MatchStandings.Ranked(new[] { you, bot }, 2), 2));
            var botWon = MatchStandings.Ranked(new[] { new Entry(2, 12, 0, true, false, ChickenClass.Warrior), new Entry(0, 40, 0, false, true, ChickenClass.Fatty) }, 0);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchWins, ("name", UiText.Get(UiKeys.LobbyBot2).ToUpperInvariant())),
                MatchStandings.WinBanner(botWon, 0));
            // Banked food but no (or an unknown) named corner: MATCH ENDED, never a made-up winner.
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchEnded), MatchStandings.WinBanner(botWon, -1));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchEnded), MatchStandings.WinBanner(botWon, 3));
            StringAssert.DoesNotContain("P1", MatchStandings.WinBanner(botWon, 0), "a CPU winner is named, never 'P1 (CPU)'");
        }

        // ---- Ranking + podium ---------------------------------------------------------------------

        private static Entry E(int corner, float total, int kills = 0) =>
            new Entry(corner, total, kills, false, false, ChickenClass.Warrior);

        [Test]
        public void Ranked_IsMostFoodFirst()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 5), E(1, 40), E(2, 12), E(3, 0) }, winnerCorner: 1);
            CollectionAssert.AreEqual(new[] { 1, 2, 0, 3 }, r.Select(e => e.Corner).ToArray());
        }

        [Test]
        public void Ranked_OnATie_PutsTheDeclaredWinnerFirst_ThenTheLowerCorner()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 20), E(3, 20), E(2, 20) }, winnerCorner: 3);
            CollectionAssert.AreEqual(new[] { 3, 0, 2 }, r.Select(e => e.Corner).ToArray());
            var noWinner = MatchStandings.Ranked(new[] { E(2, 20), E(0, 20) }, winnerCorner: -1);
            CollectionAssert.AreEqual(new[] { 0, 2 }, noWinner.Select(e => e.Corner).ToArray());
        }

        [Test]
        public void Podium_IsSecondFirstThird_LeftToRight_AndTheUxmlStepsAgree()
        {
            CollectionAssert.AreEqual(new[] { 1, 0, 2 }, MatchStandings.PodiumRankBySlot);
            var root = TestAssets.Load<VisualTreeAsset>("Assets/UI/MatchOverlays.uxml").CloneTree();
            for (int slot = 0; slot < 3; slot++)
            {
                var pod = root.Q<VisualElement>($"MePod{slot}");
                Assert.IsNotNull(pod, $"#MePod{slot}");
                Assert.IsTrue(pod.ClassListContains($"cw-me-pod--{MatchStandings.PodiumRankBySlot[slot] + 1}"),
                    $"#MePod{slot} must carry the style of rank {MatchStandings.PodiumRankBySlot[slot] + 1}");
                Assert.IsTrue(pod.ClassListContains($"cw-me-pod--tier-{MatchStandings.PodiumRankBySlot[slot] + 1}"),
                    $"#MePod{slot} starts on the step height of rank {MatchStandings.PodiumRankBySlot[slot] + 1}");
                Assert.IsNotNull(root.Q<VisualElement>($"MePod{slot}Chicken"));
                Assert.IsNotNull(root.Q<Label>($"MePod{slot}Name"));
                Assert.IsNotNull(root.Q<VisualElement>($"MePod{slot}Medal"), "the step medal follows the shared place");
            }
        }

        [Test]
        public void PodiumSteps_AreTallerTheBetterThePlacing()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            int H(int place)
            {
                var m = Regex.Match(css, $@"\.cw-me-pod--tier-{place} \.cw-me-pod__block \{{ height: (\d+)px; \}}");
                Assert.IsTrue(m.Success, $"no block height for place {place}");
                return int.Parse(m.Groups[1].Value);
            }
            Assert.Greater(H(1), H(2));
            Assert.Greater(H(2), H(3));
        }

        // ---- Ties, draws and the "You · 2nd" chip (round-2 finding 6) -------------------------------

        [Test]
        public void Places_AreCompetitionRanked_EqualFoodSharesAPlace()
        {
            var r = MatchStandings.Ranked(new[] { E(0, 40), E(1, 20), E(2, 20), E(3, 5) }, winnerCorner: 0);
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 4 }, MatchStandings.Places(r));
            var top = MatchStandings.Ranked(new[] { E(0, 20), E(1, 20), E(2, 8) }, winnerCorner: 1);
            CollectionAssert.AreEqual(new[] { 1, 1, 3 }, MatchStandings.Places(top), "a tie for first: both 1st, next is 3rd");
            Assert.AreEqual(1, top[0].Corner, "the declared winner still leads the tie (the crowned centre step)");
            CollectionAssert.IsEmpty(MatchStandings.Places(new List<Entry>()));
        }

        [Test]
        public void Places_FollowTheShownScore_NotHiddenFractions()
        {
            // 20.9 and 20.1 both show "20": the board must not call one of them 2nd.
            var r = MatchStandings.Ranked(new[] { E(0, 20.1f), E(1, 20.9f) }, winnerCorner: 1);
            CollectionAssert.AreEqual(new[] { 1, 1 }, MatchStandings.Places(r));
            Assert.AreEqual(20, MatchStandings.Score(r[0]));
        }

        [Test]
        public void AllZero_IsADraw_AnythingBankedIsNot()
        {
            Assert.IsTrue(MatchStandings.IsDraw(new[] { E(0, 0), E(1, 0), E(2, 0.6f), E(3, 0) }), "0.6 food shows as 0");
            Assert.IsTrue(MatchStandings.IsDraw(new List<Entry>()));
            Assert.IsFalse(MatchStandings.IsDraw(new[] { E(0, 0), E(1, 1) }));
            Assert.AreNotEqual(UiText.Get(UiKeys.PostmatchEnded), UiText.Get(UiKeys.PostmatchDraw));
        }

        [Test]
        public void WinBanner_IsEmptyNests_ForEveryAllZeroResult_WhateverCornerWasNamed()
        {
            // The draw is decided from the standings alone: there is no flag a caller can forget (round-3 finding 2).
            string draw = UiText.Get(UiKeys.PostmatchDraw);
            var local = new Entry(2, 0, 0, true, false, ChickenClass.Warrior);
            var all = new List<Entry>
            {
                new Entry(0, 0, 0, false, true, ChickenClass.Speedy), new Entry(1, 0.6f, 0, false, true, ChickenClass.Fatty),
                new Entry(3, 0, 0, false, true, ChickenClass.Assassin), local,
            };
            foreach (int named in new[] { -1, 0, 1, 2, 3, 7 })
            {
                var r = MatchStandings.Ranked(all, named);
                Assert.AreEqual(draw, MatchStandings.WinBanner(r, named), $"all zero, corner {named} named");
                Assert.IsNull(MatchStandings.WinSubLine(r, named));
                Assert.IsNull(MatchStandings.YouPlaceChip(r, named));
            }
            Assert.AreEqual(draw, MatchStandings.WinBanner(new List<Entry>(), -1), "nobody listed at all");
            Assert.AreEqual(draw, MatchStandings.WinBanner(MatchStandings.Ranked(new[] { local }, 2), 2), "solo, nothing banked");
        }

        [Test]
        public void EveryHeadlineWriter_GoesThroughTheStandingsOnlyWinBanner()
        {
            // Source guard for the live (UI Toolkit) path that unit tests cannot drive: the overlay builds its
            // headline from MatchStandings.WinBanner(ranked, corner); the caller-supplied draw flag is gone and
            // nothing outside MatchStandings picks the MATCH ENDED / EMPTY NESTS! key.
            string ctl = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("MatchStandings.WinBanner(ranked, gm.WinnerCorner)", ctl);
            StringAssert.DoesNotContain("bool draw = false", Read("Assets/_Game/Scripts/UI/MatchStandings.cs"));
            string scripts = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets/_Game/Scripts");
            foreach (var f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (f.Replace('\\', '/').Contains("/Editor/") || name == "MatchStandings.cs" || name == "UiKeys.cs") continue;
                string src = File.ReadAllText(f);
                StringAssert.DoesNotContain("UiKeys.PostmatchEnded", src, $"{name} writes the MATCH ENDED headline outside MatchStandings.WinBanner");
                StringAssert.DoesNotContain("UiKeys.PostmatchDraw", src, $"{name} writes the EMPTY NESTS! headline outside MatchStandings.WinBanner");
            }
        }

        [Test]
        public void TiedFirst_NamesTheKoTieBreak_OnlyWhenKosActuallyDecidedIt()
        {
            string koLine = UiText.Get(UiKeys.PostmatchTiedKos), plain = UiText.Get(UiKeys.PostmatchTied);
            // Level food, winner has more knockouts (GameManager's 2nd tie-break): a visible reason.
            var ko = MatchStandings.Ranked(new[] { new Entry(0, 20, 2, false, true, ChickenClass.Speedy), new Entry(1, 20, 0, true, false, ChickenClass.Warrior) }, 0);
            Assert.AreEqual(koLine, MatchStandings.WinSubLine(ko, 0));
            // Level food AND level knockouts: GameManager fell back to the lower corner, which nobody can see.
            var corner = MatchStandings.Ranked(new[] { new Entry(0, 20, 1, false, true, ChickenClass.Speedy), new Entry(1, 20, 1, true, false, ChickenClass.Warrior) }, 0);
            Assert.AreEqual(plain, MatchStandings.WinSubLine(corner, 0));
            // Same shown food, different hidden fractions: won on a number the board does not show.
            var frac = MatchStandings.Ranked(new[] { new Entry(0, 20.9f, 0, false, true, ChickenClass.Speedy), new Entry(1, 20.1f, 3, true, false, ChickenClass.Warrior) }, 0);
            Assert.AreEqual(plain, MatchStandings.WinSubLine(frac, 0));
            // No tie: the winner's class, as before.
            var clear = MatchStandings.Ranked(new[] { new Entry(0, 30, 0, false, true, ChickenClass.Speedy), new Entry(1, 20, 0, true, false, ChickenClass.Warrior) }, 0);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchWinSub, ("cls", MatchStandings.ClassName(ChickenClass.Speedy))), MatchStandings.WinSubLine(clear, 0));
        }

        [Test]
        public void YouChip_SaysTied_WhenAnotherBirdSharesYourPlace()
        {
            var you = new Entry(2, 20, 0, true, false, ChickenClass.Warrior);
            var win = new Entry(0, 40, 0, false, true, ChickenClass.Fatty);
            var mate = new Entry(1, 20, 0, false, true, ChickenClass.Speedy);
            var r = MatchStandings.Ranked(new[] { you, win, mate }, 0);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchYouPlaceTied, ("you", UiText.Get(UiKeys.LabelYou)), ("place", MatchStandings.Ordinal(2))),
                MatchStandings.YouPlaceChip(r, 0));
        }

        [Test]
        public void YouChip_LivesOnYourOwnStep_NotUnderTheWinnersClass()
        {
            string ctl = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("v.Name.text = e.IsLocal && youChip != null ? youChip", ctl, "the chip replaces the name on your own step");
            StringAssert.Contains("chipOnPodium", ctl);
        }

        [Test]
        public void DrawLayout_HasAFourthPod_AndLevelGroundRules()
        {
            var root = TestAssets.Load<VisualTreeAsset>("Assets/UI/MatchOverlays.uxml").CloneTree();
            for (int i = 0; i < 4; i++)
            {
                Assert.IsNotNull(root.Q<VisualElement>($"MePod{i}"), $"#MePod{i}");
                Assert.IsNotNull(root.Q<VisualElement>($"MePod{i}Chicken"));
                Assert.IsNotNull(root.Q<Label>($"MePod{i}Name"));
            }
            Assert.IsTrue(root.Q<VisualElement>("MePod3").ClassListContains("cw-me-pod--hidden"), "the 4th pod is draw-only");
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            StringAssert.Contains(".cw-me-podium--draw .cw-me-pod__block", css);
            StringAssert.Contains(".cw-me-podium--draw .cw-me-pod__chicken { margin-top: 0; }", css);
            StringAssert.Contains("PopulatePodium(ranked, places,", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        [Test]
        public void GuestSeat_ShowsNoStatePill_UntilJoined()
        {
            Assert.IsFalse(WaitingRoomRules.ShowsStatePill(SessionMode.Join, seat: 0), "a guest has not joined yet");
            Assert.IsTrue(WaitingRoomRules.ShowsStatePill(SessionMode.Host, 0));
            Assert.IsTrue(WaitingRoomRules.ShowsStatePill(SessionMode.Solo, 0));
            Assert.IsTrue(WaitingRoomRules.ShowsStatePill(SessionMode.Solo, 2), "CPU seats keep READY");
        }

        [Test]
        public void RestockBanner_SaysWhatHappened()
        {
            Assert.AreEqual("PILES RESTOCKED!", UiText.Get(UiKeys.HudEventRestock));
        }

        [Test]
        public void YouChip_ShowsYourPlace_OnlyWhenSomeoneElseWon()
        {
            var you = new Entry(2, 12, 0, isLocal: true, isBot: false, ChickenClass.Warrior);
            var bot = new Entry(0, 40, 0, isLocal: false, isBot: true, ChickenClass.Fatty);
            var other = new Entry(1, 5, 0, isLocal: false, isBot: true, ChickenClass.Speedy);
            var r = MatchStandings.Ranked(new[] { you, bot, other }, winnerCorner: 0);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchYouPlace, ("you", UiText.Get(UiKeys.LabelYou)), ("place", UiText.Get(UiKeys.HudRank2))),
                MatchStandings.YouPlaceChip(r, 0));
            StringAssert.Contains("2nd", MatchStandings.YouPlaceChip(r, 0));
            Assert.IsNull(MatchStandings.YouPlaceChip(r, 2), "you won: the banner says so");
            Assert.IsNull(MatchStandings.YouPlaceChip(r, -1), "no winner: no chip");
            Assert.IsNull(MatchStandings.YouPlaceChip(new List<Entry> { bot, other }, 0), "spectating: no chip");

            // Tied with the declared winner: you share 1st.
            var tied = MatchStandings.Ranked(new[] { new Entry(2, 40, 0, true, false, ChickenClass.Warrior), bot }, winnerCorner: 0);
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchYouPlaceTied, ("you", UiText.Get(UiKeys.LabelYou)), ("place", UiText.Get(UiKeys.HudRank1))),
                MatchStandings.YouPlaceChip(tied, 0), "a shared 1st reads 'tied 1st', not a bare '1st'");
        }

        [Test]
        public void Ordinal_IsOneSharedHelper_ForTheHudAndThePostMatchChip()
        {
            Assert.AreEqual(UiText.Get(UiKeys.HudRank1), MatchStandings.Ordinal(1));
            Assert.AreEqual(UiText.Get(UiKeys.HudRank3), MatchStandings.Ordinal(3));
            Assert.AreEqual(UiText.Get(UiKeys.HudRank4), MatchStandings.Ordinal(4));
            string hud = Read("Assets/_Game/Scripts/UI/MatchHudController.cs");
            StringAssert.Contains("MatchStandings.Ordinal(", hud);
            StringAssert.DoesNotContain("UiKeys.HudRank1", hud, "the HUD keeps no private copy of the ordinals");
        }

        [Test]
        public void KoColumn_ShowsOnlyOnceSomeoneHasAKnockout()
        {
            Assert.IsFalse(MatchStandings.ShowKoColumn(new[] { E(0, 1), E(1, 2) }));
            Assert.IsFalse(MatchStandings.ShowKoColumn(new List<Entry>()));
            Assert.IsTrue(MatchStandings.ShowKoColumn(new[] { E(0, 1), E(1, 2, kills: 1) }));
            StringAssert.Contains(".cw-me-rows--no-ko .cw-me-rowstats { display: none; }", Read("Assets/UI/Styles/MatchOverlays.uss"));
        }

        [Test]
        public void GoalReached_OnlyAtOrAboveTheTarget()
        {
            Assert.IsTrue(MatchStandings.GoalReached(40, 40));
            Assert.IsFalse(MatchStandings.GoalReached(39.5f, 40));
            Assert.IsFalse(MatchStandings.GoalReached(10, 0));
        }

        // ---- Safe area ----------------------------------------------------------------------------

        [Test]
        public void SafeArea_FullScreen_PadsNothing()
        {
            Assert.AreEqual(Vector4.zero, SafeAreaPadding.Insets(new Rect(0, 0, 2400, 1080), new Vector2Int(2400, 1080), p => p));
        }

        [Test]
        public void SafeArea_Cutout_BecomesPanelUnitInsets_FlippedToTopLeftOrigin()
        {
            // 2400x1080 landscape phone: 110 px cutout on the left, 48 px gesture bar at the bottom.
            // Screen.safeArea has a bottom-left origin, so the bar is yMin = 48. Panel at half scale.
            var sa = new Rect(110, 48, 2400 - 110, 1080 - 48);
            var insets = SafeAreaPadding.Insets(sa, new Vector2Int(2400, 1080), p => p * 0.5f);
            Assert.AreEqual(55f, insets.x, 1e-3f, "left");
            Assert.AreEqual(0f, insets.y, 1e-3f, "top");
            Assert.AreEqual(0f, insets.z, 1e-3f, "right");
            Assert.AreEqual(24f, insets.w, 1e-3f, "bottom");
        }

        [Test]
        public void SafeArea_Cutout_LandscapeLeft_InsetsTheLeftEdgeOnly()
        {
            // Landscape-left (home button / gesture side on the right): the camera cutout is on the left.
            var sa = new Rect(130, 0, 2400 - 130, 1080);
            var insets = SafeAreaPadding.Insets(sa, new Vector2Int(2400, 1080), p => p * 0.5f);
            Assert.AreEqual(new Vector4(65f, 0f, 0f, 0f), insets);
        }

        [Test]
        public void SafeArea_Cutout_LandscapeRight_InsetsTheRightEdgeOnly()
        {
            // The same phone turned the other way: the cutout is now on the right, so xMax shrinks.
            var sa = new Rect(0, 0, 2400 - 130, 1080);
            var insets = SafeAreaPadding.Insets(sa, new Vector2Int(2400, 1080), p => p * 0.5f);
            Assert.AreEqual(new Vector4(0f, 0f, 65f, 0f), insets);
        }

        [Test]
        public void SafeArea_Cutout_AtTheTop_InsetsTheTopEdge_NotTheBottom()
        {
            // A notch / status bar at the top: Screen.safeArea is bottom-left origin, so it is yMax that
            // shrinks (1080 - 90), which must come out as a TOP inset in the panel's top-left space.
            var sa = new Rect(0, 0, 2400, 1080 - 90);
            var insets = SafeAreaPadding.Insets(sa, new Vector2Int(2400, 1080), p => p * 0.5f);
            Assert.AreEqual(new Vector4(0f, 45f, 0f, 0f), insets);
        }

        [Test]
        public void SafeArea_IsOneSharedRule_UsedByTheMenuHudAndTouchControls()
        {
            // Round-2 finding 15: the menu's private copy is gone; every screen that pads for the safe
            // area goes through SafeAreaPadding (finding 5: the HUD top bar and both touch clusters too).
            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("new SafeAreaPadding(_pageHost)", menu);
            StringAssert.DoesNotContain("ApplySafeArea", menu);
            StringAssert.DoesNotContain("RuntimePanelUtils.ScreenToPanel", menu);
            StringAssert.Contains("new SafeAreaPadding(_topBarRoot)", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"));
            StringAssert.Contains("new SafeAreaPadding(SafeAreaPadding.Edge.Offsets, joyZone, abilityZone)",
                Read("Assets/_Game/Scripts/Input/TouchControlsController.cs"));
            string touch = Read("Assets/UI/TouchControls.uxml");
            StringAssert.Contains("name=\"JoystickRoot\"", touch);
            StringAssert.Contains("name=\"AbilityRoot\"", touch);
        }

        // ---- Stylesheet / copy guards ---------------------------------------------------------------

        [Test]
        public void IntroDigit_HasASixPixelInkOutline_AndSitsAboveCentre()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            var digit = Regex.Match(css, @"\.cw-intro-number \{([^}]*)\}");
            Assert.IsTrue(digit.Success);
            StringAssert.Contains("-unity-text-outline-width: 6px;", digit.Groups[1].Value);
            var stack = Regex.Match(css, @"\.cw-intro-stack \{[^}]*top: -(\d+)%;");
            Assert.IsTrue(stack.Success, ".cw-intro-stack must lift the digit above centre");
            Assert.GreaterOrEqual(int.Parse(stack.Groups[1].Value), 15);
            var root = TestAssets.Load<VisualTreeAsset>("Assets/UI/MatchOverlays.uxml").CloneTree();
            Assert.AreEqual("IntroStack", root.Q<Label>("IntroNumber")?.parent?.name, "the digit lives in #IntroStack");
        }

        [Test]
        public void OverlayButtons_UseTheMenusOutlinedLabels()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            var btn = Regex.Match(css, @"\n\.cw-btn \{([^}]*)\}");
            Assert.IsTrue(btn.Success);
            StringAssert.Contains("-unity-text-outline-width: 4px;", btn.Groups[1].Value);
            StringAssert.Contains("text-shadow:", btn.Groups[1].Value);
            StringAssert.DoesNotContain("NO text-shadow", css, "the old ban comment is obsolete (both render on 6000.3)");
        }

        [Test]
        public void Overlays_ShowNoStrayDash_AndNoStaleGoal()
        {
            foreach (var path in new[] { "Assets/UI/MatchOverlays.uxml", "Assets/UI/MatchTopBar.uxml" })
            {
                string xml = Read(path);
                foreach (Match m in Regex.Matches(xml, "text=\"([^\"]*)\""))
                {
                    string text = m.Groups[1].Value;
                    string shown = UiText.IsKeyReference(text) ? UiText.Get(UiText.KeyOfReference(text)) : text;
                    StringAssert.DoesNotContain("—", shown, $"{path}: {text}");
                    StringAssert.DoesNotContain("150", shown, $"{path}: {text}");
                }
            }
            foreach (var key in new[] { UiKeys.PostmatchNoWinner, UiKeys.PostmatchWinSub, UiKeys.PostmatchTargetReached, UiKeys.PostmatchTargetTimeout })
                StringAssert.DoesNotContain("—", UiText.Get(key), key);
            StringAssert.DoesNotContain(": 150", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"));
            StringAssert.DoesNotContain(": 150", Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
        }

        [Test]
        public void NameplatesAndHudRows_UseTheSameIdentity_NotCornerNumbers()
        {
            string plate = Read("Assets/_Game/Scripts/Visuals/ChickenNameplate.cs");
            StringAssert.Contains("MatchStandings.DisplayName(isLocal, isBot, klass, corner)", plate);
            StringAssert.DoesNotContain("(CPU)", plate);
            StringAssert.DoesNotContain("$\"P{corner", plate);
            string hud = Read("Assets/_Game/Scripts/UI/MatchHudController.cs");
            StringAssert.Contains("MatchStandings.DisplayName(isLocal, isBot, cls, corner)", hud);
            StringAssert.DoesNotContain("$\"P{corner", hud);
            Assert.AreEqual("★ DashFox ★", UiText.Format(UiKeys.NameplateBounty, ("name", "DashFox")));
        }

        [Test]
        public void InMatchLobby_NamesNoDeletedPerks()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            foreach (var stale in new[] { "\"Tough\"", "\"Slippery\"", "\"Immovable\"", "\"Combo\"" })
                StringAssert.DoesNotContain(stale, src);
            // Round 2, finding 2: the waiting room's seat line is "{CLASS} · {role}" like the menu Coop, not the perk.
            StringAssert.Contains("MatchStandings.ClassRoleLine(cls)", src, "seat lines read class · role, like THE COOP");
        }
    }
}
