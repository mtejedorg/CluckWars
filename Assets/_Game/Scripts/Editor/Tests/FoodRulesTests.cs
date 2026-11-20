using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The pile food is balance data (D1b, 2026-11-20): <see cref="MatchConfigSO"/> carries the three amounts, the map generator
    /// spawns them, the oracle measures them. Nothing here restates an amount: the rules are checked on the shipped config.
    /// </summary>
    public sealed class FoodRulesTests
    {
        private const string MapGeneratorSource = "Assets/_Game/Scripts/Gameplay/MapGenerator.cs";

        private static MatchConfigSO Config => TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

        private static OracleFood FoodOf(MatchConfigSO c) =>
            new OracleFood { Personal = c.PersonalPileFood, Contested = c.ContestedPileFood, Centre = c.CentrePileFood };

        [Test]
        public void ShippedFood_ObeysMaestrosRules()
        {
            var cfg = Config;
            var food = FoodOf(cfg);
            Assert.IsTrue(FoodRules.OwnPlusOneIsNotEnough(food, cfg.FoodTargetToWin),
                $"own pile {food.Personal} + one contested {food.Contested} must stay under the win target {cfg.FoodTargetToWin}");
            Assert.IsTrue(FoodRules.OwnPlusTwoIsEnough(food, cfg.FoodTargetToWin),
                $"own pile {food.Personal} + two contested {food.Contested} must reach the win target {cfg.FoodTargetToWin}");
        }

        [Test]
        public void Rules_RejectFoodThatBreaksThem()
        {
            var tooMuch = new OracleFood { Personal = 20f, Contested = 20f, Centre = 0f };
            var tooLittle = new OracleFood { Personal = 5f, Contested = 10f, Centre = 0f };
            Assert.IsFalse(FoodRules.OwnPlusOneIsNotEnough(tooMuch, 40f));
            Assert.IsFalse(FoodRules.OwnPlusTwoIsEnough(tooLittle, 40f));
            var edge = new OracleFood { Personal = 10f, Contested = 15f };
            Assert.IsTrue(FoodRules.OwnPlusTwoIsEnough(edge, 40f), "exactly the target is enough");
            Assert.IsFalse(FoodRules.OwnPlusOneIsNotEnough(new OracleFood { Personal = 15f, Contested = 25f }, 40f), "exactly the target is enough, so it breaks rule one");
        }

        [Test]
        public void Generator_SpawnsTheConfiguredAmounts()
        {
            // The generator's SpawnFoodPiles needs a live runner, so this guards the wiring in the source: the
            // amounts come from the injected MatchConfigSO fields and no serialized amount field is left.
            var src = System.IO.File.ReadAllText(MapGeneratorSource);
            StringAssert.Contains("_matchConfig.CentrePileFood", src);
            StringAssert.Contains("_matchConfig.PersonalPileFood", src);
            StringAssert.Contains("_matchConfig.ContestedPileFood", src);
            StringAssert.DoesNotContain("PileAmount", src, "the three serialized amount fields are gone");
            StringAssert.Contains("MatchConfigSO matchConfig", src, "injected through Construct, not a second serialized reference");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(src, @"\[SerializeField\][^;]*MatchConfigSO"),
                "no serialized MatchConfigSO on the generator (one reference project-wide, in ProjectInstaller)");
        }

        [Test]
        public void Scene_CarriesNoPileAmountFields()
        {
            var yaml = System.IO.File.ReadAllText(TestAssets.GameScenePath);
            StringAssert.DoesNotContain("PileAmount:", yaml, "Game.unity must not carry pile amounts: they are MatchConfig.asset data");
        }

        [Test]
        public void OracleFood_FollowsAVerdictOnTheMatchConfig()
        {
            var abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var match = Config;
            using (var book = new BalanceBook(new QuietLog(), abilities, classes, match))
            {
                var shipped = OracleKits.Food(new OracleInputs { Abilities = abilities, Classes = classes, Match = match });
                Assert.AreEqual(match.PersonalPileFood, shipped.Personal);
                Assert.AreEqual(match.ContestedPileFood, shipped.Contested);
                Assert.AreEqual(match.CentrePileFood, shipped.Centre);

                float raised = match.PersonalPileFood + 2f;
                var verdict = book.Evaluate(
                    $"{{\"schema\":1,\"assets\":{{\"{match.name}\":{{\"PersonalPileFood\":{raised.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}}}}}", "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);

                var changed = OracleKits.Food(new OracleInputs { Book = book, Verdict = verdict, Abilities = abilities, Classes = classes, Match = match });
                Assert.AreEqual(raised, changed.Personal, "a delta on MatchConfig.PersonalPileFood changes what the oracle measures");
                Assert.AreEqual(shipped.Contested, changed.Contested);
                Assert.AreEqual(match.PersonalPileFood, Config.PersonalPileFood, "the live asset is untouched");
            }
        }

        private sealed class QuietLog : CluckWars.Logging.ILogService
        {
            public CluckWars.Logging.LogLevel MinLevel { get; set; }
            public bool IsEnabled(CluckWars.Logging.LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, System.Exception exception = null) { }
        }
    }
}
