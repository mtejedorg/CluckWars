using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.Services;
using CluckWars.UI;
using ShutdownReason = Fusion.ShutdownReason;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Re-audit round 2, Phase 5 chunk 2: findings 2 (in-match waiting room), 12 (session end), 14 (copy
    /// leftovers) and decision 3 (solo PLAY AGAIN goes straight to GET READY). Pure rules
    /// (<see cref="WaitingRoomRules"/>, <see cref="SessionEndCopy"/>, <see cref="MatchFlowRules"/>) plus source /
    /// stylesheet guards for the wiring that needs a runner.
    /// </summary>
    public sealed class WaitingRoomAndSessionEndTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Decision 3: solo PLAY AGAIN -------------------------------------------------------------

        [Test]
        public void PlayAgain_Solo_GoesStraightToGetReady_MultiplayerKeepsTheWaitingRoom()
        {
            Assert.AreEqual(MatchState.Starting, MatchFlowRules.StateAfterPlayAgain(solo: true));
            Assert.AreEqual(MatchState.WaitingForPlayers, MatchFlowRules.StateAfterPlayAgain(solo: false));
            Assert.AreNotEqual(MatchState.Active, MatchFlowRules.StateAfterPlayAgain(true), "every round still settles + counts down");
        }

        [Test]
        public void GameManager_RequestPlayAgain_BeginsStartingForSolo()
        {
            string gm = Read("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            int start = gm.IndexOf("public void RequestPlayAgain()", StringComparison.Ordinal);
            int end = gm.IndexOf("private static WorldDirt ScanWorldDirt()", StringComparison.Ordinal);
            Assert.Greater(start, 0); Assert.Greater(end, start);
            string body = gm.Substring(start, end - start);
            StringAssert.Contains("MatchFlowRules.CanPlayAgain(HasStateAuthority, State)", body, "authority + ended round only");
            StringAssert.Contains("GameMode.Single", body);
            StringAssert.Contains("MatchFlowRules.StateAfterPlayAgain(solo)", body);
            StringAssert.Contains("BeginStarting(", body, "solo goes through the same GET READY settle as every round");
            Assert.Less(body.IndexOf("ResetWorldForNewRound();", StringComparison.Ordinal),
                body.IndexOf("BeginStarting(", StringComparison.Ordinal), "the world resets before GET READY");
        }

        // ---- Finding 2: the waiting room -------------------------------------------------------------

        [Test]
        public void ReadyCount_CountsSeatsThatShowReady_NotConnectedPlayers()
        {
            Assert.AreEqual(4, WaitingRoomRules.ReadyCount(new[] { true, true, true, true }), "four READY cards read 4/4, never 1/4");
            Assert.AreEqual(2, WaitingRoomRules.ReadyCount(new[] { true, false, true, false }));
            Assert.AreEqual(0, WaitingRoomRules.ReadyCount(new bool[4]));
        }

        [Test]
        public void HostTag_OnlyOnTheMastersHumanSeat_AndNeverInSolo()
        {
            Assert.IsTrue(WaitingRoomRules.ShowHostTag(solo: false, seatIsMaster: true, seatIsBot: false));
            Assert.IsFalse(WaitingRoomRules.ShowHostTag(solo: true, seatIsMaster: true, seatIsBot: false), "no HOST in solo");
            Assert.IsFalse(WaitingRoomRules.ShowHostTag(false, false, false), "a guest is not the host");
            Assert.IsFalse(WaitingRoomRules.ShowHostTag(false, true, true), "a CPU seat says CPU");
        }

        [Test]
        public void SeatLine_IsClassAndRole_LikeTheMenuCoop()
        {
            foreach (ChickenClass cls in Enum.GetValues(typeof(ChickenClass)))
            {
                string line = MatchStandings.ClassRoleLine(cls);
                StringAssert.StartsWith(MatchStandings.ClassName(cls), line);
                StringAssert.EndsWith(MatchStandings.RoleName(cls), line);
                StringAssert.DoesNotContain("{", line);
            }
            Assert.AreEqual(UiText.Get(UiKeys.RoleFatty), MatchStandings.RoleName(ChickenClass.Fatty));
        }

        [Test]
        public void WaitingRoom_IsBuiltOnTheCoopTheme_ScopedToItsOwnOverlay()
        {
            string xml = Read("Assets/UI/MatchOverlays.uxml");
            const string theme = "Styles/CluckWarsTheme.uss";
            Assert.AreEqual(1, Regex.Matches(xml, Regex.Escape(theme)).Count, "the menu theme is attached once");
            int room = xml.IndexOf("name=\"LobbyOverlay\"", StringComparison.Ordinal);
            int session = xml.IndexOf("name=\"SessionEndOverlay\"", StringComparison.Ordinal);
            int style = xml.IndexOf(theme, StringComparison.Ordinal);
            Assert.Greater(style, room, "inside #LobbyOverlay, so the post-match screen keeps MatchOverlays.uss alone");
            Assert.Less(style, session);
            string block = xml.Substring(room, session - room);
            foreach (var needle in new[] { "name=\"LobbyLeaveBtn\"", "name=\"LobbyChangeBirdBtn\"", "name=\"LobbyStartBtn\"",
                         "name=\"LobbySafe\"", "class=\"cw-lineup\"", "class=\"cw-rule-chips\"", "cw-btn cw-btn--green" })
                StringAssert.Contains(needle, block);

            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("\"cw-seat\"", src, "seats are THE COOP's .cw-seat cards");
            StringAssert.DoesNotContain("cw-player-card", src, "the old dark card is gone");
            StringAssert.DoesNotContain("cw-player-card", Read("Assets/UI/Styles/MatchOverlays.uss"));
        }

        [Test]
        public void ReadyPill_ClearsFourAndAHalfToOne()
        {
            // Was cream on #33a332 green, 3.0:1. The Coop's READY badge is ink on gold.
            string css = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            Color bg = RuleColor(css, @"\.cw-seat__state \{", "background-color");
            Color fg = RuleColor(css, @"\.cw-seat__state-label \{", "color");
            float ratio = AbilityPalette.ContrastRatio(bg, fg);
            Assert.GreaterOrEqual(ratio, 4.5f, $"READY label {fg} on {bg} is {ratio:0.0}:1");
        }

        private static Color RuleColor(string css, string selector, string property)
        {
            var rule = Regex.Match(css, selector + @"([^}]*)\}");
            Assert.IsTrue(rule.Success, $"no rule {selector}");
            var m = Regex.Match(rule.Groups[1].Value, @"(?:^|[\s;])" + Regex.Escape(property) + @":\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)");
            Assert.IsTrue(m.Success, $"{selector} has no rgb() {property}");
            return new Color32(byte.Parse(m.Groups[1].Value), byte.Parse(m.Groups[2].Value), byte.Parse(m.Groups[3].Value), 255);
        }

        [Test]
        public void BackPress_LeavesOnlyWhileTheWaitingRoomIsShown()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            int refresh = src.IndexOf("private void RefreshLobby(", StringComparison.Ordinal);
            int hidden = src.IndexOf("if (!show) return;", refresh, StringComparison.Ordinal);
            int back = src.IndexOf("_input.GetBackPressed()", StringComparison.Ordinal);
            Assert.Greater(refresh, 0);
            Assert.AreEqual(1, Regex.Matches(src, @"GetBackPressed\(\)").Count, "one poll, in the waiting room");
            Assert.Greater(back, hidden, "polled after the room-hidden early-out");
            StringAssert.Contains("OnLobbyLeave()", src.Substring(back, 200));
        }

        [Test]
        public void ChangeBird_LandsOnPickYourBird_AndWinsOverBackToLobby()
        {
            var i = MatchFlowRules.DecidePostMatchIntent(openLobbyFlag: false, changeBirdFlag: true, hasValidLastSetup: true, SessionMode.Host);
            Assert.AreEqual(MenuLanding.ClassSelect, i.Landing);
            Assert.IsTrue(i.ClearFlag);
            i = MatchFlowRules.DecidePostMatchIntent(true, true, false, SessionMode.Join);
            Assert.AreEqual(MenuLanding.ClassSelect, i.Landing, "even with no valid last setup: the selection is still held");
            Assert.IsTrue(i.ClearFlag);
            i = MatchFlowRules.DecidePostMatchIntent(true, false, true, SessionMode.Host);
            Assert.AreEqual(MenuLanding.Lobby, i.Landing, "LEAVE keeps BACK TO LOBBY's landing");
            i = MatchFlowRules.DecidePostMatchIntent(false, false, true, SessionMode.Host);
            Assert.AreEqual(MenuLanding.MainMenu, i.Landing);
            Assert.IsFalse(i.ClearFlag);
            Assert.IsFalse(new SessionSelectionService().OpenClassSelectOnMenuLoad, "starts off");
        }

        // ---- Finding 12: session end -----------------------------------------------------------------

        [Test]
        public void SessionEnd_EveryShutdownReason_ReadsInVoice_NeverItsEnumName()
        {
            var names = Enum.GetNames(typeof(ShutdownReason));
            foreach (ShutdownReason reason in Enum.GetValues(typeof(ShutdownReason)))
            {
                string key = SessionEndCopy.ReasonKey(reason);
                Assert.IsTrue(UiText.HasKey(key), $"{reason}: key {key} is not in UiText.csv");
                string line = SessionEndCopy.For(reason);
                Assert.IsNotEmpty(line, reason.ToString());
                StringAssert.DoesNotContain("{", line, reason.ToString());
                foreach (var n in names)   // whole words: "Ok" must not trip on "spooked"
                    Assert.IsFalse(Regex.IsMatch(line, $@"\b{n}\b", RegexOptions.IgnoreCase), $"{reason}: \"{line}\" shows the raw name {n}");
            }
        }

        [Test]
        public void SessionEnd_HostLeftAndLostSignal_HaveTheirOwnLines()
        {
            Assert.AreEqual(UiText.Get(UiKeys.SessionHostLeft), SessionEndCopy.For(ShutdownReason.HostMigration));
            Assert.AreEqual("The host flew the coop.", SessionEndCopy.For(ShutdownReason.HostMigration));
            Assert.AreEqual("Lost the signal to the barn.", SessionEndCopy.For(ShutdownReason.PhotonCloudTimeout));
            Assert.AreEqual(SessionEndCopy.For(ShutdownReason.PhotonCloudTimeout), SessionEndCopy.For(ShutdownReason.ConnectionTimeout));
            Assert.AreNotEqual(SessionEndCopy.For(ShutdownReason.GameIsFull), SessionEndCopy.For(ShutdownReason.Error));
        }

        [Test]
        public void SessionEnd_Headline_IsCoopClosed_UnlessTheRoundHadEnded()
        {
            Assert.AreEqual("COOP CLOSED", UiText.Get(SessionEndCopy.HeadlineKey(roundHadEnded: false)));
            Assert.AreEqual("MATCH OVER", UiText.Get(SessionEndCopy.HeadlineKey(roundHadEnded: true)));
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.DoesNotContain("_shutdownReason.ToString()", src, "the raw reason never reaches the screen");
            StringAssert.Contains("SessionEndCopy.For(", src);
            StringAssert.Contains("SessionEndCopy.HeadlineKey(", src);
            StringAssert.DoesNotContain("@session.ended", Read("Assets/UI/MatchOverlays.uxml"), "the headline is chosen in code");
        }

        // ---- Finding 14: copy leftovers ---------------------------------------------------------------

        [Test]
        public void Hoarder_Description_IsItsTemplatedDetail_FromTheSOValue()
        {
            var hoarder = TestAssets.Load<HoarderPassiveSO>(TestAssets.AbilitiesDir + "/Passives/Hoarder.asset");
            var match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            string detail = hoarder.PerkDetail(match);
            Assert.AreEqual($"Carries at least {Mathf.RoundToInt(hoarder.MinimumCapacity)} food.", detail);
            Assert.AreEqual(detail, hoarder.Description, "the asset's description is the template's output, not separate prose");
        }

        [Test]
        public void LobbyErrors_AndCooldownBadge_ReadInVoice()
        {
            foreach (var key in new[] { UiKeys.LobbyErrCreate, UiKeys.LobbyErrNoCode, UiKeys.LobbyErrJoin, UiKeys.LobbyErrLoad })
            {
                string line = UiText.Get(key);
                StringAssert.DoesNotStartWith("Could not", line, key);
                StringAssert.DoesNotContain("lobby", line.ToLowerInvariant(), key + " (in voice: a coop, not a lobby)");
            }
            foreach (var key in new[] { UiKeys.CooldownShort, UiKeys.CooldownMed })
                StringAssert.Contains("RECHARGE", UiText.Get(key), key + ": a bare SHORT / MED said nothing");
        }

        [Test]
        public void HudTopBar_Placeholders_AreDictionaryKeysOrBlank()
        {
            string xml = Read("Assets/UI/MatchTopBar.uxml");
            var bad = Regex.Matches(xml, "text=\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value)
                .Where(t => t.Length > 0 && !(UiText.IsKeyReference(t) && UiText.HasKey(UiText.KeyOfReference(t)))).ToList();
            Assert.IsEmpty(bad, "literal HUD placeholders: " + string.Join(", ", bad));
            StringAssert.Contains("UiText.ResolveTree(_root)", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"),
                "the HUD resolves its @key placeholders");
        }
    }
}
