using System.Collections.Generic;
using System.IO;
using CluckWars.Gameplay;
using Fusion;
using CluckWars.UI;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu overhaul Phase 4, re-audit item 4 ("START freezes the menu / eats the 3") and Phase 5
    /// (round-2 findings 3, 4, 11, 13): every peer settles on its own (<see cref="SettleTracker"/>), the
    /// intro arms once the whole room has settled and GET READY has been up for its minimum
    /// (<see cref="IntroArmGate"/>), the start / end audio is each peer's own and fires once per round
    /// (<see cref="MatchAudioCueTracker"/>), the comeback event only fires in the last N seconds
    /// (<see cref="ComebackEventTiming"/>) and its banner waits for GO! to clear
    /// (<see cref="EventBannerTiming"/>), and the menu loads the Game scene behind its GET READY card.
    /// </summary>
    public sealed class MatchStartFlowTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), rel));

        private const float Frame = 1f / 60f;

        private static int FramesUntilOpen(IntroArmGate gate, bool everyoneSettled, float dt, int max = 10000)
        {
            for (int i = 1; i <= max; i++)
                if (gate.Observe(everyoneSettled, dt)) return i;
            return -1;
        }

        private static int FramesUntilSettled(SettleTracker t, bool chickenUp, float dt, int max = 10000)
        {
            for (int i = 1; i <= max; i++)
                if (t.Observe(chickenUp, dt)) return i;
            return -1;
        }

        // ---- SettleTracker (one peer) -------------------------------------------------------------

        [Test]
        public void Settle_AfterTheSettleWindowOfSmoothFrames_OnceTheOwnChickenIsUp_AndLatches()
        {
            var t = new SettleTracker();
            Assert.AreEqual(UnityEngine.Mathf.CeilToInt(SettleTracker.SettleSeconds / Frame), FramesUntilSettled(t, true, Frame), 1);
            Assert.IsTrue(t.Observe(false, 0.5f), "latched: a later hitch or despawn does not un-settle the report");
        }

        [Test]
        public void Settle_AHitchOrNoChickenRestartsTheWindow()
        {
            var t = new SettleTracker();
            for (int i = 0; i < 20; i++) Assert.IsFalse(t.Observe(true, Frame)); // 0.33 s smooth
            Assert.IsFalse(t.Observe(true, 0.5f), "a load hitch: not smooth");
            for (int i = 0; i < 20; i++) Assert.IsFalse(t.Observe(true, Frame), "the 0.33 s before the hitch no longer count");
            Assert.AreEqual(5, FramesUntilSettled(t, true, Frame), 1);

            var noChicken = new SettleTracker();
            for (int i = 0; i < 120; i++) Assert.IsFalse(noChicken.Observe(false, Frame), "no own chicken yet");
            Assert.Greater(FramesUntilSettled(noChicken, true, Frame), 20, "the window starts only once the chicken is up");
        }

        // ---- IntroArmGate (the room, on the state authority) ----------------------------------------

        [Test]
        public void Gate_EveryoneSettled_OpensAfterTheMinimumShow_NotBefore()
        {
            var gate = new IntroArmGate();
            int frames = FramesUntilOpen(gate, true, Frame);
            Assert.AreEqual(IntroArmGate.MinShowSeconds, frames * Frame, 2 * Frame, "GET READY is up >= 0.7 s");
            Assert.IsFalse(gate.TimedOut);
            Assert.IsFalse(gate.ShowsWaitingNotice, "nobody was waited for");
            Assert.IsTrue(gate.Observe(true, Frame), "stays open until the caller consumes it");
        }

        [Test]
        public void Gate_PlayAgainOnAWarmWorld_NoLongerFlashesGetReady()
        {
            // PLAY AGAIN: every peer is already settled on the first frame. It used to open after the
            // 0.4 s settle plus a frame or two (~0.42 s on screen); now it holds the full minimum.
            var gate = new IntroArmGate();
            for (int i = 0; i < 30; i++) Assert.IsFalse(gate.Observe(true, Frame), "0.5 s: still GET READY");
            Assert.AreEqual(IntroArmGate.MinShowSeconds, (30 + FramesUntilOpen(gate, true, Frame)) * Frame, 2 * Frame);

            // A hitch does not use up the minimum. The first frame's delta was spent BEFORE GET READY was drawn
            // (the frame the START tap was processed on: 0.84 s measured live), so it counts nothing; after
            // that each frame counts for at most the smooth-frame cap.
            var hitchy = new IntroArmGate();
            Assert.IsFalse(hitchy.Observe(true, 0.85f), "the START frame's own hitch is not GET READY on screen");
            Assert.IsFalse(hitchy.Observe(true, 0.6f), "a 0.6 s hitch while it shows counts 0.1 s");
            Assert.GreaterOrEqual(FramesUntilOpen(hitchy, true, SettleTracker.MaxSmoothFrameSeconds), 6);
        }

        [Test]
        public void Gate_OneUnsettledPlayer_KeepsTheRoomWaiting_AndNamesThemAfterTheNoticeDelay()
        {
            var gate = new IntroArmGate();
            int noticeFrame = UnityEngine.Mathf.CeilToInt(IntroArmGate.WaitingNoticeSeconds / Frame);
            for (int i = 1; i < noticeFrame - 1; i++)
            {
                Assert.IsFalse(gate.Observe(false, Frame), "someone is still loading");
                Assert.IsFalse(gate.ShowsWaitingNotice, $"no notice before {IntroArmGate.WaitingNoticeSeconds}s (frame {i})");
            }
            for (int i = 0; i < 3; i++) gate.Observe(false, Frame);
            Assert.IsTrue(gate.ShowsWaitingNotice, "after 3 s GET READY says who the room waits for");

            Assert.IsTrue(gate.Observe(true, Frame), "the slow peer reports in: the room starts at once (GET READY was up long enough)");
            Assert.IsFalse(gate.TimedOut);
            Assert.IsFalse(gate.ShowsWaitingNotice, "the notice goes with the gate");
        }

        [Test]
        public void Gate_NeverStrandsTheRoom_ItTimesOutAtTheCapAndSaysSo()
        {
            var unsettled = new IntroArmGate();
            int frames = FramesUntilOpen(unsettled, false, Frame);
            Assert.AreEqual(IntroArmGate.MaxWaitSeconds, frames * Frame, 2 * Frame);
            Assert.IsTrue(unsettled.TimedOut);

            var hitching = new IntroArmGate();
            Assert.Greater(FramesUntilOpen(hitching, false, 0.25f), 0, "a peer that never settles still lets the room start");
            Assert.IsTrue(hitching.TimedOut);
        }

        // ---- FirstUnsettled / HostLeftMidRound ------------------------------------------------------

        private static readonly PlayerRef Host = PlayerRef.FromEncoded(1), Joiner = PlayerRef.FromEncoded(2),
            Third = PlayerRef.FromEncoded(3);

        [Test]
        public void FirstUnsettled_NamesTheFirstConnectedPlayerWithoutAReport_AndForgetsOnesWhoLeft()
        {
            var settled = new HashSet<PlayerRef> { Host };
            Assert.AreEqual(Joiner, MatchFlowRules.FirstUnsettled(new[] { Host, Joiner, Third }, settled));
            settled.Add(Joiner);
            Assert.AreEqual(Third, MatchFlowRules.FirstUnsettled(new[] { Host, Joiner, Third }, settled));
            Assert.AreEqual(PlayerRef.None, MatchFlowRules.FirstUnsettled(new[] { Host, Joiner }, settled),
                "the third player left: no longer waited for");
            Assert.AreEqual(PlayerRef.None, MatchFlowRules.FirstUnsettled(new PlayerRef[0], new HashSet<PlayerRef>()),
                "no players: nothing to wait for");
        }

        [Test]
        public void HostLeft_OnlyWhenTheWatchedManagerGoesDuringGetReadyOrARound_AndTheSessionCarriesOn()
        {
            Assert.IsTrue(MatchFlowRules.HostLeftMidRound(MatchState.Active, true, false, false), "manager despawned mid-round");
            Assert.IsTrue(MatchFlowRules.HostLeftMidRound(MatchState.Starting, false, true, false), "authority moved during GET READY");
            Assert.IsFalse(MatchFlowRules.HostLeftMidRound(MatchState.Active, true, false, true),
                "the session is ending: the session-end screen says so instead");
            Assert.IsFalse(MatchFlowRules.HostLeftMidRound(MatchState.Ended, true, false, false), "the end screen: not mid-round");
            Assert.IsFalse(MatchFlowRules.HostLeftMidRound(MatchState.WaitingForPlayers, true, false, false), "the waiting room");
            Assert.IsFalse(MatchFlowRules.HostLeftMidRound(MatchState.Active, false, false, false), "nothing changed");
        }

        // ---- ComebackEventTiming (decision 2) ------------------------------------------------------

        private const float Window = 10f;

        [Test]
        public void Comeback_FiresOnlyInTheLastNSeconds_OfARunningRound_Once()
        {
            var none = MatchEventKind.None;
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, false, 10.01f, Window, none), "10.01 s left: too early");
            Assert.IsTrue(ComebackEventTiming.ShouldFire(MatchState.Active, false, 10f, Window, none), "10 s left");
            Assert.IsTrue(ComebackEventTiming.ShouldFire(MatchState.Active, false, 3f, Window, none), "inside the window");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, false, 45f, Window, none), "GO of a 45 s match");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, true, 5f, Window, none), "never during the intro");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, false, 5f, 0f, none), "threshold 0 disables it");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, false, 5f, Window, MatchEventKind.GoldenPile),
                "an event is already live: once per round");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Starting, false, 5f, Window, none), "GET READY");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Ended, false, 0f, Window, none), "the round is over");
        }

        [Test]
        public void Comeback_ShippedConfig_IsTheLast10Seconds_AndInsideTheMatch()
        {
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchConfigSO>("Assets/_Game/Data/MatchConfig.asset");
            Assert.IsNotNull(cfg, "MatchConfig.asset");
            Assert.AreEqual(10f, cfg.ComebackEventSecondsLeft, "decision 2: the last 10 s");
            Assert.Less(cfg.ComebackEventSecondsLeft, cfg.MatchDurationSeconds, "the event can never fire at GO");
            Assert.IsFalse(ComebackEventTiming.ShouldFire(MatchState.Active, false, cfg.MatchDurationSeconds,
                cfg.ComebackEventSecondsLeft, MatchEventKind.None));
        }

        // ---- MatchAudioCueTracker ------------------------------------------------------------------

        private static MatchAudioCueTracker.Cue[] Run(MatchAudioCueTracker t, params (MatchState s, bool intro, bool ev)[] frames)
        {
            var cues = new MatchAudioCueTracker.Cue[frames.Length];
            for (int i = 0; i < frames.Length; i++) cues[i] = t.Observe(frames[i].s, frames[i].intro, frames[i].ev);
            return cues;
        }

        [Test]
        public void Audio_MusicAtGo_IntensifyOnceAtTheEvent_AndTheEndCue_ExactlyOncePerRound_PlayAgainIncluded()
        {
            // Cue.Go starts the match MUSIC only: the GO stinger is the intro overlay's (MenuAudio.CountdownGo),
            // so exactly one sound owns GO (decision 4; guarded below in GameManager_PlaysNoSoundOfItsOwnAtGo).
            var t = new MatchAudioCueTracker();
            var None = MatchAudioCueTracker.Cue.None;
            var Go = MatchAudioCueTracker.Cue.Go;
            var Up = MatchAudioCueTracker.Cue.Intensify;
            var End = MatchAudioCueTracker.Cue.End;
            for (int round = 0; round < 2; round++)
            {
                // Round 1 = solo spawn straight into Starting; round 2 = PLAY AGAIN back into Starting.
                var first = round == 0 ? MatchState.Starting : MatchState.WaitingForPlayers;
                CollectionAssert.AreEqual(
                    new[] { None, None, None, None, Go, None, Up, None, None, End, None, None },
                    Run(t, (first, false, false), (MatchState.Starting, false, false),
                        (MatchState.Active, true, false), (MatchState.Active, true, false),
                        (MatchState.Active, false, false), (MatchState.Active, false, false),
                        (MatchState.Active, false, true), (MatchState.Active, false, true), (MatchState.Active, false, true),
                        (MatchState.Ended, false, true), (MatchState.Ended, false, true), (MatchState.WaitingForPlayers, false, false)),
                    $"round {round + 1}");
                Assert.IsFalse(t.RoundAudible, "the end cue stopped the music");
            }
        }

        [Test]
        public void Audio_ARoundWithoutAnEvent_NeverIntensifies()
        {
            var t = new MatchAudioCueTracker();
            t.Observe(MatchState.Active, true);
            Assert.AreEqual(MatchAudioCueTracker.Cue.Go, t.Observe(MatchState.Active, false));
            for (int i = 0; i < 100; i++) Assert.AreEqual(MatchAudioCueTracker.Cue.None, t.Observe(MatchState.Active, false, false));
            Assert.AreEqual(MatchAudioCueTracker.Cue.End, t.Observe(MatchState.Ended, false));
        }

        [Test]
        public void Audio_AFreshTrackerWhoseFirstLookIsARunningRound_GetsJoinedRunning_AndIsAudibleUntilTheEnd()
        {
            var t = new MatchAudioCueTracker();
            Assert.IsFalse(t.RoundAudible);
            Assert.AreEqual(MatchAudioCueTracker.Cue.JoinedRunning, t.Observe(MatchState.Active, false));
            Assert.IsTrue(t.RoundAudible, "a host change now must stop the music it started (GameManager.Despawned)");
            Assert.AreEqual(MatchAudioCueTracker.Cue.End, t.Observe(MatchState.Ended, false));
            Assert.IsFalse(t.RoundAudible);
        }

        [Test]
        public void Audio_APeerArrivingAfterGo_GetsTheMusic_AndOneArrivingAtTheEndGetsNothing()
        {
            var lateJoiner = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.JoinedRunning, lateJoiner.Observe(MatchState.Active, false, true),
                "music first, even when the event is already live");
            Assert.AreEqual(MatchAudioCueTracker.Cue.Intensify, lateJoiner.Observe(MatchState.Active, false, true), "then the intense loop");
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, lateJoiner.Observe(MatchState.Active, false, true));
            Assert.AreEqual(MatchAudioCueTracker.Cue.End, lateJoiner.Observe(MatchState.Ended, false), "it saw the round run");

            var midIntro = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, midIntro.Observe(MatchState.Active, true));
            Assert.AreEqual(MatchAudioCueTracker.Cue.Go, midIntro.Observe(MatchState.Active, false), "a joiner mid-intro gets the full GO");

            var atEnd = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, atEnd.Observe(MatchState.Ended, false), "no stray end cue for a round it never saw");
        }

        // ---- World-ready predicate (IntroArmGate input) ----------------------------------------------

        [Test]
        public void WorldReady_NeedsAChickenForEveryRealPlayer_AndADecoyDoesNotCount()
        {
            var players = new[] { Host, Joiner };
            Assert.IsTrue(MatchFlowRules.EveryPlayerHasAChicken(players, new[] { (Host, false), (Joiner, false), (PlayerRef.None, false) }),
                "both chickens up (bots have no input authority and are ignored)");
            Assert.IsFalse(MatchFlowRules.EveryPlayerHasAChicken(players, new[] { (Host, false) }),
                "a joiner whose chicken has not spawned yet");
            Assert.IsFalse(MatchFlowRules.EveryPlayerHasAChicken(players, new[] { (Host, false), (Joiner, true) }),
                "the joiner's Doppelganger decoy is not its chicken");
            Assert.IsTrue(MatchFlowRules.EveryPlayerHasAChicken(new PlayerRef[0], new (PlayerRef, bool)[0]), "no players: nothing to wait for");
        }

        // ---- IntroOverlayRule ----------------------------------------------------------------------

        [Test]
        public void IntroOverlay_GetReadyBeforeTheFirstManagerAndDuringStarting_NeverAfterOneWasLost()
        {
            var Ready = IntroOverlayRule.PreIntro.GetReady; var None = IntroOverlayRule.PreIntro.None;
            Assert.AreEqual(Ready, IntroOverlayRule.Decide(false, MatchState.WaitingForPlayers, false, false, 0.5f), "session still starting");
            Assert.AreEqual(Ready, IntroOverlayRule.Decide(true, MatchState.Starting, true, false, 0f), "world settling");
            Assert.AreEqual(None, IntroOverlayRule.Decide(true, MatchState.Active, true, false, 0f), "the countdown logic takes over");
            Assert.AreEqual(None, IntroOverlayRule.Decide(true, MatchState.WaitingForPlayers, true, false, 0f), "the lobby, not GET READY");
            Assert.AreEqual(None, IntroOverlayRule.Decide(false, MatchState.WaitingForPlayers, true, false, 0f), "host change: the manager went away");
            Assert.AreEqual(None, IntroOverlayRule.Decide(false, MatchState.WaitingForPlayers, false, true, 0f), "BACK TO LOBBY is leaving");
            Assert.AreEqual(IntroOverlayRule.PreIntro.TimedOut,
                IntroOverlayRule.Decide(false, MatchState.WaitingForPlayers, false, false, IntroOverlayRule.NoManagerTimeoutSeconds),
                "a manager that never arrives: hide and report, never a GET READY forever");
        }

        // ---- EventBannerTiming ---------------------------------------------------------------------

        [Test]
        public void EventBanner_WaitsForGoToClear_AndSkipsItsStingRightAfterGo()
        {
            Assert.IsFalse(EventBannerTiming.CanShow(true, 0f), "never over the countdown");
            Assert.IsFalse(EventBannerTiming.CanShow(false, IntroCueTracker.GoHoldSeconds - 0.05f), "never over GO!");
            Assert.IsTrue(EventBannerTiming.CanShow(false, IntroCueTracker.GoHoldSeconds));
            Assert.IsFalse(EventBannerTiming.PlaysSting(IntroCueTracker.GoHoldSeconds), "the GO stinger just played");
            Assert.IsTrue(EventBannerTiming.PlaysSting(30f), "the usual case: the event fires long after GO and keeps its sting");
            Assert.IsTrue(EventBannerTiming.CanShow(false, float.PositiveInfinity), "a peer that joined after GO");
            Assert.IsTrue(EventBannerTiming.PlaysSting(float.PositiveInfinity));
        }

        // ---- Wiring the rules depend on ------------------------------------------------------------

        [Test]
        public void StartMatch_LoadsTheGameSceneInTheBackground_BehindTheGetReadyCard()
        {
            string menu = Read("Assets/_Game/Scripts/UI/MenuUiController.cs");
            StringAssert.Contains("_sceneLoader.LoadNextAsync()", menu);
            StringAssert.DoesNotContain("_sceneLoader.LoadNext();", menu, "the synchronous load froze the menu");
            StringAssert.Contains("SceneManager.LoadSceneAsync(", Read("Assets/_Game/Scripts/Bootstrap/SceneLoader.cs"));

            string xml = Read("Assets/UI/Lobby.uxml");
            int card = xml.IndexOf("name=\"GetReadyCard\"", System.StringComparison.Ordinal);
            Assert.Greater(card, xml.IndexOf("name=\"StartBtn\"", System.StringComparison.Ordinal), "drawn over START (last child)");
            StringAssert.Contains("@countdown.getReady", xml.Substring(card));
        }

        [Test]
        public void GetReady_IsOneSharedCard_InTheMenuAndTheArena()
        {
            string lobby = Read("Assets/UI/Lobby.uxml"), overlays = Read("Assets/UI/MatchOverlays.uxml");
            foreach (var xml in new[] { lobby, overlays })
            {
                StringAssert.Contains("Styles/GetReadyCard.uss", xml);
                StringAssert.Contains("class=\"cw-getready__plate\"", xml);
            }
            StringAssert.Contains("name=\"IntroGetReady\"", overlays);
            StringAssert.DoesNotContain("IntroRibbon", overlays, "the old ribbon sat under GO!");
            StringAssert.DoesNotContain(".cw-getready", Read("Assets/UI/Styles/CluckWarsTheme.uss"), "one copy of the card style");
        }

        [Test]
        public void GameManager_ArmsEveryIntroThroughStarting_AndPlaysNoStartAudioWhenArming()
        {
            string gm = Read("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            StringAssert.Contains("State = MatchState.Starting;", gm);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(gm, @"\bStartMatch\(\);").Count,
                "StartMatch is reached only through the settle gate (ArmIntroAfterSettle), never straight from START or Spawned");
            int start = gm.IndexOf("private void StartMatch()", System.StringComparison.Ordinal);
            int end = gm.IndexOf("private void EvaluateWinCondition()", System.StringComparison.Ordinal);
            Assert.Greater(start, 0); Assert.Greater(end, start);
            string body = gm.Substring(start, end - start);
            StringAssert.DoesNotContain("PlaySFX", body, "the stinger plays at GO on every peer, not when the authority arms the timer");
            StringAssert.DoesNotContain("PlayMusic", body);
            StringAssert.Contains("MatchDurationSeconds + _introSeconds", body, "playable time is unchanged by the intro");
        }

        [Test]
        public void GameManager_PlaysNoSoundOfItsOwnAtGo_TheIntroOverlayOwnsTheOneGoStinger()
        {
            // Decision 4: two GO sounds used to fire on the same frame (GameManager's MatchStart + the overlay's
            // CountdownGo). The comeback banner's sting (MatchHud) is gated by EventBannerTiming.PlaysSting.
            string gm = Read("Assets/_Game/Scripts/Gameplay/GameManager.cs");
            StringAssert.DoesNotContain(".MatchStart", gm, "GameManager must not play the MatchStart stinger");
            int go = gm.IndexOf("case MatchAudioCueTracker.Cue.Go:", System.StringComparison.Ordinal);
            int nextBreak = gm.IndexOf("break;", go, System.StringComparison.Ordinal);
            Assert.Greater(go, 0); Assert.Greater(nextBreak, go);
            StringAssert.DoesNotContain("PlaySFX", gm.Substring(go, nextBreak - go), "the Go cue starts music only");

            string overlays = Read("Assets/_Game/Scripts/UI/MatchOverlaysController.cs");
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(overlays, @"\.CountdownGo\(\)").Count,
                "exactly one GO stinger, in the intro overlay");
        }
    }
}
