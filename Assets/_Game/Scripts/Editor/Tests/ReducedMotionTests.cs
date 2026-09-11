using System.Reflection;
using CluckWars.Settings;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the reduced-motion accessibility preference: that it persists, that it defaults
    /// to the opposite of <see cref="PlayerPreferences.AbilityRangeGuidesEnabled"/> on purpose,
    /// and — the part that actually matters — that
    /// <see cref="MatchCamera.ApplyShake"/> still honours it.
    /// </summary>
    /// <remarks>
    /// The gate is one <c>if</c> at the top of a method whose body is entirely about shake
    /// bookkeeping, which is exactly the kind of line a later refactor of that bookkeeping
    /// drops without noticing. Nothing else in the game would fail if it went: the shake would
    /// simply come back, and only a motion-sensitive player would ever find out. So the
    /// suppression is asserted as OBSERVED BEHAVIOUR through the public entry point rather
    /// than by checking that some flag is read somewhere.
    /// </remarks>
    public sealed class ReducedMotionTests
    {
        /// <summary>
        /// Runs <paramref name="body"/> with the two preference keys wiped, then puts the
        /// developer's own values back. The suite must not be able to change the machine it
        /// runs on — <c>PlayerPrefs</c> is real per-user storage, not a fixture.
        /// </summary>
        private static void WithCleanPrefs(System.Action body)
        {
            bool hadReduced   = PlayerPrefs.HasKey(PlayerPreferences.ReducedMotionKey);
            int  savedReduced = PlayerPrefs.GetInt(PlayerPreferences.ReducedMotionKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.ReducedMotionKey);
                PlayerPreferences.ResetCache();
                body();
            }
            finally
            {
                if (hadReduced) PlayerPrefs.SetInt(PlayerPreferences.ReducedMotionKey, savedReduced);
                else PlayerPrefs.DeleteKey(PlayerPreferences.ReducedMotionKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        /// <summary>
        /// Reads <c>MatchCamera</c>'s private shake peak. Reflection rather than a test-only
        /// public accessor: the shake state is genuinely internal bookkeeping and widening it
        /// for a test would invite production code to start depending on it.
        /// </summary>
        private static float ShakePeakOf(MatchCamera camera)
        {
            var field = typeof(MatchCamera).GetField("_shakePeak",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field,
                "MatchCamera._shakePeak is gone. If the shake state was renamed or reshaped, " +
                "re-point this test at it — do not delete it, it is the only thing asserting " +
                "that reduced motion actually suppresses shake.");
            return (float)field.GetValue(camera);
        }

        /// <summary>
        /// A <c>MatchCamera</c> on a throwaway GameObject. <c>Awake</c> is deliberately NOT
        /// driven: <c>ApplyShake</c> touches only the shake fields, so an uninitialised camera
        /// is the narrowest possible fixture for it.
        /// </summary>
        private static MatchCamera NewProbeCamera(out GameObject go)
        {
            go = new GameObject("ReducedMotionProbeCamera");
            return go.AddComponent<MatchCamera>();
        }

        // ---- Persistence -----------------------------------------------------

        [Test]
        public void ReducedMotion_DefaultsToOff_AndAnExplicitChoiceReachesDisk()
        {
            WithCleanPrefs(() =>
            {
                Assert.IsFalse(PlayerPreferences.ReducedMotionEnabled,
                    "With no stored preference, reduced motion must default to OFF. It removes " +
                    "feedback the game is designed around, so it is opt-in — unlike the range " +
                    "guides, which add information and default ON.");

                PlayerPreferences.ReducedMotionEnabled = true;
                PlayerPreferences.ResetCache();
                Assert.IsTrue(PlayerPreferences.ReducedMotionEnabled,
                    "An explicit 'on' must survive a cache drop, i.e. it really reached PlayerPrefs.");

                PlayerPreferences.ReducedMotionEnabled = false;
                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.ReducedMotionEnabled,
                    "An explicit 'off' must survive a cache drop too — a setter that only ever " +
                    "wrote the 'on' case would pass a one-directional test.");
            });
        }

        [Test]
        public void ReducedMotion_HasItsOwnPrefsKey()
        {
            Assert.AreNotEqual(PlayerPreferences.AbilityRangeGuidesKey,
                PlayerPreferences.ReducedMotionKey,
                "The two preferences share a PlayerPrefs key, so each one silently overwrites " +
                "the other. PlayerPrefs is a single flat store — every key must be distinct.");

            StringAssert.StartsWith("CluckWars.", PlayerPreferences.ReducedMotionKey,
                "PlayerPrefs keys are namespaced so nothing a future plugin writes can collide " +
                "with them.");
        }

        [Test]
        public void ResetCache_DropsTheReducedMotionCache_NotJustTheRangeGuidesOne()
        {
            // The regression this exists for: ResetCache started life clearing one flag, and a
            // second preference that forgot to add itself would leak across Editor play sessions
            // AND make every test above assert against a stale cache instead of PlayerPrefs.
            WithCleanPrefs(() =>
            {
                Assert.IsFalse(PlayerPreferences.ReducedMotionEnabled, "Fixture precondition.");

                // Write behind the property's back so only a genuine re-read can see it.
                PlayerPrefs.SetInt(PlayerPreferences.ReducedMotionKey, 1);
                PlayerPreferences.ResetCache();

                Assert.IsTrue(PlayerPreferences.ReducedMotionEnabled,
                    "ResetCache did not drop the reduced-motion cache, so the getter is still " +
                    "serving the pre-reset value.");
            });
        }

        // ---- The gate --------------------------------------------------------

        [Test]
        public void ApplyShake_IsFullySuppressed_WhenReducedMotionIsOn()
        {
            WithCleanPrefs(() =>
            {
                GameObject go = null;
                try
                {
                    var camera = NewProbeCamera(out go);

                    PlayerPreferences.ReducedMotionEnabled = true;
                    camera.ApplyShake(FeedbackTuning.DeathShakeMagnitude,
                                      FeedbackTuning.DeathShakeDurationSeconds);

                    Assert.AreEqual(0f, ShakePeakOf(camera), 1e-5f,
                        "Reduced motion is on and the camera still accepted a shake impulse. " +
                        "Suppression is FULL, not attenuated: a scaled-down shake is still " +
                        "unrequested camera movement and still triggers the vestibular response " +
                        "this preference exists to avoid.");
                }
                finally
                {
                    if (go != null) Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void ApplyShake_StillShakes_WhenReducedMotionIsOff()
        {
            // Without this the suppression test would also pass against a MatchCamera whose
            // shake was simply broken, which is the cheapest possible way to "fix" the feature.
            WithCleanPrefs(() =>
            {
                GameObject go = null;
                try
                {
                    var camera = NewProbeCamera(out go);

                    PlayerPreferences.ReducedMotionEnabled = false;
                    camera.ApplyShake(FeedbackTuning.DeathShakeMagnitude,
                                      FeedbackTuning.DeathShakeDurationSeconds);

                    Assert.AreEqual(FeedbackTuning.DeathShakeMagnitude, ShakePeakOf(camera), 1e-5f,
                        "With reduced motion off, ApplyShake must pass the requested magnitude " +
                        "through untouched.");
                }
                finally
                {
                    if (go != null) Object.DestroyImmediate(go);
                }
            });
        }

        [Test]
        public void ApplyShake_HonoursALiveToggle_WithoutNeedingARestart()
        {
            // The preference is read per call, not cached at Awake, precisely so a player who
            // turns this on mid-match stops being shaken immediately rather than next launch.
            WithCleanPrefs(() =>
            {
                GameObject go = null;
                try
                {
                    var camera = NewProbeCamera(out go);

                    // ApplyShake is max-wins, so the second impulse must be the STRONGER one or
                    // the assertion below would also pass on a camera that simply ignored it.
                    Assert.Greater(FeedbackTuning.DeathShakeMagnitude,
                                   FeedbackTuning.CasterMicroShakeMagnitude,
                        "This test needs the death shake to out-rank the caster micro-shake to " +
                        "discriminate. Re-tuned so it no longer does? Swap in another pair.");

                    PlayerPreferences.ReducedMotionEnabled = false;
                    camera.ApplyShake(FeedbackTuning.CasterMicroShakeMagnitude,
                                      FeedbackTuning.CasterMicroShakeDurationSeconds);
                    Assert.Greater(ShakePeakOf(camera), 0f, "Fixture precondition.");

                    PlayerPreferences.ReducedMotionEnabled = true;
                    camera.ApplyShake(FeedbackTuning.DeathShakeMagnitude,
                                      FeedbackTuning.DeathShakeDurationSeconds);

                    Assert.AreEqual(FeedbackTuning.CasterMicroShakeMagnitude, ShakePeakOf(camera), 1e-5f,
                        "A shake issued AFTER the player turned reduced motion on was still " +
                        "accepted — the preference is being cached somewhere it should not be, " +
                        "so the toggle only takes effect on the next launch.");
                }
                finally
                {
                    if (go != null) Object.DestroyImmediate(go);
                }
            });
        }
    }
}
