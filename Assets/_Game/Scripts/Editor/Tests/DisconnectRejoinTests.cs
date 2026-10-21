using System;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Diagnostics;
using CluckWars.Localization;
using CluckWars.Networking;
using CluckWars.Services;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using NetDisconnectReason = Fusion.Sockets.NetDisconnectReason;
using ShutdownReason = Fusion.ShutdownReason;

namespace CluckWars.Tests
{
    /// <summary>
    /// CW-218: a mobile player backgrounded past the Fusion timeout was dropped to the main menu with no message and no
    /// way back. Maestro's decision (2026-10-21): a "You were disconnected" sheet on the menu with REJOIN (the ordinary
    /// join-by-code flow, a fresh late join; no session resume, no state restore) and BACK TO MENU. Pure rules
    /// (<see cref="DisconnectRules"/>), the remembered join code (<see cref="IUGSService.LastJoinCode"/>), the copy and
    /// the UXML, plus source guards for the wiring that needs a runner and two scenes. The real trigger (an Android app
    /// sent to the background) needs a device.
    /// </summary>
    public sealed class DisconnectRejoinTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        // ---- Classification -------------------------------------------------------------------------------------------

        [Test]
        public void Cw218_TimeoutThenPluginLogic_IsThisPeersLostConnection_NotAHostLeaving()
        {
            // session-213904 phone.log: OnDisconnectedFromServer reason=Timeout -> OnShutdown DisconnectedByPluginLogic.
            var kind = DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, NetDisconnectReason.Timeout, leftOnPurpose: false);
            Assert.AreEqual(SessionExitKind.ConnectionLost, kind);

