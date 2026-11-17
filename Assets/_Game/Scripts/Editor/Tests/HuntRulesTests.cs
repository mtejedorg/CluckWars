using CluckWars.Abilities;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>The hunter's pure rules (<see cref="HuntRules"/>): what blocks the pounce, the count, why a mark ended. Numbers
    /// come from <c>MarkKill.asset</c> and <see cref="FeedbackTuning"/>, never copied.</summary>
    public class HuntRulesTests
    {
        private static MarkKillAbilitySO Asset()
        {
            var so = AssetDatabase.LoadAssetAtPath<MarkKillAbilitySO>("Assets/_Game/Data/Abilities/MarkKill.asset");
            Assert.IsNotNull(so, "MarkKill.asset missing");
            return so;
        }

        [Test]
        public void Blocker_FollowsEggWallFarStunPriority()
        {
            float range = Asset().KillRange;
            for (int mask = 0; mask < 16; mask++)
            {
                bool shelled = (mask & 1) != 0, walled = (mask & 2) != 0, far = (mask & 4) != 0, notStunned = (mask & 8) != 0;
                var got = HuntRules.BlockerFor(true, false, shelled, walled, far ? range + 1f : range - 1f, range, !notStunned);
                var want = shelled ? HuntBlocker.Shelled : walled ? HuntBlocker.Walled : far ? HuntBlocker.TooFar : notStunned ? HuntBlocker.NotStunned : HuntBlocker.None;
                Assert.AreEqual(want, got, $"shelled={shelled} walled={walled} far={far} notStunned={notStunned}");
            }
        }

        [Test]
        public void Blocker_NoneWhileArmingOrLegal()
        {
            float range = Asset().KillRange;
            Assert.AreEqual(HuntBlocker.None, HuntRules.BlockerFor(false, false, true, true, range + 5f, range, false), "arming names nothing");
            Assert.AreEqual(HuntBlocker.None, HuntRules.BlockerFor(true, true, true, true, range + 5f, range, false), "a legal pounce names nothing");
        }

        [Test]
        public void Blocker_OrderAgreesWithMarkerModeFor()
        {
            // The marker shows the shell before the wall, the glyph names the egg before the wall: the same precedence.
            foreach (bool shelled in new[] { false, true })
            foreach (bool walled in new[] { false, true })
            {
                var mode = MarkPresentationModel.MarkerModeFor(false, shelled, walled);
                var blocker = HuntRules.BlockerFor(true, false, shelled, walled, 0f, 10f, true);
                if (mode == MarkMarkerMode.Shelled) Assert.AreEqual(HuntBlocker.Shelled, blocker);
                else if (mode == MarkMarkerMode.Walled) Assert.AreEqual(HuntBlocker.Walled, blocker);
                else Assert.AreEqual(HuntBlocker.None, blocker);
            }
        }

        [Test]
        public void TooFar_UsesShippedKillRange()
        {
            float range = Asset().KillRange;
            Assert.IsFalse(HuntRules.TooFar(range, range), "exactly at the range is legal (distance <= range)");
            Assert.IsFalse(HuntRules.TooFar(range - 0.01f, range));
            Assert.IsTrue(HuntRules.TooFar(range + 0.01f, range));
        }

        [Test]
        public void Count_ArmingShowsShippedArmSecondsDownToOne()
        {
            int arm = UnityEngine.Mathf.RoundToInt(Asset().ArmSeconds);
            Assert.GreaterOrEqual(arm, 1);
            for (int s = arm; s >= 1; s--)
                Assert.AreEqual(s.ToString(), HuntRules.CountText(false, s, 0.5f), "arming shows the arm seconds, not the window");
        }

        [Test]
        public void Count_LiveSecondShowsTenthsNeverZero()
        {
            var so = Asset();
            float live = so.WindowSeconds - so.ArmSeconds;
            Assert.AreEqual(UnityEngine.Mathf.CeilToInt(live).ToString(), HuntRules.CountText(true, 0, live), "a full live window reads whole seconds");
            Assert.AreEqual("0.8", HuntRules.CountText(true, 0, 0.8f));
            Assert.AreEqual("0.1", HuntRules.CountText(true, 0, 0.01f), "alive never reads 0.0");
            Assert.AreEqual("1", HuntRules.CountText(true, 0, 1f));
            Assert.AreEqual("2", HuntRules.CountText(true, 0, 1.01f));
            for (float r = live; r > 0f; r -= 0.037f)
                Assert.AreNotEqual("0.0", HuntRules.CountText(true, 0, r));
            Assert.GreaterOrEqual(HuntRules.LiveTenths(0f), 1);
        }

        private static HuntLastFrame Last(bool canKill = false, bool shelled = false, bool walled = false, float remaining = 0f, MarkRole role = MarkRole.Assassin) =>
            new HuntLastFrame { Role = role, Armed = true, CanKill = canKill, Shelled = shelled, Walled = walled, Remaining = remaining };

        [Test]
        public void EndCause_Table()
        {
            float tol = FeedbackTuning.MarkEndExpiryToleranceSeconds;
            float expired = tol * 0.5f, early = tol * 4f;
            Assert.AreEqual(MarkEndCause.Safe, HuntRules.EndCauseFor(Last(walled: true, remaining: expired), true, false), "expired + wall");
            Assert.AreEqual(MarkEndCause.Safe, HuntRules.EndCauseFor(Last(shelled: true, remaining: expired), true, false), "expired + egg");
            Assert.AreEqual(MarkEndCause.Missed, HuntRules.EndCauseFor(Last(remaining: expired), true, false), "expired + star / far / nothing");
            Assert.AreEqual(MarkEndCause.Missed, HuntRules.EndCauseFor(Last(walled: true, remaining: early), true, false), "lost early is never SAFE");
            Assert.AreEqual(MarkEndCause.Missed, HuntRules.EndCauseFor(Last(remaining: early), true, false), "early end, not legal");
            Assert.AreEqual(MarkEndCause.Pounced, HuntRules.EndCauseFor(Last(canKill: true, remaining: early), true, false), "last frame legal");
            Assert.AreEqual(MarkEndCause.Pounced, HuntRules.EndCauseFor(Last(canKill: true, remaining: expired), true, false));
            Assert.AreEqual(MarkEndCause.None, HuntRules.EndCauseFor(Last(remaining: expired), false, false), "round stopped");
            Assert.AreEqual(MarkEndCause.None, HuntRules.EndCauseFor(Last(remaining: expired), true, true), "hunter removed");
            Assert.AreEqual(MarkEndCause.None, HuntRules.EndCauseFor(Last(remaining: expired, role: MarkRole.Victim), true, false), "not the hunter");
            Assert.AreEqual(MarkEndCause.None, HuntRules.EndCauseFor(Last(remaining: expired, role: MarkRole.None), true, false));
        }

        [Test]
        public void EndWord_OnlyForMissedAndSafe()
        {
            Assert.IsTrue(HuntRules.ShowsEndWord(MarkEndCause.Missed));
            Assert.IsTrue(HuntRules.ShowsEndWord(MarkEndCause.Safe));
            Assert.IsFalse(HuntRules.ShowsEndWord(MarkEndCause.Pounced));
            Assert.IsFalse(HuntRules.ShowsEndWord(MarkEndCause.None));
        }

        [Test]
        public void BracketColour_ArmingSeatLiveLegalYellowLiveBlockedRed()
        {
            Assert.AreEqual(CluckWars.UI.PlayerPalette.ForCorner(2), HuntRules.BracketColour(false, false, 2));
            Assert.AreEqual(FeedbackTuning.MarkKillColor, HuntRules.BracketColour(true, true, 2));
            Assert.AreEqual(FeedbackTuning.MarkHuntBlockedColor, HuntRules.BracketColour(true, false, 2));
            Assert.AreEqual(FeedbackTuning.MarkSkullLiveColor, FeedbackTuning.MarkHuntBlockedColor, "one red");
        }

        // ---- The hunter's screen edge (K10) ---------------------------------------------------------------------------

        [Test]
        public void ScreenTint_NothingUntilTheDrawLocks_ThenDarkWhileArming_ThenRedOrYellow()
        {
            foreach (bool armed in new[] { false, true })
            foreach (bool canKill in new[] { false, true })
                Assert.AreEqual(HuntScreenTint.None, HuntRules.ScreenTintFor(false, armed, canKill), $"draw running armed={armed} canKill={canKill}");
            Assert.AreEqual(HuntScreenTint.Arming, HuntRules.ScreenTintFor(true, false, false));
            Assert.AreEqual(HuntScreenTint.Arming, HuntRules.ScreenTintFor(true, false, true), "arming wins over a stray canKill");
            Assert.AreEqual(HuntScreenTint.Blocked, HuntRules.ScreenTintFor(true, true, false));
            Assert.AreEqual(HuntScreenTint.Pounce, HuntRules.ScreenTintFor(true, true, true));
        }

        [Test]
        public void ScreenTintColour_BlockedAndPounceAreTheBracketsOwn_OthersDrawNoGlow()
        {
            for (int corner = 0; corner < 4; corner++)
            {
                Assert.AreEqual(HuntRules.BracketColour(true, false, corner), HuntRules.ScreenTintColour(HuntScreenTint.Blocked, corner));
                Assert.AreEqual(HuntRules.BracketColour(true, true, corner), HuntRules.ScreenTintColour(HuntScreenTint.Pounce, corner));
                Assert.AreEqual(UnityEngine.Color.clear, HuntRules.ScreenTintColour(HuntScreenTint.None, corner));
                Assert.AreEqual(UnityEngine.Color.clear, HuntRules.ScreenTintColour(HuntScreenTint.Arming, corner), "arming is the dark edge only");
            }
            Assert.AreNotEqual(HuntRules.ScreenTintColour(HuntScreenTint.Blocked, 0), HuntRules.ScreenTintColour(HuntScreenTint.Pounce, 0));
        }
    }
}
