using System;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP6 / DECISIONS_2026-10-21 C4/C5: free rotation in the menus and the waiting room, locked to the held orientation
    /// from GET READY through the results, upside-down portrait off.
    /// </summary>
    public sealed class OrientationPolicyTests
    {
        private static string Read(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        [Test]
        public void EveryMatchState_HasADecidedLock()
        {
            // Exhaustive on purpose: a new MatchState fails here until someone decides whether it rotates.
            foreach (MatchState state in Enum.GetValues(typeof(MatchState)))
            {
                bool expected = state switch
                {
                    MatchState.WaitingForPlayers => false,
                    MatchState.Starting => true,
                    MatchState.Active => true,
                    MatchState.Ended => true,
                    _ => throw new AssertionException($"MatchState.{state} has no orientation decision in this test."),
                };
                Assert.AreEqual(expected, OrientationLockRules.ShouldLock(true, state), state.ToString());
                Assert.IsFalse(OrientationLockRules.ShouldLock(false, state), $"no match object ({state}) rotates freely");
            }
        }

        [Test]
        public void ARound_LocksAtGetReady_StaysLockedThroughPlayAgain_UnlocksInTheWaitingRoom()
        {
            var seq = new[]
            {
                (MatchState.WaitingForPlayers, false),
                (MatchState.Starting, true),     // GET READY
                (MatchState.Active, true),
                (MatchState.Ended, true),        // results
                (MatchState.Starting, true),     // PLAY AGAIN
                (MatchState.Active, true),
                (MatchState.Ended, true),
                (MatchState.WaitingForPlayers, false),   // BACK TO LOBBY
            };
            foreach (var (state, locked) in seq)
                Assert.AreEqual(locked, OrientationLockRules.ShouldLock(true, state), state.ToString());
        }

        [TestCase(ScreenOrientation.LandscapeLeft,  2400, 1080, ScreenOrientation.LandscapeLeft)]
        [TestCase(ScreenOrientation.LandscapeRight, 2400, 1080, ScreenOrientation.LandscapeRight)]
        [TestCase(ScreenOrientation.Portrait,       1080, 2400, ScreenOrientation.Portrait)]
        [TestCase(ScreenOrientation.AutoRotation,   2400, 1080, ScreenOrientation.LandscapeLeft)]
        [TestCase(ScreenOrientation.AutoRotation,   1080, 2400, ScreenOrientation.Portrait)]
        [TestCase(ScreenOrientation.LandscapeLeft,  1080, 2400, ScreenOrientation.Portrait)]       // the shape wins a disagreement
        [TestCase(ScreenOrientation.Portrait,       2400, 1080, ScreenOrientation.LandscapeLeft)]
        [TestCase(ScreenOrientation.AutoRotation,   1000, 1000, ScreenOrientation.LandscapeLeft)]
        public void LockTarget_IsTheHeldOrientation(ScreenOrientation reported, int w, int h, ScreenOrientation expected)
        {
            Assert.AreEqual(expected, OrientationLockRules.LockTarget(reported, w, h));
        }

        [TestCase(ScreenOrientation.LandscapeLeft)]
        [TestCase(ScreenOrientation.LandscapeRight)]
        [TestCase(ScreenOrientation.Portrait)]
        [TestCase(ScreenOrientation.PortraitUpsideDown)]
        public void OnlyAllow_OpensExactlyOneOrientation(ScreenOrientation o)
        {
            var f = OrientationLockRules.OnlyAllow(o);
            int open = (f.LandscapeLeft ? 1 : 0) + (f.LandscapeRight ? 1 : 0) + (f.Portrait ? 1 : 0) + (f.PortraitUpsideDown ? 1 : 0);
            Assert.AreEqual(1, open, f.ToString());
        }

        // ---- Lock / restore state machine (OrientationLockState) ----------------------------------------------------

        private static readonly AutorotateFlags Project = new AutorotateFlags(true, true, true, false);

        [Test]
        public void Lock_AllowsOnlyTheHeldOrientation_AndRemembersWhatItFound()
        {
            var s = new OrientationLockState();
            var write = s.Update(true, Project, ScreenOrientation.LandscapeRight, 2400, 1080);
            Assert.IsTrue(write.HasValue);
            Assert.AreEqual(OrientationLockRules.OnlyAllow(ScreenOrientation.LandscapeRight), write.Value);
            Assert.IsTrue(s.Locked);
            Assert.AreEqual(ScreenOrientation.LandscapeRight, s.LockedTo);
            Assert.AreEqual(Project, s.Restore);
        }

        [Test]
        public void SteadyState_WritesNothing_LockedOrNot()
        {
            var s = new OrientationLockState();
            Assert.IsNull(s.Update(false, Project, ScreenOrientation.Portrait, 1080, 2400), "free and still free");
            s.Update(true, Project, ScreenOrientation.Portrait, 1080, 2400);
            // While locked the switches read back as the lock; a repeat frame must not re-capture them as the restore.
            var locked = OrientationLockRules.OnlyAllow(ScreenOrientation.Portrait);
            Assert.IsNull(s.Update(true, locked, ScreenOrientation.Portrait, 1080, 2400));
            Assert.AreEqual(Project, s.Restore);
        }

        [Test]
        public void Unlock_GivesBackExactlyWhatWasFound()
        {
            var found = new AutorotateFlags(true, false, true, false);   // e.g. something else had narrowed them
            var s = new OrientationLockState();
            s.Update(true, found, ScreenOrientation.Portrait, 1080, 2400);
            var write = s.Update(false, OrientationLockRules.OnlyAllow(ScreenOrientation.Portrait), ScreenOrientation.Portrait, 1080, 2400);
            Assert.AreEqual(found, write);
            Assert.IsFalse(s.Locked);
        }

        [Test]
        public void Release_RestoresOnlyWhenLocked()
        {
            var s = new OrientationLockState();
            Assert.IsNull(s.Release(), "leaving the scene unlocked writes nothing");
            s.Update(true, Project, ScreenOrientation.LandscapeLeft, 2400, 1080);
            Assert.AreEqual(Project, s.Release());
            Assert.IsNull(s.Release(), "a second release is a no-op");
        }

        [Test]
        public void Relock_AfterTheWaitingRoom_FollowsTheNewHeldOrientation()
        {
            var s = new OrientationLockState();
            s.Update(true, Project, ScreenOrientation.LandscapeLeft, 2400, 1080);
            s.Update(false, OrientationLockRules.OnlyAllow(ScreenOrientation.LandscapeLeft), ScreenOrientation.LandscapeLeft, 2400, 1080);
            // The player turned the phone in the waiting room; the next GET READY locks portrait.
            var write = s.Update(true, Project, ScreenOrientation.Portrait, 1080, 2400);
            Assert.AreEqual(OrientationLockRules.OnlyAllow(ScreenOrientation.Portrait), write);
            Assert.AreEqual(Project, s.Restore);
        }

        [Test]
        public void Policy_WritesTheSwitches_OnlyThroughTheStateMachine()
        {
            string src = Read("Assets/_Game/Scripts/UI/OrientationPolicy.cs");
            StringAssert.Contains("_lock.Update(wanted, AutorotateFlags.Read(), Screen.orientation, Screen.width, Screen.height)", src);
            StringAssert.Contains("_lock.Release()", src);
        }

        [Test]
        public void ProjectSettings_AutoRotate_BothOrientations_UpsideDownOff()
        {
            Assert.AreEqual(UIOrientation.AutoRotation, PlayerSettings.defaultInterfaceOrientation, "menus rotate freely");
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeLeft);
            Assert.IsTrue(PlayerSettings.allowedAutorotateToLandscapeRight);
            Assert.IsTrue(PlayerSettings.allowedAutorotateToPortrait);
            Assert.IsFalse(PlayerSettings.allowedAutorotateToPortraitUpsideDown, "C5: upside-down portrait is off");
        }

        [Test]
        public void Policy_ObservesTheReplicatedState_ReadOnly_AndExistsWithTheMatchHud()
        {
            string src = Read("Assets/_Game/Scripts/UI/OrientationPolicy.cs");
            StringAssert.Contains("gm.State", src);
            Assert.IsFalse(Regex.IsMatch(src, @"\.State\s*=[^=]"), "OrientationPolicy must never write GameManager.State.");
            Assert.IsFalse(Regex.IsMatch(src, @"\bRpc\b|\[Networked\]"), "the lock is local to each peer.");
            StringAssert.Contains("gameObject.AddComponent<OrientationPolicy>()", Read("Assets/_Game/Scripts/UI/MatchHudController.cs"));
        }
    }
}
