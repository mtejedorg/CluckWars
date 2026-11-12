using System;
using CluckWars.Gameplay;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A05 (architecture audit 2026-11-12, closed as not a defect): the menu's class colours differ from the match
    /// tints in <c>ChickenClassRegistry.asset</c> on purpose (Phase 4 re-audit item 10). These tests pin why: the
    /// class tiles on PICK YOUR BIRD stay tellable apart and keep their text contrast. Numbers measured on the
    /// shipped tints: Warrior / Speedy dE00 23.9, weakest pair (Speedy / Fatty) 14.6, weakest ink contrast
    /// (Warrior) 6.92:1. With the registry tints Warrior / Speedy is 6.9.
    /// </summary>
    public sealed class MenuClassTintTests
    {
        private static readonly ChickenClass[] Classes = (ChickenClass[])Enum.GetValues(typeof(ChickenClass));

        [Test]
        public void EveryClass_HasATileFill()
        {
            foreach (var cls in Classes)
                Assert.DoesNotThrow(() => MenuUiController.ClassTileFill(cls), $"{cls} has no UI class tint");
        }

        [Test]
        public void WarriorAndSpeedyTiles_AreClearlyDifferentColours()
        {
            // The bar the project already uses for "must never read as the other colour"
            // (AbilityPalette.MinPlayerDeltaE). The registry tints score 6.9 here: the defect item 10 fixed.
            float de = AbilityPaletteTests.DeltaE2000(
                MenuUiController.ClassTileFill(ChickenClass.Warrior), MenuUiController.ClassTileFill(ChickenClass.Speedy));
            Assert.GreaterOrEqual(de, AbilityPalette.MinPlayerDeltaE, $"Warrior vs Speedy tile dE00 {de:0.0}");
        }

        [Test]
        public void EveryPairOfTiles_StaysAtLeastAsFarApartAsTheWeakestPairToday()
        {
            for (int i = 0; i < Classes.Length; i++)
            for (int j = i + 1; j < Classes.Length; j++)
            {
                float de = AbilityPaletteTests.DeltaE2000(
                    MenuUiController.ClassTileFill(Classes[i]), MenuUiController.ClassTileFill(Classes[j]));
                Assert.GreaterOrEqual(de, 14f, $"{Classes[i]} vs {Classes[j]} tile dE00 {de:0.0} (weakest today: Speedy / Fatty 14.6)");
            }
        }

        [Test]
        public void InkOnEveryTile_KeepsTheContrastTheTintsGiveToday()
        {
            foreach (var cls in Classes)
            {
                float contrast = AbilityPalette.ContrastRatio(MenuUiController.ClassTileInk, MenuUiController.ClassTileFill(cls));
                Assert.GreaterOrEqual(contrast, 6.9f, $"ink on the {cls} tile is {contrast:0.00}:1 (weakest today: Warrior 6.92:1)");
            }
        }
    }
}
