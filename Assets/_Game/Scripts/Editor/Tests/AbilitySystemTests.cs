using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the ability system's authoring contract: the registry is complete, no
    /// ability is a duplicate or an orphan, and every asset carries the metadata the
    /// bot AI, the HUD and the character-select picker each depend on.
    /// </summary>
    /// <remarks>
    /// Adding an ability is a four-step ritual (subclass <c>AbilityBaseSO</c>, author
    /// an asset, add it to <c>AbilityRegistrySO.All</c>, add an
    /// <c>AbilityIconStyle</c> entry). Every step is silent when skipped — the ability
    /// just never appears, or appears with no icon, or is never picked by a bot. These
    /// tests turn each of those silences into a named failure.
    ///
    /// Activation behaviour itself (<c>OnActivate</c>/<c>OnDeactivate</c>) is NOT
    /// covered: it mutates a live <c>ChickenController</c> and runs physics scans, so
    /// it needs a NetworkRunner. That stays in the manual plan.
    /// </remarks>
    public sealed class AbilitySystemTests
    {
        private static List<AbilityBaseSO> AllAssets() =>
            TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);

        /// <summary>Every concrete ability subclass compiled into the game assembly.</summary>
        private static List<Type> ConcreteAbilityTypes() =>
            typeof(AbilityBaseSO).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AbilityBaseSO)) && !t.IsAbstract)
                .ToList();

        // ---- Registry completeness ---------------------------------------------

        [Test]
        public void Registry_ContainsEveryAbilityAsset_AndNothingElse()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            Assert.IsNotNull(reg.All, "AbilityRegistry.All is null — the character-select picker shows nothing.");

            var registered = reg.All.Where(a => a != null).ToList();
            var onDisk     = AllAssets();

            var missing = onDisk.Except(registered).Select(a => a.name).OrderBy(n => n).ToList();
            Assert.IsEmpty(missing,
                "Ability assets exist on disk but are not in AbilityRegistry.All, so no player or bot " +
                "can ever equip them. Drag them into the registry asset.");

            var stray = registered.Except(onDisk).Select(a => a.name).OrderBy(n => n).ToList();
            Assert.IsEmpty(stray,
                $"AbilityRegistry.All references abilities that do not live in {TestAssets.AbilitiesDir}.");
        }

        [Test]
        public void Registry_HasNoNullOrDuplicateEntries()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            Assert.IsNotNull(reg.All, "AbilityRegistry.All is null.");

            int nulls = reg.All.Count(a => a == null);
            Assert.AreEqual(0, nulls,
                $"{nulls} empty slot(s) in AbilityRegistry.All. The picker iterates this array, so an " +
                "empty row renders as a blank, unselectable card.");

            var dupes = reg.All.Where(a => a != null)
                .GroupBy(a => a).Where(g => g.Count() > 1)
                .Select(g => g.Key.name).ToList();
            Assert.IsEmpty(dupes, "The same ability appears twice in AbilityRegistry.All.");
        }

        [Test]
        public void EveryConcreteAbilityType_HasAtLeastOneAuthoredAsset()
        {
            // A subclass with no asset is dead code that still shows up in code search
            // and misleads the next person balancing the kit.
            var typesWithAssets = AllAssets().Select(a => a.GetType()).Distinct().ToHashSet();
            var orphans = ConcreteAbilityTypes().Where(t => !typesWithAssets.Contains(t))
                .Select(t => t.Name).OrderBy(n => n).ToList();

            Assert.IsEmpty(orphans,
                $"These AbilityBaseSO subclasses have no asset in {TestAssets.AbilitiesDir} — " +
                "they can never be equipped by anyone.");
        }

        // ---- Per-asset authoring ------------------------------------------------

        [Test]
        public void EveryAbility_HasIdentityCopy_ForTheHudAndPicker()
        {
            foreach (var a in AllAssets())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.DisplayName),
                    $"{a.name}: DisplayName is blank — the character-select card has no title.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.ShortLabel),
                    $"{a.name}: ShortLabel is blank — the ability hex button has no text fallback.");
                Assert.LessOrEqual(a.ShortLabel.Length, 4,
                    $"{a.name}: ShortLabel '{a.ShortLabel}' is {a.ShortLabel.Length} chars; the hex button " +
                    "fits about 4 and overflows past that.");
                Assert.IsFalse(string.IsNullOrEmpty(a.ResolveIcon()),
                    $"{a.name}: ResolveIcon() returned empty — neither the asset's Icon nor the " +
                    "subclass DefaultIcon is set.");
            }
        }

        [Test]
        public void EveryAbility_HasADistinctShortLabel()
        {
            var dupes = AllAssets()
                .Where(a => !string.IsNullOrWhiteSpace(a.ShortLabel))
                .GroupBy(a => a.ShortLabel, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"'{g.Key}' on [{string.Join(", ", g.Select(a => a.name))}]")
                .ToList();

            Assert.IsEmpty(dupes,
                "Two abilities share a ShortLabel — the two hex buttons are indistinguishable on the HUD.");
        }

        [Test]
        public void EveryAbility_HasAVisibleAccentColor()
        {
            // AccentColor is written to unityBackgroundImageTintColor on the hex
            // sprite in both TouchControlsController and MenuUiController. Alpha 0
            // tints the hex to nothing.
            foreach (var a in AllAssets())
            {
                Assert.Greater(a.AccentColor.a, 0f,
                    $"{a.name}: AccentColor alpha is 0 — its hex button renders fully transparent.");
            }
        }

        [Test]
        public void EveryAbility_HasSaneTimings()
        {
            foreach (var a in AllAssets())
            {
                if (a is PassiveAbilitySO) continue;
                Assert.GreaterOrEqual(a.Duration, 0.05f,
                    $"{a.name}: Duration {a.Duration} is below the SO's own [Min(0.05)].");
                Assert.GreaterOrEqual(a.Cooldown, 0f,
                    $"{a.name}: negative Cooldown {a.Cooldown}.");
                Assert.GreaterOrEqual(a.Cooldown, a.Duration,
                    $"{a.name}: Cooldown {a.Cooldown}s is shorter than Duration {a.Duration}s. " +
                    "AbilityController counts the cooldown from activation, so the ability can be " +
                    "re-cast while its previous activation is still live — OnDeactivate then fires " +
                    "after the re-cast and tears down the buff it should have kept.");
            }
        }

        [Test]
        public void EveryAbility_ResolvesToAConcreteBotRole()
        {
            // BotController dispatches on ResolveBotRole(); a residual Auto would fall
            // through every role-preference branch and the bot would never cast it.
            foreach (var a in AllAssets())
            {
                Assert.AreNotEqual(BotRole.Auto, a.ResolveBotRole(),
                    $"{a.name}: ResolveBotRole() returned Auto. Category={a.Category} is not covered by " +
                    "AbilityBaseSO.ResolveBotRole's switch, so bots will never use this ability.");
            }
        }

        [Test]
        public void EveryAbility_HasACategoryTheCharacterSelectPickerRenders()
        {
            // MenuUiController groups the ability grid by a fixed Cats[] array. An
            // ability whose Category is outside the declared enum lands in no group
            // and simply vanishes from the picker.
            var declared = Enum.GetValues(typeof(AbilityCategory)).Cast<AbilityCategory>().ToHashSet();

            foreach (var a in AllAssets())
            {
                Assert.IsTrue(declared.Contains(a.Category),
                    $"{a.name}: Category {(byte)a.Category} is not a declared AbilityCategory value; " +
                    "the character-select grid would drop it silently.");
            }
        }

        // ---- Range gating -------------------------------------------------------

        [Test]
        public void RangeGatedAbilities_ExposeANonZeroIndicatorRange()
        {
            // AbilityBaseSO.IsUsable short-circuits to `true` when IndicatorRange <= 0,
            // so an ability that claims RequiresEnemyInRange but reports range 0 gets
            // NO grey-out and NO TryActivate refusal — it burns its cooldown on a
            // guaranteed whiff, which is the exact bug WS2 fixed for Flying Peck.
            foreach (var a in AllAssets())
            {
                if (!a.RequiresEnemyInRange) continue;
                Assert.Greater(a.IndicatorRange, 0f,
                    $"{a.name}: RequiresEnemyInRange is true but IndicatorRange is {a.IndicatorRange}. " +
                    "IsUsable() then always returns true and the whiff-protection is silently disabled.");
            }
        }

        [Test]
        public void PhysicsScanningAbilities_SearchTheChickensLayer()
        {
            // These five run Physics.OverlapSphere against SearchMask. Chicken.prefab
            // sits on layer 8 (Chickens); a mask that excludes it makes the ability a
            // no-op that still plays its VFX and burns its cooldown.
            const int chickensLayer = 8;
            var scanners = new HashSet<string>
            {
                nameof(PeckAbilitySO),
                nameof(CluckShockAbilitySO),
                nameof(RollPushAbilitySO),
                nameof(RollTrampleAbilitySO),
                nameof(SneakyStealAbilitySO),
            };

            var checkedAny = false;
            foreach (var a in AllAssets())
            {
                if (!scanners.Contains(a.GetType().Name)) continue;
                checkedAny = true;

                int mask = a.SearchMask.value;
                Assert.AreNotEqual(0, mask,
                    $"{a.name}: SearchMask is 0 — its Physics.OverlapSphere can never hit anything.");
                Assert.AreNotEqual(0, mask & (1 << chickensLayer),
                    $"{a.name}: SearchMask {mask} excludes layer {chickensLayer} (Chickens), which is the " +
                    "layer Chicken.prefab lives on. The ability scans and finds no targets, every time.");
            }

            Assert.IsTrue(checkedAny,
                "No physics-scanning ability assets were found — the scanner type list is stale.");
        }

        // ---- Icon mapping -------------------------------------------------------

        [Test]
        public void AbilityIconStyle_CoversEveryConcreteAbilityType()
        {
            // AbilityIconStyle.ClassFor keys off the concrete type name. A new ability
            // whose type is missing from the dictionary returns null, and both the
            // touch HUD and the menu quietly hide the icon element.
            var missing = new List<string>();

            foreach (var type in ConcreteAbilityTypes())
            {
                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    if (AbilityIconStyle.ClassFor(probe) == null) missing.Add(type.Name);
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }

            Assert.IsEmpty(missing.OrderBy(n => n).ToList(),
                "These ability types have no AbilityIconStyle entry, so their hex buttons render " +
                "without an icon in both the touch HUD and character-select.");
        }

        [Test]
        public void AbilityIconStyle_MapsEachTypeToItsOwnUssClass()
        {
            var byClass = new Dictionary<string, string>();
            var dupes = new List<string>();

            foreach (var type in ConcreteAbilityTypes())
            {
                var probe = (AbilityBaseSO)ScriptableObject.CreateInstance(type);
                try
                {
                    var cls = AbilityIconStyle.ClassFor(probe);
                    if (cls == null) continue;
                    if (byClass.TryGetValue(cls, out var other)) dupes.Add($"{type.Name} and {other} both use '{cls}'");
                    else byClass[cls] = type.Name;
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }

            Assert.IsEmpty(dupes, "Two ability types share one icon USS class — they draw the same sprite.");
        }

        [Test]
        public void AbilityIconStyle_ReturnsNull_ForNull()
        {
            // Callers rely on this to decide whether to hide the icon element; an
            // exception here would take out the whole ability-hex refresh path.
            Assert.IsNull(AbilityIconStyle.ClassFor(null));
        }
    }
}
