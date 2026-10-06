using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Services;
using CluckWars.Settings;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Menu overhaul decision 5 (explicit PLAY AGAIN / BACK TO LOBBY, never automatic): the stored
    /// last setup (<see cref="PlayerPreferences.LastSetup"/> + <see cref="LastSetupResolver"/>) and
    /// the pure flow rules in <see cref="MatchFlowRules"/>. The Fusion-side reset
    /// (<c>GameManager.RequestPlayAgain</c>) is covered by the Play-mode run, not mocked here.
    /// </summary>
    public sealed class PlayAgainFlowTests
    {
        private static readonly string[] Keys =
        {
            PlayerPreferences.LastSetupClassKey, PlayerPreferences.LastSetupSubclassKey,
            PlayerPreferences.LastSetupAbilitiesKey, PlayerPreferences.LastSetupModeKey,
        };

        private (bool has, int i, string s)[] _saved;

        [SetUp]
        public void SetUp()
        {
            // The tests write real PlayerPrefs; put the developer's own stored setup back afterwards.
            _saved = Keys.Select(k => (PlayerPrefs.HasKey(k), PlayerPrefs.GetInt(k, 0), PlayerPrefs.GetString(k, ""))).ToArray();
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            PlayerPreferences.ResetCache();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (!_saved[i].has) { PlayerPrefs.DeleteKey(Keys[i]); continue; }
                // Class/Subclass/Mode are ints, Abilities is a string.
                if (Keys[i] == PlayerPreferences.LastSetupAbilitiesKey) PlayerPrefs.SetString(Keys[i], _saved[i].s);
                else PlayerPrefs.SetInt(Keys[i], _saved[i].i);
            }
            PlayerPrefs.Save();
            PlayerPreferences.ResetCache();
        }

        // ---- Helpers -------------------------------------------------------------------------------

        private static AbilityRegistrySO Registry() =>
            TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        /// <summary>A legal 4-slot loadout for the passive: its pre-equips, then hand-pickable abilities.</summary>
        private static AbilityBaseSO[] LegalLoadout(AbilityRegistrySO reg, ChickenClass cls, PassiveAbilitySO passive)
        {
            var peck = reg.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);
            PreEquippedLoadout.Resolve(cls, passive, reg.All, peck, out var peckSlot, out var signature);
            var picks = new List<AbilityBaseSO>();
            if (peckSlot != null) picks.Add(peckSlot);
            if (signature != null) picks.Add(signature);
            foreach (var a in reg.GetClassAbilitiesForClass(cls).Concat(reg.SharedAbilities))
            {
                if (picks.Count >= AbilityController.SlotCount) break;
                if (a is PeckAbilitySO || picks.Contains(a)) continue;
                picks.Add(a);
            }
            Assert.AreEqual(AbilityController.SlotCount, picks.Count, $"test fixture: no legal 4-slot loadout for {passive.name}");
            return picks.ToArray();
        }

        private static IEnumerable<(ChickenClass cls, PassiveAbilitySO passive)> AllPerks(AbilityRegistrySO reg)
        {
            foreach (var p in reg.Passives)
                if (PreEquippedLoadout.TryGetSingleClass(p.AllowedClasses, out var cls))
                    yield return (cls, p);
        }

        private static (ChickenClass cls, PassiveAbilitySO passive, AbilityBaseSO[] slots) First(AbilityRegistrySO reg)
        {
            var (cls, p) = AllPerks(reg).First();
            return (cls, p, LegalLoadout(reg, cls, p));
        }

        private static bool Resolve(LastSetupRecord rec, AbilityRegistrySO reg, out ResolvedLastSetup r, out string problem) =>
            LastSetupResolver.TryResolve(rec, reg.All, out r, out problem);

        // ---- PlayerPreferences.LastSetup -----------------------------------------------------------

        [Test]
        public void LastSetup_IsNullUntilSomethingIsStored()
        {
            Assert.IsNull(PlayerPreferences.LastSetup);
        }

        [Test]
        public void LastSetup_RoundTripsThroughPlayerPrefs_AndSurvivesAResetCache()
        {
            var rec = new LastSetupRecord { Class = 2, Subclass = 5, Abilities = new[] { "A", "B", "C", "D" }, Mode = (int)SessionMode.Host };
            PlayerPreferences.LastSetup = rec;

            PlayerPreferences.ResetCache(); // forces the next read to come from PlayerPrefs, not the cache
            var back = PlayerPreferences.LastSetup;

            Assert.IsNotNull(back);
            Assert.AreEqual(2, back.Class);
            Assert.AreEqual(5, back.Subclass);
            CollectionAssert.AreEqual(rec.Abilities, back.Abilities);
            Assert.AreEqual((int)SessionMode.Host, back.Mode);
        }

        [Test]
        public void LastSetup_SettingNull_ClearsEverything()
        {
            PlayerPreferences.LastSetup = new LastSetupRecord { Class = 1, Subclass = 3, Abilities = new[] { "A" }, Mode = 0 };
            PlayerPreferences.LastSetup = null;
            PlayerPreferences.ResetCache();
            Assert.IsNull(PlayerPreferences.LastSetup);
            foreach (var k in Keys) Assert.IsFalse(PlayerPrefs.HasKey(k), $"{k} must be deleted.");
        }

        [Test]
        public void LastSetup_KeysAreNamespacedAndDistinct()
        {
            foreach (var k in Keys) StringAssert.StartsWith("CluckWars.", k);
            Assert.AreEqual(Keys.Length, Keys.Distinct().Count());
        }

        [Test]
        public void ResetCache_DropsTheCachedLastSetup()
        {
            PlayerPreferences.LastSetup = new LastSetupRecord { Class = 1, Subclass = 3, Abilities = new[] { "A" }, Mode = 0 };
            // Another writer (a second process, a test) changes the store behind the cache.
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            Assert.IsNotNull(PlayerPreferences.LastSetup, "the cache is expected to hold the old value until reset");
            PlayerPreferences.ResetCache();
            Assert.IsNull(PlayerPreferences.LastSetup);
        }

        // ---- LastSetupResolver ---------------------------------------------------------------------

        [Test]
        public void Resolver_AcceptsALegalLoadout_ForEveryShippedPerk()
        {
            var reg = Registry();
            int checkedPerks = 0;
            foreach (var (cls, passive) in AllPerks(reg))
            {
                var slots = LegalLoadout(reg, cls, passive);
                var rec = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);

                // Through PlayerPrefs too: this is the path the menu actually takes.
                PlayerPreferences.LastSetup = rec;
                PlayerPreferences.ResetCache();

                Assert.IsTrue(Resolve(PlayerPreferences.LastSetup, reg, out var r, out var problem), $"{passive.name}: {problem}");
                Assert.AreEqual(cls, r.Class);
                Assert.AreSame(passive, r.Passive);
                CollectionAssert.AreEqual(slots, r.Slots, passive.name);
                Assert.AreEqual(SessionMode.Solo, r.Mode);
                checkedPerks++;
            }
            Assert.GreaterOrEqual(checkedPerks, 8, "all eight specializations should be covered");
        }

        [Test]
        public void Resolver_RejectsNullAndUnknownIds()
        {
            var reg = Registry();
            var (cls, passive, slots) = First(reg);
            var ok = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);

            Assert.IsFalse(Resolve(null, reg, out _, out _));

            var badClass = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo); badClass.Class = 99;
            Assert.IsFalse(Resolve(badClass, reg, out _, out var p1)); StringAssert.Contains("class", p1);

            var badMode = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo); badMode.Mode = 42;
            Assert.IsFalse(Resolve(badMode, reg, out _, out var p2)); StringAssert.Contains("mode", p2);

            var noSub = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo); noSub.Subclass = 0;
            Assert.IsFalse(Resolve(noSub, reg, out _, out var p3)); StringAssert.Contains("perk", p3);

            var ghostSub = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo); ghostSub.Subclass = 200;
            Assert.IsFalse(Resolve(ghostSub, reg, out _, out _));

            Assert.IsTrue(Resolve(ok, reg, out _, out var pOk), pOk);
        }

        [Test]
        public void Resolver_RejectsAPerkThatBelongsToAnotherClass()
        {
            var reg = Registry();
            var (cls, passive, slots) = First(reg);
            var rec = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
            rec.Class = (int)(ChickenClass)(((int)cls + 1) % 4); // a different, valid class
            Assert.IsFalse(Resolve(rec, reg, out _, out var problem));
            StringAssert.Contains("does not belong", problem);
        }

        [Test]
        public void Resolver_RejectsWrongSlotCounts()
        {
            var reg = Registry();
            var (cls, passive, slots) = First(reg);
            foreach (int count in new[] { 0, 1, AbilityController.SlotCount - 1, AbilityController.SlotCount + 1 })
            {
                var rec = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
                rec.Abilities = Enumerable.Range(0, count).Select(i => slots[i % slots.Length].name).ToArray();
                Assert.IsFalse(Resolve(rec, reg, out _, out var problem), $"{count} slots");
                StringAssert.Contains("slots", problem);
            }
        }

        [Test]
        public void Resolver_RejectsAMissingEmptyOrDuplicatedAbility()
        {
            var reg = Registry();
            var (cls, passive, slots) = First(reg);

            var gone = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
            gone.Abilities[3] = "Renamed_Or_Deleted_Ability";
            Assert.IsFalse(Resolve(gone, reg, out _, out var p1)); StringAssert.Contains("no longer exists", p1);

            var empty = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
            empty.Abilities[2] = string.Empty;
            Assert.IsFalse(Resolve(empty, reg, out _, out _));

            var dup = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
            dup.Abilities[3] = dup.Abilities[2];
            Assert.IsFalse(Resolve(dup, reg, out _, out var p3)); StringAssert.Contains("twice", p3);
        }

        [Test]
        public void Resolver_RejectsAnAbilityTheClassMayNotUse_AndAHandPickedPeck()
        {
            var reg = Registry();
            var (cls, passive, slots) = First(reg);

            PreEquippedLoadout.Resolve(cls, passive, reg.All, reg.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO), out var peckSlot, out var signature);
            int free = Array.FindIndex(slots, a => a != peckSlot && a != signature);
            Assert.GreaterOrEqual(free, 0, "fixture needs a hand-picked slot");

            var offClass = reg.ActiveAbilities.FirstOrDefault(a =>
                !AbilityRegistrySO.IsAllowedFor(a, cls) && !AbilityRegistrySO.IsPreEquipOnly(a) && !slots.Contains(a));
            Assert.IsNotNull(offClass, "fixture needs an ability the class cannot pick");
            var rec = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
            rec.Abilities[free] = offClass.name;
            Assert.IsFalse(Resolve(rec, reg, out _, out var problem));
            StringAssert.Contains("not legal", problem);

            // A Peck is pre-equip-only: it is legal in its own slot and nowhere else.
            var peck = reg.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO && a != peckSlot && !slots.Contains(a));
            if (peck != null)
            {
                var rec2 = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
                rec2.Abilities[free] = peck.name;
                Assert.IsFalse(Resolve(rec2, reg, out _, out _), "a Peck cannot be hand-picked into a free slot");
            }
        }

        [Test]
        public void Resolver_RejectsALoadoutMissingThePerksPreEquip()
        {
            var reg = Registry();
            var peck = reg.ActiveAbilities.FirstOrDefault(a => a is PeckAbilitySO);
            foreach (var (cls, passive) in AllPerks(reg))
            {
                PreEquippedLoadout.Resolve(cls, passive, reg.All, peck, out var peckSlot, out var signature);
                var required = signature ?? peckSlot;
                if (required == null) continue;

                var slots = LegalLoadout(reg, cls, passive);
                var spare = reg.GetClassAbilitiesForClass(cls).Concat(reg.SharedAbilities)
                    .FirstOrDefault(a => !(a is PeckAbilitySO) && !slots.Contains(a));
                if (spare == null) continue;

                var rec = LastSetupResolver.Record(cls, passive, slots, SessionMode.Solo);
                rec.Abilities[Array.IndexOf(slots, required)] = spare.name;
                Assert.IsFalse(Resolve(rec, reg, out _, out var problem), $"{passive.name} without {required.name}");
                StringAssert.Contains("pre-equipped", problem);
                return;
            }
            Assert.Inconclusive("no perk with a replaceable pre-equip in the shipped data");
        }

        [Test]
        public void Resolver_RejectsACorruptStoredRecord_WithoutThrowing()
        {
            var reg = Registry();
            PlayerPrefs.SetInt(PlayerPreferences.LastSetupClassKey, 1); // marker only: the rest is missing
            PlayerPreferences.ResetCache();
            var rec = PlayerPreferences.LastSetup;
            Assert.IsNotNull(rec);
            Assert.DoesNotThrow(() => Resolve(rec, reg, out _, out _));
            Assert.IsFalse(Resolve(rec, reg, out _, out _));
        }

        // ---- MatchFlowRules ------------------------------------------------------------------------

        [Test]
        public void PlayAgain_NeedsAuthority_AndAnEndedRound()
        {
            Assert.IsTrue(MatchFlowRules.CanPlayAgain(true, MatchState.Ended));
            Assert.IsFalse(MatchFlowRules.CanPlayAgain(false, MatchState.Ended), "a non-host sees PLAY AGAIN disabled");
            Assert.IsFalse(MatchFlowRules.CanPlayAgain(true, MatchState.Active), "never mid-round");
            Assert.IsFalse(MatchFlowRules.CanPlayAgain(true, MatchState.WaitingForPlayers), "already in the waiting room");
            Assert.IsFalse(MatchFlowRules.CanPlayAgain(false, MatchState.Active));
        }

        [Test]
        public void PlayAgain_ResetsIntoTheWaitingRoom_NeverStraightIntoAMatch()
        {
            Assert.AreEqual(MatchState.WaitingForPlayers, MatchFlowRules.PlayAgainState);
            Assert.AreNotEqual(MatchState.Active, MatchFlowRules.PlayAgainState);
        }

        [TestCase(SessionMode.Solo, true, MenuLanding.Lobby)]
        [TestCase(SessionMode.Host, true, MenuLanding.Lobby)]
        [TestCase(SessionMode.Join, true, MenuLanding.MainMenu)]
        [TestCase(SessionMode.Solo, false, MenuLanding.MainMenu)]
        [TestCase(SessionMode.Host, false, MenuLanding.MainMenu)]
        public void BackToLobby_LandsOnTheCoopForSoloAndHost_AndTheMainMenuOtherwise(SessionMode mode, bool valid, MenuLanding expected)
        {
            Assert.AreEqual(expected, MatchFlowRules.LandingAfterMatch(mode, valid));
        }

        [TestCase(SessionMode.Solo, SessionMode.Solo)]
        [TestCase(SessionMode.Host, SessionMode.Host)]
        [TestCase(SessionMode.Join, SessionMode.Solo)]
        public void MainMenuPlayAgain_OpensSoloOrHost_NeverJoin(SessionMode last, SessionMode expected)
        {
            Assert.AreEqual(expected, MatchFlowRules.MainMenuPlayAgainMode(last));
        }

        [Test]
        public void PostMatchIntent_ClearsTheOneShotFlag_EvenWithAnInvalidSetup()
        {
            // BACK TO LOBBY with no valid stored setup: the flag is still consumed (otherwise every
            // later menu load would re-trigger it) and the player lands on the main menu in Solo.
            var intent = MatchFlowRules.DecidePostMatchIntent(true, false, SessionMode.Host);
            Assert.IsTrue(intent.ClearFlag);
            Assert.AreEqual(MenuLanding.MainMenu, intent.Landing);
            Assert.AreEqual(SessionMode.Solo, intent.Mode, "the mode must not be read from an invalid setup");
        }

        [TestCase(SessionMode.Solo, MenuLanding.Lobby)]
        [TestCase(SessionMode.Host, MenuLanding.Lobby)]
        [TestCase(SessionMode.Join, MenuLanding.MainMenu)]
        public void PostMatchIntent_WithAValidSetup_ConsumesTheFlagAndLandsPerMode(SessionMode mode, MenuLanding expected)
        {
            var intent = MatchFlowRules.DecidePostMatchIntent(true, true, mode);
            Assert.IsTrue(intent.ClearFlag);
            Assert.AreEqual(expected, intent.Landing);
            Assert.AreEqual(mode, intent.Mode);
        }

        [Test]
        public void PostMatchIntent_WithoutTheFlag_DoesNothing()
        {
            var intent = MatchFlowRules.DecidePostMatchIntent(false, true, SessionMode.Host);
            Assert.IsFalse(intent.ClearFlag);
            Assert.AreEqual(MenuLanding.MainMenu, intent.Landing);
        }

        [Test]
        public void SelectionService_OpenLobbyOnMenuLoad_StartsOff()
        {
            Assert.IsFalse(new SessionSelectionService().OpenLobbyOnMenuLoad);
        }

        // ---- Round reset values + dirty-world detection ---------------------------------------------

        [Test]
        public void RoundResetFields_AreTheWaitingRoom_WithTimersClearedAndNoWinner()
        {
            var f = RoundResetFields.Fresh;
            Assert.AreEqual(MatchState.WaitingForPlayers, f.State);
            Assert.IsFalse(f.IntroTimer.IsRunning, "intro timer cleared");
            Assert.IsFalse(f.MatchTimer.IsRunning, "match timer cleared");
            Assert.AreEqual(Fusion.PlayerRef.None, f.WinnerPlayer);
            Assert.AreEqual(-1, f.WinnerCorner);
            Assert.AreEqual(0f, f.WinnerFoodTotal);
            Assert.AreEqual(MatchEventKind.None, f.ActiveEvent);
        }

        [Test]
        public void IsWorldDirty_FalseForAFreshWorld_TrueForEachKindOfLeftoverState()
        {
            Assert.IsFalse(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, false)));
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(3f, false, false)), "banked food");
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, true, false)), "drained pile");
            Assert.IsTrue(MatchFlowRules.IsWorldDirty(new WorldDirt(0f, false, true)), "golden pile from a previous host");
        }

        // ---- LastSetup empty loadout ----------------------------------------------------------------

        [Test]
        public void LastSetup_EmptyAbilities_RoundTripsAsAnEmptyArray_NotOneEmptyName()
        {
            PlayerPreferences.LastSetup = new LastSetupRecord { Class = 1, Subclass = 3, Abilities = new string[0], Mode = 0 };
            PlayerPreferences.ResetCache();
            var back = PlayerPreferences.LastSetup;
            Assert.IsNotNull(back);
            Assert.AreEqual(0, back.Abilities.Length);
            Assert.AreEqual(0, PlayerPreferences.DecodeAbilities("").Length);
            CollectionAssert.AreEqual(new[] { "A", "B" }, PlayerPreferences.DecodeAbilities("A|B"));
        }

        // ---- Hand-written legal loadout -------------------------------------------------------------

        [Test]
        public void Resolver_AcceptsAHandWrittenLegalLoadout_ForAKnownPerk()
        {
            // Not built through PreEquippedLoadout.Resolve, so a regression in that helper cannot
            // make the fixture and the resolver agree on a wrong answer. Warrior / Relentless per the
            // shipped data: Peck.PeckSlotPreEquippedBy and Headbutt.SignaturePreEquippedBy name
            // Relentless, plus one Warrior ability (CluckShock) and one shared one (EggShell).
            var reg = Registry();
            var names = new[] { "Peck", "Headbutt", "CluckShock", "EggShell" };
            var rec = new LastSetupRecord
            {
                Class = (int)ChickenClass.Warrior, Subclass = (int)ChickenSubclass.Warrior_Relentless,
                Abilities = names, Mode = (int)SessionMode.Solo,
            };
            Assert.IsTrue(Resolve(rec, reg, out var r, out var problem), problem);
            CollectionAssert.AreEqual(names, r.Slots.Select(a => a.name).ToArray());
        }
    }
}