            // The old screen read the shutdown reason alone and told the player the host had left.
            Assert.AreEqual("The host flew the coop.", SessionEndCopy.For(ShutdownReason.DisconnectedByPluginLogic));
            Assert.AreEqual("Lost the signal to the barn.", SessionEndCopy.For(kind, ShutdownReason.DisconnectedByPluginLogic));
        }

        [Test]
        public void PcFreezeKick_PluginLogicWithNoTransportReason_GetsTheMenuNotice_InHostAndJoin_NotInSolo()
        {
            // session-20261021-205133 client2.log: 15 s freeze -> 'Kick Player [Player:2][2] from Session: ServerLogic'
            // (DisconnectMessage 104) -> OnShutdown DisconnectedByPluginLogic, transport=none. The overlay used to log
            // exit=HostLeft and run the 5 s countdown with no sheet.
            var kind = DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, null, leftOnPurpose: false);
            Assert.AreEqual(SessionExitKind.ConnectionLost, kind);
            Assert.IsTrue(DisconnectRules.ShowsMenuNotice(kind, SessionMode.Join));
            Assert.IsTrue(DisconnectRules.ShowsMenuNotice(kind, SessionMode.Host));
            Assert.IsFalse(DisconnectRules.ShowsMenuNotice(kind, SessionMode.Solo));
            Assert.IsTrue(DisconnectRules.OffersRejoin(SessionMode.Join, "ABC123"));
            Assert.IsFalse(DisconnectRules.ShowsRejoin(true, LobbyAvailability.Gone), "a match that really ended still says 'over or full'");
            Assert.AreEqual(SessionExitKind.Voluntary, DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, null, leftOnPurpose: true),
                "our own LEAVE is never a kick");
        }

        [Test]
        public void Classify_WithoutATransportLoss_FollowsTheShutdownReason()
        {
            Assert.AreEqual(SessionExitKind.HostLeft, DisconnectRules.Classify(ShutdownReason.HostMigration, null, false));
            Assert.AreEqual(SessionExitKind.ConnectionLost, DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, NetDisconnectReason.ByRemote, false),
                "the server closing us carries no cause of its own; the kick decides");
            foreach (var r in new[] { ShutdownReason.PhotonCloudTimeout, ShutdownReason.ConnectionTimeout, ShutdownReason.OperationTimeout,
                                      ShutdownReason.DisconnectedByPluginLogic })
                Assert.AreEqual(SessionExitKind.ConnectionLost, DisconnectRules.Classify(r, null, false), r.ToString());
            foreach (var r in new[] { ShutdownReason.GameIsFull, ShutdownReason.GameNotFound, ShutdownReason.MaxCcuReached,
                                      ShutdownReason.ConnectionRefused, ShutdownReason.IncompatibleConfiguration })
                Assert.AreEqual(SessionExitKind.Other, DisconnectRules.Classify(r, null, false), r.ToString());
            Assert.AreEqual(SessionExitKind.SessionClosed, DisconnectRules.Classify(ShutdownReason.Ok, NetDisconnectReason.Requested, false));
            Assert.AreEqual(SessionExitKind.SessionClosed, DisconnectRules.Classify(ShutdownReason.GameClosed, null, false));
            Assert.AreEqual(SessionExitKind.Other, DisconnectRules.Classify(ShutdownReason.GameIsFull, null, false));
            Assert.AreEqual(SessionExitKind.Other, DisconnectRules.Classify(ShutdownReason.Error, null, false));
        }

        [Test]
        public void Classify_EveryTransportLoss_IsALostConnection_WhateverTheShutdownSays()
        {
            var losses = new[] { NetDisconnectReason.Timeout, NetDisconnectReason.SendWindowFull, NetDisconnectReason.SequenceOutOfBounds, NetDisconnectReason.Unknown };
            foreach (ShutdownReason reason in Enum.GetValues(typeof(ShutdownReason)))
                foreach (var loss in losses)
                    Assert.AreEqual(SessionExitKind.ConnectionLost, DisconnectRules.Classify(reason, loss, false), $"{reason} after {loss}");
        }

        [Test]
        public void ReversedCallbackOrder_ShutdownBeforeTheTransportReason_IsStillALostConnection()
        {
            // Pinned intent: if Fusion reports OnShutdown(DisconnectedByPluginLogic) BEFORE OnDisconnectedFromServer, the
            // handler has no transport reason yet, and the answer is the same as with it: this peer was dropped.
            var kind = DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, transportReason: null, leftOnPurpose: false);
            Assert.AreEqual(DisconnectRules.Classify(ShutdownReason.DisconnectedByPluginLogic, NetDisconnectReason.Timeout, false), kind);
            Assert.AreEqual(SessionExitKind.ConnectionLost, kind);
            Assert.IsTrue(DisconnectRules.ShowsMenuNotice(kind, SessionMode.Join));
            Assert.AreEqual("Lost the signal to the barn.", SessionEndCopy.For(kind, ShutdownReason.DisconnectedByPluginLogic));

            // ...and a reason left over from an earlier runner cannot leak into it: StartAsync resets it, OnShutdown does not.
            string src = Read("Assets/_Game/Scripts/Networking/FusionNetworkService.cs");
            int start = src.IndexOf("private async Task StartAsync(", StringComparison.Ordinal);
            Assert.Greater(src.IndexOf("LastDisconnectReason = null;", start, StringComparison.Ordinal), start);
            int shutdown = src.IndexOf("INetworkRunnerCallbacks.OnShutdown", StringComparison.Ordinal);
            string onShutdown = src.Substring(shutdown, src.IndexOf("OnInputMissing", shutdown, StringComparison.Ordinal) - shutdown);
            StringAssert.DoesNotContain("LastDisconnectReason", onShutdown, "handlers read it inside OnShutdown; clearing it there would race them");
        }

        [Test]
        public void Rejoin_StillALobbyMember_ReReadsTheLobby_KeepsHostOwnership_AndLogsTheUgsReason()
        {
            string src = Read("Assets/_Game/Scripts/Services/UGSService.cs");
            int join = src.IndexOf("public async Task<LobbyInfo> JoinLobbyByCodeAsync(", StringComparison.Ordinal);
            string body = src.Substring(join, src.IndexOf("public async Task<LobbyInfo> JoinLobbyAsync(", join, StringComparison.Ordinal) - join);
            int reRead = body.IndexOf("LobbyService.Instance.GetLobbyAsync(member.Id)", StringComparison.Ordinal);
            int newJoin = body.IndexOf("LobbyService.Instance.JoinLobbyByCodeAsync(code)", StringComparison.Ordinal);
            Assert.Greater(reRead, 0, "a member re-reads its own lobby");
            Assert.Less(reRead, newJoin, "before any new join");
            string memberPath = body.Substring(reRead, newJoin - reRead);
            StringAssert.DoesNotContain("_isHost", memberPath.Replace("host={_isHost}", ""), "a host that timed out stays the host");
            StringAssert.Contains("e.Reason", body);
            StringAssert.Contains("e.ErrorCode", body);

            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("REJOIN '{_rejoinCode}' failed: {e.GetType().Name}", menu);
        }

        [Test]
        public void Classify_LeavingOnPurpose_IsAlwaysVoluntary()
        {
            foreach (ShutdownReason reason in Enum.GetValues(typeof(ShutdownReason)))
            {
                Assert.AreEqual(SessionExitKind.Voluntary, DisconnectRules.Classify(reason, null, leftOnPurpose: true), reason.ToString());
                Assert.AreEqual(SessionExitKind.Voluntary, DisconnectRules.Classify(reason, NetDisconnectReason.Timeout, leftOnPurpose: true), reason.ToString());
            }
        }

        // ---- What the player is offered --------------------------------------------------------------------------------

        [Test]
        public void MenuNotice_OnlyForALostConnection_InAMultiplayerSession()
        {
            Assert.IsTrue(DisconnectRules.ShowsMenuNotice(SessionExitKind.ConnectionLost, SessionMode.Join));
            Assert.IsTrue(DisconnectRules.ShowsMenuNotice(SessionExitKind.ConnectionLost, SessionMode.Host), "a host that lost its link: the master moved on");
            Assert.IsFalse(DisconnectRules.ShowsMenuNotice(SessionExitKind.ConnectionLost, SessionMode.Solo), "solo has no session to go back to");
            foreach (var kind in new[] { SessionExitKind.Voluntary, SessionExitKind.HostLeft, SessionExitKind.SessionClosed, SessionExitKind.Other })
                Assert.IsFalse(DisconnectRules.ShowsMenuNotice(kind, SessionMode.Join), kind + " keeps the in-match session-end screen");
        }

        [Test]
        public void Rejoin_NeedsAMultiplayerModeAndARememberedCode()
        {
            Assert.IsTrue(DisconnectRules.OffersRejoin(SessionMode.Join, "ABC123"));
            Assert.IsTrue(DisconnectRules.OffersRejoin(SessionMode.Host, "ABC123"));
            Assert.IsFalse(DisconnectRules.OffersRejoin(SessionMode.Solo, "ABC123"));
            Assert.IsFalse(DisconnectRules.OffersRejoin(SessionMode.Join, null));
            Assert.IsFalse(DisconnectRules.OffersRejoin(SessionMode.Join, ""));
            Assert.IsFalse(DisconnectRules.OffersRejoin(SessionMode.Join, "   "));
        }

        [Test]
        public void Rejoin_HiddenOnlyWhenTheLobbyIsKnownToBeGone()
        {
            Assert.IsTrue(DisconnectRules.ShowsRejoin(true, LobbyAvailability.Open));
            Assert.IsTrue(DisconnectRules.ShowsRejoin(true, LobbyAvailability.Unknown), "can't tell: let the join attempt decide");
            Assert.IsFalse(DisconnectRules.ShowsRejoin(true, LobbyAvailability.Gone));
            Assert.IsFalse(DisconnectRules.ShowsRejoin(false, LobbyAvailability.Open));
        }

        // ---- The remembered code (behind IUGSService) -------------------------------------------------------------------

        [Test]
        public void NullUgs_RemembersTheLastCode_UntilTheLobbyIsLeft()
        {
            var ugs = new NullUGSService();
            Assert.IsNull(ugs.LastJoinCode);

            ugs.JoinLobbyByCodeAsync("  ABC123 \n").Wait();
            Assert.AreEqual("ABC123", ugs.LastJoinCode);

            ugs.CreateLobbyAsync("whatever", 4).Wait();
            Assert.AreEqual("cluck-lan", ugs.LastJoinCode, "the code the offline host's session runs on");

            Assert.IsTrue(ugs.LeaveLobbyAsync().IsCompleted);
            Assert.IsNull(ugs.LastJoinCode);

            var check = ugs.CheckLobbyAsync("ABC123");
            Assert.IsTrue(check.IsCompleted, "the menu awaits this on the offline path");
            Assert.AreEqual(LobbyAvailability.Unknown, check.Result);
        }

        [Test]
        public void UgsService_KeepsTheCodeOnCreateAndJoin_ClearsItOnLeave_AndAChecksNeverThrow()
        {
            string src = Read("Assets/_Game/Scripts/Services/UGSService.cs");
            Assert.AreEqual(2, Regex.Matches(src, @"LastJoinCode\s*=\s*lobby\.LobbyCode").Count, "create + join by code");
            StringAssert.IsMatch(@"LastJoinCode\s*=\s*joined\.LobbyCode", src, "join by id");
            StringAssert.IsMatch(@"LastJoinCode\s*=\s*null", src, "leave");
            StringAssert.Contains("LobbyExceptionReason.LobbyNotFound", src, "a deleted / expired lobby reads Gone");
        }

        [Test]
        public void SessionSelection_DisconnectNotice_StartsOff()
        {
            Assert.IsFalse(new SessionSelectionService().DisconnectNoticeOnMenuLoad);
        }

        // ---- Copy + UXML -------------------------------------------------------------------------------------------------

        [Test]
        public void Copy_IsMaestrosWording()
        {
            Assert.AreEqual("YOU WERE DISCONNECTED", UiText.Get(UiKeys.DisconnectTitle));
            Assert.AreEqual("REJOIN", UiText.Get(UiKeys.BtnRejoin));
            Assert.AreEqual("BACK TO MENU", UiText.Get(UiKeys.BtnBackToMenu));
            Assert.AreEqual("The match is over or full.", UiText.Get(UiKeys.DisconnectRejoinFailed));
            Assert.AreEqual("Rejoining…", UiText.Get(UiKeys.DisconnectRejoining));
        }

        [Test]
        public void MainMenuUxml_HasTheDisconnectSheet_HiddenByDefault_WithBothButtons()
        {
            string uxml = Read("Assets/UI/MainMenu.uxml");
            StringAssert.Contains("name=\"DisconnectSheet\" class=\"cw-sheet-scrim\" style=\"display: none;\"", uxml,
                "the sheet must not show on an ordinary menu load");

            var root = TestAssets.Load<VisualTreeAsset>("Assets/UI/MainMenu.uxml").CloneTree();
            Assert.IsNotNull(root.Q<VisualElement>("DisconnectSheet"));
            Assert.IsNotNull(root.Q<Label>("DisconnectStatus"));
            var rejoin = root.Q<Button>("DisconnectRejoinBtn");
            var back = root.Q<Button>("DisconnectBackBtn");
            Assert.IsNotNull(rejoin);
            Assert.IsNotNull(back);
            UiText.ResolveTree(root);
            Assert.AreEqual("REJOIN", rejoin.text);
            Assert.AreEqual("BACK TO MENU", back.text);
            Assert.AreEqual("Lost the signal to the barn.", root.Q<Label>("DisconnectBody").text);

            string uss = Read("Assets/UI/Styles/CluckWarsTheme.uss");
            foreach (var cls in new[] { ".cw-dc-panel", ".cw-dc-body", ".cw-dc-status", ".cw-dc-actions", ".cw-dc-btn" })
                StringAssert.Contains(cls, uss, cls + " is styled in the menu theme");
        }

        // ---- Wiring (needs a runner + two scenes, so pinned on the source) -----------------------------------------------

        [Test]
        public void FusionService_RecordsTheTransportReason_BeforeShutdown_AndResetsItPerRunner()
        {
            string src = Read("Assets/_Game/Scripts/Networking/FusionNetworkService.cs");
            int disconnected = src.IndexOf("INetworkRunnerCallbacks.OnDisconnectedFromServer", StringComparison.Ordinal);
            Assert.Greater(disconnected, 0);
            Assert.Greater(src.IndexOf("LastDisconnectReason = reason;", disconnected, StringComparison.Ordinal), disconnected);
            StringAssert.Contains("LastDisconnectReason = null;", src, "a new runner must not inherit the last one's reason");
        }

        [Test]
        public void MatchOverlays_ClassifiesTheShutdown_AndHandsALostConnectionToTheMenu()
        {
            string src = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            int handler = src.IndexOf("private void HandleShutdown(ShutdownReason reason)", StringComparison.Ordinal);
            Assert.Greater(handler, 0);
            string body = src.Substring(handler, src.IndexOf("private void Update()", handler, StringComparison.Ordinal) - handler);
            StringAssert.Contains("_network?.LastDisconnectReason", body);
            StringAssert.Contains("DisconnectRules.Classify(reason, transport, leftOnPurpose: false)", body);
            StringAssert.Contains("DisconnectRules.ShowsMenuNotice(", body);
            StringAssert.Contains("_selection.DisconnectNoticeOnMenuLoad = true", body);
            StringAssert.Contains("_sessionEndLeaveNow = true", body, "no 5 s countdown before the menu's notice");
            Assert.Less(body.IndexOf("if (_leavingToLobby) return;", StringComparison.Ordinal),
                body.IndexOf("DisconnectRules.Classify", StringComparison.Ordinal), "an own LEAVE never raises the notice");
            StringAssert.Contains("SessionEndCopy.For(_exitKind, _shutdownReason.Value)", src, "the session-end line uses the classification");
        }

        [Test]
        public void Menu_ConsumesTheNoticeOnce_AndRejoinRunsTheJoinByCodeFlow()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            Assert.Greater(src.IndexOf("ConsumeDisconnectNotice();", StringComparison.Ordinal),
                src.IndexOf("ConsumePostMatchIntent();", StringComparison.Ordinal), "BuildAll opens the sheet once the pages exist");
            StringAssert.Contains("_selection.DisconnectNoticeOnMenuLoad = false", src, "one-shot");
            StringAssert.Contains("_ugs?.LastJoinCode", src, "the code comes from the lobby service, nothing else stored");
            StringAssert.Contains("DisconnectRules.OffersRejoin(", src);
            StringAssert.Contains("DisconnectRules.ShowsRejoin(", src);

            int rejoin = src.IndexOf("private async void OnRejoin()", StringComparison.Ordinal);
            Assert.Greater(rejoin, 0);
            string body = src.Substring(rejoin, src.IndexOf("private void SetDisconnectStatus", rejoin, StringComparison.Ordinal) - rejoin);
            StringAssert.Contains("_ugs.JoinLobbyByCodeAsync(_rejoinCode)", body);
            StringAssert.Contains("_selection.Mode = SessionMode.Join", body);
            StringAssert.Contains("_selection.SessionName = info.JoinCode", body);
            StringAssert.Contains("LoadMatchScene()", body);
            StringAssert.Contains("UiKeys.DisconnectRejoinFailed", body, "a failed rejoin says so on the sheet");
            StringAssert.Contains("catch (Exception e)", body, "async void: every failure is caught and surfaced");
        }

        [Test]
        public void SlowLobbyCheck_NeverBlocksTheSheet_AndOnlyAGoneAnswerHidesRejoin()
        {
            // session-20261021-205856: the check took 10 s and then timed out. The sheet and REJOIN must be up before it is
            // even started, the press must not wait on it, and a timeout (Unknown) must leave REJOIN where it is.
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            int consume = src.IndexOf("private void ConsumeDisconnectNotice()", StringComparison.Ordinal);
            int shown = src.IndexOf("_disconnectSheet.style.display = DisplayStyle.Flex;", consume, StringComparison.Ordinal);
            int check = src.IndexOf("CheckRejoinLobby(_rejoinCode)", consume, StringComparison.Ordinal);
            Assert.Greater(shown, consume);
            Assert.Less(shown, check, "the sheet is on screen before the lobby check starts");
            StringAssert.Contains("private async void CheckRejoinLobby(", src, "fire-and-forget: the menu never awaits it");
            StringAssert.Contains("if (CluckWars.Networking.DisconnectRules.ShowsRejoin(true, availability)) return;", src);
            Assert.IsTrue(DisconnectRules.ShowsRejoin(true, LobbyAvailability.Unknown), "a timed-out check keeps REJOIN");

            int press = src.IndexOf("public bool AutoTestPressRejoin()", StringComparison.Ordinal);
            string pressBody = src.Substring(press, src.IndexOf("OnRejoin();", press, StringComparison.Ordinal) - press);
            StringAssert.DoesNotContain("CheckLobby", pressBody, "pressing REJOIN does not wait for the check");
        }

        [Test]
        public void RejoinOutcome_IsLogged_JoinedOrFailed()
        {
            string src = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            int rejoin = src.IndexOf("private async void OnRejoin()", StringComparison.Ordinal);
            string body = src.Substring(rejoin, src.IndexOf("private void SetDisconnectStatus", rejoin, StringComparison.Ordinal) - rejoin);
            StringAssert.Contains("REJOIN: lobby '{info.JoinCode}' joined", body);
            StringAssert.Contains("_autoTestRejoinFailures++", body);
            StringAssert.Contains("failed: {e.GetType().Name}", body);
        }

        [Test]
        public void AutoRejoin_AnswersAnyDisconnectSheet_NotOnlyItsOwnFreeze_ThenReportsTheOutcome()
        {
            // session-20261021-205856: a real cloud drop at 17 s (before the 25 s mark) put the sheet up; the driver only armed
            // after its own freeze, so it never pressed REJOIN and never logged anything for 7 minutes.
            string src = Read("Assets/_Game/Scripts/Diagnostics/AutoTestDriver.cs");
            int tick = src.IndexOf("private void TickAutoRejoin()", StringComparison.Ordinal);
            string body = src.Substring(tick, src.IndexOf("// ---- loop observation", tick, StringComparison.Ordinal) - tick);
            StringAssert.DoesNotContain("if (_autoDisconnectAt < 0f", body, "arming must not depend on the freeze");
            StringAssert.Contains("_rejoinPhase == AutoRejoinPhase.Done) return;", body);
            foreach (var outcome in new[] { "outcome=joined", "outcome=failed", "outcome=timeout", "outcome=not-offered" })
                StringAssert.Contains(outcome, body);
            StringAssert.Contains("_rejoinPhase != AutoRejoinPhase.WaitSheet) return;", src, "a rejoined round is never frozen");

            Assert.IsFalse(AutoTestRules.ShouldPressRejoin(0.5f));
            Assert.IsTrue(AutoTestRules.ShouldPressRejoin(AutoTestRules.AutoRejoinPressDelaySeconds));
            Assert.AreEqual(AutoRejoinOutcome.Pending, AutoTestRules.DecideRejoinOutcome(false, false, 5f));
            Assert.AreEqual(AutoRejoinOutcome.Joined, AutoTestRules.DecideRejoinOutcome(true, false, 5f));
            Assert.AreEqual(AutoRejoinOutcome.Joined, AutoTestRules.DecideRejoinOutcome(true, true, 99f), "back in the match wins");
            Assert.AreEqual(AutoRejoinOutcome.Failed, AutoTestRules.DecideRejoinOutcome(false, true, 5f));
            Assert.AreEqual(AutoRejoinOutcome.TimedOut, AutoTestRules.DecideRejoinOutcome(false, false, AutoTestRules.AutoRejoinOutcomeWaitSeconds + 1f));
        }

        [Test]
        public void HostHeartbeat_SurvivesATransientOutage_StopsOnlyWhenTheLobbyIsGone()
        {
            // The host also dropped in that run (PhotonCloudTimeout) and its heartbeat stopped on the first timeout, so its
            // lobby would expire before its own REJOIN.
            string src = Read("Assets/_Game/Scripts/Services/UGSService.cs");
            int loop = src.IndexOf("private async Task HeartbeatLoopAsync(", StringComparison.Ordinal);
            string body = src.Substring(loop, src.IndexOf("// ---- Helpers", loop, StringComparison.Ordinal) - loop);
            StringAssert.Contains("LobbyExceptionReason.LobbyNotFound", body, "a deleted lobby stops the loop");
            StringAssert.Contains("retrying in", body, "any other failure keeps it beating");
            StringAssert.DoesNotContain("— stopping.\");", body.Substring(body.IndexOf("catch (Exception e)", StringComparison.Ordinal)),
                "the generic failure path no longer stops the heartbeat");
        }

        // ---- cw_autodisconnect (dev-tools auto-test hook) ----------------------------------------------------------------

        [Test]
        public void AutoDisconnect_ParsesSeconds_AndOnlyFiresInAMultiplayerRoundPastTheMark()
        {
            var cfg = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autojoin", "ABC123", "-cw_autodisconnect", "20" });
            Assert.AreEqual(20, cfg.AutoDisconnect);
            CollectionAssert.IsEmpty(cfg.Problems);
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.AutoDisconnectKey);

            var bad = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autodisconnect", "0" });
            Assert.AreEqual(0, bad.AutoDisconnect);
            Assert.AreEqual(1, bad.Problems.Count, "0 is not a valid mark (omit the key to switch the hook off)");

            Assert.IsFalse(AutoTestRules.ShouldAutoDisconnect(0, true, 999f), "off");
            Assert.IsFalse(AutoTestRules.ShouldAutoDisconnect(20, false, 999f), "solo never times out");
            Assert.IsFalse(AutoTestRules.ShouldAutoDisconnect(20, true, 19.9f));
            Assert.IsTrue(AutoTestRules.ShouldAutoDisconnect(20, true, 20f));
            Assert.Greater(AutoTestRules.AutoDisconnectStallSeconds, 12.8f, "longer than the stall that timed the CW-218 phone out");
        }
    }
}
