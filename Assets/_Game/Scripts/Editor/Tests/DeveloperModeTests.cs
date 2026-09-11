using CluckWars.Settings;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the Developer Mode gate: that it persists, that it defaults to OFF, and that
    /// <see cref="PlayerPreferences.ResetCache"/> drops its cache along with the others.
    /// </summary>
    /// <remarks>
    /// This preference carries more weight than its two neighbours. Range guides and reduced
    /// motion change how the game looks; this one decides whether a player build shows a route
    /// into <c>AbilityLab.unity</c> — a scene that spawns practice dummies, holds the match
    /// clock open for an hour and pins the session to Solo. The scene is in Build Settings and
    /// enabled (see <c>ProjectConfigTests</c>), so nothing else is keeping players out.
    ///
    /// Modelled on <c>ReducedMotionTests</c>, including its discipline about
    /// <c>PlayerPrefs</c>: that is real per-user storage on the developer's own machine, not
    /// a fixture, so every test here puts back what it found.
    /// </remarks>
    public sealed class DeveloperModeTests
    {
        /// <summary>
        /// Runs <paramref name="body"/> with the developer-mode key wiped, then restores the
        /// developer's own value. A test run must not be able to leave Developer Mode switched
        /// on in the Editor it ran in.
        /// </summary>
        private static void WithCleanPrefs(System.Action body)
        {
            bool had   = PlayerPrefs.HasKey(PlayerPreferences.DeveloperModeKey);
            int  saved = PlayerPrefs.GetInt(PlayerPreferences.DeveloperModeKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.DeveloperModeKey);
                PlayerPreferences.ResetCache();
                body();
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.DeveloperModeKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.DeveloperModeKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        [Test]
        public void DeveloperMode_DefaultsToOff_AndAnExplicitChoiceReachesDisk()
        {
            WithCleanPrefs(() =>
            {
                Assert.IsFalse(PlayerPreferences.DeveloperModeEnabled,
                    "With no stored preference, Developer Mode must default to OFF. It is the " +
                    "only thing hiding the Ability Lab from a player build — a default of ON " +
                    "would put a dev scene on every player's main menu.");

                PlayerPreferences.DeveloperModeEnabled = true;
                PlayerPreferences.ResetCache();
                Assert.IsTrue(PlayerPreferences.DeveloperModeEnabled,
                    "An explicit 'on' must survive a cache drop, i.e. it really reached " +
                    "PlayerPrefs. Otherwise the toggle appears to work and is forgotten on " +
                    "relaunch, which on a phone means re-enabling it every single session.");

                PlayerPreferences.DeveloperModeEnabled = false;
                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.DeveloperModeEnabled,
                    "An explicit 'off' must survive a cache drop too — a setter that only ever " +
                    "wrote the 'on' case would pass a one-directional test, and would mean " +
                    "Developer Mode could be switched on but never off.");
            });
        }

        [Test]
        public void DeveloperMode_HasItsOwnPrefsKey()
        {
            Assert.AreNotEqual(PlayerPreferences.AbilityRangeGuidesKey,
                PlayerPreferences.DeveloperModeKey,
                "Developer Mode shares a PlayerPrefs key with the range guides, so turning the " +
                "guides on would unlock the Ability Lab. PlayerPrefs is a single flat store — " +
                "every key must be distinct.");

            Assert.AreNotEqual(PlayerPreferences.ReducedMotionKey,
                PlayerPreferences.DeveloperModeKey,
                "Developer Mode shares a PlayerPrefs key with reduced motion.");

            StringAssert.StartsWith("CluckWars.", PlayerPreferences.DeveloperModeKey,
                "PlayerPrefs keys are namespaced so nothing a future plugin writes can collide " +
                "with them.");
        }

        [Test]
        public void ResetCache_DropsTheDeveloperModeCache_NotJustItsNeighbours()
        {
            // The regression this exists for: ResetCache began life clearing one flag, and each
            // preference added since has had to remember to add itself. One that forgets leaks
            // across Editor play sessions AND makes every test above assert against a stale
            // cache instead of PlayerPrefs.
            WithCleanPrefs(() =>
            {
                Assert.IsFalse(PlayerPreferences.DeveloperModeEnabled, "Fixture precondition.");

                // Write behind the property's back so only a genuine re-read can see it.
                PlayerPrefs.SetInt(PlayerPreferences.DeveloperModeKey, 1);
                PlayerPreferences.ResetCache();

                Assert.IsTrue(PlayerPreferences.DeveloperModeEnabled,
                    "ResetCache did not drop the developer-mode cache, so the getter is still " +
                    "serving the pre-reset value.");
            });
        }

        [Test]
        public void TurningDeveloperModeOff_IsEnoughToHideTheLab_WithNoRestart()
        {
            // MenuUiController reads this preference every time the main menu is shown, rather
            // than once at build time, precisely so a tester who turns the gate off sees the
            // button go away on the next HOME rather than on the next launch. Assert the
            // preference supports that, i.e. it is not latched anywhere.
            WithCleanPrefs(() =>
            {
                PlayerPreferences.DeveloperModeEnabled = true;
                Assert.IsTrue(PlayerPreferences.DeveloperModeEnabled, "Fixture precondition.");

                PlayerPreferences.DeveloperModeEnabled = false;

                Assert.IsFalse(PlayerPreferences.DeveloperModeEnabled,
                    "The getter still reports ON immediately after being set OFF, so the value " +
                    "is latched somewhere. The main-menu button would stay visible for the rest " +
                    "of the session after the tester switched the gate off.");
            });
        }
    }
}
