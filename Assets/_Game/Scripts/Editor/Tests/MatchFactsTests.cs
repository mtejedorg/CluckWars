using System.IO;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// The facts the match simulator reads off the abilities. Each expectation is the authored field, read through
    /// <see cref="SerializedObject"/> by name: nothing here restates an asset value.
    /// </summary>
    public sealed class MatchFactsTests
    {
        private AbilityRegistrySO _abilities;

        [SetUp]
        public void SetUp() => _abilities = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        private AbilityBaseSO ByJob(AbilityJob job)
        {
            var found = _abilities.All.FirstOrDefault(a => a != null && a.Job == job);
            Assert.IsNotNull(found, $"the registry holds no ability with job {job}");
            return found;
        }

        private static float Field(AbilityBaseSO ability, string name)
        {
            var property = new SerializedObject(ability).FindProperty(name);
            Assert.IsNotNull(property, $"{ability.name} has no serialized field '{name}'");
            Assert.AreEqual(SerializedPropertyType.Float, property.propertyType, $"{ability.name}.{name} is not a float");
            return property.floatValue;
        }

        [Test]
        public void JumpFacts_BlinkMetres_EqualsTheAuthoredForwardOffset()
        {
            var jump = ByJob(AbilityJob.Jump);

            Assert.AreEqual(jump.AuthoredAimForwardOffset, jump.OracleFacts.BlinkMetres);
            Assert.AreEqual(JumpResolver.GetNominalDistance(jump.JumpTier), jump.OracleFacts.BlinkMetres, "the blink is the tier's nominal distance");
        }

        [Test]
        public void JumpFacts_DeclareTheDashItReallyPerforms()
        {
            // The authored tier can be None (offset 0); the dash itself is a speed multiplier for the ability's seconds.
            var jump = ByJob(AbilityJob.Jump);

            Assert.AreEqual(Field(jump, "SpeedMultiplier"), jump.OracleFacts.SpeedMultiplier);
            Assert.AreEqual(Field(jump, "Duration"), jump.OracleFacts.SpeedSeconds);
            Assert.Greater(jump.OracleFacts.SpeedMultiplier * jump.OracleFacts.SpeedSeconds, 0f, "a Jump that covers no ground cannot cross a wall");
        }

        [Test]
        public void ShieldFacts_EqualTheAuthoredWalkAndEggFields()
        {
            var shield = ByJob(AbilityJob.Shield);
            var facts = shield.OracleFacts;

            Assert.AreEqual(Field(shield, "WalkSeconds"), facts.ShieldWalkSeconds);
            Assert.AreEqual(Field(shield, "SpeedMultiplier"), facts.ShieldWalkSpeedMultiplier);
            Assert.AreEqual(Field(shield, "EggSeconds"), facts.ShieldEggSeconds);
        }

        [Test]
        public void DeathMarkFacts_EqualTheAuthoredFields()
        {
            var mark = ByJob(AbilityJob.DeathMark);
            var facts = mark.OracleFacts;

            Assert.AreEqual(Field(mark, "ArmSeconds"), facts.MarkArmSeconds);
            Assert.AreEqual(Field(mark, "KillRange"), facts.MarkKillRange);
            Assert.AreEqual(Field(mark, "FailCooldownSeconds"), facts.MarkFailCooldown);
            Assert.AreEqual(Field(mark, "LeaderPickWeight"), facts.MarkLeaderPickWeight);
        }

        [Test]
        public void AimRadiusField_NamesTheFieldBehindAimRadius()
        {
            int named = 0;
            foreach (var ability in _abilities.All.Where(a => a != null && a.AimRadiusField != null))
            {
                Assert.AreEqual(ability.AimRadius, Field(ability, ability.AimRadiusField), ability.name);
                named++;
            }
            Assert.Greater(named, 0, "no ability names an aim radius field: the test would prove nothing");

            // The five jobs the solver tunes by radius must each name one.
            foreach (var job in new[] { AbilityJob.Steal, AbilityJob.Stun, AbilityJob.Shove, AbilityJob.Slow, AbilityJob.SlowArea })
                Assert.IsNotNull(ByJob(job).AimRadiusField, $"{job} names no aim radius field");
        }

        [Test]
        public void CooldownFields_AreSerializedFloats()
        {
            foreach (var ability in _abilities.All.Where(a => a != null))
            {
                Assert.IsNotEmpty(ability.CooldownFields, ability.name);
                foreach (var name in ability.CooldownFields) Field(ability, name);
            }
            CollectionAssert.AreEqual(new[] { nameof(AbilityBaseSO.Cooldown) }, ByJob(AbilityJob.Stun).CooldownFields, "the default is the base Cooldown");
            Assert.AreEqual(2, ByJob(AbilityJob.DeathMark).CooldownFields.Length, "Death Mark keeps a success and a fail cooldown");
        }

        [Test]
        public void ChickenMovement_HasNoSurgeLiteral()
        {
            string src = File.ReadAllText("Assets/_Game/Scripts/Gameplay/ChickenMovement.cs");

            // The factor is written once, on the named constant; no other line may carry the number.
            foreach (var line in src.Split('\n').Where(l => l.Contains("1.4f")))
                StringAssert.Contains(nameof(ChickenMovement.UnderdogSurgeSpeedFactor), line);
            StringAssert.Contains("*= " + nameof(ChickenMovement.UnderdogSurgeSpeedFactor), src);
        }

        [Test]
        public void HideFacts_HideOpacity_EqualsTheAuthoredOpacity_AndCountsAsHidden()
        {
            var hide = ByJob(AbilityJob.Hide);

            Assert.AreEqual(Field(hide, "Opacity"), hide.OracleFacts.HideOpacity);
            Assert.IsTrue(BotSightRules.IsHidden(hide.OracleFacts.HideOpacity), "the sight rule must treat a hiding chicken as hidden");
        }

        [Test]
        public void BotArrivalRadius_EqualsThePrefabField()
        {
            var prefab = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var bot = prefab.GetComponentInChildren<BotController>(true);
            Assert.IsNotNull(bot, "Chicken.prefab carries no BotController");
            float authored = new SerializedObject(bot).FindProperty("_arrivalRadius").floatValue;

            Assert.AreEqual(authored, OracleMenu.BotArrivalRadius());
            Assert.AreEqual(authored, bot.ArrivalRadius);
        }
    }
}
