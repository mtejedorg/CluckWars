using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pure-logic tests for <see cref="PreEquippedLoadout"/>: the Peck slot + Signature slot rule,
    /// where the ABILITY owns the assignment (the <c>PeckSlotPreEquippedBy</c> + <c>SignaturePreEquippedBy</c> columns). Uses
    /// throwaway ScriptableObject instances, so nothing here depends on shipped data.
    /// </summary>
    public sealed class PreEquippedLoadoutTests
    {
        private readonly List<Object> _made = new List<Object>();

        private T Make<T>() where T : ScriptableObject
        {
            var so = ScriptableObject.CreateInstance<T>();
            _made.Add(so);
            return so;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private enum Slot { Peck, Signature }

        /// <summary>Adds <paramref name="by"/> to the ability's Peck-slot or Signature-slot column.</summary>
        private static T Claim<T>(T a, ChickenSubclass by, Slot slot) where T : AbilityBaseSO
        {
            if (slot == Slot.Peck) a.PeckSlotPreEquippedBy |= by.ToFlag();
            else a.SignaturePreEquippedBy |= by.ToFlag();
            return a;
        }

        private PeckAbilitySO Peck() => Make<PeckAbilitySO>();

        private RelentlessPassiveSO Warrior(ChickenSubclass sub = ChickenSubclass.Warrior_Relentless)
        {
            var p = Make<RelentlessPassiveSO>();
            p.Subclass = sub;
            return p;
        }

        private ThiefPassiveSO Thief()
        {
            var p = Make<ThiefPassiveSO>();
            p.Subclass = ChickenSubclass.Assassin_Thief;
            return p;
        }

        private static bool Has(IReadOnlyList<PreEquipIssue> issues, PreEquipStatus s) => issues.Any(i => i.Status == s);

        private static AbilityBaseSO[] Reg(params AbilityBaseSO[] a) => a;

        // ---- Legality ------------------------------------------------------------

        [Test]
        public void Legal_AcceptsAnyNonPassiveAbility_IncludingOffClass()
        {
            Assert.IsTrue(PreEquippedLoadout.IsLegalPreEquip(Peck(), ChickenClass.Warrior));
            Assert.IsTrue(PreEquippedLoadout.IsLegalPreEquip(Make<MarkKillAbilitySO>(), ChickenClass.Assassin));
            Assert.IsTrue(PreEquippedLoadout.IsLegalPreEquip(Make<MarkKillAbilitySO>(), ChickenClass.Warrior));
            Assert.IsTrue(PreEquippedLoadout.IsLegalPreEquip(Make<HeadbuttAbilitySO>(), ChickenClass.Speedy));
        }

        [Test]
        public void Legal_RejectsNullPassivesAndAPeckOnAClassThatCannotForage()
        {
            Assert.IsFalse(PreEquippedLoadout.IsLegalPreEquip(null, ChickenClass.Warrior));
            Assert.IsFalse(PreEquippedLoadout.IsLegalPreEquip(Make<RelentlessPassiveSO>(), ChickenClass.Warrior));
            Assert.IsFalse(PreEquippedLoadout.IsLegalPreEquip(Peck(), ChickenClass.Assassin),
                "Assassin cannot forage, so no PeckAbilitySO in an Assassin slot.");
        }

        // ---- Pre-equip-only (AllowedClasses == None) -----------------------------

        [Test]
        public void NoneClass_MarkKillCtorDefault_IsPreEquipOnly()
        {
            var mk = Make<MarkKillAbilitySO>();
            Assert.AreEqual(ChickenClassFlags.None, mk.AllowedClasses);
            Assert.IsTrue(AbilityRegistrySO.IsPreEquipOnly(mk));
        }

        [Test]
        public void NoneClass_IsLegalAsAPreEquip_ForEveryClass()
        {
            var mk = Make<MarkKillAbilitySO>();
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                Assert.IsTrue(PreEquippedLoadout.IsLegalPreEquip(mk, cls), $"None-class must be a legal pre-equip for {cls}.");

            Claim(mk, ChickenSubclass.Assassin_Thief, Slot.Peck);
            PreEquippedLoadout.Resolve(ChickenClass.Assassin, Thief(), Reg(mk), Peck(), out var peckSlot, out _);
            Assert.AreSame(mk, peckSlot);
        }

        [Test]
        public void NoneClass_IsNeverPickerEligible_AndNeverComposedIntoDefaults()
        {
            var mk = Make<MarkKillAbilitySO>();
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                Assert.IsFalse(AbilityRegistrySO.IsAllowedFor(mk, cls), $"None-class must not be pickable by {cls}.");

            var reg = Make<AbilityRegistrySO>();
            reg.All = new AbilityBaseSO[] { mk };
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                CollectionAssert.DoesNotContain(reg.GetClassAbilitiesForClass(cls).ToList(), mk);
                reg.ComposeDefaultLoadout(cls, out var shared, out var c0, out var c1);
                Assert.IsNull(shared); Assert.IsNull(c0); Assert.IsNull(c1);
            }
            CollectionAssert.DoesNotContain(reg.SharedAbilities.ToList(), mk);
        }

        [Test]
        public void NoneClass_IsNotReportedOffClass_AndAnUnclaimedOneIsFlaggedUnreachable()
        {
            var mk = Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            var issues = PreEquippedLoadout.Validate(Thief(), Reg(mk), Peck());
            Assert.IsFalse(Has(issues, PreEquipStatus.OffClassAbility), "None-class is pre-equip-only, not off-class.");

            var orphan = Make<MarkKillAbilitySO>();
            Assert.AreEqual(1, PreEquippedLoadout.FindUnreachablePreEquipOnly(new AbilityBaseSO[] { orphan, Make<HeadbuttAbilitySO>() }).Count,
                "A None-class ability nobody pre-equips is dead data.");
            Assert.IsEmpty(PreEquippedLoadout.FindUnreachablePreEquipOnly(new AbilityBaseSO[] { mk }));
        }

        // ---- Resolve -------------------------------------------------------------

        [Test]
        public void Resolve_FindsAbilitiesBySubclassAndSlot()
        {
            var variant = Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            var headbutt = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            var otherSubclass = Claim(Make<ScrapAbilitySO>(), ChickenSubclass.Warrior_Bully, Slot.Signature);

            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(variant, otherSubclass, headbutt), Peck(),
                out var peckSlot, out var sig);

            Assert.AreSame(variant, peckSlot);
            Assert.AreSame(headbutt, sig, "Another subclass's claim must not leak in.");
        }

        [Test]
        public void Resolve_OneAbilityCanServeSeveralSubclasses()
        {
            var peck = Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            Claim(peck, ChickenSubclass.Warrior_Bully, Slot.Peck);
            foreach (var sub in new[] { ChickenSubclass.Warrior_Relentless, ChickenSubclass.Warrior_Bully })
            {
                PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(sub), Reg(peck), null, out var peckSlot, out _);
                Assert.AreSame(peck, peckSlot);
            }
        }

        [Test]
        public void Resolve_EmptyPeckSlot_FallsBackToPlainPeck_ForAForager()
        {
            var plain = Peck();
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(), plain, out var peckSlot, out _);
            Assert.AreSame(plain, peckSlot, "A forager must never spawn without foraging by accident.");
        }

        [Test]
        public void Resolve_NonPeckInThePeckSlot_MeansNoForcedPeck()
        {
            var headbutt = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(headbutt), Peck(), out var peckSlot, out _);
            Assert.AreSame(headbutt, peckSlot, "A deliberate non-Peck Peck slot is respected; plain Peck is not forced.");
        }

        [Test]
        public void Resolve_OffClassPreEquipSurvives()
        {
            var markKill = Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(markKill), Peck(), out _, out var sig);
            Assert.AreSame(markKill, sig);
        }

        [Test]
        public void Resolve_NullPassive_YieldsOnlyThePeckFallback()
        {
            var plain = Peck();
            PreEquippedLoadout.Resolve(ChickenClass.Fatty, null, Reg(), plain, out var peckSlot, out var sig);
            Assert.AreSame(plain, peckSlot);
            Assert.IsNull(sig);
        }

        [Test]
        public void Resolve_Assassin_NeverGetsAPeck_AndKeepsMarkKillInThePeckSlot()
        {
            var plain = Peck();
            PreEquippedLoadout.Resolve(ChickenClass.Assassin, Thief(), Reg(), plain, out var peckSlot, out _);
            Assert.IsNull(peckSlot, "No claimant and the Assassin cannot forage.");

            var illegalPeck = Claim(Peck(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            PreEquippedLoadout.Resolve(ChickenClass.Assassin, Thief(), Reg(illegalPeck), plain, out peckSlot, out _);
            Assert.IsNull(peckSlot, "A Peck claimed by an Assassin subclass is illegal and ignored.");

            var markKill = Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            PreEquippedLoadout.Resolve(ChickenClass.Assassin, Thief(), Reg(markKill), plain, out peckSlot, out _);
            Assert.AreSame(markKill, peckSlot);
        }

        [Test]
        public void Resolve_Conflict_TakesTheFirstInRegistryOrder()
        {
            var first = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            var second = Claim(Make<ScrapAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);

            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(first, second), null, out _, out var sig);
            Assert.AreSame(first, sig);
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(second, first), null, out _, out sig);
            Assert.AreSame(second, sig);
        }

        [Test]
        public void Resolve_IgnoresAPassiveClaimant()
        {
            var passive = Claim(Make<BullyPassiveSO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(passive), null, out _, out var sig);
            Assert.IsNull(sig);
        }

        // ---- Validate ------------------------------------------------------------

        [Test]
        public void Validate_CompleteSubclass_HasNoIssues()
        {
            var reg = Reg(Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Peck),
                          Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature));
            Assert.IsEmpty(PreEquippedLoadout.Validate(Warrior(), reg, Peck()));
        }

        [Test]
        public void Validate_ReportsMissingSlots_AsWarnings_FromAuthoredClaimsOnly()
        {
            var issues = PreEquippedLoadout.Validate(Warrior(), Reg(), Peck());
            Assert.IsTrue(Has(issues, PreEquipStatus.MissingPeckSlot),
                "Validation must not hide an empty Peck slot behind the runtime Peck fallback.");
            Assert.IsTrue(Has(issues, PreEquipStatus.MissingSignature));
            Assert.IsTrue(issues.All(i => i.Severity == PreEquipSeverity.Warning));
        }

        [Test]
        public void Validate_ReportsSlotConflict_NamingEveryClaimant()
        {
            var a = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            var b = Claim(Make<ScrapAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            a.name = "AAA"; b.name = "BBB";
            var issues = PreEquippedLoadout.Validate(Warrior(), Reg(a, b), Peck());
            var conflict = issues.Single(i => i.Status == PreEquipStatus.SlotConflict);
            Assert.AreEqual(PreEquipSeverity.Error, conflict.Severity);
            StringAssert.Contains("AAA", conflict.Message);
            StringAssert.Contains("BBB", conflict.Message);

            // Same ability for different slots/subclasses is not a conflict.
            var c = Claim(Make<ScrapAbilitySO>(), ChickenSubclass.Warrior_Bully, Slot.Signature);
            Assert.IsFalse(Has(PreEquippedLoadout.Validate(Warrior(), Reg(a, c), Peck()), PreEquipStatus.SlotConflict));
        }

        [Test]
        public void Validate_ReportsAnAssassinPeckAsIllegal()
        {
            var peck = Claim(Peck(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            Assert.IsTrue(Has(PreEquippedLoadout.Validate(Thief(), Reg(peck)), PreEquipStatus.IllegalPeckSlot));
            var peckSig = Claim(Peck(), ChickenSubclass.Assassin_Thief, Slot.Signature);
            Assert.IsTrue(Has(PreEquippedLoadout.Validate(Thief(), Reg(peckSig)), PreEquipStatus.IllegalSignature));
        }

        [Test]
        public void Validate_OffClassAndCannotForage_AreInfoNotesOnly()
        {
            var reg = Reg(Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck),
                          Claim(Make<ShadowstepAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature));
            var issues = PreEquippedLoadout.Validate(Warrior(), reg, Peck());

            Assert.IsTrue(Has(issues, PreEquipStatus.OffClassAbility));
            Assert.IsTrue(Has(issues, PreEquipStatus.CannotForage));
            Assert.IsTrue(issues.All(i => i.Severity == PreEquipSeverity.Info),
                "Gameplay-consequence notes must never be Warning/Error.");
        }

        [Test]
        public void Validate_CannotForage_NotRaised_WhenAPeckIsPreEquipped_TheSlotIsEmpty_OrTheClassCannotForage()
        {
            var plain = Peck();
            var peckOnly = Reg(Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Peck),
                               Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature));
            Assert.IsFalse(Has(PreEquippedLoadout.Validate(Warrior(), peckOnly, plain), PreEquipStatus.CannotForage));

            var sigOnly = Reg(Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Signature));
            Assert.IsFalse(Has(PreEquippedLoadout.Validate(Warrior(), sigOnly, plain), PreEquipStatus.CannotForage),
                "An empty Peck slot falls back to the plain Peck at runtime.");

            var mk = Reg(Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Assassin_Thief, Slot.Peck));
            Assert.IsFalse(Has(PreEquippedLoadout.Validate(Thief(), mk, plain), PreEquipStatus.CannotForage),
                "The Assassin cannot forage anyway.");
        }

        [Test]
        public void Validate_ReportsMissingSubclassIdAndClassMismatch()
        {
            Assert.IsTrue(Has(PreEquippedLoadout.Validate(Warrior(ChickenSubclass.None), Reg()), PreEquipStatus.MissingSubclassId));
            Assert.IsTrue(Has(PreEquippedLoadout.Validate(Warrior(ChickenSubclass.Assassin_Thief), Reg()), PreEquipStatus.SubclassClassMismatch));

            var multi = Warrior();
            multi.AllowedClasses = ChickenClassFlags.Warrior | ChickenClassFlags.Speedy;
            Assert.IsTrue(Has(PreEquippedLoadout.Validate(multi, Reg()), PreEquipStatus.SubclassClassMismatch),
                "A passive must serve exactly one class.");
        }

        // ---- Two slot columns ------------------------------------------------------

        [Test]
        public void Validate_SameAbilityInBothSlots_IsAnError_AndResolveKeepsItInThePeckSlotOnly()
        {
            var headbutt = Claim(Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck),
                                 ChickenSubclass.Warrior_Relentless, Slot.Signature);
            var issue = PreEquippedLoadout.Validate(Warrior(), Reg(headbutt), Peck())
                .Single(i => i.Status == PreEquipStatus.SameAbilityInBothSlots);
            Assert.AreEqual(PreEquipSeverity.Error, issue.Severity);

            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(headbutt), Peck(), out var peckSlot, out var sig);
            Assert.AreSame(headbutt, peckSlot);
            Assert.IsNull(sig, "The Signature copy of the same ability is ignored.");
        }

        [Test]
        public void Validate_SameAbilityForDifferentSubclassesInDifferentColumns_IsNotFlagged()
        {
            var headbutt = Claim(Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck),
                                 ChickenSubclass.Warrior_Bully, Slot.Signature);
            Assert.IsFalse(Has(PreEquippedLoadout.Validate(Warrior(), Reg(headbutt), Peck()), PreEquipStatus.SameAbilityInBothSlots));
        }

        [Test]
        public void Validate_PeckInTheSignatureColumn_IsAnError_AndResolveIgnoresIt()
        {
            var peckSig = Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Signature);
            var issues = PreEquippedLoadout.Validate(Warrior(), Reg(peckSig), Peck());
            var illegal = issues.Single(i => i.Status == PreEquipStatus.IllegalSignature);
            Assert.AreEqual(PreEquipSeverity.Error, illegal.Severity);
            StringAssert.Contains("Peck slot", illegal.Message);

            var plain = Peck();
            PreEquippedLoadout.Resolve(ChickenClass.Warrior, Warrior(), Reg(peckSig), plain, out var peckSlot, out var sig);
            Assert.IsNull(sig, "A Peck is never a signature.");
            Assert.AreSame(plain, peckSlot, "The empty Peck slot still falls back to the plain Peck.");
        }

        [Test]
        public void Validate_AnAssassinPeckInThePeckSlot_SaysTheAssassinCannotForage()
        {
            var peck = Claim(Peck(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            var issue = PreEquippedLoadout.Validate(Thief(), Reg(peck)).Single(i => i.Status == PreEquipStatus.IllegalPeckSlot);
            Assert.AreEqual(PreEquipSeverity.Error, issue.Severity);
            StringAssert.Contains("cannot forage", issue.Message);
        }

        [Test]
        public void ValidateRoster_FlagsAPickablePeck_ButNotANoneClassOne()
        {
            var pickable = Peck();
            pickable.AllowedClasses = ChickenClassFlags.Warrior;
            var issue = PreEquippedLoadout.ValidateRoster(new PassiveAbilitySO[0], Reg(pickable))
                .Single(i => i.Status == PreEquipStatus.PeckIsPickable);
            Assert.AreEqual(PreEquipSeverity.Error, issue.Severity);
            Assert.IsNull(issue.Passive, "PeckIsPickable is ability-level.");

            Assert.IsFalse(Has(PreEquippedLoadout.ValidateRoster(new PassiveAbilitySO[0], Reg(Peck())), PreEquipStatus.PeckIsPickable));
        }

        [Test]
        public void Peck_IsNoneClass_ByDefault_AndNeverPickable()
        {
            var peck = Peck();
            Assert.AreEqual(ChickenClassFlags.None, peck.AllowedClasses);
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                Assert.IsFalse(AbilityRegistrySO.IsAllowedFor(peck, cls));
        }

        [Test]
        public void FindUnreachable_ABothColumnClaim_CountsAsReachable()
        {
            var sigOnly = Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Assassin_Thief, Slot.Signature);
            Assert.IsEmpty(PreEquippedLoadout.FindUnreachablePreEquipOnly(new AbilityBaseSO[] { sigOnly }),
                "A claim in the Signature column alone makes a None-class ability reachable.");
            var peckOnly = Claim(Make<MarkKillAbilitySO>(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            Assert.IsEmpty(PreEquippedLoadout.FindUnreachablePreEquipOnly(new AbilityBaseSO[] { peckOnly }));
        }

        // ---- Foraging is derived from the Peck-slot column --------------------------

        [Test]
        public void ClassMayForage_IsEveryClassButTheAssassin_AndAgreesWithSctExempt()
        {
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                Assert.AreEqual(SctTargets.IsExempt(cls.ToString()), !PreEquippedLoadout.ClassMayForage(cls),
                    $"{cls}: SctTargets.Exempt and ClassMayForage disagree.");
            foreach (var name in SctTargets.Exempt)
                Assert.IsFalse(PreEquippedLoadout.ClassMayForage((ChickenClass)System.Enum.Parse(typeof(ChickenClass), name)));
            foreach (var target in SctTargets.All)
                Assert.IsTrue(PreEquippedLoadout.ClassMayForage((ChickenClass)System.Enum.Parse(typeof(ChickenClass), target.ClassName)));
        }

        [Test]
        public void Forages_FollowsThePeckSlot()
        {
            var plain = Peck();
            Assert.IsTrue(PreEquippedLoadout.Forages(ChickenClass.Warrior, Warrior(), Reg(), plain),
                "An empty Peck slot on a forager falls back to the plain Peck, so it forages.");

            var variant = Claim(Peck(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            Assert.IsTrue(PreEquippedLoadout.Forages(ChickenClass.Warrior, Warrior(), Reg(variant), plain));

            var headbutt = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            Assert.IsFalse(PreEquippedLoadout.Forages(ChickenClass.Warrior, Warrior(), Reg(headbutt), plain),
                "A deliberate non-Peck in the Peck slot means no forced Peck.");

            Assert.IsFalse(PreEquippedLoadout.Forages(ChickenClass.Warrior, Warrior(), Reg(), null),
                "No claimant and no plain Peck to fall back on: nothing to forage with.");
        }

        [Test]
        public void Forages_IsNeverTrueForTheAssassin_EvenIfPecksMaskDriftsOrAStrayPeckIsClaimed()
        {
            var plain = Peck();
            plain.AllowedClasses = ChickenClassFlags.All; // drift: the mask must not matter
            var stray = Claim(Peck(), ChickenSubclass.Assassin_Thief, Slot.Peck);
            Assert.IsFalse(PreEquippedLoadout.Forages(ChickenClass.Assassin, Thief(), Reg(stray), plain));
            Assert.IsFalse(PreEquippedLoadout.Forages(ChickenClass.Assassin, Thief(), Reg(), plain));

            PreEquippedLoadout.Resolve(ChickenClass.Assassin, Thief(), Reg(stray), plain, out var peckSlot, out var sig);
            Assert.IsNull(peckSlot);
            Assert.IsNull(sig);
        }

        [Test]
        public void ClassForages_IsTrueWhenAnySpecializationOfTheClassForages()
        {
            var plain = Peck();
            var headbutt = Claim(Make<HeadbuttAbilitySO>(), ChickenSubclass.Warrior_Relentless, Slot.Peck);
            var relentless = Warrior();                                   // Peck slot = Headbutt: does not forage
            var bully = Warrior(ChickenSubclass.Warrior_Bully);                   // empty Peck slot: falls back, forages
            var passives = new PassiveAbilitySO[] { relentless, bully };

            Assert.IsTrue(PreEquippedLoadout.ClassForages(ChickenClass.Warrior, passives, Reg(headbutt), plain));
            Assert.IsFalse(PreEquippedLoadout.ClassForages(ChickenClass.Warrior, new PassiveAbilitySO[] { relentless }, Reg(headbutt), plain));
            Assert.IsFalse(PreEquippedLoadout.ClassForages(ChickenClass.Speedy, passives, Reg(headbutt), plain),
                "Passives of another class do not count.");
            Assert.IsFalse(PreEquippedLoadout.ClassForages(ChickenClass.Assassin, new PassiveAbilitySO[] { Thief() }, Reg(), plain));
            Assert.IsFalse(PreEquippedLoadout.ClassForages(ChickenClass.Warrior, null, Reg(), plain));
        }

        // ---- ValidateRoster ------------------------------------------------------

        [Test]
        public void ValidateRoster_FlagsDuplicateSubclassIds()
        {
            var issues = PreEquippedLoadout.ValidateRoster(new PassiveAbilitySO[] { Warrior(), Warrior() }, Reg());
            Assert.AreEqual(1, issues.Count(i => i.Status == PreEquipStatus.DuplicateSubclass));
        }

        [Test]
        public void ValidateRoster_FlagsEnumValuesWithNoAsset_ButNotNone()
        {
            var issues = PreEquippedLoadout.ValidateRoster(new PassiveAbilitySO[] { Warrior() }, Reg());
            var noAsset = issues.Where(i => i.Status == PreEquipStatus.SubclassHasNoAsset).Select(i => i.Subclass).ToList();
            CollectionAssert.DoesNotContain(noAsset, ChickenSubclass.None);
            CollectionAssert.DoesNotContain(noAsset, ChickenSubclass.Warrior_Relentless);
            Assert.AreEqual(System.Enum.GetValues(typeof(ChickenSubclass)).Length - 2, noAsset.Count);
        }

        [Test]
        public void ClassOf_MapsEverySubclass_AndRejectsNone()
        {
            foreach (ChickenSubclass s in System.Enum.GetValues(typeof(ChickenSubclass)))
            {
                if (s == ChickenSubclass.None) continue;
                Assert.DoesNotThrow(() => s.ClassOf(), $"{s} has no class mapping.");
            }
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ChickenSubclass.None.ClassOf());
            Assert.AreEqual(ChickenClass.Assassin, ChickenSubclass.Assassin_Spoiler.ClassOf());
            Assert.AreEqual(ChickenClass.Fatty, ChickenSubclass.Fatty_Bulwark.ClassOf());
        }

        [Test]
        public void SubclassFlags_ToFlagAndEnumerationRoundTrip()
        {
            Assert.AreEqual(ChickenSubclassFlags.None, ChickenSubclass.None.ToFlag());
            var set = ChickenSubclassFlags.Warrior_Bully | ChickenSubclassFlags.Assassin_Thief;
            CollectionAssert.AreEqual(new[] { ChickenSubclass.Warrior_Bully, ChickenSubclass.Assassin_Thief }, set.Subclasses().ToArray());
        }
    }
}
