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

        /// <summary>
        /// One-shot scene-load handoff: set by the post-match BACK TO LOBBY before it loads
        /// Bootstrap; <c>MenuUiController</c> reads it once on build, clears it, and opens
        /// straight onto THE COOP with the last setup instead of the main menu. This service is
        /// ProjectContext-scoped, so it survives the scene load with no static state to reset.
        /// </summary>
        bool OpenLobbyOnMenuLoad { get; set; }

        /// <summary>
        /// One-shot scene-load handoff like <see cref="OpenLobbyOnMenuLoad"/>, set by the in-match waiting
        /// room's CHANGE BIRD: the menu opens on PICK YOUR BIRD (the current selection kept) instead of the
        /// main menu or THE COOP. Read once and cleared by <c>MenuUiController</c>; it wins over
        /// <see cref="OpenLobbyOnMenuLoad"/> when both are set.
        /// </summary>
        bool OpenClassSelectOnMenuLoad { get; set; }

        event Action<ChickenClass> OnSelectionChanged;
    }
}
