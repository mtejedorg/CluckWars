using System;
using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;

namespace CluckWars.Gameplay
{
    /// <summary>What is wrong with (or worth noting about) a subclass's pre-equipped slots or identity.</summary>
    public enum PreEquipStatus
    {
        MissingSubclassId,
        SubclassClassMismatch,
        MissingPeckSlot,
        IllegalPeckSlot,
        MissingSignature,
        /// <summary>A Peck in the Signature column (Peck belongs in the Peck slot), or any Peck an Assassin cannot use.</summary>
        IllegalSignature,
        /// <summary>More than one ability claims the same slot for one subclass; runtime takes the first in registry order.</summary>
        SlotConflict,
        /// <summary>One ability fills BOTH slots of one subclass; the Signature copy is ignored at runtime.</summary>
        SameAbilityInBothSlots,
        /// <summary>Ability-level: a <see cref="PeckAbilitySO"/> whose AllowedClasses is not None, i.e. hand-pickable.</summary>
        PeckIsPickable,
        DuplicateSubclass,
        SubclassHasNoAsset,
        /// <summary>Gameplay note: a class that can forage will spawn with no Peck at all.</summary>
        CannotForage,
        /// <summary>Gameplay note: a slot holds an ability whose AllowedClasses excludes this class.</summary>
        OffClassAbility,
    }

    public enum PreEquipSeverity { Info, Warning, Error }

    /// <summary>One finding from <see cref="PreEquippedLoadout"/> validation.</summary>
    public readonly struct PreEquipIssue
    {
        public readonly PreEquipStatus Status;
        public readonly PreEquipSeverity Severity;
        /// <summary>The offending passive; null for <see cref="PreEquipStatus.SubclassHasNoAsset"/> and <see cref="PreEquipStatus.PeckIsPickable"/>.</summary>
        public readonly PassiveAbilitySO Passive;
        public readonly ChickenSubclass Subclass;
        public readonly string Message;

        public PreEquipIssue(PreEquipStatus status, PassiveAbilitySO passive, ChickenSubclass subclass, string message)
        {
            Status = status; Passive = passive; Subclass = subclass; Message = message;
            Severity = SeverityOf(status);
        }

        /// <summary>Info = consequence worth knowing; Warning = an unfilled slot (design gap); Error = broken data.</summary>
        public static PreEquipSeverity SeverityOf(PreEquipStatus status) => status switch
        {
            PreEquipStatus.CannotForage or PreEquipStatus.OffClassAbility => PreEquipSeverity.Info,
            PreEquipStatus.MissingPeckSlot or PreEquipStatus.MissingSignature => PreEquipSeverity.Warning,
            _ => PreEquipSeverity.Error,
        };

        public override string ToString() => Message;
    }

    /// <summary>
    /// The single rule for what a specialization ("subclass") pre-equips: a <b>Peck slot</b> and a
    /// <b>Signature slot</b>. The ABILITY owns the assignment through two flag columns,
    /// <see cref="AbilityBaseSO.PeckSlotPreEquippedBy"/> and <see cref="AbilityBaseSO.SignaturePreEquippedBy"/>,
    /// so two abilities claiming one slot for one subclass is visible data that validation flags,
    /// not a silent overwrite. Pure (no scene dependencies) so the spawner, menu picker,
    /// Balance Editor and tests share it.
    /// </summary>
    /// <remarks>
    /// <b>AllowedClasses means "hand-selectable in the picker" and nothing else.</b> Peck is
    /// None-class: nobody picks it; it is forced through the Peck-slot column. Whether a subclass
    /// forages is therefore DATA: it forages iff <see cref="Resolve"/> puts a
    /// <see cref="PeckAbilitySO"/> in its Peck slot (<see cref="Forages"/>). The single hard rule
    /// is <see cref="ClassMayForage"/>: the Assassin never does.
    /// <para>
    /// <b>Legality.</b> The <c>AllowedClasses</c> filter is deliberately NOT applied to pre-equips:
    /// every class shares <c>Chicken.prefab</c>, so no ability has a class-specific runtime
    /// dependency; an off-class pre-equip is a design choice (an Info note), and a None-class
    /// ability is legal too, which is what makes it pre-equip-only. The one exception is
    /// <see cref="PeckAbilitySO"/>, legal only for a class that <see cref="ClassMayForage"/>, and
    /// only in the Peck slot.
    /// </para>
    /// <para>
    /// <b>Resolve vs Validate.</b> <see cref="Resolve"/> is forgiving at runtime: an EMPTY Peck slot
    /// on a class that may forage falls back to the plain Peck, so a forager can never spawn without
    /// foraging by accident (SCT axiom). A Peck slot deliberately filled with a legal non-Peck ability
    /// means "no forced Peck". On a claim conflict the first claimant in registry order wins.
    /// <see cref="Validate"/> reads the authored claims only, no fallback.
    /// </para>
    /// </remarks>
    public static class PreEquippedLoadout
    {
        /// <summary>
        /// The one hard design rule about foraging: every class forages except the Assassin
        /// (Predation axiom: it earns food by taking it from rivals; mirrors
        /// <c>SctTargets.Exempt</c>, pinned by a test). This is NOT read from any ability's
        /// AllowedClasses.
        /// </summary>
        public static bool ClassMayForage(ChickenClass cls) => cls != ChickenClass.Assassin;

