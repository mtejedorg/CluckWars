using Fusion;
using UnityEngine;

namespace CluckWars.Networking
{
    /// <summary>
    /// The Fusion input struct sampled once per simulation tick on the input authority
    /// and replicated to every peer. Keep this small — it goes over the wire each tick.
    /// </summary>
    public struct PlayerNetworkInput : INetworkInput
    {
        public Vector2 Movement;
        public NetworkButtons Buttons;
    }

    /// <summary>
    /// Bit indices used inside <see cref="PlayerNetworkInput.Buttons"/>.
    /// Cast to <c>int</c> when calling <c>NetworkButtons.IsSet</c> / <c>Set</c>.
    /// </summary>
    public enum InputButton
    {
        // Attack removed in v0.3 — all combat is ability-driven.
        Ability1 = 0,
        Ability2 = 1,
        Ability3 = 2, // Assassin (Combo passive) only.
    }
}
