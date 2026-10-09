using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 6 chunk 7c. The line a player reads for each active move is templated from the
    /// move's own serialized fields (like the perk lines), so the words can never state a number
    /// the asset does not hold: Speed Burst said 2.5x for a 1.5x asset, and Sneaky Steal / Feather
    /// Aura said 3m for a 5.4 m reach. These tests would have caught all three.
    /// </summary>
    public sealed class AbilityDescriptionTests
    {
        [SetUp] public void SetUp() => UiText.Reset();
        [TearDown] public void TearDown() => UiText.Reset();

        private static readonly Regex Number = new Regex(@"\d+(\.\d+)?", RegexOptions.Compiled);

        private static AbilityBaseSO[] Actives() =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Where(a => !(a is PassiveAbilitySO)).OrderBy(a => a.name).ToArray();

        private static AbilityBaseSO Active(string name) => Actives().First(a => a.name == name);

        private static string[] NumbersIn(string text) => Number.Matches(text).Select(m => m.Value).ToArray();

        private sealed class NoTemplateAbility : AbilityBaseSO
        {
            public override void OnActivate(AbilityContext ctx) { }
            public override void OnDeactivate(AbilityContext ctx) { }
        }

        [Test]
        public void EveryShippedActiveAbility_HasATemplate_ThatRendersCleanAndFits()
        {
            var all = Actives();
            Assert.AreEqual(29, all.Length, "Expected the 29 shipped active abilities; a new one needs an ability.*.desc template.");
            foreach (var a in all)
            {
                Assert.IsNotNull(a.DescriptionKey, $"{a.name} has no DescriptionKey.");
                string text = a.DescriptionText;
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{a.name}: empty description.");
                Assert.IsFalse(text.Contains("{") || text.Contains("}") || text.Contains("#"), $"{a.name}: unresolved placeholder or marker in: {text}");
                Assert.LessOrEqual(text.Length, 110, $"{a.name}: {text.Length} chars: {text}");
                foreach (var banned in new[] { "cast", "target", "cooldown" })
                    StringAssert.DoesNotContain(banned, text.ToLowerInvariant(), $"{a.name}: kid-facing text must not say {banned}.");
            }
            Assert.IsEmpty(UiText.Problems, string.Join("\n", UiText.Problems));
        }

        [Test]
        public void AssetDescriptionField_IsTheRenderedText()
        {
            foreach (var a in Actives())
                Assert.AreEqual(a.DescriptionText, a.Description, $"{a.name}: the .asset Description is stale; paste the rendered text into it.");
        }

        [Test]
        public void Templates_ContainNoHandTypedNumbers()
        {
            foreach (var a in Actives())
            {
                string template = UiText.Get(a.DescriptionKey);
                Assert.IsEmpty(NumbersIn(template), $"{a.name}: the template has a literal number; supply it as an argument from a field: {template}");
            }
        }

        [Test]
        public void EveryNumberInTheRenderedText_IsAnArgument_AndEveryArgumentIsUsed()
        {
            foreach (var a in Actives())
            {
                var args = a.DescriptionArgs().Select(x => Convert.ToString(x.value, CultureInfo.InvariantCulture)).ToArray();
                var shown = NumbersIn(a.DescriptionText);
                CollectionAssert.AreEquivalent(args, shown, $"{a.name}: numbers shown vs numbers supplied: {a.DescriptionText}");
            }
        }

        [Test]
        public void EveryArgument_IsARealFieldValue_ToDisplayPrecision()
        {
            foreach (var a in Actives())
            {
                var fields = new List<double>();
                foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType == typeof(float)) fields.Add((float)f.GetValue(a));
                    else if (f.FieldType == typeof(int)) fields.Add((int)f.GetValue(a));
                }
                fields.Add(a.IndicatorRange);
                fields.Add(a.AimRadius);
                // A 0..1 factor is shown as a whole percent.
                var real = fields.Concat(fields.Select(v => Math.Round(v * 100.0, MidpointRounding.AwayFromZero))).ToList();

                foreach (var (name, value) in a.DescriptionArgs())
                {
                    double shown = double.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    Assert.IsTrue(real.Any(r => Math.Abs(r - shown) <= 0.005 + 1e-6),
                        $"{a.name}: argument {{{name}}} = {shown} is not any field value of the asset ({string.Join(", ", fields)}).");
                }
            }
        }

        [Test]
        public void PinnedNumbers_MatchTheShippedAssets()
        {
            Assert.AreEqual(1.5f, ((SpeedBurstAbilitySO)Active("SpeedBurst")).SpeedMultiplier, 1e-4f);
            StringAssert.Contains("1.5x", Active("SpeedBurst").DescriptionText);
            StringAssert.DoesNotContain("2.5x", Active("SpeedBurst").DescriptionText);

            Assert.AreEqual(5.4f, ((SneakyStealAbilitySO)Active("SneakySteal")).StealRange, 1e-4f);
            StringAssert.Contains("5.4m", Active("SneakySteal").DescriptionText);
            Assert.AreEqual(5.4f, ((FeatherAuraAbilitySO)Active("FeatherAura")).AuraRadius, 1e-4f);
            StringAssert.Contains("5.4m", Active("FeatherAura").DescriptionText);
            StringAssert.DoesNotContain(" 3m", Active("FeatherAura").DescriptionText);
        }

        [Test]
        public void ChangingAField_ChangesTheText_WithoutTouchingAnyWording()
        {
            var burst = ScriptableObject.CreateInstance<SpeedBurstAbilitySO>();
            var steal = ScriptableObject.CreateInstance<SneakyStealAbilitySO>();
            try
            {
                burst.SpeedMultiplier = 3f; burst.Duration = 0.75f;
                steal.StealRange = 7.25f; steal.StealAmount = 9f;
                Assert.AreEqual("A 0.75s sprint at 3x movement speed.", burst.DescriptionText);
                Assert.AreEqual("Yanks 9 food from the nearest carrier within 7.25m.", steal.DescriptionText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(burst);
                UnityEngine.Object.DestroyImmediate(steal);
            }
        }

        [Test]
        public void StealthAbilities_KeepTheBreaksItLine()
        {
            StringAssert.Contains("Using a move breaks it.", Active("Invisibility").DescriptionText);
            StringAssert.Contains("Using a move breaks it.", Active("SmokeRoost").DescriptionText);
        }

        [Test]
        public void AbilityWithNoTemplate_FallsBackToItsDescription_AndSaysSoOnce()
        {
            var a = ScriptableObject.CreateInstance<NoTemplateAbility>();
            try
            {
                a.Description = "Raw text.";
                Assert.AreEqual("Raw text.", a.DescriptionText);
                Assert.AreEqual("Raw text.", a.DescriptionText);
                Assert.AreEqual(1, UiText.Problems.Count, "The missing template is reported once, not on every read.");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); }
        }
    }
}
