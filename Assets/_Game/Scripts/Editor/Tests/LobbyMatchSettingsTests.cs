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
        public void LobbyUxml_SettingValues_AreLeftBlankForTheControllerToFill()
        {
            // The menu lobby's TIME and GOAL values carry NO authored text any more: the wording
            // moved into the dictionary (lobby.value.goal) and MenuUiController.RefreshMatchSettings
            // always fills both from the live MatchConfig. An authored number could only go stale.
            var tree = TestAssets.Load<VisualTreeAsset>(TestAssets.LobbyUxmlPath).CloneTree();

            foreach (var name in new[] { "SetTime", "SetGoal" })
            {
                var label = tree.Q<Label>(name);
                Assert.IsNotNull(label,
                    $"{TestAssets.LobbyUxmlPath} has no Label named '{name}'. " +
                    "MenuUiController.RefreshMatchSettings queries it by name.");
                Assert.IsEmpty(label.text,
                    $"'{name}' has authored text '{label.text}'. It is bound at runtime from " +
                    "MatchConfig; an authored value is a number that can drift from the real rules.");
            }
        }

        [Test]
        public void MatchOverlaysUxml_SettingValues_AreBlank_AndBoundFromTheLiveConfig()
        {
            // Phase 4: same rule as the menu lobby above. The in-match waiting room's TIME and GOAL
            // carry no authored number (an authored one could only go stale); MatchOverlaysController
            // .RefreshLobby fills both from MatchConfig through MatchSettingsText on the very frame the
            // overlay opens, so no frame shows the blank.
            var tree = TestAssets.Load<VisualTreeAsset>(TestAssets.MatchOverlaysUxmlPath).CloneTree();
            foreach (var name in new[] { "LobbySettingTime", "LobbySettingGoal" })
            {
                var label = tree.Q<Label>(name);
                Assert.IsNotNull(label,
                    $"{TestAssets.MatchOverlaysUxmlPath} has no Label named '{name}'. " +
                    "MatchOverlaysController.RefreshLobby queries it by name.");
                Assert.IsEmpty(label.text,
                    $"'{name}' has authored text '{label.text}'. It is bound at runtime from " +
                    "MatchConfig; an authored value is a number that can drift from the real rules.");
            }

            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                "Assets/_Game/Scripts/UI/MatchOverlaysController.cs"));
            StringAssert.Contains("_lobbySettingTime.text = MatchSettingsText.Time(_matchConfig.MatchDurationSeconds)", src,
                "the waiting room's TIME must come from MatchConfig through MatchSettingsText");
            StringAssert.Contains("_lobbySettingGoal.text = MatchSettingsText.Goal(_matchConfig.FoodTargetToWin)", src,
                "the waiting room's GOAL must come from MatchConfig through MatchSettingsText");
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