        /// <summary>
        /// The one predicate for BOTH slots: a real, non-passive ability, and, if it is a Peck,
        /// one the class may use (<see cref="ClassMayForage"/>).
        /// </summary>
        public static bool IsLegalPreEquip(AbilityBaseSO a, ChickenClass cls) =>
            a != null && !(a is PassiveAbilitySO)
            && (!(a is PeckAbilitySO) || ClassMayForage(cls));

        /// <summary>Non-passive abilities whose Peck-slot column contains <paramref name="subclass"/>, in the order given (registry order). More than one is a conflict.</summary>
        public static List<AbilityBaseSO> PeckSlotClaimants(IEnumerable<AbilityBaseSO> allAbilities, ChickenSubclass subclass) =>
            Claimants(allAbilities, subclass, signatureColumn: false);

        /// <summary>Non-passive abilities whose Signature-slot column contains <paramref name="subclass"/>, in the order given (registry order). More than one is a conflict.</summary>
        public static List<AbilityBaseSO> SignatureClaimants(IEnumerable<AbilityBaseSO> allAbilities, ChickenSubclass subclass) =>
            Claimants(allAbilities, subclass, signatureColumn: true);

        private static List<AbilityBaseSO> Claimants(IEnumerable<AbilityBaseSO> allAbilities, ChickenSubclass subclass, bool signatureColumn)
        {
            var result = new List<AbilityBaseSO>();
            if (allAbilities == null || subclass == ChickenSubclass.None) return result;
            var flag = subclass.ToFlag();
            foreach (var a in allAbilities)
            {
                if (a == null || a is PassiveAbilitySO) continue;
                var column = signatureColumn ? a.SignaturePreEquippedBy : a.PeckSlotPreEquippedBy;
                if ((column & flag) != 0 && !result.Contains(a)) result.Add(a);
            }
            return result;
        }

        /// <summary>
        /// Resolves what <paramref name="passive"/>'s subclass pre-equips for <paramref name="cls"/>
        /// by finding the abilities that claim it. See the class remarks for fallback rules.
        /// A <see cref="PeckAbilitySO"/> in the Signature column is ignored (Peck belongs in the Peck slot).
        /// </summary>
        /// <param name="plainPeck">The registry's plain Peck, used only as the empty-slot safety net.</param>
        public static void Resolve(ChickenClass cls, PassiveAbilitySO passive, IEnumerable<AbilityBaseSO> allAbilities,
            AbilityBaseSO plainPeck, out AbilityBaseSO peckSlot, out AbilityBaseSO signature)
        {
            var sub = passive != null ? passive.Subclass : ChickenSubclass.None;
            var authoredPeck = PeckSlotClaimants(allAbilities, sub).FirstOrDefault();
            if (IsLegalPreEquip(authoredPeck, cls)) peckSlot = authoredPeck;
            else if (plainPeck is PeckAbilitySO && IsLegalPreEquip(plainPeck, cls)) peckSlot = plainPeck;
            else peckSlot = null;

            var authoredSig = SignatureClaimants(allAbilities, sub).FirstOrDefault();
            signature = IsLegalPreEquip(authoredSig, cls) && !(authoredSig is PeckAbilitySO) && authoredSig != peckSlot
                ? authoredSig : null;
        }

        /// <summary>
        /// Does a chicken of <paramref name="cls"/> with <paramref name="passive"/> spawn with Peck?
        /// Derived from <see cref="Resolve"/> (its Peck slot is a <see cref="PeckAbilitySO"/>) so
        /// there is exactly one definition of "forages".
        /// </summary>
        public static bool Forages(ChickenClass cls, PassiveAbilitySO passive, IEnumerable<AbilityBaseSO> allAbilities, AbilityBaseSO plainPeck)
        {
            Resolve(cls, passive, allAbilities, plainPeck, out var peckSlot, out _);
            return peckSlot is PeckAbilitySO;
        }

