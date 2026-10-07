using System.IO;
using CluckWars.Gameplay;
using Fusion;
using CluckWars.UI;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu overhaul Phase 4, re-audit item 4 ("START freezes the menu / eats the 3"): the solo intro
    /// arms only once the loaded world renders smoothly (<see cref="IntroArmGate"/>), the start / end
    /// audio is each peer's own and fires once per round (<see cref="MatchAudioCueTracker"/>), the
    /// final-minute banner waits for GO! to clear (<see cref="EventBannerTiming"/>), and the menu loads
    /// the Game scene in the background behind its GET READY card.
    /// </summary>
    public sealed class MatchStartFlowTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), rel));

        private const float Frame = 1f / 60f;

        private static int FramesUntilOpen(IntroArmGate gate, bool ready, float dt, int max = 10000)
        {
            for (int i = 1; i <= max; i++)
                if (gate.Observe(ready, dt)) return i;
            return -1;
        }

        // ---- IntroArmGate ------------------------------------------------------------------------

        [Test]
        public void Gate_OpensAfterTheSettleWindowOfSmoothFrames_OnceTheWorldIsReady()
        {
            var gate = new IntroArmGate();
            int frames = FramesUntilOpen(gate, true, Frame);
            Assert.AreEqual(UnityEngine.Mathf.CeilToInt(IntroArmGate.SettleSeconds / Frame), frames, 1);
            Assert.IsFalse(gate.TimedOut);
            Assert.IsTrue(gate.Observe(true, Frame), "stays open until the caller consumes it");
        }

        [Test]
        public void Gate_AHitchRestartsTheSettle()
        {
            var gate = new IntroArmGate();
            for (int i = 0; i < 20; i++) Assert.IsFalse(gate.Observe(true, Frame)); // 0.33 s smooth
            Assert.IsFalse(gate.Observe(true, 0.5f), "a load hitch: not smooth");
            for (int i = 0; i < 20; i++) Assert.IsFalse(gate.Observe(true, Frame), "the 0.33 s before the hitch no longer count");
            Assert.AreEqual(5, FramesUntilOpen(gate, true, Frame), 1);
        }

        [Test]
        public void Gate_WaitsForTheWorld_ThenSettles()
        {
            var gate = new IntroArmGate();
            for (int i = 0; i < 60; i++) Assert.IsFalse(gate.Observe(false, Frame), "no chicken yet");
            Assert.Greater(FramesUntilOpen(gate, true, Frame), 20, "the settle starts only once the world is ready");
            Assert.IsFalse(gate.TimedOut);
        }

        [Test]
        public void Gate_NeverStrandsThePlayer_ItTimesOutAndSaysSo()
        {
            var unready = new IntroArmGate();
            int frames = FramesUntilOpen(unready, false, Frame);
            Assert.AreEqual(IntroArmGate.MaxWaitSeconds, frames * Frame, 2 * Frame);
            Assert.IsTrue(unready.TimedOut);

            var hitching = new IntroArmGate();
            Assert.Greater(FramesUntilOpen(hitching, true, 0.25f), 0, "a device that never runs smoothly still starts");
            Assert.IsTrue(hitching.TimedOut);
        }

        // ---- MatchAudioCueTracker ------------------------------------------------------------------

        private static MatchAudioCueTracker.Cue[] Run(MatchAudioCueTracker t, params (MatchState s, bool intro)[] frames)
        {
            var cues = new MatchAudioCueTracker.Cue[frames.Length];
            for (int i = 0; i < frames.Length; i++) cues[i] = t.Observe(frames[i].s, frames[i].intro);
            return cues;
        }

        [Test]
        public void Audio_StingerAndMusicAtGo_AndTheEndCue_ExactlyOncePerRound_PlayAgainIncluded()
        {
            var t = new MatchAudioCueTracker();
            var None = MatchAudioCueTracker.Cue.None;
            for (int round = 0; round < 2; round++)
            {
                // Round 1 = solo spawn straight into Starting; round 2 = PLAY AGAIN's waiting room, START, Starting.
                var first = round == 0 ? MatchState.Starting : MatchState.WaitingForPlayers;
                CollectionAssert.AreEqual(
                    new[] { None, None, None, None, None, MatchAudioCueTracker.Cue.Go, None, None, MatchAudioCueTracker.Cue.End, None, None },
                    Run(t, (first, false), (MatchState.Starting, false), (MatchState.Starting, false),
                        (MatchState.Active, true), (MatchState.Active, true),
                        (MatchState.Active, false), (MatchState.Active, false), (MatchState.Active, false),
                        (MatchState.Ended, false), (MatchState.Ended, false), (MatchState.WaitingForPlayers, false)),
                    $"round {round + 1}");
                Assert.IsFalse(t.RoundAudible, "the end cue stopped the music");
            }
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

        // ---- World-ready predicate (IntroArmGate input) ----------------------------------------------

        private static readonly PlayerRef Host = PlayerRef.FromEncoded(1), Joiner = PlayerRef.FromEncoded(2);

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

        [Test]
        public void Audio_APeerArrivingAfterGo_GetsTheMusicButNoStinger_AndOneArrivingAtTheEndGetsNothing()
        {
            var lateJoiner = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.JoinedRunning, lateJoiner.Observe(MatchState.Active, false));
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, lateJoiner.Observe(MatchState.Active, false));
            Assert.AreEqual(MatchAudioCueTracker.Cue.End, lateJoiner.Observe(MatchState.Ended, false), "it saw the round run");

            var midIntro = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, midIntro.Observe(MatchState.Active, true));
            Assert.AreEqual(MatchAudioCueTracker.Cue.Go, midIntro.Observe(MatchState.Active, false), "a joiner mid-intro gets the full GO");

            var atEnd = new MatchAudioCueTracker();
            Assert.AreEqual(MatchAudioCueTracker.Cue.None, atEnd.Observe(MatchState.Ended, false), "no stray end cue for a round it never saw");
        }

        // ---- EventBannerTiming ---------------------------------------------------------------------

        [Test]
        public void EventBanner_WaitsForGoToClear_AndSkipsItsStingRightAfterGo()
        {
            Assert.IsFalse(EventBannerTiming.CanShow(true, 0f), "never over the countdown");
            Assert.IsFalse(EventBannerTiming.CanShow(false, IntroCueTracker.GoHoldSeconds - 0.05f), "never over GO!");
            Assert.IsTrue(EventBannerTiming.CanShow(false, IntroCueTracker.GoHoldSeconds));
            Assert.IsFalse(EventBannerTiming.PlaysSting(IntroCueTracker.GoHoldSeconds), "the GO stinger just played");
            Assert.IsTrue(EventBannerTiming.PlaysSting(30f), "a later final minute (longer matches) keeps its sting");
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
    }
}
