using System;
using System.Collections.Generic;
using CluckWars.Gameplay;

namespace CluckWars.Abilities
{
    /// <summary>
    /// Stable identity of a class specialization ("subclass"). Each value corresponds to exactly
    /// one <see cref="PassiveAbilitySO"/> asset, whose <see cref="PassiveAbilitySO.Subclass"/>
    /// field carries it.
    /// </summary>
    /// <remarks>
    /// Explicit values, append-only — <c>.asset</c> files serialise the integer. Byte-backed for
    /// future Fusion use (the selection travels as the passive asset today, so this is not
    /// networked yet). <c>None</c> is the "unauthored" marker and is flagged by
    /// <see cref="PreEquippedLoadout"/> validation.
    /// </remarks>
    public enum ChickenSubclass : byte
    {
        None                = 0,
        Warrior_Relentless  = 1, // Warrior
        Warrior_Bully       = 2, // Warrior
        Speedy_Slippery     = 3, // Speedy
        Speedy_Featherfoot  = 4, // Speedy
        Fatty_Hoarder       = 5, // Fatty
        Fatty_Bulwark       = 6, // Fatty
        Assassin_Spoiler    = 7, // Assassin
        Assassin_Thief      = 8, // Assassin
    }

    /// <summary>
    /// Bit-set of <see cref="ChickenSubclass"/> values, so an ability can say "I am pre-equipped by
    /// these subclasses" (<see cref="AbilityBaseSO.PeckSlotPreEquippedBy"/> /
    /// <see cref="AbilityBaseSO.SignaturePreEquippedBy"/>). Member-for-member mirror of
    /// <see cref="ChickenSubclass"/>: same names, same order, flag == 1 &lt;&lt; (subclass - 1).
    /// Pinned by <c>ContractsAndEnumsTests</c>. <c>ushort</c> leaves room beyond today's eight.
    /// </summary>
    [Flags]
    public enum ChickenSubclassFlags : ushort
    {
        None                = 0,
        Warrior_Relentless  = 1 << 0,
        Warrior_Bully       = 1 << 1,
        Speedy_Slippery     = 1 << 2,
        Speedy_Featherfoot  = 1 << 3,
        Fatty_Hoarder       = 1 << 4,
        Fatty_Bulwark       = 1 << 5,
        Assassin_Spoiler    = 1 << 6,
        Assassin_Thief      = 1 << 7,
    }

    public static class ChickenSubclassExtensions
    {
        /// <summary>The single-bit flag for a subclass; <c>None</c> maps to <c>None</c>.</summary>
        public static ChickenSubclassFlags ToFlag(this ChickenSubclass subclass) =>
            subclass == ChickenSubclass.None ? ChickenSubclassFlags.None
                                             : (ChickenSubclassFlags)(1 << ((int)subclass - 1));

        /// <summary>Every subclass whose bit is set in <paramref name="flags"/>, in enum order.</summary>
        public static IEnumerable<ChickenSubclass> Subclasses(this ChickenSubclassFlags flags)
        {
            foreach (ChickenSubclass s in Enum.GetValues(typeof(ChickenSubclass)))
                if (s != ChickenSubclass.None && (flags & s.ToFlag()) != 0) yield return s;
        }

        /// <summary>
        /// The class a subclass belongs to. <see cref="ChickenSubclass.None"/> has no class and
        /// throws <see cref="ArgumentOutOfRangeException"/> — callers must check for None first
        /// (<see cref="PreEquippedLoadout.Validate"/> does), rather than silently getting
        /// Warrior (the zero value of <see cref="ChickenClass"/>).
        /// </summary>
        public static ChickenClass ClassOf(this ChickenSubclass subclass) => subclass switch
        {
            ChickenSubclass.Warrior_Relentless or ChickenSubclass.Warrior_Bully     => ChickenClass.Warrior,
            ChickenSubclass.Speedy_Slippery   or ChickenSubclass.Speedy_Featherfoot => ChickenClass.Speedy,
            ChickenSubclass.Fatty_Hoarder    or ChickenSubclass.Fatty_Bulwark   => ChickenClass.Fatty,
            ChickenSubclass.Assassin_Spoiler    or ChickenSubclass.Assassin_Thief     => ChickenClass.Assassin,
            _ => throw new ArgumentOutOfRangeException(nameof(subclass), subclass,
                "ChickenSubclass.None (or an unknown value) belongs to no class."),
        };
    }
}
