using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-199: pins the class stats and the registry's active set at the values the assets hold today. A deliberate
    /// balance change updates these literals in the same commit; an accidental one (an asset edited by a tool or a
    /// merge) fails here and names the asset and field.
    /// </summary>
    public class ClassStatsAndRegistryGuardTests
    {
        private static ChickenStatsSO Stats(string cls) =>
            TestAssets.Load<ChickenStatsSO>("Assets/_Game/Data/Classes/" + cls + ".asset");

        [TestCase("Assassin", 70, 10, 0.9f)]
        [TestCase("Fatty", 120, 35, 1.35f)]
        [TestCase("Speedy", 80, 10, 0.85f)]
        [TestCase("Warrior", 100, 20, 1f)]
        public void ClassStats_MatchTheShippedAssets(string cls, int maxHp, int cargo, float scale)
        {
            var s = Stats(cls);
            string asset = $"Assets/_Game/Data/Classes/{cls}.asset";
            // ChickenStatsSO has no MaxHP field any more (no damage in the game); the serialized value is still in the
            // asset, so it is pinned from the YAML rather than let to drift unseen.
            string yaml = File.ReadAllText(Path.Combine(Application.dataPath, "..", asset));
            var m = Regex.Match(yaml, @"^  MaxHP: (\d+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(m.Success, $"{asset} field MaxHP is gone from the asset");
            Assert.AreEqual(maxHp, int.Parse(m.Groups[1].Value), $"{asset} field MaxHP changed");
            Assert.AreEqual(cargo, s.CargoCapacity, $"{asset} field CargoCapacity changed");
            Assert.AreEqual(scale, s.Scale, 1e-4f, $"{asset} field Scale changed");
        }

        [Test]
        public void AbilityRegistry_ActiveSet_IsTheElevenGenericJobs()
        {
            var registry = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var active = registry.ActiveAbilities.Where(a => !a.Retired).Select(a => a.name).OrderBy(n => n).ToArray();
            var expected = new[]
            {
                "CluckShock", "FeatherAura", "Invisibility", "MarkKill", "Peck", "Shadowstep",
                "Slow", "Snatch", "SpeedBurst", "TurtleMode", "WingSlam",
            }.OrderBy(n => n).ToArray();

            Assert.AreEqual(expected.Length, active.Length,
                $"{TestAssets.AbilityRegistryPath} field All: active (non-retired, non-passive) count changed; now: {string.Join(", ", active)}");
            CollectionAssert.AreEqual(expected, active,
                $"{TestAssets.AbilityRegistryPath} field All: the active ability set changed");
        }
    }
}
