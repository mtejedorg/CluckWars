using CluckWars.Gameplay;
using CluckWars.Installers;
using CluckWars.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the "MATCH SETTINGS" card against advertising rules the match does not
    /// enforce. The lobby once claimed "TIME 3:00 / GOAL 150 food" while MatchConfig.asset
    /// said 45 s / 40 food and the in-match HUD said so too — the labels carried
    /// <c>name</c> attributes but nothing ever bound them, so the placeholder copy shipped.
    /// </summary>
    /// <remarks>
    /// Every expectation here is <b>derived from the loaded MatchConfig.asset</b>, never
    /// from the literals "0:45" / "40 food". A test that hard-codes its own inputs passes
    /// forever regardless of what the game ships — the same trap that let a +20% speed
    /// drift stay green in BalanceOracleTests.
    /// </remarks>
    public sealed class LobbyMatchSettingsTests
    {
        // ---- Formatter ----------------------------------------------------------

        [Test]
        public void MatchSettingsText_RendersTheLiveConfig_InTheCardsFormat()
        {
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            int expectedMinutes = Mathf.FloorToInt(cfg.MatchDurationSeconds / 60f);
            int expectedSeconds = Mathf.FloorToInt(cfg.MatchDurationSeconds - expectedMinutes * 60f);

            Assert.AreEqual($"{expectedMinutes}:{expectedSeconds:00}",
                MatchSettingsText.Time(cfg.MatchDurationSeconds),
                "The TIME row must read as m:ss with a zero-padded seconds field. A bare " +
                "float ('45') or an unpadded one ('0:5') reads as a different match length.");

            Assert.AreEqual($"{cfg.FoodTargetToWin} food",
                MatchSettingsText.Goal(cfg.FoodTargetToWin),
                "The GOAL row must name the food target from MatchConfig. This is the number " +
                "the player plays toward; the in-match HUD shows the same one as 'FIRST TO N'.");
        }

        [Test]
        public void MatchSettingsText_Time_TruncatesFractionalSeconds()
        {
            // MatchDurationSeconds is a float, so a fractional value is authorable. Pinning
            // truncation (not rounding) keeps the lobby agreeing with the live match timer
            // in MatchHudController, which also floors. Rounding would advertise 0:46 for a
            // match the HUD starts counting down from 00:45.
            Assert.AreEqual("0:45", MatchSettingsText.Time(45.9f),
                "Fractional seconds must truncate, matching MatchHudController's countdown.");
            Assert.AreEqual("1:05", MatchSettingsText.Time(65f),
                "Seconds must roll into minutes and stay zero-padded.");
            Assert.AreEqual("0:00", MatchSettingsText.Time(-1f),
                "A negative duration must clamp, not render '-1:-1'.");
        }

        // ---- Authored UXML ------------------------------------------------------

        [Test]
        public void LobbyUxml_AuthoredSettings_MatchTheLiveConfig()
        {
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            AssertAuthoredSettings(
                TestAssets.LobbyUxmlPath, "SetTime", "SetGoal", cfg,
                "the menu lobby (MenuUiController.RefreshMatchSettings)");
        }

        [Test]
        public void MatchOverlaysUxml_AuthoredSettings_MatchTheLiveConfig()
        {
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            AssertAuthoredSettings(
                TestAssets.MatchOverlaysUxmlPath, "LobbySettingTime", "LobbySettingGoal", cfg,
                "the in-match waiting room (MatchOverlaysController.RefreshLobby)");
        }

        /// <summary>
        /// Asserts the text a settings card is authored with equals what the runtime binding
        /// will overwrite it with. Both must agree: the authored value is what the player
        /// sees for the frame before the controller runs, and what a reader of the UXML
        /// believes the rules are.
        /// </summary>
        private static void AssertAuthoredSettings(
            string uxmlPath, string timeLabelName, string goalLabelName,
            MatchConfigSO cfg, string boundBy)
        {
            var uxml = TestAssets.Load<VisualTreeAsset>(uxmlPath);
            var tree = uxml.CloneTree();

            var time = tree.Q<Label>(timeLabelName);
            var goal = tree.Q<Label>(goalLabelName);

            Assert.IsNotNull(time,
                $"{uxmlPath} has no Label named '{timeLabelName}'. {boundBy} queries it by name — " +
                "renaming or removing it silently leaves the placeholder duration on screen.");
            Assert.IsNotNull(goal,
                $"{uxmlPath} has no Label named '{goalLabelName}'. {boundBy} queries it by name — " +
                "renaming or removing it silently leaves the placeholder goal on screen.");

            Assert.AreEqual(MatchSettingsText.Time(cfg.MatchDurationSeconds), time.text,
                $"{uxmlPath} advertises TIME '{time.text}' but MatchConfig.asset is " +
                $"{cfg.MatchDurationSeconds}s. Two numbers describing one match will drift; " +
                "MatchConfig is the authority. Re-author the UXML text to match it.");
            Assert.AreEqual(MatchSettingsText.Goal(cfg.FoodTargetToWin), goal.text,
                $"{uxmlPath} advertises GOAL '{goal.text}' but MatchConfig.asset is " +
                $"{cfg.FoodTargetToWin} food. The lobby must not promise rules the match " +
                "will not enforce.");
        }

        // ---- The single serialized reference ------------------------------------

        [Test]
        public void ProjectContextPrefab_MatchConfigSlot_ResolvesToTheRealAsset()
        {
            // MatchConfigSO is bound project-wide from this one inspector slot (it used to
            // be a second slot on GameInstaller in Game.unity, which is how the lobby and
            // the match came to disagree). An empty slot here is silent: ProjectInstaller
            // falls back to a default instance, so every consumer — menu lobby, GameManager,
            // ChickenCargo, FusionNetworkService — runs on numbers nobody authored.
            var prefab = TestAssets.Load<GameObject>(TestAssets.ProjectContextPrefabPath);
            var cfg    = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            var installer = prefab.GetComponentInChildren<ProjectInstaller>(true);
            Assert.IsNotNull(installer,
                $"{TestAssets.ProjectContextPrefabPath} has no ProjectInstaller. Nothing would " +
                "be bound at all and the game cannot resolve a single service.");

            var slot = TestAssets.PrivateField<MatchConfigSO>(installer, "_matchConfig");

            Assert.IsNotNull(slot,
                "ProjectInstaller._matchConfig is empty on ProjectContext.prefab. The match " +
                "would silently run on MatchConfigSO's C# field initialisers instead of " +
                $"{TestAssets.MatchConfigPath}. Assign the asset to the slot.");
            Assert.AreSame(cfg, slot,
                "ProjectInstaller._matchConfig points at a different MatchConfigSO than " +
                $"{TestAssets.MatchConfigPath}. There must be exactly one match config in " +
                "play, or the lobby and the match can disagree again.");
        }
    }
}
