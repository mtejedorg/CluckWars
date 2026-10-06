using System;
using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Services;

namespace CluckWars.Settings
{
    /// <summary>
    /// The last class / perk / loadout / mode the player committed from the menu, exactly as
    /// stored in <see cref="PlayerPreferences"/>: raw ids, not yet validated. Identity is by
    /// stable id so it survives asset reshuffles: the class enum value, the
    /// <see cref="ChickenSubclass"/> byte (the perk), and each ability's asset name
    /// (<c>AbilityBaseSO.name</c>, the registry's identity for an ability).
    /// </summary>
    public sealed class LastSetupRecord
    {
        public int Class;
        public int Subclass;
        /// <summary>One asset name per loadout slot, in slot order.</summary>
        public string[] Abilities = Array.Empty<string>();
        public int Mode;
    }

    /// <summary>A <see cref="LastSetupRecord"/> that passed <see cref="LastSetupResolver"/>.</summary>
    public readonly struct ResolvedLastSetup
    {
        public readonly ChickenClass Class;
        public readonly PassiveAbilitySO Passive;
        public readonly AbilityBaseSO[] Slots;
        public readonly SessionMode Mode;

        public ResolvedLastSetup(ChickenClass cls, PassiveAbilitySO passive, AbilityBaseSO[] slots, SessionMode mode)
        {
            Class = cls; Passive = passive; Slots = slots; Mode = mode;
        }
    }

    /// <summary>
    /// Turns a stored <see cref="LastSetupRecord"/> back into live assets, or says why it cannot.
    /// A setup that no longer exists (renamed ability, removed perk), is illegal for its class, or
    /// has the wrong slot count is "no last setup" - never a crash and never an illegal loadout.
    /// Pure (takes the ability list, touches no scene), so the menu and tests share it.
    /// </summary>
    public static class LastSetupResolver
    {
        public static LastSetupRecord Record(ChickenClass cls, PassiveAbilitySO passive,
            IReadOnlyList<AbilityBaseSO> slots, SessionMode mode)
        {
            var names = new string[slots?.Count ?? 0];
            for (int i = 0; i < names.Length; i++) names[i] = slots[i] != null ? slots[i].name : string.Empty;
            return new LastSetupRecord
            {
                Class = (int)cls,
                Subclass = passive != null ? (int)passive.Subclass : 0,
                Abilities = names,
                Mode = (int)mode,
            };
        }

        /// <param name="all">The registry's <c>All</c> list (abilities and passives).</param>
        /// <param name="problem">Why the record was rejected; null when it resolved.</param>
        public static bool TryResolve(LastSetupRecord rec, IEnumerable<AbilityBaseSO> all,
            out ResolvedLastSetup result, out string problem)
        {
            result = default;
            problem = null;
            if (rec == null) { problem = "no record"; return false; }

            var abilities = new List<AbilityBaseSO>();
            if (all != null) foreach (var a in all) if (a != null) abilities.Add(a);

            if (rec.Class < 0 || rec.Class > 255 || !Enum.IsDefined(typeof(ChickenClass), (byte)rec.Class))
            { problem = $"unknown class id {rec.Class}"; return false; }
            var cls = (ChickenClass)(byte)rec.Class;

            if (rec.Mode < 0 || !Enum.IsDefined(typeof(SessionMode), rec.Mode))
            { problem = $"unknown session mode {rec.Mode}"; return false; }
            var mode = (SessionMode)rec.Mode;

            // Perk: the passive carrying the stored subclass id, and it must belong to this class.
            PassiveAbilitySO passive = null;
            if (rec.Subclass > 0 && rec.Subclass <= 255 && Enum.IsDefined(typeof(ChickenSubclass), (byte)rec.Subclass))
            {
                var sub = (ChickenSubclass)(byte)rec.Subclass;
                foreach (var a in abilities)
                    if (a is PassiveAbilitySO p && p.Subclass == sub) { passive = p; break; }
            }
            if (passive == null) { problem = $"no perk with subclass id {rec.Subclass}"; return false; }
            if (!AbilityRegistrySO.IsAllowedFor(passive, cls) || passive.Subclass.ClassOf() != cls)
            { problem = $"perk '{passive.name}' does not belong to {cls}"; return false; }

            // Loadout: exactly one name per slot, each a real, distinct, legal active ability.
            if (rec.Abilities == null || rec.Abilities.Length != AbilityController.SlotCount)
            { problem = $"expected {AbilityController.SlotCount} loadout slots, found {rec.Abilities?.Length ?? 0}"; return false; }

            AbilityBaseSO plainPeck = null;
            foreach (var a in abilities) if (a is PeckAbilitySO) { plainPeck = a; break; }
            PreEquippedLoadout.Resolve(cls, passive, abilities, plainPeck, out var peckSlot, out var signature);

            var slots = new AbilityBaseSO[AbilityController.SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                string name = rec.Abilities[i];
                AbilityBaseSO found = null;
                if (!string.IsNullOrEmpty(name))
                    foreach (var a in abilities)
                        if (!(a is PassiveAbilitySO) && a.name == name) { found = a; break; }
                if (found == null) { problem = $"slot {i}: ability '{name}' no longer exists"; return false; }
                if (Array.IndexOf(slots, found) >= 0) { problem = $"slot {i}: '{name}' is equipped twice"; return false; }

                // Legal = hand-pickable by this class (never a Peck: that is pre-equip-only), or the
                // perk's own pre-equip, which is legal by design even when AllowedClasses is None.
                bool preEquip = found == peckSlot || found == signature;
                bool pickable = !(found is PeckAbilitySO) && AbilityRegistrySO.IsAllowedFor(found, cls);
                if (!preEquip && !pickable) { problem = $"slot {i}: '{name}' is not legal for {cls}"; return false; }
                slots[i] = found;
            }

            // The perk's pre-equips are mandatory; a stored loadout missing one predates a data change.
            if (peckSlot != null && Array.IndexOf(slots, peckSlot) < 0)
            { problem = $"loadout lacks the perk's pre-equipped '{peckSlot.name}'"; return false; }
            if (signature != null && Array.IndexOf(slots, signature) < 0)
            { problem = $"loadout lacks the perk's pre-equipped '{signature.name}'"; return false; }

            result = new ResolvedLastSetup(cls, passive, slots, mode);
            return true;
        }
    }
}