        /// <summary>
        /// Class-level "forages": true when ANY specialization of <paramref name="cls"/> (a passive
        /// whose AllowedClasses is exactly that class) <see cref="Forages"/>. Used where the UI
        /// has a class but no specialization yet.
        /// </summary>
        public static bool ClassForages(ChickenClass cls, IEnumerable<PassiveAbilitySO> passives,
            IEnumerable<AbilityBaseSO> allAbilities, AbilityBaseSO plainPeck)
        {
            if (passives == null) return false;
            var abilities = allAbilities?.ToList();
            foreach (var p in passives)
                if (p != null && TryGetSingleClass(p.AllowedClasses, out var c) && c == cls
                    && Forages(cls, p, abilities, plainPeck))
                    return true;
            return false;
        }

        /// <summary>
        /// Checks one passive's identity and the pre-equip claims on its subclass (no runtime
        /// fallback). Info-severity entries are consequence notes and do not mean incompleteness.
        /// </summary>
        /// <param name="plainPeck">Optional: the plain Peck, enabling the <see cref="PreEquipStatus.CannotForage"/> note.</param>
        public static IReadOnlyList<PreEquipIssue> Validate(PassiveAbilitySO passive, IEnumerable<AbilityBaseSO> allAbilities,
            AbilityBaseSO plainPeck = null)
        {
            var issues = new List<PreEquipIssue>();
            if (passive == null) return issues;

            string who = passive.name;
            var sub = passive.Subclass;

            if (sub == ChickenSubclass.None)
                issues.Add(new PreEquipIssue(PreEquipStatus.MissingSubclassId, passive, sub,
                    $"{who}: Subclass id is None — pick one."));

            if (!TryGetSingleClass(passive.AllowedClasses, out var cls))
            {
                issues.Add(new PreEquipIssue(PreEquipStatus.SubclassClassMismatch, passive, sub,
                    $"{who}: AllowedClasses ({passive.AllowedClasses}) must be exactly one class."));
                return issues; // slots cannot be judged without a class
            }

            if (sub != ChickenSubclass.None && sub.ClassOf() != cls)
                issues.Add(new PreEquipIssue(PreEquipStatus.SubclassClassMismatch, passive, sub,
                    $"{who}: Subclass {sub} belongs to {sub.ClassOf()}, but AllowedClasses is {cls}."));

            var pecks = PeckSlotClaimants(allAbilities, sub);
            var sigs = SignatureClaimants(allAbilities, sub);
            var peck = pecks.FirstOrDefault();
            var sig = sigs.FirstOrDefault();
            bool peckOk = false, sigOk = false;

            if (pecks.Count > 1)
                issues.Add(new PreEquipIssue(PreEquipStatus.SlotConflict, passive, sub,
                    $"{who}: {pecks.Count} abilities claim the Peck slot ({string.Join(", ", pecks.Select(a => a.name))}) — runtime uses '{peck.name}'."));
            if (sigs.Count > 1)
                issues.Add(new PreEquipIssue(PreEquipStatus.SlotConflict, passive, sub,
                    $"{who}: {sigs.Count} abilities claim the Signature slot ({string.Join(", ", sigs.Select(a => a.name))}) — runtime uses '{sig.name}'."));

            foreach (var both in pecks.Where(sigs.Contains))
                issues.Add(new PreEquipIssue(PreEquipStatus.SameAbilityInBothSlots, passive, sub,
                    $"{who}: '{both.name}' fills BOTH the Peck slot and the Signature slot — the Signature copy is ignored."));

            if (peck == null)
                issues.Add(new PreEquipIssue(PreEquipStatus.MissingPeckSlot, passive, sub, $"{who}: no Peck-slot ability."));
            else if (!IsLegalPreEquip(peck, cls))
                issues.Add(new PreEquipIssue(PreEquipStatus.IllegalPeckSlot, passive, sub,
                    $"{who}: Peck slot '{peck.name}' is illegal — the {cls} cannot forage."));
            else peckOk = true;

            foreach (var p in sigs.Where(a => a is PeckAbilitySO))
                issues.Add(new PreEquipIssue(PreEquipStatus.IllegalSignature, passive, sub,
                    $"{who}: Signature '{p.name}' is a Peck — Peck belongs in the Peck slot."));

            if (sig == null)
                issues.Add(new PreEquipIssue(PreEquipStatus.MissingSignature, passive, sub, $"{who}: no Signature-slot ability."));
            else if (!(sig is PeckAbilitySO))
                sigOk = true; // a Peck signature was already reported above

            // ---- Gameplay-consequence notes (Info; never block) ----
            if (peckOk && IsOffClass(peck, cls))
                issues.Add(new PreEquipIssue(PreEquipStatus.OffClassAbility, passive, sub,
                    $"{who}: Peck slot '{peck.name}' is an off-class ability — allowed, but bypasses class identity."));
            if (sigOk && IsOffClass(sig, cls))
                issues.Add(new PreEquipIssue(PreEquipStatus.OffClassAbility, passive, sub,
                    $"{who}: Signature '{sig.name}' is an off-class ability — allowed, but bypasses class identity."));

            // A forager whose Peck slot was deliberately filled with something else gets no
            // forced Peck (an EMPTY slot would fall back to it, so that case is not noted).
            if (peckOk && plainPeck is PeckAbilitySO && ClassMayForage(cls) && !(peck is PeckAbilitySO))
                issues.Add(new PreEquipIssue(PreEquipStatus.CannotForage, passive, sub,
                    $"{who}: no pre-equipped Peck — this subclass cannot gather food at all (Peck is never hand-picked)."));

            return issues;
        }

