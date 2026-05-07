using System;
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

        event Action<ChickenClass> OnSelectionChanged;
    }
}
