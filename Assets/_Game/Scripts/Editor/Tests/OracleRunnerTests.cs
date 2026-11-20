using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEditor;

namespace CluckWars.Tests
{
    /// <summary>
    /// The oracle report on the shipped assets and the baked map. No test restates an asset value: every expectation is
    /// derived from the asset, from a second run, or from the report's own arrays.
    /// </summary>
    public sealed class OracleRunnerTests
    {
        private sealed class QuietLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private AbilityRegistrySO _abilities;
        private ChickenClassRegistrySO _classes;
        private MatchConfigSO _match;
        private OracleMapSO _map;
        private BalanceBook _book;
        private OracleReport _baseline;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            _classes = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            _match = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);
            _map = AssetDatabase.LoadAssetAtPath<OracleMapSO>(OracleMapBaker.AssetPath);
            Assert.IsNotNull(_map, $"{OracleMapBaker.AssetPath} is missing: run {OracleMapBaker.MenuPath}.");
            _book = new BalanceBook(new QuietLog(), _abilities, _classes, _match);
            _baseline = OracleRunner.Run(Inputs(), null);
        }

        [OneTimeTearDown]
        public void DisposeOnce() => _book?.Dispose();

        private OracleInputs Inputs(BalanceVerdict verdict = null) => new OracleInputs
        {
            Map = _map, Book = _book, Verdict = verdict, Abilities = _abilities, Classes = _classes, Match = _match,
        };

        private static KitLine Line(OracleReport report, ChickenSubclass subclass) =>
            report.Economy.Single(l => l.Label == subclass.ToString());

        [Test]
        public void EveryKit_HasALine()
        {
            var expected = _abilities.Passives.Select(p => p.Subclass.ToString()).ToList();
            CollectionAssert.AreEquivalent(expected, _baseline.Economy.Select(l => l.Label));
            CollectionAssert.AreEquivalent(expected, _baseline.EconomyGoldenPile.Select(l => l.Label));
            CollectionAssert.AreEquivalent(expected, _baseline.EconomyRestock.Select(l => l.Label));
            foreach (var line in _baseline.Economy.Where(l => l.Applicable))
            {
                Assert.AreEqual(4 * OracleRunner.Routes.Length, line.BankedBySeatRoute.Length, line.Label);
                Assert.AreEqual(line.BankedBySeatRoute.Length, line.SecondsBySeatRoute.Length, line.Label);
                Assert.Greater(line.BestBanked, 0f, $"{line.Label} banked nothing: the run proves nothing");
            }
        }

        [Test]
        public void NonForager_HasNoEconomyNumber()
        {
            foreach (var subclass in new[] { ChickenSubclass.Assassin_Spoiler, ChickenSubclass.Assassin_Thief })
            {
                foreach (var list in new[] { _baseline.Economy, _baseline.EconomyGoldenPile, _baseline.EconomyRestock })
                {
                    var line = list.Single(l => l.Label == subclass.ToString());
                    Assert.IsFalse(line.Applicable, subclass.ToString());
                    Assert.IsEmpty(line.BankedBySeatRoute, subclass.ToString());
                    Assert.IsEmpty(line.SecondsBySeatRoute, subclass.ToString());
                    Assert.AreEqual(0f, line.Result, subclass.ToString());
                    Assert.AreEqual(0f, line.BestBanked, subclass.ToString());
                }
            }
            // Applicable is exactly "the class may forage": nothing else turns a kit off.
            foreach (var passive in _abilities.Passives)
                Assert.AreEqual(ChickenClasses.MayForage(passive.Subclass.ClassOf()),
                    _baseline.Economy.Single(l => l.Label == passive.Subclass.ToString()).Applicable, passive.name);
        }

        [Test]
        public void Spread_IsBestMinusWorstRoute()
        {
            foreach (var line in _baseline.Economy.Where(l => l.Applicable))
            {
                float max = line.BankedBySeatRoute.Max(), min = line.BankedBySeatRoute.Min();
                Assert.AreEqual(max, line.BestBanked, 1e-4f, line.Label);
                Assert.AreEqual(max - min, line.Spread, 1e-4f, line.Label);
                Assert.AreEqual(line.BankedBySeatRoute.Average(), line.MeanBanked, 1e-3f, line.Label);
                Assert.AreEqual(OracleRunner.Routes[Array.IndexOf(line.BankedBySeatRoute, max) % OracleRunner.Routes.Length], line.BestRoute, line.Label);
                Assert.AreEqual(line.BestBanked / _baseline.ClockSeconds, line.IncomePerSecond, 1e-4f, line.Label);
            }
        }

        [Test]
        public void Offset_MovesTheResultByIncomeTimesSeconds()
        {
            string label = ChickenSubclass.Fatty_Hoarder.ToString();
            const float seconds = 3f;
            var offsets = new Dictionary<string, float> { { label, seconds } };

            var shifted = OracleRunner.Run(Inputs(), offsets);

            var before = Line(_baseline, ChickenSubclass.Fatty_Hoarder);
            var after = Line(shifted, ChickenSubclass.Fatty_Hoarder);
            Assert.AreEqual(seconds, after.OffsetSeconds, 1e-6f);
            Assert.AreEqual(before.BestBanked, after.BestBanked, 1e-6f, "an offset must not change the measurement");
            Assert.AreEqual(before.BestBanked + seconds * before.IncomePerSecond, after.Result, 1e-3f);

            foreach (var line in shifted.Economy.Where(l => l.Label != label && l.Applicable))
            {
                Assert.AreEqual(0f, line.OffsetSeconds, line.Label);
                Assert.AreEqual(line.BestBanked, line.Result, 1e-6f, $"{line.Label}: a missing label means no offset");
            }
        }

        [Test]
        public void Verdict_ChangesTheMeasurement()
        {
            float raised = _match.DepositRatePerSecond * 2f;
            var verdict = _book.Evaluate(
                $"{{\"schema\":1,\"assets\":{{\"{_match.name}\":{{\"DepositRatePerSecond\":{raised.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}}}}}", "test");
            Assert.IsTrue(verdict.IsAccepted, verdict.Reason);

            var changed = OracleRunner.Run(Inputs(verdict), null);

            Assert.AreEqual(verdict.Hash, changed.BalanceHash);
            Assert.AreNotEqual(_baseline.BalanceHash, changed.BalanceHash);
            foreach (var line in _baseline.Economy.Where(l => l.Applicable))
            {
                var other = changed.Economy.Single(l => l.Label == line.Label);
                CollectionAssert.AreNotEqual(line.BankedBySeatRoute, other.BankedBySeatRoute,
                    $"{line.Label}: a faster deposit rate left the numbers unchanged");
            }
        }

        [Test]
        public void MaxSpoilerKills_FollowsTheMarkAsset()
        {
            var mark = _abilities.All.OfType<MarkKillAbilitySO>().FirstOrDefault()
                       ?? _abilities.Passives.SelectMany(p => p.Kit).OfType<MarkKillAbilitySO>().First();
            float clock = _match.MatchDurationSeconds;
            float period = mark.WindowSeconds + mark.SuccessCooldownSeconds;
            float room = clock - mark.RoundStartCooldownSeconds - mark.WindowSeconds;
            int expected = room <= 0f ? 0 : (int)Math.Ceiling(room / period);

            Assert.AreEqual(expected, SneakyTables.MaxSpoilerKills(mark.RoundStartCooldownSeconds, mark.WindowSeconds, mark.SuccessCooldownSeconds, clock));
        }

        [Test]
        public void SpoilerTable_HasFiveKillRowsPerCargoLevel_AndMoreKillsNeverPayLess()
        {
            var mark = _abilities.All.OfType<MarkKillAbilitySO>().FirstOrDefault()
                       ?? _abilities.Passives.SelectMany(p => p.Kit).OfType<MarkKillAbilitySO>().First();
            int rowsPerLevel = SneakyTables.MaxSpoilerKills(mark.RoundStartCooldownSeconds, mark.WindowSeconds, mark.SuccessCooldownSeconds, _match.MatchDurationSeconds) + 1;
            var levels = _baseline.Spoiler.GroupBy(r => r.CargoPerKill).ToList();

            Assert.AreEqual(3, levels.Count, "empty, mean load and full load");
            Assert.AreEqual(0f, levels[0].Key, "the first level is an empty victim");
            foreach (var level in levels)
            {
                var rows = level.OrderBy(r => r.Kills).ToList();
                CollectionAssert.AreEqual(Enumerable.Range(0, rowsPerLevel), rows.Select(r => r.Kills), $"cargo {level.Key}");
                for (int i = 1; i < rows.Count; i++)
                {
                    Assert.GreaterOrEqual(rows[i].Final, rows[i - 1].Final, $"cargo {level.Key}, kills {rows[i].Kills}");
                    Assert.GreaterOrEqual(rows[i].FinalWithPassive, rows[i - 1].FinalWithPassive, $"cargo {level.Key}, kills {rows[i].Kills}");
                    Assert.LessOrEqual(rows[i].LeaderFinal, rows[i - 1].LeaderFinal, $"cargo {level.Key}, kills {rows[i].Kills}");
                }
                Assert.AreEqual(0f, rows[0].Final, "no kills, no food");
            }
        }

        [Test]
        public void SpoilerTable_PassiveAddsExactlyTheBonus()
        {
            var passive = _abilities.Passives.OfType<SpoilerPassiveSO>().Single();
            float bonus = passive.MatchEndBonusFood(null);
            Assert.Greater(bonus, 0f, "the Spoiler passive pays nothing: the test would prove nothing");
            Assert.AreEqual(bonus, _baseline.SpoilerPassiveBonus);
            foreach (var row in _baseline.Spoiler)
            {
                Assert.AreEqual(bonus, row.FinalWithPassive - row.Final, 1e-4f, $"kills {row.Kills}, cargo {row.CargoPerKill}");
                Assert.AreEqual(row.Kills * (_match.ExecuteBounty + row.CargoPerKill), row.Final, 1e-4f);
            }
        }

        [Test]
        public void ThiefTable_MoreVictimCargoNeverNeedsMoreSteals()
        {
            var levels = _baseline.Thief.GroupBy(r => r.VictimCargo).OrderBy(g => g.Key).ToList();
            Assert.AreEqual(3, levels.Count);

            int Needed(IEnumerable<ThiefRow> rows)
            {
                var hit = rows.OrderBy(r => r.Steals).FirstOrDefault(r => r.Banked >= _baseline.WinTarget);
                return hit == null ? int.MaxValue : hit.Steals;
            }

            for (int i = 1; i < levels.Count; i++)
                Assert.LessOrEqual(Needed(levels[i]), Needed(levels[i - 1]), $"cargo {levels[i].Key} needs more steals than {levels[i - 1].Key}");
            foreach (var level in levels)
            {
                var rows = level.OrderBy(r => r.Steals).ToList();
                Assert.AreEqual(1, rows[0].Steals);
                for (int i = 1; i < rows.Count; i++)
                {
                    Assert.GreaterOrEqual(rows[i].Banked, rows[i - 1].Banked, $"cargo {level.Key}");
                    Assert.Greater(rows[i].SecondsNeeded, rows[i - 1].SecondsNeeded, $"cargo {level.Key}: another steal cannot be quicker");
                }
            }
        }

        [Test]
        public void SpoilerTable_OneKill_ComparesAgainstTheFarmersLeft()
        {
            var banked = _baseline.StandardFarmerBanked;
            Assert.AreEqual(3, banked.Length, "the three farmers the Spoiler hunts");
            var sorted = banked.OrderByDescending(b => b).ToArray();
            float clock = _baseline.ClockSeconds;
            foreach (var row in _baseline.Spoiler.Where(r => r.Kills == 1))
            {
                float reducedLeader = Math.Max(0f, sorted[0] - (row.CargoPerKill + sorted[0] / clock * _baseline.SpoilerRemovalSeconds));
                float expected = Math.Max(reducedLeader, Math.Max(sorted[1], sorted[2]));
                Assert.AreEqual(expected, row.LeaderFinal, 1e-3f, $"cargo {row.CargoPerKill}");
                Assert.GreaterOrEqual(row.LeaderFinal, sorted[1], "an untouched farmer is still there to beat");
            }
        }

        [Test]
        public void SpoilerTable_KillsGoToTheBestFarmerFirst_ThenTheNext()
        {
            var t = new SpoilerTableInputs
            {
                ExecuteBounty = 5f, PassiveBonus = 15f, ClockSeconds = 45f, WinTarget = 40f, RemovalSeconds = 3f,
                MeanCargo = 10f, Capacity = 14f, FarmerBanked = new[] { 19f, 21f, 20f }, MaxKills = 4,
            };
            var rows = SneakyTables.Spoiler(t).Where(r => r.CargoPerKill == 10f).OrderBy(r => r.Kills).ToList();

            Assert.AreEqual(21f, rows[0].LeaderFinal, 1e-4f);
            Assert.AreEqual(20f, rows[1].LeaderFinal, 1e-4f, "after one kill the second farmer is the best left, not the reduced leader");
            Assert.AreEqual(19f, rows[2].LeaderFinal, 1e-4f, "after two kills the third farmer is");
            Assert.Less(rows[3].LeaderFinal, 19f, "three kills reach every farmer");
        }

        private (OracleKit thief, SnatchAbilitySO steal, PassiveAbilitySO passive) ThiefParts()
        {
            var thief = OracleKits.All(Inputs()).Single(k => k.Label == ChickenSubclass.Assassin_Thief.ToString());
            var steal = _abilities.All.OfType<SnatchAbilitySO>().Single(a => a.Job == AbilityJob.Steal);
            var passive = _abilities.Passives.OfType<ThiefPassiveSO>().Single();
            return (thief, steal, passive);
        }

        private float MeanBaseToContested(bool jumps)
        {
            float sum = 0f;
            for (int seat = 0; seat < 4; seat++)
            {
                sum += _map.Leg(OracleMapSO.BaseIndex(seat), OracleMapSO.ContestedIndex(seat), jumps);
                sum += _map.Leg(OracleMapSO.BaseIndex(seat), OracleMapSO.ContestedIndex((seat + 3) % 4), jumps);
            }
            return sum / 8f;
        }

        [Test]
        public void ThiefTable_ATripIsARoundTrip()
        {
            var (thief, steal, passive) = ThiefParts();
            float cooldown = passive.ModifyCooldown(steal.Cooldown, steal, null);
            float roundTrip = 2f * MeanBaseToContested(thief.Jumps) / thief.MoveSpeed;
            Assert.Greater(roundTrip, 0f);

            foreach (var row in _baseline.Thief)
            {
                float expected = row.Steals * cooldown + row.Trips * roundTrip + row.Banked / thief.DepositRate;
                Assert.AreEqual(expected, row.SecondsNeeded, 1e-3f, $"steals {row.Steals}, cargo {row.VictimCargo}");
            }
        }

        [Test]
        public void ThiefTable_ASteal_NeverTakesMoreThanTheRoom()
        {
            var (thief, steal, passive) = ThiefParts();
            float perSteal = passive.ModifyStealAmount(steal.StealAmount, null);
            foreach (var row in _baseline.Thief)
            {
                float full = Math.Min(perSteal, row.VictimCargo);
                Assert.LessOrEqual(row.Trips, row.Steals, "no more trips than steals");
                Assert.GreaterOrEqual(row.Trips, (int)Math.Ceiling(row.Banked / thief.CargoCapacity - 1e-4f), $"{row.Banked} food cannot ride in fewer loads");
                Assert.LessOrEqual(row.Banked, row.Steals * full + 1e-4f);
                // When two full steals do not fit in the cargo, every steal needs its own trip.
                if (full > 0f && 2f * full > thief.CargoCapacity) Assert.AreEqual(row.Steals, row.Trips, $"steals {row.Steals}, cargo {row.VictimCargo}");
            }
        }

        [Test]
        public void ThiefTable_GoesHomeWhenItCannotTakeAFullSteal()
        {
            var t = new ThiefTableInputs
            {
                StealAmount = 5f, StealMultiplier = 1f, StealCooldown = 4f, CargoCapacity = 12f, MoveSpeed = 10f, DepositRate = 10f,
                MeanBaseToContestedLeg = 10f, ClockSeconds = 45f, WinTarget = 40f, MeanCargo = 100f, Capacity = 60f,
            };
            var rows = SneakyTables.Thief(t).Where(r => r.VictimCargo == 100f).OrderBy(r => r.Steals).ToList();

            Assert.AreEqual(1, rows[0].Trips);
            Assert.AreEqual(1, rows[1].Trips, "two steals of 5 fit in 12");
            Assert.AreEqual(10f, rows[1].Banked, 1e-4f);
            Assert.AreEqual(2, rows[2].Trips, "a third full steal does not fit: home first, then another load");
            Assert.AreEqual(15f, rows[2].Banked, 1e-4f, "the steal that did not fit took nothing extra");
            Assert.AreEqual(4f * 3 + 2 * (2f * 10f / 10f) + 15f / 10f, rows[2].SecondsNeeded, 1e-3f);
        }

        [Test]
        public void Report_ListsWhatItDoesNotModel()
        {
            Assert.IsNotEmpty(_baseline.NotModelled);
            string md = _baseline.ToMarkdown();
            foreach (var item in _baseline.NotModelled) StringAssert.Contains(item, md);
            foreach (var needle in new[] { "ight", "ursuit", "catter", "bot", "Peck cast", "jump" })
                Assert.IsTrue(_baseline.NotModelled.Any(n => n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0), needle);
            Assert.IsTrue(md.TrimEnd().EndsWith(_baseline.NotModelled.Last()), "the not-modelled list is the last thing in the report");
        }

        [Test]
        public void Markdown_HasOneSentenceAboveEachTable_AndEveryKitName()
        {
            var lines = _baseline.ToMarkdown().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            int tables = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].StartsWith("|") || (i > 0 && lines[i - 1].StartsWith("|"))) continue;
                tables++;
                Assert.AreEqual(string.Empty, lines[i - 1], "a blank line sits between the sentence and the table");
                Assert.IsFalse(string.IsNullOrWhiteSpace(lines[i - 2]), "a sentence sits above the table");
                Assert.IsFalse(lines[i - 2].StartsWith("#") || lines[i - 2].StartsWith("|"), "the line above the table is prose, not a heading");
            }
            Assert.AreEqual(5, tables, "economy x3, Spoiler, Thief");
            string md = string.Join("\n", lines);
            foreach (var line in _baseline.Economy) StringAssert.Contains(line.Label, md);
        }

        [Test]
        public void Json_RoundTripsTheKitLabels()
        {
            Assert.IsTrue(BalanceJson.TryParse(_baseline.ToJson(), out var root, out var error), error);
            Assert.IsTrue(root.TryGet("economy", out var economy));
            CollectionAssert.AreEqual(
                _baseline.Economy.Select(l => l.Label),
                economy.Items.Select(i => { i.TryGet("label", out var label); return label.Text; }));
            Assert.IsTrue(root.TryGet("balanceHash", out var hash));
            Assert.AreEqual(_baseline.BalanceHash, hash.Text);
            Assert.IsTrue(root.TryGet("notModelled", out var notModelled));
            Assert.AreEqual(_baseline.NotModelled.Count, notModelled.Items.Count);
        }
    }
}
