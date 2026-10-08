using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Localization;
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
            var bot = new Entry(0, 40, 0, isLocal: false, isBot: true, ChickenClass.Fatty);
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchYouWin), MatchStandings.WinBanner(true, you));
            Assert.AreEqual(UiText.Format(UiKeys.PostmatchWins, ("name", UiText.Get(UiKeys.LobbyBot2).ToUpperInvariant())),
                MatchStandings.WinBanner(true, bot));
            Assert.AreEqual(UiText.Get(UiKeys.PostmatchEnded), MatchStandings.WinBanner(false, default));
            StringAssert.DoesNotContain("P1", MatchStandings.WinBanner(true, bot), "a CPU winner is named, never 'P1 (CPU)'");
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
                Assert.IsNotNull(root.Q<VisualElement>($"MePod{slot}Chicken"));
                Assert.IsNotNull(root.Q<Label>($"MePod{slot}Name"));
            }
        }

        [Test]
        public void PodiumSteps_AreTallerTheBetterThePlacing()
        {
            string css = Read("Assets/UI/Styles/MatchOverlays.uss");
            int H(int place)
            {
                var m = Regex.Match(css, $@"\.cw-me-pod--{place} \.cw-me-pod__block \{{ height: (\d+)px; \}}");
                Assert.IsTrue(m.Success, $"no block height for place {place}");
                return int.Parse(m.Groups[1].Value);
            }
            Assert.Greater(H(1), H(2));
            Assert.Greater(H(2), H(3));
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
