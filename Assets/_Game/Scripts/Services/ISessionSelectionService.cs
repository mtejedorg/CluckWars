using System;
using CluckWars.Gameplay;

namespace CluckWars.Services
{
    /// <summary>
    /// Holds the local player's chicken-class choice for the upcoming match.
    /// Bound app-wide in <c>ProjectInstaller</c> so the menu (Bootstrap scene) can
    /// write to it and the spawner (Game scene) can read from it across the
    /// scene transition.
    /// </summary>
    public interface ISessionSelectionService
    {
        ChickenClass SelectedClass { get; set; }
        event Action<ChickenClass> OnSelectionChanged;
    }
}
