using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 7b (round-3 findings 4, 7, 17, 18): leaving a running match, the music fades, the GO scrim and the
    /// Coop's BACK. Pure rules plus guards on the source / UXML that wires them.
    /// </summary>
    public sealed class Phase6Chunk7bTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Finding 4: what Back does per phase -------------------------------------------------------------------

        [Test]
        public void Back_WaitingRoomLeaves_PodiumGoesBackToLobby_RoundOpensTheSheet()
        {
            Assert.AreEqual(MatchBackAction.LeaveWaitingRoom, MatchBackRules.Resolve(MatchState.WaitingForPlayers, false, false));
            Assert.AreEqual(MatchBackAction.BackToLobby, MatchBackRules.Resolve(MatchState.Ended, false, false));
            Assert.AreEqual(MatchBackAction.OpenLeaveSheet, MatchBackRules.Resolve(MatchState.Active, false, false), "round + countdown");
            Assert.AreEqual(MatchBackAction.OpenLeaveSheet, MatchBackRules.Resolve(MatchState.Starting, false, false), "GET READY");
        }

        [Test]
        public void Back_WithTheSheetUp_ClosesIt_ThenOpensItAgainOnTheNextPress()
        {
            Assert.AreEqual(MatchBackAction.CloseLeaveSheet, MatchBackRules.Resolve(MatchState.Active, true, false));
            Assert.AreEqual(MatchBackAction.CloseLeaveSheet, MatchBackRules.Resolve(MatchState.Starting, true, false));
            Assert.AreEqual(MatchBackAction.OpenLeaveSheet, MatchBackRules.Resolve(MatchState.Active, false, false));
        }

        [Test]
        public void Back_IsIgnoredWhileLeavingOrBeforeAManagerExists()
        {
            foreach (MatchState s in System.Enum.GetValues(typeof(MatchState)))
                Assert.AreEqual(MatchBackAction.None, MatchBackRules.Resolve(s, false, leaving: true), $"leaving in {s}");
            Assert.AreEqual(MatchBackAction.None, MatchBackRules.Resolve(null, false, false));
            Assert.AreEqual(MatchBackAction.None, MatchBackRules.Resolve(null, true, false));
        }

        [Test]
        public void Back_WithAMoveHeld_OnlyCancelsIt_NeverOpensTheSheet()
        {
            // Esc is also the keyboard's ability cancel ("Hold to aim, Esc to cancel").
            Assert.AreEqual(MatchBackAction.None, MatchBackRules.Resolve(MatchState.Active, false, false, abilityHeld: true));
            Assert.AreEqual(MatchBackAction.CloseLeaveSheet, MatchBackRules.Resolve(MatchState.Active, true, false, abilityHeld: true));
            Assert.AreEqual(MatchBackAction.BackToLobby, MatchBackRules.Resolve(MatchState.Ended, false, false, abilityHeld: true));
            Assert.AreEqual(MatchBackAction.LeaveWaitingRoom, MatchBackRules.Resolve(MatchState.WaitingForPlayers, false, false, abilityHeld: true));
        }

        [Test]
        public void Sheet_BelongsToStartingAndActiveOnly()
        {
            Assert.IsTrue(MatchBackRules.SheetAllowed(MatchState.Starting));
            Assert.IsTrue(MatchBackRules.SheetAllowed(MatchState.Active));
            Assert.IsFalse(MatchBackRules.SheetAllowed(MatchState.Ended), "a round that ends under the sheet closes it");
            Assert.IsFalse(MatchBackRules.SheetAllowed(MatchState.WaitingForPlayers));
            Assert.IsFalse(MatchBackRules.SheetAllowed(null));
        }

        [Test]
        public void Pause_SoloActiveUnderTheSheetOnly_MultiplayerNeverPauses()
        {
            Assert.IsTrue(MatchBackRules.PausesSimulation(solo: true, MatchState.Active, sheetOpen: true));
            Assert.IsFalse(MatchBackRules.PausesSimulation(solo: false, MatchState.Active, sheetOpen: true), "the match keeps going without you");
            Assert.IsFalse(MatchBackRules.PausesSimulation(solo: true, MatchState.Active, sheetOpen: false));
            Assert.IsFalse(MatchBackRules.PausesSimulation(solo: true, MatchState.Starting, sheetOpen: true), "nothing is running yet");
            Assert.IsFalse(MatchBackRules.PausesSimulation(solo: true, MatchState.Ended, sheetOpen: true));
        }

        [Test]
        public void Sheet_Copy_IsTheBriefsWording()
        {
            Assert.AreEqual("Leave match?", UiText.Get(UiKeys.LeaveTitle));
            Assert.AreEqual("Paused. Your bird waits right here.", UiText.Get(UiKeys.LeaveBodySolo));
            Assert.AreEqual("The match keeps going without you.", UiText.Get(UiKeys.LeaveBodyMp));
            Assert.AreEqual("KEEP PLAYING", UiText.Get(UiKeys.LeaveKeep));
            Assert.AreEqual("LEAVE", UiText.Get(UiKeys.NavLeave));
        }

        [Test]
        public void Sheet_Markup_HasTheTitleBodyAndBothButtons_WithKeepPlayingFirst()
        {
            string uxml = Read("Assets/UI/MatchOverlays.uxml");
            int sheet = uxml.IndexOf("name=\"LeaveSheet\"", System.StringComparison.Ordinal);
            Assert.Greater(sheet, 0, "the sheet is in MatchOverlays.uxml");
            string tail = uxml.Substring(sheet);
            StringAssert.Contains("@leave.title", tail);
            StringAssert.Contains("name=\"LeaveBody\"", tail);
            int keep = tail.IndexOf("name=\"LeaveKeepBtn\"", System.StringComparison.Ordinal);
            int leave = tail.IndexOf("name=\"LeaveConfirmBtn\"", System.StringComparison.Ordinal);
            Assert.Greater(keep, 0); Assert.Greater(leave, keep, "KEEP PLAYING (the primary) comes first");
            StringAssert.Contains("cw-btn--green", tail.Substring(keep, 120), "KEEP PLAYING is the green primary");
            StringAssert.Contains("@leave.keep", tail);
            StringAssert.Contains("@nav.leave", tail);
            Assert.Greater(sheet, uxml.IndexOf("name=\"IntroOverlay\"", System.StringComparison.Ordinal), "above the countdown overlay");
        }

        [Test]
        public void Controller_WiresTheSheet_ThroughTheSharedPhaseRule_AndAlwaysReleasesThePause()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("MatchBackRules.Resolve(", src);
            StringAssert.Contains("runner.SinglePlayerPause(want)", src);
            StringAssert.Contains("LeaveSheetState.IsOpen = true", src);
            StringAssert.Contains("LeaveSheetState.IsOpen = false", src);
            StringAssert.Contains("_leaveKeepBtn?.Focus()", src, "KEEP PLAYING takes the default focus");
            // The pause is released before the runner shuts down, and when the controller goes away.
            int leave = src.IndexOf("private void LeaveSession(", System.StringComparison.Ordinal);
            int close = src.IndexOf("CloseLeaveSheet();", leave, System.StringComparison.Ordinal);
            int disable = src.IndexOf("LeaveToLobbyAsync().ContinueWith", leave, System.StringComparison.Ordinal);
            Assert.Greater(close, leave); Assert.Less(close, disable, "unpaused before ShutdownAsync");
            int onDisable = src.IndexOf("private void OnDisable()", System.StringComparison.Ordinal);
            StringAssert.Contains("CloseLeaveSheet();", src.Substring(onDisable, 300));
        }

        [Test]
        public void Network_WhileTheSheetIsUp_SendsNoMovesAndNoPresses()
        {
            string src = Read("Assets/_Game/Scripts/Networking/FusionNetworkService.cs");
            int onInput = src.IndexOf("INetworkRunnerCallbacks.OnInput(", System.StringComparison.Ordinal);
            int gate = src.IndexOf("if (LeaveSheetState.IsOpen)", onInput, System.StringComparison.Ordinal);
            int movement = src.IndexOf("_inputProvider.GetMovement()", onInput, System.StringComparison.Ordinal);
            Assert.Greater(gate, onInput);
            Assert.Less(gate, movement, "the gate comes before any device is read");
            StringAssert.Contains("InputButton.AbilityCancel", src.Substring(gate, 500), "a held aim is cancelled, not fired");
            StringAssert.Contains("|| LeaveSheetState.IsOpen", src, "the press latch is cleared too");
        }

        // ---- Finding 7: music ---------------------------------------------------------------------------------------

        [Test]
        public void Music_TheMenuBedStartsOnlyOnceTheMatchLoopHasFadedOut()
        {
            Assert.AreEqual(0.6f, MatchPresentationRules.EndMusicFadeOutSeconds, 1e-6f);
            Assert.IsFalse(MatchPresentationRules.StartsPodiumBed(true, false, 0.2f), "still fading out");
            Assert.IsTrue(MatchPresentationRules.StartsPodiumBed(true, false, MatchPresentationRules.EndMusicFadeOutSeconds));
            Assert.IsFalse(MatchPresentationRules.StartsPodiumBed(true, true, 5f), "started once");
            Assert.IsFalse(MatchPresentationRules.StartsPodiumBed(false, false, 5f), "no podium, no bed");
        }

        [Test]
        public void Music_GoFadesTheMatchLoopIn_AndTheRoundEndFadesItOut_NeverCuts()
        {
            Assert.AreEqual(0.25f, MatchPresentationRules.GoMusicFadeInSeconds, 1e-6f);
            string gm = Read("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            int end = gm.IndexOf("case MatchAudioCueTracker.Cue.End:", System.StringComparison.Ordinal);
            string endCase = gm.Substring(end, 600);
            StringAssert.Contains("FadeOutMusic(MatchPresentationRules.EndMusicFadeOutSeconds)", endCase);
            StringAssert.DoesNotContain("StopMusic()", endCase, "the round end no longer cuts the music dead");
            StringAssert.Contains("fadeInSeconds: MatchPresentationRules.GoMusicFadeInSeconds", gm);
        }

        [Test]
        public void Music_ThePodiumBedIsStartedByTheOverlay_NotTheNetwork()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("_audio.StartMenuBed()", src);
            StringAssert.Contains("MatchPresentationRules.StartsPodiumBed(", src);
            StringAssert.DoesNotContain("Rpc", Read("Assets/_Game/Scripts/Gameplay/MatchPresentationRules.cs"), "audio is local presentation");
        }

        // ---- Finding 17: the GO scrim -------------------------------------------------------------------------------

        [Test]
        public void Scrim_DimsGetReadyAndTheCountdown_ButNotGo()
        {
            Assert.IsTrue(MatchPresentationRules.IntroScrimVisible(getReady: true, introActive: false), "GET READY");
            Assert.IsTrue(MatchPresentationRules.IntroScrimVisible(getReady: false, introActive: true), "3-2-1");
            Assert.IsFalse(MatchPresentationRules.IntroScrimVisible(getReady: false, introActive: false), "GO!");
        }

        [Test]
        public void Scrim_IsATogglableClass_AndTheTopBarDimFollowsIt()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            StringAssert.Contains("EnableInClassList(\"cw-overlay--introdim\", on)", src);
            StringAssert.Contains("SetTopBarDimmed(_introScrim && IsShown(_introOverlay))", src);
        }

        // ---- Finding 18: the Coop's BACK -----------------------------------------------------------------------------

        [Test]
        public void CoopBack_GoesHomeWhenOpenedDirectly_AndThroughGearUpOtherwise()
        {
            Assert.AreEqual(MenuBackStep.Loadout, MenuUiController.MenuBackTarget(3, lobbyViaGearUp: true));
            Assert.AreEqual(MenuBackStep.MainMenu, MenuUiController.MenuBackTarget(3, lobbyViaGearUp: false));
            Assert.AreEqual(MenuBackStep.Loadout, MenuUiController.MenuBackTarget(3), "the default keeps the Gear Up path");
            // Other pages are untouched by the flag.
            Assert.AreEqual(MenuBackStep.ClassSelect, MenuUiController.MenuBackTarget(2, lobbyViaGearUp: false));
            Assert.AreEqual(MenuBackStep.MainMenu, MenuUiController.MenuBackTarget(1, lobbyViaGearUp: false));
            Assert.AreEqual(MenuBackStep.None, MenuUiController.MenuBackTarget(0, lobbyViaGearUp: false));
        }

        [Test]
        public void CoopBack_ButtonAndEscShareTheSameFlag_SetByTheTwoEntryPaths()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("MenuBackTarget(PageOrder(_currentPage), _lobbyViaGearUp)", src, "Esc / back");
            StringAssert.Contains("b.clicked += OnLobbyBack;", src, "the BACK button");
            // READY (through Gear Up) -> true; OpenLobbyWithLastSetup (PLAY AGAIN / BACK TO LOBBY) -> false.
            int ready = src.IndexOf("private void OnReady()", System.StringComparison.Ordinal);
            StringAssert.Contains("_lobbyViaGearUp = true;", src.Substring(ready, 500));
            int direct = src.IndexOf("public bool OpenLobbyWithLastSetup(", System.StringComparison.Ordinal);
            StringAssert.Contains("_lobbyViaGearUp = false;", src.Substring(direct, 1400));
            StringAssert.Contains("UiKeys.NavHome", src, "a direct Coop labels its exit HOME");
        }
    }
}
