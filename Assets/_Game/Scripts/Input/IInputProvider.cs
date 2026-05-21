using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Platform-agnostic input source. Swapped at composition root:
    /// <see cref="KeyboardInputProvider"/> on Standalone, MobileInputProvider on Android.
    /// Read by <c>FusionNetworkService</c> on each <c>OnInput</c> tick — gameplay
    /// code never reads this directly.
    /// </summary>
    /// <remarks>
    /// v0.3: Attack removed. Ability3 added for Assassin's third ability slot.
    /// </remarks>
    public interface IInputProvider
    {
        /// <summary>Stick / WASD axis, range roughly [-1,1] per axis. Not normalized.</summary>
        Vector2 GetMovement();
        bool GetAbility1Pressed();
        bool GetAbility2Pressed();
        /// <summary>Ability slot 3. Assassin (Combo passive) only; returns false for every other class.</summary>
        bool GetAbility3Pressed();
    }
}
