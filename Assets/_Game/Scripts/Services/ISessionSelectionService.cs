using System;
using CluckWars.Abilities;
using CluckWars.Gameplay;

namespace CluckWars.Services
{
    /// <summary>
    /// How the local player wants to enter the upcoming match.
    /// </summary>
    public enum SessionMode
    {
        /// <summary>Single-player Fusion runner, no networking. Default for solo dev.</summary>
        Solo = 0,
        /// <summary>Shared Mode session — this client creates the session.</summary>
        Host = 1,
        /// <summary>Shared Mode session — this client joins an existing session by name.</summary>
        Join = 2,
    }

    /// <summary>
    /// Holds the local player's choices for the upcoming match: class, network mode,
    /// and session name. Bound app-wide in <c>ProjectInstaller</c> so the menu
    /// (Bootstrap scene) can write to it and the spawner / network service (Game
    /// scene) can read from it across the scene transition.
    /// </summary>
    public interface ISessionSelectionService
    {
        ChickenClass SelectedClass { get; set; }
        SessionMode Mode { get; set; }

        /// <summary>Session name used by Host / Join. Single LAN preset for the demo.</summary>
        string SessionName { get; set; }

        /// <summary>Class passive chosen by the local player. Mandatory in ADR 0003.</summary>
        PassiveAbilitySO Passive { get; set; }

        /// <summary>Ability the local player wants equipped in slot 0. Null = use prefab default.</summary>
        AbilityBaseSO Ability0 { get; set; }

        /// <summary>Ability the local player wants equipped in slot 1. Null = use prefab default.</summary>
        AbilityBaseSO Ability1 { get; set; }

        /// <summary>Ability the local player wants equipped in slot 2. Assassin (Combo passive) only. Null = use prefab default.</summary>
        AbilityBaseSO Ability2 { get; set; }

        /// <summary>Fourth ability slot, added with v0.7's four-slot loadouts.</summary>
        AbilityBaseSO Ability3 { get; set; }

        event Action<ChickenClass> OnSelectionChanged;
    }
}
