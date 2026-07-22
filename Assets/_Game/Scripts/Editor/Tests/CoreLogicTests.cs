using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CluckWars.UI;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// EditMode unit tests for the project's Fusion-independent pure logic. Lives in an
    /// Editor folder on purpose: it compiles into Assembly-CSharp-Editor (which references
    /// the game's Assembly-CSharp), so it can exercise gameplay/UI helpers WITHOUT adding a
    /// runtime .asmdef that would disturb Fusion's IL weaver. Run via Unity Test Runner
    /// (Window ▸ General ▸ Test Runner ▸ EditMode) or the MCP `tests-run` tool.
    ///
    /// Scope note: NetworkBehaviour logic (movement, damage RPCs, cargo, match flow) is NOT
    /// unit-testable here — it needs a live NetworkRunner. That surface is covered by the
    /// MCP-driven play-mode smoke checks and the manual test plan (see docs/TESTING.md).
    ///
    /// This file is the "small pure helpers" bucket. The rest of the suite is split by
    /// subsystem so a red test names the area immediately — see DataIntegrityTests,
    /// AbilitySystemTests, EconomyAndPilesTests, ContractsAndEnumsTests,
    /// ServicesAndInputTests and ProjectConfigTests.
    /// </summary>
    public sealed class CoreLogicTests
    {
        // ---- UiGfx.Hex32 — 6-digit hex → opaque Color32 -----------------------

        [Test]
        public void Hex32_ParsesGold_ToExpectedBytes()
        {
            Color32 c = UiGfx.Hex32("f5c842"); // ART.md §6.1 gold token
            Assert.AreEqual(245, c.r);
            Assert.AreEqual(200, c.g);
            Assert.AreEqual(66,  c.b);
            Assert.AreEqual(255, c.a, "Hex32 must always return a fully opaque colour.");
        }

        [Test]
        public void Hex32_ParsesBlackAndWhite()
        {
            Color32 black = UiGfx.Hex32("000000");
            Assert.AreEqual(0, black.r); Assert.AreEqual(0, black.g); Assert.AreEqual(0, black.b);
            Assert.AreEqual(255, black.a);

            Color32 white = UiGfx.Hex32("ffffff");
            Assert.AreEqual(255, white.r); Assert.AreEqual(255, white.g); Assert.AreEqual(255, white.b);
            Assert.AreEqual(255, white.a);
        }

        [Test]
        public void Hex32_IsCaseInsensitive()
        {
            // The ART.md tokens are copied out of the design's CSS, which mixes cases.
            Color32 lower = UiGfx.Hex32("f5c842");
            Color32 upper = UiGfx.Hex32("F5C842");
            Assert.AreEqual(lower.r, upper.r);
            Assert.AreEqual(lower.g, upper.g);
            Assert.AreEqual(lower.b, upper.b);
        }

        [Test]
        public void UiGfxThemeTokens_AreAllDistinct()
        {
            // The §6.1 palette. Two tokens collapsing to the same value means a UI
            // surface silently lost its contrast against its own border or background.
            var tokens = typeof(UiGfx)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Color))
                .ToDictionary(f => f.Name, f => (Color)f.GetValue(null));

            Assert.IsNotEmpty(tokens, "UiGfx exposes no Color tokens — the test is stale.");

            var dupes = tokens.GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1)
                .Select(g => string.Join(" == ", g.Select(kv => kv.Key)))
                .ToList();

            Assert.IsEmpty(dupes, "Two UiGfx theme tokens resolve to the same colour.");
        }

        // ---- MenuUiController.GetPassiveInfo — per-class passive metadata ------

        [Test]
        public void GetPassiveInfo_ReturnsAName_ForEveryRealClass()
        {
            foreach (ChickenClass cls in new[]
                     { ChickenClass.Warrior, ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin })
            {
                var info = MenuUiController.GetPassiveInfo(cls);
                Assert.IsNotEmpty(info.name, $"{cls} should have a passive name.");
                Assert.AreNotEqual("—", info.name, $"{cls} fell through to the unknown-class arm.");
            }
        }

        [Test]
        public void GetPassiveInfo_AssassinPassive_IsCombo()
        {
            // The Combo passive is what gates the Assassin's 3rd ability slot — pin it so a
            // rename can't silently desync the slot-picker gate from the passive copy.
            var info = MenuUiController.GetPassiveInfo(ChickenClass.Assassin);
            Assert.AreEqual("COMBO", info.name);
        }

        [Test]
        public void GetPassiveInfo_UnknownClass_FallsThroughToDash()
        {
            var info = MenuUiController.GetPassiveInfo((ChickenClass)200);
            Assert.AreEqual("—", info.name);
        }

        // ---- ChickenClassRegistrySO — lookup + documented Warrior fallback ----

        private static ChickenClassRegistrySO MakeRegistry(params ChickenClass[] classes)
        {
            var so = ScriptableObject.CreateInstance<ChickenClassRegistrySO>();
            var entries = new ChickenClassRegistrySO.Entry[classes.Length];
            for (int i = 0; i < classes.Length; i++)
                entries[i] = new ChickenClassRegistrySO.Entry { Class = classes[i] };

            typeof(ChickenClassRegistrySO)
                .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(so, entries);
            return so;
        }

        [Test]
        public void TryGet_ReturnsTrue_ForPresentClass_False_ForMissing()
        {
            var reg = MakeRegistry(ChickenClass.Warrior, ChickenClass.Fatty);
            try
            {
                Assert.IsTrue(reg.TryGet(ChickenClass.Fatty, out var found));
                Assert.AreEqual(ChickenClass.Fatty, found.Class);
                Assert.IsFalse(reg.TryGet(ChickenClass.Speedy, out _), "Speedy is not in this registry.");
            }
            finally { Object.DestroyImmediate(reg); }
        }

        [Test]
        public void GetOrDefault_FallsBackToWarrior_WhenClassMissing()
        {
            // Documented contract: "Defaults to Warrior if the requested class is missing."
            var reg = MakeRegistry(ChickenClass.Warrior, ChickenClass.Fatty);
            try
            {
                var entry = reg.GetOrDefault(ChickenClass.Speedy); // absent
                Assert.AreEqual(ChickenClass.Warrior, entry.Class);
            }
            finally { Object.DestroyImmediate(reg); }
        }

        [Test]
        public void GetOrDefault_ReturnsExactEntry_WhenClassPresent()
        {
            var reg = MakeRegistry(ChickenClass.Warrior, ChickenClass.Fatty);
            try
            {
                var entry = reg.GetOrDefault(ChickenClass.Fatty);
                Assert.AreEqual(ChickenClass.Fatty, entry.Class);
            }
            finally { Object.DestroyImmediate(reg); }
        }
    }
}
