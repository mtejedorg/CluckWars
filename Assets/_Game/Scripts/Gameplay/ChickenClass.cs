namespace CluckWars.Gameplay
{
    /// <summary>
    /// The four playable archetypes. Order is stable — referenced by index in
    /// <see cref="ChickenClassRegistrySO"/> and serialized into <see cref="ChickenController"/>'s
    /// networked state, so DO NOT renumber. Append new classes at the end.
    /// </summary>
    public enum ChickenClass : byte
    {
        Warrior  = 0, // all-rounder
        Speedy   = 1, // hit-and-run, high speed
        Fatty    = 2, // huge cargo, slow
        Assassin = 3, // squishy, two abilities
    }
}