        /// <summary>
        /// <see cref="Validate"/> for every passive, plus roster-level checks: two passives
        /// sharing a Subclass id, any <see cref="ChickenSubclass"/> value (other than None)
        /// with no asset, and any <see cref="PeckAbilitySO"/> that is hand-pickable
        /// (<see cref="PreEquipStatus.PeckIsPickable"/>).
        /// </summary>
        public static IReadOnlyList<PreEquipIssue> ValidateRoster(IEnumerable<PassiveAbilitySO> passives,
            IEnumerable<AbilityBaseSO> allAbilities, AbilityBaseSO plainPeck = null)
        {
            var issues = new List<PreEquipIssue>();
            var seen = new Dictionary<ChickenSubclass, PassiveAbilitySO>();
            var abilities = allAbilities?.ToList();

            if (passives != null)
            {
                foreach (var p in passives)
                {
                    if (p == null) continue;
                    issues.AddRange(Validate(p, abilities, plainPeck));

                    if (p.Subclass == ChickenSubclass.None) continue;
                    if (seen.TryGetValue(p.Subclass, out var first))
                        issues.Add(new PreEquipIssue(PreEquipStatus.DuplicateSubclass, p, p.Subclass,
                            $"{p.name}: Subclass {p.Subclass} is already used by {first.name}."));
                    else
                        seen[p.Subclass] = p;
                }
            }

            foreach (ChickenSubclass value in Enum.GetValues(typeof(ChickenSubclass)))
            {
                if (value == ChickenSubclass.None || seen.ContainsKey(value)) continue;
                issues.Add(new PreEquipIssue(PreEquipStatus.SubclassHasNoAsset, null, value,
                    $"Subclass {value} has no PassiveAbilitySO asset."));
            }

            if (abilities != null)
                foreach (var a in abilities)
                    if (a is PeckAbilitySO && a.AllowedClasses != ChickenClassFlags.None)
                        issues.Add(new PreEquipIssue(PreEquipStatus.PeckIsPickable, null, ChickenSubclass.None,
                            $"{a.name}: a Peck must have AllowedClasses = None (never hand-picked; forced via the Peck slot) but has {a.AllowedClasses}."));

            return issues;
        }

        /// <summary>
        /// Off-class = pickable by some classes but not this one. A pre-equip-only ability
        /// (<c>AllowedClasses == None</c>) is not off-class: it belongs to no class by design.
        /// </summary>
        private static bool IsOffClass(AbilityBaseSO a, ChickenClass cls) =>
            a.AllowedClasses != ChickenClassFlags.None && !AbilityRegistrySO.IsAllowedFor(a, cls);

        /// <summary>
        /// Pre-equip-only abilities (<c>AllowedClasses == None</c>) claimed by no subclass in
        /// EITHER slot column: nobody can pick them and no subclass starts with them, so they
        /// are dead data.
        /// </summary>
        public static IReadOnlyList<AbilityBaseSO> FindUnreachablePreEquipOnly(IEnumerable<AbilityBaseSO> abilities)
        {
            var result = new List<AbilityBaseSO>();
            if (abilities != null)
                foreach (var a in abilities)
                    if (a != null && !(a is PassiveAbilitySO) && AbilityRegistrySO.IsPreEquipOnly(a)
                        && a.PeckSlotPreEquippedBy == ChickenSubclassFlags.None
                        && a.SignaturePreEquippedBy == ChickenSubclassFlags.None)
                        result.Add(a);
            return result;
        }

        /// <summary>True (and the class) when <paramref name="flags"/> has exactly one class bit set.</summary>
        public static bool TryGetSingleClass(ChickenClassFlags flags, out ChickenClass cls)
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
    }
}
