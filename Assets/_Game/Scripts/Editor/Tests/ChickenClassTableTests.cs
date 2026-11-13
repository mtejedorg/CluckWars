using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Localization;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A17 (architecture audit 2026-11-12): one code-side definition per chicken class (<see cref="ChickenClasses"/>).
    /// The hand-written tables it replaced are encoded here once and compared value for value; and every table that
    /// still carries per-class data must answer for every <see cref="ChickenClass"/> value, so a fifth class fails here,
    /// naming each place that needs data.
    /// </summary>
    public sealed class ChickenClassTableTests
    {
        private static IEnumerable<ChickenClass> Classes() => Enum.GetValues(typeof(ChickenClass)).Cast<ChickenClass>();

        [Test]
        public void EveryClass_HasAnEntry_AndAll_IsTheEnum()
        {
            CollectionAssert.AreEqual(Classes().ToArray(), ChickenClasses.All, "ChickenClasses.All must list every class in enum order");
            foreach (var cls in Classes())
            {
                Assert.IsTrue(ChickenClasses.TryGet(cls, out var info), $"{cls} has no entry in ChickenClasses: add one");
                Assert.AreEqual(cls, info.Class);
                Assert.AreEqual(cls, ChickenClasses.Of(cls).Class);
                foreach (string key in new[] { info.ShortNameKey, info.RoleKey, info.StrongKey, info.WeakKey })
                {
                    Assert.IsFalse(string.IsNullOrEmpty(key), $"{cls}: a wording key is missing");
                    Assert.AreNotEqual(key, UiText.Get(key), $"{cls}: '{key}' has no text in the wording dictionary");
                }
                Assert.Greater(info.Bot.ThinkInterval, 0f, $"{cls}: no bot profile");
            }
            Assert.IsFalse(ChickenClasses.TryGet((ChickenClass)200, out _));
            Assert.AreEqual(ChickenClass.Warrior, ChickenClasses.Of((ChickenClass)200).Class, "the fallback every replaced switch had");
        }

        // ---- flags ------------------------------------------------------------------------------------------------

        /// <summary>The mapping AbilityRegistrySO carried twice.</summary>
        private static ChickenClassFlags OldFlagOf(ChickenClass cls) => cls switch
        {
            ChickenClass.Warrior  => ChickenClassFlags.Warrior,
            ChickenClass.Speedy   => ChickenClassFlags.Speedy,
            ChickenClass.Fatty    => ChickenClassFlags.Fatty,
            ChickenClass.Assassin => ChickenClassFlags.Assassin,
            _ => ChickenClassFlags.None,
        };

        /// <summary>The inverse PreEquippedLoadout carried.</summary>
        private static bool OldTryGetSingleClass(ChickenClassFlags flags, out ChickenClass cls)
        {
            switch (flags)
            {
                case ChickenClassFlags.Warrior:  cls = ChickenClass.Warrior;  return true;
                case ChickenClassFlags.Speedy:   cls = ChickenClass.Speedy;   return true;
                case ChickenClassFlags.Fatty:    cls = ChickenClass.Fatty;    return true;
                case ChickenClassFlags.Assassin: cls = ChickenClass.Assassin; return true;
                default: cls = default; return false;
            }
        }

        [Test]
        public void TheDerivedFlag_EqualsTheOldHandWrittenMapping_BothWays()
        {
            for (int raw = 0; raw < 256; raw++)
            {
                var cls = (ChickenClass)raw;
                Assert.AreEqual(OldFlagOf(cls), ChickenClasses.FlagOf(cls), $"class {raw}");
                Assert.AreEqual(OldFlagOf(cls), AbilityRegistrySO.FlagOf(cls), $"class {raw}");
            }
            foreach (var cls in Classes())
            {
                Assert.AreEqual((ChickenClassFlags)(1 << (int)cls), ChickenClasses.Of(cls).Flag);
                Assert.AreEqual(Enum.Parse(typeof(ChickenClassFlags), cls.ToString()), ChickenClasses.Of(cls).Flag,
                    "the flag of a class carries the class's name");
            }
            for (int raw = 0; raw < 256; raw++)
            {
                var flags = (ChickenClassFlags)raw;
                bool old = OldTryGetSingleClass(flags, out var oldCls);
                Assert.AreEqual(old, ChickenClasses.TryGetSingleClass(flags, out var cls), $"flags {raw}");
                Assert.AreEqual(old, PreEquippedLoadout.TryGetSingleClass(flags, out var viaLoadout), $"flags {raw}");
                if (old) { Assert.AreEqual(oldCls, cls); Assert.AreEqual(oldCls, viaLoadout); }
            }
            Assert.AreEqual(ChickenClassFlags.All, ChickenClasses.All.Aggregate(ChickenClassFlags.None, (m, c) => m | ChickenClasses.FlagOf(c)));
        }

        [Test]
        public void ChickenClassFlags_ValuesArePinned()
        {
            Assert.AreEqual(0, (int)ChickenClassFlags.None);
            Assert.AreEqual(1, (int)ChickenClassFlags.Warrior);
            Assert.AreEqual(2, (int)ChickenClassFlags.Speedy);
            Assert.AreEqual(4, (int)ChickenClassFlags.Fatty);
            Assert.AreEqual(8, (int)ChickenClassFlags.Assassin);
            Assert.AreEqual(15, (int)ChickenClassFlags.All);
            Assert.AreEqual(6, Enum.GetValues(typeof(ChickenClassFlags)).Length, "serialized in every ability asset: append only");
        }

        // ---- forage, roster ----------------------------------------------------------------------------------------

        [Test]
        public void Foraging_IsEveryClassButTheAssassin_AndSctExemptionIsDerivedFromIt()
        {
            for (int raw = 0; raw < 256; raw++)
            {
                var cls = (ChickenClass)raw;
                Assert.AreEqual(cls != ChickenClass.Assassin, ChickenClasses.MayForage(cls), $"class {raw}");
                Assert.AreEqual(cls != ChickenClass.Assassin, PreEquippedLoadout.ClassMayForage(cls), $"class {raw}");
            }
            CollectionAssert.AreEqual(new[] { "Assassin" }, SctTargets.Exempt, "the string list this rule used to be mirrored in");
        }

        [Test]
        public void TheSoloBotRoster_IsOneList()
        {
            CollectionAssert.AreEqual(new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin }, ChickenClasses.SoloBotRoster);
            Assert.AreSame(ChickenClasses.SoloBotRoster, MatchStandings.SoloBotClasses);
            var menuRoster = typeof(MenuUiController).GetField("BotClasses", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(menuRoster);
            Assert.AreSame(ChickenClasses.SoloBotRoster, menuRoster.GetValue(null));
            string spawn = RuntimeSourceGuardTests.Code("Assets/_Game/Scripts/Gameplay/MatchBootstrapper.cs");
            StringAssert.Contains("var botClasses = ChickenClasses.SoloBotRoster;", spawn);
            foreach (var path in RuntimeSourceGuardTests.RuntimeFiles().Where(p => !p.EndsWith("/Gameplay/ChickenClassInfo.cs")))
                StringAssert.DoesNotContain("{ ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin }",
                    RuntimeSourceGuardTests.Code(path), path + ": a second copy of the solo roster");
        }

        // ---- bot profiles and wording keys: the old tables, value for value ---------------------------------------

        private static readonly (ChickenClass Class, BotProfile Profile)[] OldProfiles =
        {
            (ChickenClass.Warrior, new BotProfile(BotStrategy.Brawler, 0.85f, 0.60f, 5.5f, 11.0f, 0.10f, 9.0f, 6.0f, 0.0f, 0.35f, 0.28f)),
            (ChickenClass.Speedy, new BotProfile(BotStrategy.Runner, 0.45f, 0.10f, 7.5f, 0f, 1f, 0f, 0f, 1.0f, 0f, 0.22f)),
            (ChickenClass.Fatty, new BotProfile(BotStrategy.Bunker, 0.90f, 0.35f, 6.0f, 0f, 1f, 0f, 7.0f, 0.6f, 0.15f, 0.30f)),
            (ChickenClass.Assassin, new BotProfile(BotStrategy.Predator, 0.30f, 0.05f, 6.5f, 16.0f, 0.05f, 7.0f, 0f, 0f, 1.0f, 0.25f)),
        };

        [Test]
        public void BotProfiles_AreTheOnesBotTacticsCarried()
        {
            Assert.AreEqual(Classes().Count(), OldProfiles.Length);
            foreach (var (cls, old) in OldProfiles)
            {
                var now = BotTactics.ProfileFor(cls);
                foreach (var field in typeof(BotProfile).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    Assert.AreEqual(field.GetValue(old), field.GetValue(now), $"{cls}.{field.Name}");
            }
            Assert.AreEqual(BotStrategy.Brawler, BotTactics.ProfileFor((ChickenClass)200).Strategy, "unknown class: the Warrior's, as before");
        }

        [Test]
        public void WordingKeys_AreTheOnesTheThreeUiTablesCarried()
        {
            var old = new Dictionary<ChickenClass, (string Short, string Role, string Strong, string Weak)>
            {
                [ChickenClass.Warrior]  = (UiKeys.ClassWarriorShort,  UiKeys.RoleWarrior,  UiKeys.CalloutWarriorStrong,  UiKeys.CalloutWarriorWeak),
                [ChickenClass.Speedy]   = (UiKeys.ClassSpeedyShort,   UiKeys.RoleSpeedy,   UiKeys.CalloutSpeedyStrong,   UiKeys.CalloutSpeedyWeak),
                [ChickenClass.Fatty]    = (UiKeys.ClassFattyShort,    UiKeys.RoleFatty,    UiKeys.CalloutFattyStrong,    UiKeys.CalloutFattyWeak),
                [ChickenClass.Assassin] = (UiKeys.ClassAssassinShort, UiKeys.RoleAssassin, UiKeys.CalloutAssassinStrong, UiKeys.CalloutAssassinWeak),
            };
            foreach (var cls in Classes())
            {
                var info = ChickenClasses.Of(cls);
                Assert.AreEqual(old[cls], (info.ShortNameKey, info.RoleKey, info.StrongKey, info.WeakKey), cls.ToString());
                Assert.AreEqual(UiText.Get(old[cls].Short), MatchStandings.ClassName(cls));
                Assert.AreEqual(UiText.Get(old[cls].Role), MatchStandings.RoleName(cls));
            }
        }

        // ---- the per-class data that stays where it is: each must answer for every class --------------------------

        [Test]
        public void EveryRemainingPerClassTable_AnswersForEveryClass()
        {
            var problems = new List<string>();

            // The stats asset (speed, capacity, tint, model), through the registry.
            var registry = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            foreach (var cls in Classes())
                if (!registry.TryGet(cls, out _)) problems.Add($"ChickenClassRegistry.asset: no stats for {cls}");

            // The menu: the deliberate menu tints and the class tiles of CharacterSelectClass.uxml.
            const BindingFlags priv = BindingFlags.Static | BindingFlags.NonPublic;
            var tints = (Dictionary<ChickenClass, Color>)typeof(MenuUiController).GetField("ClassTints", priv).GetValue(null);
            foreach (var cls in Classes()) if (!tints.ContainsKey(cls)) problems.Add($"MenuUiController.ClassTints: no menu tint for {cls}");
            var chips = (string[])typeof(MenuUiController).GetField("ChipNames", priv).GetValue(null);
            if (chips.Length != Classes().Count()) problems.Add($"MenuUiController.ChipNames: {chips.Length} tiles for {Classes().Count()} classes");
            else foreach (var cls in Classes())
                if (chips[(int)cls] != "Class" + cls) problems.Add($"MenuUiController.ChipNames[{(int)cls}] is '{chips[(int)cls]}', expected 'Class{cls}'");

            // The podium: where the crown sits on each class's hero render (the Warrior's is the switch default).
            var anchors = Classes().Select(PodiumLayout.StaticHeadAnchor).ToList();
            if (anchors.Distinct().Count() != anchors.Count)
                problems.Add("PodiumLayout.StaticHeadAnchor: two classes share one head anchor (a new class fell into the default arm)");

            // The picker cluck: one cue per class.
            var audio = ScriptableObject.CreateInstance<UiAudioSO>();
            try
            {
                var cueFields = typeof(UiAudioSO).GetFields().Where(f => f.Name.StartsWith("Cluck")).ToList();
                float volume = 0.1f;
                foreach (var f in cueFields) { f.SetValue(audio, new UiCue(volume)); volume += 0.1f; }
                var cues = Classes().Select(c => (object)audio.CluckFor(c)).ToList();
                if (cues.Distinct().Count() != cues.Count || cueFields.Count != cues.Count)
                    problems.Add("UiAudioSO.CluckFor: not one distinct Cluck<Class> cue per class");
            }
            finally { UnityEngine.Object.DestroyImmediate(audio); }

            Assert.IsEmpty(problems, "Per-class data is missing for a class:\n" + string.Join("\n", problems));
        }
    }
}
