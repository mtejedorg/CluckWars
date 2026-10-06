using CluckWars.Settings;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>Guards <see cref="PlayerPreferences.PerformanceModeEnabled"/>: the platform-dependent default, persistence, key hygiene and cache reset.</summary>
    public sealed class PerformanceModePreferenceTests
    {
        private static void WithCleanPrefs(System.Action body)
        {
            bool had = PlayerPrefs.HasKey(PlayerPreferences.PerformanceModeKey);
            int saved = PlayerPrefs.GetInt(PlayerPreferences.PerformanceModeKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.PerformanceModeKey);
                PlayerPreferences.ResetCache();
                body();
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(PlayerPreferences.PerformanceModeKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.PerformanceModeKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        [Test]
        public void PerformanceMode_DefaultsToOnForMobilePlatforms_OffOtherwise_AndAChoiceSticks()
        {
            WithCleanPrefs(() =>
            {
                Assert.AreEqual(Application.isMobilePlatform, PlayerPreferences.PerformanceModeEnabled,
                    "With no stored choice, Performance Mode must follow Application.isMobilePlatform " +
                    "(ON on phones/tablets, OFF on desktop).");

                // An explicit choice beats the platform default in BOTH directions, so the
                // opposite of the default is the one that proves the write went to disk.
                bool opposite = !Application.isMobilePlatform;
                PlayerPreferences.PerformanceModeEnabled = opposite;
                PlayerPreferences.ResetCache();
                Assert.AreEqual(opposite, PlayerPreferences.PerformanceModeEnabled,
                    "An explicit choice must survive a cache drop, i.e. it really reached PlayerPrefs.");

                PlayerPreferences.PerformanceModeEnabled = !opposite;
                PlayerPreferences.ResetCache();
                Assert.AreEqual(!opposite, PlayerPreferences.PerformanceModeEnabled,
                    "Changing the choice back must persist too.");
            });
        }

        [Test]
        public void PerformanceMode_HasItsOwnNamespacedPrefsKey()
        {
            Assert.AreNotEqual(PlayerPreferences.PerformanceModeKey, PlayerPreferences.AbilityRangeGuidesKey);
            Assert.AreNotEqual(PlayerPreferences.PerformanceModeKey, PlayerPreferences.ReducedMotionKey);
            Assert.AreNotEqual(PlayerPreferences.PerformanceModeKey, PlayerPreferences.DeveloperModeKey);
            StringAssert.StartsWith("CluckWars.", PlayerPreferences.PerformanceModeKey);
        }

        [Test]
        public void ResetCache_DropsThePerformanceModeCache()
        {
            WithCleanPrefs(() =>
            {
                bool before = PlayerPreferences.PerformanceModeEnabled; // loads and caches the default
                PlayerPrefs.SetInt(PlayerPreferences.PerformanceModeKey, before ? 0 : 1); // behind the property's back
                PlayerPreferences.ResetCache();

                Assert.AreEqual(!before, PlayerPreferences.PerformanceModeEnabled,
                    "ResetCache did not drop the Performance Mode cache; the getter served the pre-reset value.");
            });
        }
    }
}
