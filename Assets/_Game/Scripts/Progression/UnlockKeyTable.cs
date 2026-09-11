using System;
using CluckWars.Gameplay;

namespace CluckWars.Progression
{
    /// <summary>
    /// The translation boundary from gameplay's role enum to progression's stable keys. The only
    /// type under <c>Progression/</c> allowed to name a gameplay type.
    /// </summary>
    public static class UnlockKeyTable
    {
        /// <summary>The stable unlock/save key for <paramref name="role"/>, e.g. <c>"class.warrior"</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The role has no row in this table.</exception>
        /// <remarks>
        /// Written out row by row on purpose, never <c>ToString()</c> or <c>nameof</c>: an enum
        /// member's name is a refactoring target, and a saved key is a promise to a player.
        /// Renaming a member must never change the key it maps to, or everyone who unlocked
        /// against it loses it.
        /// </remarks>
        public static string RoleKey(ChickenClass role) => role switch
        {
            ChickenClass.Warrior  => "class.warrior",
            ChickenClass.Speedy   => "class.speedy",
            ChickenClass.Fatty    => "class.fatty",
            ChickenClass.Assassin => "class.assassin",

            // Only reachable when a role was added to the enum and not here: a wiring bug, so it
            // is loud. UnlockKeyTests.RoleKeys_ArePinnedAndCoverEveryRole keeps it from shipping.
            _ => throw new ArgumentOutOfRangeException("role",
                     $"Role value {(byte)role} has no unlock key. Add a row to UnlockKeyTable.RoleKey — " +
                     "never derive a key from the value's name."),
        };
    }
}
